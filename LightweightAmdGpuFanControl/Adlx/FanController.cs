using System.Linq;
using ADLXWrapper;
using LightweightAmdGpuFanControl.Automation;
using LightweightAmdGpuFanControl.Recovery;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl.Adlx;

/// <summary>
/// Controls AMD GPU fan speed via ADLX manual fan tuning.
/// <para>
/// Restore: before the first fan write the GPU's tuning-at-factory state is captured next to the fan state.
/// Releasing control then follows <see cref="ReleasePlanner"/>: a GPU that was entirely at factory goes back
/// through ADLX ResetToFactory (confirmed by IsAtFactory); a GPU with its own tuning, or an unknown pre-app mode,
/// gets its captured or journaled fan points (and zero-RPM flag) re-applied. Journal failures are logged and
/// never block or fail a fan write.
/// </para>
/// </summary>
public sealed class FanController
{
    private const int MinFanPercent = 20;
    private const int MaxFanPercent = 85;

    private readonly SystemServices _systemServices;
    private readonly LogService? _log;
    private GPUTuningService? _tuningService;
    private ManualFanTuning? _manualFanTuning;
    private GPU? _gpu;
    private int _lastLoggedPercent = -1;
    private int _lastAppliedPercent = -1;
    private BaselineJournalSession? _journal;
    private GpuIdentity? _identity;
    private FanBaseline? _baseline;
    private BaselineSource _source = BaselineSource.Captured;
    private BaselineChoiceKind _choiceKind = BaselineChoiceKind.NoEntry;
    private bool _journalDirty;
    private bool _releasePending;
    private bool _atFactoryUnavailable;

    /// <summary>Identity of the initialized GPU; null before a successful Initialize or if capture failed.</summary>
    public GpuIdentity? Identity => _identity;

    /// <summary>Whether restore targets a recovered journal baseline or the wrapper's captured state (v1.0).</summary>
    public BaselineSource BaselineSource => _source;

    /// <summary>How the restore baseline was chosen; <c>NoEntry</c> when there is no journal.</summary>
    public BaselineChoiceKind ChoiceKind => _choiceKind;

    /// <summary>The plan used by the most recent release; null before one ran.</summary>
    public ReleasePlan? LastReleasePlan { get; private set; }

    public FanController(SystemServices systemServices, LogService? log = null)
    {
        _systemServices = systemServices;
        _log = log;
    }

