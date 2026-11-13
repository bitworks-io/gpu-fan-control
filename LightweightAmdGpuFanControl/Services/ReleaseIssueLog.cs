using LightweightAmdGpuFanControl.Automation;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Process-wide list of problems returning a GPU's fan to the driver's automatic control (see
/// <see cref="ReleaseIssues"/>), keyed per GPU by name plus uniqueId. FanController adds to it during a release;
/// the automation status writer reports it in <c>status.json</c>.
/// </summary>
internal static class ReleaseIssueLog
{
    private static readonly object Gate = new();
    private static readonly List<(string Key, string Issue)> Issues = new();

    public static void Add(string gpuKey, string issue)
    {
        lock (Gate)
        {
            if (!Issues.Contains((gpuKey, issue)))
                Issues.Add((gpuKey, issue));
        }
    }

    /// <summary>Drops one GPU's records; called before each release so a later exact release clears them.</summary>
    public static void Clear(string gpuKey)
    {
        lock (Gate)
            Issues.RemoveAll(i => i.Key == gpuKey);
    }

    public static IReadOnlyList<string> Snapshot()
    {
        lock (Gate)
            return Issues.Select(i => i.Issue).ToArray();
    }
}
