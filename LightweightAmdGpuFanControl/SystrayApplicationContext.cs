using System.Diagnostics;
using LightweightAmdGpuFanControl.Forms;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl;

/// <summary>
/// Application context that hosts the systray icon and menu.
/// </summary>
public sealed class SystrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private readonly FanControlService _fanControlService;
    private readonly LogService _logService;
    private PreferencesForm? _preferencesForm;

    public SystrayApplicationContext()
    {
        _settingsService = new SettingsService();
        _startupService = new StartupService();
        _logService = new LogService();
        _fanControlService = new FanControlService(_settingsService, _logService);

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Lightweight AMD GPU Fan Control",
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => ShowPreferences();
        _notifyIcon.ContextMenuStrip = CreateContextMenu();
        _fanControlService.Start(this, _notifyIcon);

        // Best-effort restore of automatic fan control on any exit path, including crashes and
        // logoff/shutdown. A hard kill (Task Manager / power loss) cannot run these; control is
        // re-established on next launch and the driver reclaims it on reset.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _fanControlService.RestoreAll();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _fanControlService.RestoreAll();
        Application.ThreadException += (_, _) => _fanControlService.RestoreAll();
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => _fanControlService.RestoreAll();
    }

    private static Icon CreateTrayIcon()
    {
        using var stream = typeof(SystrayApplicationContext).Assembly
            .GetManifestResourceStream("LightweightAmdGpuFanControl.Assets.tray.ico");
        if (stream == null)
            return CreateFallbackIcon();

        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    private static Icon CreateFallbackIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(0, 120, 215));
            g.FillEllipse(brush, 2, 2, 12, 12);
        }
        return (Icon)Icon.FromHandle(bmp.GetHicon()).Clone();
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Preferences...", null, (_, _) => ShowPreferences());
        menu.Items.Add("Help / Fan control not working", null, (_, _) => OpenFanControlHelp());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        return menu;
    }

    private void ShowPreferences()
    {
        if (_preferencesForm != null)
        {
            _preferencesForm.BringToFront();
            _preferencesForm.Focus();
            return;
        }

        var form = new PreferencesForm(_settingsService, _startupService);
        form.FormClosed += (_, _) => _preferencesForm = null;
        _preferencesForm = form;
        form.Show();
    }

    private static void OpenFanControlHelp()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://help.argusmonitor.com/GPUfancontrolforAMDRadeon.html",
                UseShellExecute = true
            });
        }
        catch
        {
            // Fallback: show brief help in message
            MessageBox.Show(
                "In AMD Adrenalin: Performance → Tuning → GPU → Tuning Control.\nEnable 'Manual Tuning, Custom'.\nFan Tuning: ON, Zero RPM: OFF.\nApply Changes.",
                "Fan Control Help",
                MessageBoxButtons.OK
            );
        }
    }

    private void Exit()
    {
        _fanControlService.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
    }
}
