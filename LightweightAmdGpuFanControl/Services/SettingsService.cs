using System.Text.Json;
using LightweightAmdGpuFanControl.Models;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Loads and saves application settings to %LOCALAPPDATA%/Bitworks/LightweightAmdGpuFanControl/settings.json.
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

    private static void Validate(AppSettings s)
    {
        s.TargetTempC = Math.Clamp(s.TargetTempC, AppSettings.MinTargetTempC, AppSettings.MaxTargetTempC);
    }
}