    /// <summary>
    /// Checks if manual fan tuning is supported for the given GPU.
    /// </summary>
    public bool IsFanControlSupported(GPU? gpu = null)
    {
        gpu ??= _systemServices.GetGPUs().FirstOrDefault();
        if (gpu == null) return false;

        try
        {
            _tuningService ??= _systemServices.GetGPUTuningService();
            return _tuningService.IsManualFanTuningSupported(gpu);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Initializes fan control for the primary GPU. Call before SetFanPercent.
    /// </summary>
    /// <returns>True if manual fan tuning is available.</returns>
    public bool Initialize(GPU? gpu = null)
    {
        gpu ??= _systemServices.GetGPUs().FirstOrDefault();
        if (gpu == null) return false;

        return Initialize(gpu, out _);
    }

    /// <summary>As <see cref="Initialize(GPU?)"/>, reporting why a GPU was not usable.</summary>
    public bool Initialize(GPU gpu, out string? reason) => Initialize(gpu, out reason, null);

    /// <summary>
    /// As above, also capturing the pre-app fan state and consulting <paramref name="journal"/> for a
    /// dirty entry left by an unclean stop. Journal handling never affects the result.
    /// </summary>
    internal bool Initialize(GPU gpu, out string? reason, BaselineJournalSession? journal)
    {
        reason = null;
        try
        {
            _tuningService ??= _systemServices.GetGPUTuningService();
            if (!_tuningService.IsManualFanTuningSupported(gpu))
            {
                reason = "manual fan tuning not supported";
                return false;
            }

            _manualFanTuning = _tuningService.GetManualFanTuning(gpu);
            _gpu = gpu;
            if (_manualFanTuning == null)
                return false;

            // The pre-app fan state is what a release restores. Without it the fan could stay at the app's last
            // duty at exit, so fail closed: a capture failure makes the GPU unusable (no channel is created).
            CaptureBaseline(gpu, journal);
            if (_baseline == null)
            {
                _manualFanTuning = null;
                _gpu = null;
                reason = "no releasable fan baseline could be captured";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _manualFanTuning = null;
            _gpu = null;
            reason = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private void CaptureBaseline(GPU gpu, BaselineJournalSession? journal)
    {
        var speeds = _manualFanTuning!.GetCurrentState();
        bool zeroSupported = _manualFanTuning.SupportsZeroRPM;
        bool zeroRpm = zeroSupported && _manualFanTuning.GetZeroRPMState();
        var captured = new FanBaseline(speeds, zeroSupported, zeroRpm, ReadAtFactory(gpu));
        var identity = new GpuIdentity(gpu.Name ?? "", gpu.UniqueId, speeds.Length);

        _identity = identity;
        _baseline = captured;
        _source = BaselineSource.Captured;
        _choiceKind = BaselineChoiceKind.NoEntry;
        _journal = null;
        _journalDirty = false;
        _releasePending = false;
        if (journal == null)
            return;

        try
        {
            var choice = journal.Choose(identity, captured);
            _baseline = choice.Baseline;
            _source = choice.Source;
            _choiceKind = choice.Kind;
            _journal = journal;
            // A recovered entry is already dirty on disk.
            _journalDirty = choice.Source == BaselineSource.Journal;
            _releasePending = _journalDirty;
        }
        catch (Exception ex)
        {
            // Journal trouble never blocks control: keep the captured baseline, without a journal.
            _log?.Log($"[fan] journal disabled for '{gpu.Name}': {ex.GetType().Name}: {ex.Message}");
            _baseline = captured;
            _source = BaselineSource.Captured;
            _choiceKind = BaselineChoiceKind.NoEntry;
            _journal = null;
            _journalDirty = false;
            _releasePending = false;
        }
    }

    /// <summary>
    /// Read-only IsAtFactory before any write. Null when the driver cannot answer; the pre-app mode is then unknown
    /// and the release re-applies the captured points.
    /// </summary>
    private bool? ReadAtFactory(GPU gpu)
    {
        try
        {
            return _tuningService!.IsAtFactory(gpu);
        }
        catch (Exception ex)
        {
            _atFactoryUnavailable = true;
            _log?.Log($"[fan] IsAtFactory unavailable for '{gpu.Name}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Re-applies a fan baseline: zero-RPM first (when supported), then the per-state speeds. Throws if the driver rejects it.</summary>
    private void ReapplyPoints(FanBaseline points)
    {
        if (_manualFanTuning!.SupportsZeroRPM)
            _manualFanTuning.SetZeroRPM(points.ZeroRpm);
        _manualFanTuning.SetFanTuningStates(points.Speeds.ToArray());
    }

    /// <summary>
    /// ADLX ResetToFactory, then IsAtFactory must read true. Any failure or an unconfirmed reset is logged as an
    /// incomplete release and returns false.
    /// </summary>
    private bool TryFactoryReset()
    {
        try
        {
            _tuningService!.ResetToFactory(_gpu!);
            if (_tuningService.IsAtFactory(_gpu!))
                return true;
            _log?.Log("[fan] release incomplete: the GPU does not read at factory after ResetToFactory");
        }
        catch (Exception ex)
        {
            _log?.Log($"[fan] release incomplete: ResetToFactory/IsAtFactory failed ({ex.GetType().Name}: {ex.Message})");
        }
        return false;
    }

    private void ReportIssue(string kind)
    {
        ReleaseIssueLog.Add(ReleaseIssues.Format(kind, _gpu?.Name ?? ""));
    }

    /// <summary>
    /// Executes <paramref name="step"/> (chosen by <see cref="ReleaseDecider"/>) and reports whether it succeeded: a
    /// factory reset that IsAtFactory confirmed, or points that were applied. Failures are logged here, never thrown.
    /// </summary>
    private bool RunStep(ReleaseStep step, ReleasePlan plan, FanBaseline? points)
    {
        string name = _gpu?.Name ?? "";
        switch (step)
        {
            case ReleaseStep.FactoryReset:
                if (TryFactoryReset())
                {
                    _log?.Log($"[fan] release action=FactoryReset confirmed=true for '{name}' ({plan.Why})");
                    return true;
                }
                return false;

            case ReleaseStep.ReapplyPoints:
                try
                {
                    ReapplyPoints(points!);
                }
                catch (Exception ex)
                {
                    _log?.Log($"[fan] release incomplete for '{name}': re-applying the fan points failed ({ex.GetType().Name}: {ex.Message})");
                    return false;
                }
                _log?.Log($"[fan] release action=ReapplyPoints confirmed=n/a for '{name}' ({plan.Why})");
                if (plan.ModeUnknown)
                {
                    _log?.Log($"[fan] pre-app mode unknown for '{name}': fan points re-applied, but the GPU may still be under manual tuning. " +
                              "If its fan does not return to the driver's automatic control, reset GPU tuning in AMD Software.");
                    if (_atFactoryUnavailable)
                        ReportIssue(ReleaseIssues.Unconfirmed);
                }
                return true;

            default:
                _log?.Log($"[fan] release incomplete for '{name}': no pre-app fan state was captured");
                return false;
        }
    }

    /// <summary>Records the pre-app baseline as dirty before the first write of this run. Never throws.</summary>
    private void MarkDirtyBeforeFirstWrite()
    {
        try
        {
            if (_journal != null && !_journalDirty && _identity != null && _baseline != null)
            {
                // Flag first so a failed save is not retried on every write (the session logs it).
                _journalDirty = true;
                _journal.MarkDirty(_identity, _baseline);
            }
        }
        catch (Exception ex)
        {
            _log?.Log($"[fan] journal mark-dirty failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the fan speed as a percentage. Clamped to 20-85 for this app.
    /// </summary>
    /// <param name="percent">Fan speed percent.</param>
    public void SetFanPercent(int percent)
    {
        if (_manualFanTuning == null)
            throw new InvalidOperationException("Fan control not initialized. Call Initialize() first.");

        int requested = percent;
        percent = MathCompat.Clamp(percent, MinFanPercent, MaxFanPercent);

        try
        {
            MarkDirtyBeforeFirstWrite();
            _releasePending = true; // from here on the GPU may be under manual tuning

            // The AMD driver latches the manual fan setpoint at the highest value set since manual
            // control was last released: writing a LOWER fan curve is accepted (the read-back
            // confirms it) but the fan does NOT ramp down until control is released. So when the
            // new value is LOWER than what we last applied, first release to the driver's captured
            // state (the captured fan points), then re-apply the lower value — this forces a downward
            // re-evaluation. Increases apply directly (they ramp up correctly). This mirrors why closing
            // the app (which releases control) makes the fan drop. Never a factory reset mid-run.
            bool releasedForDecrease = _lastAppliedPercent >= 0 && percent < _lastAppliedPercent;
            if (releasedForDecrease)
            {
                try { if (_baseline != null) ReapplyPoints(_baseline); } catch { /* best-effort release before re-apply */ }
            }

            if (_manualFanTuning.SupportsZeroRPM)
                _manualFanTuning.SetZeroRPM(false);

            // Percent-native fan curve: set every tuning-state point to `percent`% (ADLXHelper::SetSpeed).
            _manualFanTuning.SetFanTuningStates2(percent);
            _lastAppliedPercent = percent;

            LogApplied(requested, percent, releasedForDecrease);
        }
        catch (Exception ex)
        {
            _log?.Log($"[fan] set FAILED requested={requested}% clamped={percent}%", ex);
            throw new InvalidOperationException($"Failed to set fan speed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Diagnostic: one log line per CHANGED setpoint. Records capability flags, the tuning-state
    /// speed range, and the curve read back AFTER applying — proving the app is writing the
    /// (lowered) value each time. Pair with the live physical fan % shown in Preferences to tell
    /// an application bug (setpoint not written) from a driver behaviour (setpoint ignored).
    /// Never throws; diagnostics must not affect control.
    /// </summary>
    private void LogApplied(int requested, int clamped, bool releasedForDecrease)
    {
        if (_log == null || clamped == _lastLoggedPercent)
            return;
        _lastLoggedPercent = clamped;
        try
        {
            var state = _manualFanTuning!.GetCurrentState();
            var readback = state is { Length: > 0 } ? string.Join(",", state) : "n/a";
            _log.Log($"[fan] applied requested={requested}% clamped={clamped}% " +
                     $"releasedForDecrease={releasedForDecrease} " +
                     $"zeroRpmSupported={_manualFanTuning.SupportsZeroRPM} " +
                     $"targetSpeedSupported={_manualFanTuning.SupportsTargetFanSpeed} " +
                     $"tuningSpeedRange=[{_manualFanTuning.SpeedRange.Min},{_manualFanTuning.SpeedRange.Max}] " +
                     $"curveAfter=[{readback}]");
        }
        catch
        {
            // Diagnostics must never affect control.
        }
    }

    /// <summary>
    /// Gets the current fan speed setting (from manual tuning state).
    /// </summary>
    public int[]? GetCurrentFanState()
    {
        try
        {
            return _manualFanTuning?.GetCurrentState();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the fan to its pre-app state (see <see cref="ReleasePlanner"/>). Best-effort.
    /// </summary>
    public void RestoreAutomatic() => TryRestoreAutomatic();

    /// <summary>
    /// As <see cref="RestoreAutomatic"/>, reporting success. Nothing is written when the app never wrote to this GPU
    /// and no recovered journal entry is pending. The journal entry is marked clean only after an exact release (a
    /// factory reset that IsAtFactory did not confirm returns false and leaves the entry dirty). Never throws.
    /// </summary>
    public bool TryRestoreAutomatic()
    {
        if (_manualFanTuning == null) return false;
        try
        {
            var plan = ReleasePlanner.Plan(_baseline?.PreAppAtFactory);
            var step = ReleaseDecider.First(_releasePending, plan, _baseline != null);
            if (step == ReleaseStep.Nothing)
            {
                _lastAppliedPercent = -1;
                return true;
            }

            LastReleasePlan = plan;
            ReleaseIssueLog.Clear(_gpu?.Name ?? ""); // a later exact release replaces an earlier incomplete record
            bool ok = RunStep(step, plan, _baseline);
            var result = ReleaseDecider.Result(step, ok, _baseline != null);
            if (result.ReapplyPointsFallback && _baseline != null)
            {
                try { ReapplyPoints(_baseline); }
                catch (Exception ex) { _log?.Log($"[fan] release fallback re-apply failed: {ex.GetType().Name}: {ex.Message}"); }
                _log?.Log($"[fan] release action=FactoryReset confirmed=false for '{_gpu?.Name}'; fan points re-applied, journal entry left dirty");
            }
            if (result.ReportIncomplete)
                ReportIssue(ReleaseIssues.Incomplete);

            bool exact = result.Exact;
            _lastAppliedPercent = -1; // control released; the next Set starts a fresh latch
            if (exact)
            {
                _releasePending = false;
                if (result.MarkClean && _journalDirty && _journal != null && _identity != null)
                {
                    _journal.MarkClean(_identity);
                    _journalDirty = false;
                }
            }
            return exact;
        }
        catch (Exception ex)
        {
            // Best-effort: nothing else we can do if the driver rejects the restore.
            _log?.Log($"[fan] restore failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
