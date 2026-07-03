using LightweightAmdGpuFanControl.Models;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl.Forms;

/// <summary>
/// Preferences dialog. Exposes per-GPU enablement, the shared fan curve settings, and a feedback link.
/// Load-modify-save on OK to preserve fields this form does not expose.
/// </summary>
public class PreferencesForm : Form
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private readonly FanControlService _fanControlService;

    private NumericUpDown _targetTempControl = null!;
    private NumericUpDown _minFanControl = null!;
    private NumericUpDown _maxFanControl = null!;
    private CheckBox _startWithWindowsCheck = null!;
    private CheckedListBox _gpuList = null!;
    private Label _statusLabel = null!;
    private System.Windows.Forms.Timer _statusTimer = null!;

    // GPU items we actually populated; null means we showed the "Detecting…" placeholder.
    private List<GpuConfig>? _loadedGpus;

    public PreferencesForm(
        SettingsService settingsService,
        StartupService startupService,
        FanControlService fanControlService)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _fanControlService = fanControlService;

        Text = "Lightweight AMD GPU Fan Control — Preferences";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(430, 560);

        var settings = _settingsService.Load();

        int y = 18;

        // ── Core GPU temperature target ──────────────────────────────────────
        var targetLabel = new Label
        {
            Text = "Core GPU temperature target (°C):",
            Location = new Point(20, y + 3),
            AutoSize = true
        };

        _targetTempControl = new NumericUpDown
        {
            Minimum = AppSettings.MinTargetTempC,
            Maximum = AppSettings.MaxTargetTempC,
            Value = settings.TargetTempC,
            Location = new Point(280, y),
            Width = 70
        };

        y += 36;

        // ── Minimum fan speed ────────────────────────────────────────────────
        var minFanLabel = new Label
        {
            Text = "Minimum fan speed (%):",
            Location = new Point(20, y + 3),
            AutoSize = true
        };

        _minFanControl = new NumericUpDown
        {
            Minimum = AppSettings.MinFanFloor,  // 20
            Maximum = 70,
            Value = Math.Clamp(settings.MinFanPercent, AppSettings.MinFanFloor, 70),
            Location = new Point(280, y),
            Width = 70
        };

        y += 36;

        // ── Maximum fan speed ────────────────────────────────────────────────
        var maxFanLabel = new Label
        {
            Text = "Maximum fan speed (%):",
            Location = new Point(20, y + 3),
            AutoSize = true
        };

        _maxFanControl = new NumericUpDown
        {
            Minimum = 40,
            Maximum = AppSettings.MaxFanCeiling,  // 85
            Value = Math.Clamp(settings.MaxFanPercent, 40, AppSettings.MaxFanCeiling),
            Location = new Point(280, y),
            Width = 70
        };

        y += 24;
        var maxFanHint = new Label
        {
            Text = "85% is the manufacturer-recommended maximum.",
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            Location = new Point(22, y),
            AutoSize = true
        };

        y += 26;

        // ── Start with Windows ───────────────────────────────────────────────
        _startWithWindowsCheck = new CheckBox
        {
            Text = "Start with Windows",
            Checked = settings.StartWithWindows,
            Location = new Point(20, y),
            AutoSize = true
        };

        y += 32;

        // ── GPU list ─────────────────────────────────────────────────────────
        var gpuLabel = new Label
        {
            Text = "Controlled GPUs:",
            Location = new Point(20, y),
            AutoSize = true
        };
        y += 20;

        _gpuList = new CheckedListBox
        {
            Location = new Point(20, y),
            Size = new Size(385, 80),
            CheckOnClick = true
        };
        PopulateGpuList(settings);
        y += 88;

        // ── Live status ──────────────────────────────────────────────────────
        _statusLabel = new Label
        {
            Location = new Point(20, y),
            Size = new Size(385, 52),
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f)
        };
        RefreshStatus();
        y += 58;

        // ── Feedback link ────────────────────────────────────────────────────
        var feedbackNote = new Label
        {
            Text = "Feature requests welcome.",
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            Location = new Point(20, y),
            AutoSize = true
        };
        y += 16;

        var feedbackLink = new LinkLabel
        {
            Text = "Send feedback / request a feature →",
            Location = new Point(20, y),
            AutoSize = true
        };
        feedbackLink.LinkClicked += (_, _) => AppLinks.Open(AppLinks.ContactFormUrl);
        y += 24;

        // ── Buttons row ──────────────────────────────────────────────────────
        var aboutButton = new Button
        {
            Text = "About…",
            Location = new Point(20, y + 2),
            Width = 80
        };
        aboutButton.Click += (_, _) => new AboutForm().ShowDialog(this);

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(250, y),
            Width = 75
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(335, y),
            Width = 75
        };
        okButton.Click += OkButton_Click;

        Controls.AddRange(new System.Windows.Forms.Control[]
        {
            targetLabel, _targetTempControl,
            minFanLabel, _minFanControl,
            maxFanLabel, _maxFanControl, maxFanHint,
            _startWithWindowsCheck,
            gpuLabel, _gpuList,
            _statusLabel,
            feedbackNote, feedbackLink,
            aboutButton, cancelButton, okButton
        });

        AcceptButton = okButton;
        CancelButton = cancelButton;

        // Status refresh timer — stopped on form close.
        _statusTimer = new System.Windows.Forms.Timer { Interval = 1000, Enabled = true };
        _statusTimer.Tick += (_, _) => RefreshStatus();

        FormClosed += (_, _) =>
        {
            _statusTimer.Stop();
            _statusTimer.Dispose();
        };
    }

    private void PopulateGpuList(AppSettings settings)
    {
        _gpuList.Items.Clear();

        if (settings.Gpus == null || settings.Gpus.Count == 0)
        {
            _gpuList.Items.Add("Detecting GPUs…", false);
            _loadedGpus = null;
            return;
        }

        var statuses = _fanControlService.LatestStatuses;
        _loadedGpus = settings.Gpus;
        foreach (var gpu in settings.Gpus)
        {
            // Prefer the friendly display name from the live status snapshot.
            var match = statuses.FirstOrDefault(s => s.GpuId == gpu.GpuId);
            var displayName = string.IsNullOrEmpty(match.Name) ? gpu.GpuId : match.Name;
            _gpuList.Items.Add(displayName, gpu.Enabled);
        }
    }

    private void RefreshStatus()
    {
        var statuses = _fanControlService.LatestStatuses;

        if (_fanControlService.IsPaused)
        {
            _statusLabel.Text = "Paused — fans on driver automatic.";
            return;
        }

        if (statuses.Count == 0)
        {
            _statusLabel.Text = "Starting…";
            return;
        }

        var lines = statuses
            .Select(s =>
            {
                var temp = s.TemperatureC.HasValue ? $"{s.TemperatureC:0}°C" : "—°C";
                var fan = s.FanPercent.HasValue ? $"fan {s.FanPercent}%" : "fan —";
                var state = s.Controlling ? "" : " (not controlling)";
                return $"{s.Name}: {temp} · {fan}{state}";
            })
            .ToArray();

        _statusLabel.Text = string.Join(Environment.NewLine, lines);
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        var settings = _settingsService.Load();
        settings.TargetTempC = (int)_targetTempControl.Value;
        settings.MinFanPercent = (int)_minFanControl.Value;
        settings.MaxFanPercent = (int)_maxFanControl.Value;
        settings.StartWithWindows = _startWithWindowsCheck.Checked;

        // Write back GPU enablement only when we displayed real GPU entries.
        if (_loadedGpus != null && _loadedGpus.Count == _gpuList.Items.Count)
        {
            for (int i = 0; i < _gpuList.Items.Count; i++)
                _loadedGpus[i].Enabled = _gpuList.GetItemChecked(i);

            settings.Gpus = _loadedGpus;
        }

        _settingsService.Save(settings);
        _startupService.SetEnabled(settings.StartWithWindows);
    }
}
