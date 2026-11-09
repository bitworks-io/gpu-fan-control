using System.Diagnostics;
using LightweightAmdGpuFanControl.Automation;
using LightweightAmdGpuFanControl.Cli;
using LightweightAmdGpuFanControl.IO;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Writes <c>status.json</c> for unattended (--automation) runs. Every write is best-effort: a failure is
/// logged and never throws or stops fan control.
/// </summary>
internal sealed class AutomationStatusWriter
{
    private const int ExtraAttempts = 2;
    private const int RetryDelayMs = 100;

    private readonly string _path;
    private readonly LogService _log;
    private readonly int _pid;
    private readonly DateTime _startedUtc;
    private readonly string _version;
    private readonly object _lock = new();
    private readonly HashSet<string> _failedStates = new();
    private string? _terminalState;
    private volatile IReadOnlyList<StatusGpu>? _engagedGpus;
    private bool _loggedIgnored;

    public AutomationStatusWriter(string path, LogService log)
    {
        _path = path;
        _log = log;
        using var process = Process.GetCurrentProcess();
        _pid = process.Id;
        try { _startedUtc = process.StartTime.ToUniversalTime(); }
        catch { _startedUtc = DateTime.UtcNow; }
        var v = typeof(Program).Assembly.GetName().Version;
        _version = v == null ? "" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        ControlIssueLog.Changed += OnControlIssuesChanged;
    }

    /// <summary>A GPU became refused or degraded after <c>engaged</c> was written: rewrite the engaged status with the issue.</summary>
    private void OnControlIssuesChanged()
    {
        // Raised under the control lock: never write the file there. Each queued write reads the latest issue
        // snapshot when it runs, so reordering cannot leave an older list on disk.
        if (_engagedGpus == null)
            return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var gpus = _engagedGpus;
            if (gpus != null)
                Engaged(gpus);
        });
    }

    public static string ResolvePath(LaunchOptions options, LogService log)
    {
        if (options.StatusFile == null)
            return AppPaths.DefaultStatusPath;

        var validated = StatusFilePath.Validate(options.StatusFile, Directory.Exists, IsReparsePoint, out var err);
        if (validated != null)
            return validated;

        log.Log($"--status-file rejected ({err}); using the default path");
        return AppPaths.DefaultStatusPath;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.Exists(path) || Directory.Exists(path))
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    public void Starting() =>
        Write(() => AutomationStatus.Starting(_pid, _startedUtc, DateTime.UtcNow, _version));

    public void Engaged(IReadOnlyList<StatusGpu> gpus)
    {
        _engagedGpus = gpus;
        // The issue lists are read inside the write lock (see Write), so a slower write can never land an older list last.
        Write(() => AutomationStatus.Engaged(_pid, _startedUtc, DateTime.UtcNow, _version, gpus,
            ControlIssueLog.Snapshot(), ReleaseIssueLog.Snapshot()));
    }

    public void Unavailable(string reason) =>
        Write(() => AutomationStatus.Unavailable(_pid, _startedUtc, DateTime.UtcNow, _version, reason));

    public void Stopped() =>
        Write(() => AutomationStatus.Stopped(_pid, _startedUtc, DateTime.UtcNow, _version, ReleaseIssueLog.Snapshot(), ControlIssueLog.Snapshot()));

    /// <summary>
    /// Builds the status inside the write lock, so the content (including the issue lists) is current at the moment it
    /// is written and the terminal-state check applies to it.
    /// </summary>
    private void Write(Func<AutomationStatus> build)
    {
        string state = "?";
        try
        {
            lock (_lock)
            {
                var status = build();
                state = status.State;
                string json = status.ToJson();
                // stopped / unavailable are final: a late write (e.g. engaged after a race) must not overwrite them.
                if (_terminalState != null)
                {
                    if (!_loggedIgnored)
                    {
                        _loggedIgnored = true;
                        _log.Log($"status.json: ignoring {status.State} after {_terminalState}");
                    }
                    return;
                }
                if (status.State == AutomationStatus.StateStopped || status.State == AutomationStatus.StateUnavailable)
                    _terminalState = status.State;

                string? err = null;
                for (int attempt = 0; attempt <= ExtraAttempts; attempt++)
                {
                    if (attempt > 0) Thread.Sleep(RetryDelayMs); // a reader may briefly hold the file
                    if (AtomicFile.TryWriteAllText(_path, json, out err))
                        return;
                }

                if (_failedStates.Add(status.State))
                    _log.Log($"status.json write failed ({status.State}): {err}");
            }
        }
        catch (Exception ex)
        {
            try { _log.Log($"status.json write failed ({state}): {ex.Message}"); } catch { }
        }
    }
}
