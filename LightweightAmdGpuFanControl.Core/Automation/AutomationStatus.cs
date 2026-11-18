using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LightweightAmdGpuFanControl.Automation;

public static class StatusReasons
{
    public const string NoBackend = "no-backend";
    public const string SelfTestFailed = "self-test-failed";
    public const string NoEnabledGpu = "no-enabled-gpu";
    public const string Exit = "exit";
}

/// <summary>Kinds of release problem reported in <c>status.json</c> <c>releaseIssues</c> as <c>kind: GPU name</c>.</summary>
public static class ReleaseIssues
{
    /// <summary>The GPU could not be confirmed back at the driver's automatic control; its journal entry stays dirty.</summary>
    public const string Incomplete = "release-incomplete";

    /// <summary>Fan points were re-applied, but the driver could not say whether the GPU is back at its automatic control.</summary>
    public const string Unconfirmed = "release-unconfirmed";

    public static string Format(string kind, string gpuName) => kind + ": " + gpuName;
}

/// <summary>States reported in <c>status.json</c> <c>gpuIssues</c>.</summary>
public static class GpuIssueStates
{
    /// <summary>A write failed at start or in the self-test; the GPU is never written.</summary>
    public const string Refused = "refused";

    /// <summary>A write failed during regulation; the GPU was released once and is no longer regulated this run.</summary>
    public const string Degraded = "degraded";
}

public sealed class StatusGpuIssue
{
    public string Gpu { get; set; } = "";
    public string State { get; set; } = "";
    /// <summary>The wrapper's error text.</summary>
    public string Message { get; set; } = "";
}

public sealed class StatusGpu
{
    public string Name { get; set; } = "";
    public string Backend { get; set; } = "";
}

