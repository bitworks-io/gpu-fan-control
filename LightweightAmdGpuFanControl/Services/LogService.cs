namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Simple file logger to %LOCALAPPDATA%/Bitworks/LightweightAmdGpuFanControl/log.txt
/// </summary>
public class LogService
{
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB

    private readonly string _logPath;
    private readonly string _rolledLogPath;
    private readonly object _lock = new();

    public LogService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "Bitworks", "LightweightAmdGpuFanControl");
        Directory.CreateDirectory(folder);
        _logPath = Path.Combine(folder, "log.txt");
        _rolledLogPath = Path.Combine(folder, "log.1.txt");
    }

    public void Log(string message, Exception? ex = null)
    {
        lock (_lock)
        {
            try
            {
                RollLogIfNeeded();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
                if (ex != null)
                    line += $"\n  {ex.GetType().Name}: {ex.Message}";
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch
            {
                // Ignore logging failures
            }
        }
    }

    /// <summary>Rolls log.txt to log.1.txt once it exceeds <see cref="MaxLogSizeBytes"/>. Caller
    /// holds <c>_lock</c>. Best-effort: a rotation failure must not block logging.</summary>
    private void RollLogIfNeeded()
    {
        try
        {
            var info = new FileInfo(_logPath);
            if (!info.Exists || info.Length <= MaxLogSizeBytes)
                return;

            if (File.Exists(_rolledLogPath))
                File.Delete(_rolledLogPath);
            File.Move(_logPath, _rolledLogPath);
        }
        catch
        {
            // Ignore rotation failures; logging continues to append to the current file.
        }
    }
}
