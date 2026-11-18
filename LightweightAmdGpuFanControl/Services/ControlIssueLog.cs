using LightweightAmdGpuFanControl.Automation;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Process-wide list of GPUs that are refused or degraded because a fan write failed (see
/// <see cref="GpuIssueStates"/>). The control service adds to it; the automation status writer reports it in
/// <c>status.json</c> as <c>gpuIssues</c> and rewrites the file when it changes.
/// </summary>
internal static class ControlIssueLog
{
    private static readonly object Gate = new();
    private static readonly List<StatusGpuIssue> Issues = new();

    /// <summary>Raised after an issue is added. Handlers must not throw.</summary>
    public static event Action? Changed;

    public static void Add(string gpu, string state, string message)
    {
        lock (Gate)
            Issues.Add(new StatusGpuIssue { Gpu = gpu, State = state, Message = message });
        try { Changed?.Invoke(); } catch { /* reporting must never affect fan control */ }
    }

    public static IReadOnlyList<StatusGpuIssue> Snapshot()
    {
        lock (Gate)
            return Issues.ToArray();
    }
}
