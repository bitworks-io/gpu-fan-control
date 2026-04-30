namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Simple file logger to %LOCALAPPDATA%/Bitworks/LightweightAmdGpuFanControl/log.txt
/// </summary>
public class LogService
{
    private readonly string _logPath;
    private readonly object _lock = new();

    public LogService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "Bitworks", "LightweightAmdGpuFanControl");
        Directory.CreateDirectory(folder);
        _logPath = Path.Combine(folder, "log.txt");
    }

    public void Log(string message, Exception? ex = null)
    {
        lock (_lock)
        {
            try
            {
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
}
