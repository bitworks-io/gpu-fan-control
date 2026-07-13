using System.Diagnostics;
using LightweightAmdGpuFanControl.Forms;
using LightweightAmdGpuFanControl.Models;
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
    private readonly System.Windows.Forms.Timer _tooltipTimer;
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
        TaskScheduler.UnobservedTaskException += (_, e) => { _fanControlService.RestoreAll(); e.SetObserved(); };

        // Live tooltip refresh (~2.5s matches poll interval).
        _tooltipTimer = new System.Windows.Forms.Timer { Interval = 2500, Enabled = true };
        _tooltipTimer.Tick += (_, _) => RefreshTooltip();
    }

    private void RefreshTooltip()
    {
        string text;
        if (_fanControlService.IsPaused)
        {
            text = "AMD Fan Control — Paused";
        }
        else
        {
            var statuses = _fanControlService.LatestStatuses;
            if (statuses.Count > 0)
            {
                var first = statuses[0];
                var temp = first.TemperatureC.HasValue ? $"{first.TemperatureC:0}°C" : "—°C";
                var fan = first.FanPercent.HasValue ? $"Fan {first.FanPercent}%" : "Fan —";
                var suffix = statuses.Count > 1 ? $" (+{statuses.Count - 1})" : "";
                text = $"GPU {temp} · {fan}{suffix}";
            }
            else
            {
                text = "Lightweight AMD GPU Fan Control";
            }

            if (_fanControlService.IsContentionMode)
                text += " · reduced polling";
        }

        // NotifyIcon.Text has a 63-character hard limit; truncate to avoid an exception.
        if (text.Length > 63)
            text = text.Substring(0, 63);

        _notifyIcon.Text = text;
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
        menu.Items.Add("About...", null, (_, _) => new AboutForm().ShowDialog());
        menu.Items.Add(new ToolStripSeparator());

        // Pause/Resume — text is updated in menu.Opening so it reflects the current state.
        var pauseItem = new ToolStripMenuItem("Pause fan control");
        pauseItem.Click += (_, _) =>
        {
            if (_fanControlService.IsPaused)
                _fanControlService.Resume();
            else
                _fanControlService.Pause();
        };
        menu.Items.Add(pauseItem);

        // Automatic mode
        menu.Items.Add("Automatic fan curve", null, (_, _) =>
        {
            var s = _settingsService.Load();
            s.Mode = FanMode.Auto;
            _settingsService.Save(s);
        });

        // Manual fixed speed
        menu.Items.Add("Manual fixed speed…", null, (_, _) =>
        {
            var s = _settingsService.Load();
            if (TryPromptManualSpeed(s.ManualFanPercent, AppSettings.MinFanFloor, AppSettings.MaxFanCeiling, out int pct))
            {
                s.Mode = FanMode.Manual;
                s.ManualFanPercent = pct;
                _settingsService.Save(s);
            }
        });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Help / Fan control not working", null, (_, _) => OpenFanControlHelp());
        menu.Items.Add("Report an issue…", null, (_, _) => AppLinks.Open(AppLinks.IssuesUrl));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());

        // Update dynamic items just before the menu appears.
        menu.Opening += (_, _) =>
        {
            pauseItem.Text = _fanControlService.IsPaused ? "Resume fan control" : "Pause fan control";
        };

        return menu;
    }

    private static bool TryPromptManualSpeed(int currentValue, int min, int max, out int result)
    {
        result = currentValue;

        using var form = new Form
        {
            Text = "Manual fan speed",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Size = new Size(280, 140)
        };

        var label = new Label
        {
            Text = "Fixed fan speed (%):",
            Location = new Point(20, 20),
            AutoSize = true
        };

        var upDown = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = MathCompat.Clamp(currentValue, min, max),
            Location = new Point(170, 17),
            Width = 70
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(100, 65), Width = 70 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(180, 65), Width = 70 };

        form.Controls.AddRange(new System.Windows.Forms.Control[] { label, upDown, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (form.ShowDialog() == DialogResult.OK)
        {
            result = (int)upDown.Value;
            return true;
        }
        return false;
    }

    private void ShowPreferences()
    {
        if (_preferencesForm != null)
        {
            _preferencesForm.BringToFront();
            _preferencesForm.Focus();
            return;
        }

        var form = new PreferencesForm(_settingsService, _startupService, _fanControlService);
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
                FileName = AppLinks.FanHelpUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(
                "In AMD Adrenalin: Performance → Tuning → GPU → Tuning Control.\nEnable 'Manual Tuning, Custom'.\nFan Tuning: ON, Zero RPM: OFF.\nApply Changes.",
                "Fan Control Help",
                MessageBoxButtons.OK);
        }
    }

    private void Exit()
    {
        _tooltipTimer.Stop();
        _tooltipTimer.Dispose();
        _fanControlService.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
    }
}
