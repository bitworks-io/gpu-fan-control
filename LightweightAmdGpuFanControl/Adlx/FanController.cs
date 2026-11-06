using System.Linq;
using ADLXWrapper;
using LightweightAmdGpuFanControl.Recovery;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl.Adlx;

/// <summary>
/// Controls AMD GPU fan speed via ADLX manual fan tuning.
/// <para>
/// Restore: when no usable dirty journal entry exists (always the case without a journal), restoring
/// calls <c>ManualFanTuning.Reset</c> exactly as v1.0 did. When the journal holds a valid dirty
/// entry for this GPU (an earlier run stopped uncleanly), the journaled speeds and zero-RPM flag are
/// applied instead. Journal failures are logged and never block or fail a fan write.
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
    private bool _journalDirty;

    /// <summary>Identity of the initialized GPU; null before a successful Initialize or if capture failed.</summary>
    public GpuIdentity? Identity => _identity;

    /// <summary>Whether restore targets a recovered journal baseline or the wrapper's captured state (v1.0).</summary>
    public BaselineSource BaselineSource => _source;

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

            try
            {
                CaptureBaseline(gpu, journal);
            }
            catch (Exception ex)
            {
                _log?.Log($"[fan] journal disabled for '{gpu.Name}': {ex.GetType().Name}: {ex.Message}");
                _journal = null;
                _source = BaselineSource.Captured;
                _baseline = null;
                _identity = null;
                _journalDirty = false;
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
        var captured = new FanBaseline(speeds, zeroSupported, zeroRpm);
        var identity = new GpuIdentity(gpu.Name ?? "", gpu.UniqueId, speeds.Length);

        if (journal == null)
        {
            _identity = identity;
            _baseline = captured;
            _source = BaselineSource.Captured;
            _journal = null;
            _journalDirty = false;
            return;
        }

        var choice = journal.Choose(identity, captured);
        _identity = identity;
        _baseline = choice.Baseline;
        _source = choice.Source;
        _journal = journal;
        // A recovered entry is already dirty on disk.
        _journalDirty = choice.Source == BaselineSource.Journal;
    }

    /// <summary>
    /// Releases manual control. With a recovered journal baseline: zero-RPM first, then the journaled
    /// speeds (the same order as <c>Reset</c>). Otherwise exactly the v1.0 <c>Reset</c>.
    /// </summary>
    private void ApplyBaseline()
    {
        if (_source == BaselineSource.Journal && _baseline != null)
        {
            if (_manualFanTuning!.SupportsZeroRPM)
                _manualFanTuning.SetZeroRPM(_baseline.ZeroRpm);
            _manualFanTuning.SetFanTuningStates(_baseline.Speeds.ToArray());
        }
        else
        {
            _manualFanTuning!.Reset();
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

            // The AMD driver latches the manual fan setpoint at the highest value set since manual
            // control was last released: writing a LOWER fan curve is accepted (the read-back
            // confirms it) but the fan does NOT ramp down until control is released. So when the
            // new value is LOWER than what we last applied, first release to the driver's captured
            // state (Reset), then re-apply the lower value — this forces a downward re-evaluation.
            // Increases apply directly (they ramp up correctly). This mirrors why closing the app
            // (which Resets) makes the fan drop.
            bool releasedForDecrease = _lastAppliedPercent >= 0 && percent < _lastAppliedPercent;
            if (releasedForDecrease)
            {
                try { ApplyBaseline(); } catch { /* best-effort release before re-apply */ }
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
    /// Returns the fan to its pre-app state: the recovered journal baseline if one was adopted at
    /// Initialize, otherwise the state captured by the wrapper (v1.0 <c>Reset</c>). Best-effort.
    /// </summary>
    public void RestoreAutomatic() => TryRestoreAutomatic();

    /// <summary>
    /// As <see cref="RestoreAutomatic"/>, reporting success. The journal entry is marked clean only
    /// after the restore was applied without error. Never throws.
    /// </summary>
    public bool TryRestoreAutomatic()
    {
        if (_manualFanTuning == null) return false;
        try
        {
            ApplyBaseline();
            _lastAppliedPercent = -1; // control released; the next Set starts a fresh latch
            if (_journalDirty && _journal != null && _identity != null)
            {
                _journal.MarkClean(_identity);
                _journalDirty = false;
            }
            return true;
        }
        catch
        {
            // Best-effort: nothing else we can do if the driver rejects the restore.
            return false;
        }
    }
}
