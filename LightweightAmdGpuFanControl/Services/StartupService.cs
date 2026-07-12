using System.Windows.Forms;
using Microsoft.Win32;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Manages the "Start with Windows" registry entry (HKCU Run key).
/// </summary>
public class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LightweightAmdGpuFanControl";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(ValueName) != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;

            if (enabled)
            {
                var exePath = Application.ExecutablePath;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(ValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to update startup setting: {ex.Message}", ex);
        }
    }
}
