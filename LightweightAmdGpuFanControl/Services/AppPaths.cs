namespace LightweightAmdGpuFanControl.Services;

/// <summary>Well-known files in the per-user data folder (the same folder <see cref="LogService"/> uses).</summary>
internal static class AppPaths
{
    private static readonly object Gate = new();
    private static string? _dataFolder;

    /// <summary>%LOCALAPPDATA%\Bitworks\LightweightAmdGpuFanControl, created on first use.</summary>
    public static string DataFolder
    {
        get
        {
            lock (Gate)
            {
                if (_dataFolder == null)
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var folder = Path.Combine(appData, "Bitworks", "LightweightAmdGpuFanControl");
                    Directory.CreateDirectory(folder);
                    _dataFolder = folder;
                }
                return _dataFolder;
            }
        }
    }

    public static string DefaultStatusPath => Path.Combine(DataFolder, "status.json");

    public static string BaselinePath => Path.Combine(DataFolder, "baseline.json");

    public static string NewProbePath() =>
        Path.Combine(DataFolder, "probe-" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture) + ".txt");
}
