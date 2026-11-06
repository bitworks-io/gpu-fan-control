namespace LightweightAmdGpuFanControl.Cli;

public enum LaunchMode { Tray, Probe, RestoreAuto, Stop }

/// <summary>Process exit codes shared by every launch mode.</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Error = 1;
    public const int Usage = 2;
    public const int NothingToDo = 3;
    public const int AlreadyRunning = 4;
}

/// <summary>
/// Parsed command line. Parsing is pure and never throws; a usage problem is reported in
/// <see cref="Error"/> (caller exits with <see cref="ExitCodes.Usage"/>). Unknown arguments are
/// collected in <see cref="Ignored"/> so a newer caller does not break an older build.
/// </summary>
public sealed class LaunchOptions
{
    public const string StatusFileMissingValue = "--status-file needs a path";

    public LaunchMode Mode { get; init; } = LaunchMode.Tray;
    public bool Automation { get; init; }
    public bool NoPreload { get; init; }
    public IReadOnlyList<string> Ignored { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }
    /// <summary>Value of <c>--status-file</c>; unvalidated. Never causes a usage error.</summary>
    public string? StatusFile { get; init; }
    public string? StatusFileError { get; init; }

    public static LaunchOptions Parse(IReadOnlyList<string>? args)
    {
        bool probe = false, restore = false, stop = false, automation = false, noPreload = false;
        string? statusFile = null, statusFileError = null;
        var ignored = new List<string>();

        if (args != null)
        {
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i] ?? "";
                if (Is(a, "--probe")) probe = true;
                else if (Is(a, "--restore-auto")) restore = true;
                else if (Is(a, "--stop")) stop = true;
                else if (Is(a, "--automation")) automation = true;
                else if (Is(a, "--no-preload")) noPreload = true;
                else if (Is(a, "--status-file"))
                {
                    string? next = i + 1 < args.Count ? args[i + 1] : null;
                    if (next == null || next.StartsWith("--", StringComparison.Ordinal))
                    {
                        statusFile = null;
                        statusFileError = StatusFileMissingValue;
                    }
                    else
                    {
                        statusFile = next;
                        statusFileError = null;
                        i++;
                    }
                }
                else ignored.Add(a);
            }
        }

        string? error = null;
        if (probe && restore) error = "--probe and --restore-auto cannot be combined";
        else if (stop && probe) error = "--stop and --probe cannot be combined";
        else if (stop && restore) error = "--stop and --restore-auto cannot be combined";
        else if (noPreload && !probe) error = "--no-preload is only valid with --probe";

        return new LaunchOptions
        {
            Mode = probe ? LaunchMode.Probe : restore ? LaunchMode.RestoreAuto : stop ? LaunchMode.Stop : LaunchMode.Tray,
            Automation = automation,
            NoPreload = noPreload,
            Ignored = ignored,
            Error = error,
            StatusFile = statusFile,
            StatusFileError = statusFileError,
        };
    }

    private static bool Is(string arg, string flag) => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase);
}