/// <summary>
/// <c>status.json</c> schema 1 for unattended runs. A reader must match <see cref="Pid"/> and
/// <see cref="StartedUtc"/> against the process it launched, because a killed run leaves its last state behind.
/// The boolean "engaged" is exposed as <see cref="IsEngaged"/> because the static factory
/// <c>Engaged(...)</c> cannot share its name with a property.
/// </summary>
public sealed class AutomationStatus
{
    public const string StateStarting = "starting";
    public const string StateEngaged = "engaged";
    public const string StateUnavailable = "unavailable";
    public const string StateStopped = "stopped";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public int Schema { get; set; } = 1;
    public string State { get; set; } = StateStarting;
    [System.Text.Json.Serialization.JsonPropertyName("engaged")]
    public bool IsEngaged { get; set; }
    public string? Reason { get; set; }
    public string? Gpu { get; set; }
    public string? Backend { get; set; }
    public List<StatusGpu> Gpus { get; set; } = new();
    public int Pid { get; set; }
    public string StartedUtc { get; set; } = "";
    public string UpdatedUtc { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>Per-GPU problems returning the fan to the driver's automatic control (see <see cref="ReleaseIssues"/>); empty when none.</summary>
    public List<string> ReleaseIssues { get; set; } = new();
    /// <summary>GPUs that are refused or degraded because a fan write failed; empty when none.</summary>
    public List<StatusGpuIssue> GpuIssues { get; set; } = new();

    public static AutomationStatus Starting(int pid, DateTime startedUtc, DateTime nowUtc, string version) =>
        Make(StateStarting, pid, startedUtc, nowUtc, version, null, null);

    public static AutomationStatus Engaged(int pid, DateTime startedUtc, DateTime nowUtc, string version, IReadOnlyList<StatusGpu> gpus,
        IReadOnlyList<StatusGpuIssue>? gpuIssues = null, IReadOnlyList<string>? releaseIssues = null)
    {
        var status = Make(StateEngaged, pid, startedUtc, nowUtc, version, null, gpus);
        if (releaseIssues != null)
            status.ReleaseIssues = new List<string>(releaseIssues);
        if (gpuIssues != null)
            status.GpuIssues = new List<StatusGpuIssue>(gpuIssues);
        return status;
    }

    public static AutomationStatus Unavailable(int pid, DateTime startedUtc, DateTime nowUtc, string version, string reason) =>
        Make(StateUnavailable, pid, startedUtc, nowUtc, version, reason, null);

    public static AutomationStatus Stopped(int pid, DateTime startedUtc, DateTime nowUtc, string version,
        IReadOnlyList<string>? releaseIssues = null, IReadOnlyList<StatusGpuIssue>? gpuIssues = null)
    {
        var status = Make(StateStopped, pid, startedUtc, nowUtc, version, StatusReasons.Exit, null);
        if (releaseIssues != null)
            status.ReleaseIssues = new List<string>(releaseIssues);
        if (gpuIssues != null)
            status.GpuIssues = new List<StatusGpuIssue>(gpuIssues);
        return status;
    }

    /// <summary>Parses a status.json text; null when it is empty or not a status object. Never throws.</summary>
    public static AutomationStatus? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<AutomationStatus>(json!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Camel-case, indented, with nulls included (<c>"reason": null</c>).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static AutomationStatus Make(string state, int pid, DateTime started, DateTime now, string version,
        string? reason, IReadOnlyList<StatusGpu>? gpus)
    {
        var list = gpus == null ? new List<StatusGpu>() : new List<StatusGpu>(gpus);
        var first = list.Count > 0 ? list[0] : null;
        return new AutomationStatus
        {
            State = state,
            IsEngaged = state == StateEngaged,
            Reason = reason,
            Gpu = state == StateEngaged ? first?.Name : null,
            Backend = state == StateEngaged ? first?.Backend : null,
            Gpus = state == StateEngaged ? list : new List<StatusGpu>(),
            Pid = pid,
            StartedUtc = FormatUtc(started),
            UpdatedUtc = FormatUtc(now),
            Version = version ?? "",
        };
    }

    private static string FormatUtc(DateTime t) =>
        t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>
/// Pure validation of a caller-supplied <c>--status-file</c> path. Windows-style paths handled with
/// string operations on '\' so behaviour is identical on any host OS. Never throws.
/// </summary>
public static class StatusFilePath
{
    private static readonly Regex DriveRooted = new(@"^[A-Za-z]:\\", RegexOptions.Compiled);
    private const string IllegalChars = "<>\"|?*/";

    public static string? Validate(string? path, Func<string, bool> directoryExists, Func<string, bool> isReparsePoint,
        out string? error)
    {
        error = null;
        try
        {
            string? problem = Check(path, directoryExists, isReparsePoint);
            if (problem == null) return path;
            error = problem;
            return null;
        }
        catch (Exception ex)
        {
            error = "status file path could not be checked: " + ex.Message;
            return null;
        }
    }

    private static string? Check(string? path, Func<string, bool> directoryExists, Func<string, bool> isReparsePoint)
    {
        if (string.IsNullOrEmpty(path)) return "status file path is empty";
        if (path!.StartsWith("\\\\", StringComparison.Ordinal)) return "status file path must not be a UNC path";
        if (!DriveRooted.IsMatch(path)) return "status file path must be absolute with a drive letter";
        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];
            if (c < 0x20 || c == 0x7f || IllegalChars.IndexOf(c) >= 0) return "status file path contains an illegal character";
            if (c == ':' && i != 1) return "status file path contains ':' outside the drive prefix";
        }
        var segments = path.Substring(3).Split('\\');
        foreach (var seg in segments)
            if (seg == "." || seg == "..") return "status file path must not contain relative segments";
        if (segments[segments.Length - 1].Length == 0) return "status file path has no file name";

        int last = path.LastIndexOf('\\');
        string parent = last <= 2 ? path.Substring(0, 3) : path.Substring(0, last);
        if (!directoryExists(parent)) return "status file folder does not exist";
        if (isReparsePoint(parent)) return "status file folder is a reparse point";
        if (isReparsePoint(path)) return "status file is a reparse point";
        return null;
    }
}
