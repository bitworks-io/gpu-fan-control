using System.Text.Json;
using LightweightAmdGpuFanControl.Models;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Loads and saves application settings to %LOCALAPPDATA%/Bitworks/LightweightAmdGpuFanControl/settings.json.
/// A single instance is shared between the UI and the control loop, so changes apply live via the cache.
/// </summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private AppSettings? _cached;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "Bitworks", "LightweightAmdGpuFanControl");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
    }

    /// <summary>Test/override constructor pointing at an explicit settings file.</summary>
    public SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
        var dir = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public AppSettings Load()
    {
        if (_cached != null)
            return _cached;

        if (!File.Exists(_settingsPath))
        {
            _cached = new AppSettings();
            return _cached;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            _cached = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            Validate(_cached);
            return _cached;
        }
        catch
        {
            _cached = new AppSettings();
            return _cached;
        }
    }

    public void Save(AppSettings settings)
    {
        Validate(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
        _cached = settings;
    }

    /// <summary>Clamps every field to its supported range and enforces MinFanPercent &lt; MaxFanPercent.</summary>
    private static void Validate(AppSettings s)
    {
        s.TargetTempC = Math.Clamp(s.TargetTempC, AppSettings.MinTargetTempC, AppSettings.MaxTargetTempC);
        s.HysteresisC = Math.Clamp(s.HysteresisC, AppSettings.MinHysteresisC, AppSettings.MaxHysteresisC);

        s.MinFanPercent = Math.Clamp(s.MinFanPercent, AppSettings.MinFanFloor, 70);
        s.MaxFanPercent = Math.Clamp(s.MaxFanPercent, 40, AppSettings.MaxFanCeiling);
        if (s.MinFanPercent >= s.MaxFanPercent)
        {
            s.MaxFanPercent = Math.Min(AppSettings.MaxFanCeiling, s.MinFanPercent + 5);
            if (s.MinFanPercent >= s.MaxFanPercent)
                s.MinFanPercent = s.MaxFanPercent - 5;
        }

        var criticalMin = Math.Max(s.TargetTempC + 10, 50);
        s.CriticalTempC = Math.Clamp(s.CriticalTempC, criticalMin, 100);

        s.ManualFanPercent = Math.Clamp(s.ManualFanPercent, s.MinFanPercent, s.MaxFanPercent);

        s.Gpus ??= new List<GpuConfig>();
    }
}
