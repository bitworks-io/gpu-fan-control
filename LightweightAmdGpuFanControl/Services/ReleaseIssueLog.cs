using LightweightAmdGpuFanControl.Automation;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Process-wide list of problems returning a GPU's fan to the driver's automatic control (see
/// <see cref="ReleaseIssues"/>). FanController adds to it during a
/// release; the automation status writer reports it in <c>status.json</c> when the run stops.
/// </summary>
internal static class ReleaseIssueLog
{
    private static readonly object Gate = new();
    private static readonly List<string> Issues = new();

    public static void Add(string issue)
    {
        lock (Gate)
        {
            if (!Issues.Contains(issue))
                Issues.Add(issue);
        }
    }

    /// <summary>Drops a GPU's incomplete or unconfirmed records; called before each release so a later exact release clears them.</summary>
    public static void Clear(string gpuName)
    {
        lock (Gate)
        {
            Issues.Remove(ReleaseIssues.Format(ReleaseIssues.Incomplete, gpuName));
            Issues.Remove(ReleaseIssues.Format(ReleaseIssues.Unconfirmed, gpuName));
        }
    }

    public static IReadOnlyList<string> Snapshot()
    {
        lock (Gate)
            return Issues.ToArray();
    }
}
