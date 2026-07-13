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

    private RadioButton _autoModeRadio = null!;
    private RadioButton _manualModeRadio = null!;
    private NumericUpDown _manualFanControl = null!;
    private NumericUpDown _targetTempControl = null!;
    private NumericUpDown _minFanControl = null!;
    private NumericUpDown _maxFanControl = null!;
    private CheckBox _startWithWindowsCheck = null!;
    private CheckedListBox _gpuList = null!;
    private Label _statusLabel = null!;
    private System.Windows.Forms.Timer _statusTimer = null!;
    private TableLayoutPanel _table = null!;

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
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(12);
        // NOTE: form size is set explicitly in OnShown from the table's actual (laid-out, DPI-scaled)
        // bounds. The table is un-docked (AutoSize) rather than Dock=Fill, because a docked control's
        // PreferredSize reflects its docked size, not its content size — that previously clipped the
        // button row.

        var settings = _settingsService.Load();

        // ── Root layout ──────────────────────────────────────────────────────
        // Two columns (labels | controls); every row auto-sizes to its content
        // so the dialog grows/shrinks to fit under any DPI scale factor.
        var table = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Location = new Point(Padding.Left, Padding.Top)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int row = 0;

        // ── Fan control mode ─────────────────────────────────────────────────
        // Mirrors the tray's Automatic / Manual choice so the current state is visible and
        // editable here. The two radios group within their own FlowLayoutPanel.
        var modeLabel = new Label
        {
            Text = "Fan control mode:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 12, 3)
        };
        _autoModeRadio = new RadioButton
        {
            Text = "Automatic (temperature curve)",
            AutoSize = true,
            Checked = settings.Mode != FanMode.Manual,
            Margin = new Padding(3, 0, 3, 0)
        };
        _manualModeRadio = new RadioButton
        {
            Text = "Manual (fixed speed)",
            AutoSize = true,
            Checked = settings.Mode == FanMode.Manual,
            Margin = new Padding(3, 0, 3, 0)
        };
        var modePanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(3, 3, 3, 3)
        };
        modePanel.Controls.Add(_autoModeRadio);
        modePanel.Controls.Add(_manualModeRadio);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(modeLabel, 0, row);
        table.Controls.Add(modePanel, 1, row);
        row++;

        // ── Manual fixed fan speed (applies only in Manual mode) ─────────────
        var manualFanLabel = new Label
        {
            Text = "Manual fan speed (%):",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 12, 6)
        };
        _manualFanControl = new NumericUpDown
        {
            Minimum = AppSettings.MinFanFloor,    // 20
            Maximum = AppSettings.MaxFanCeiling,  // 85
            Value = MathCompat.Clamp(settings.ManualFanPercent, AppSettings.MinFanFloor, AppSettings.MaxFanCeiling),
            Width = 70,
            Enabled = _manualModeRadio.Checked,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 3)
        };
        _manualModeRadio.CheckedChanged += (_, _) => _manualFanControl.Enabled = _manualModeRadio.Checked;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(manualFanLabel, 0, row);
        table.Controls.Add(_manualFanControl, 1, row);
        row++;

        // ── Automatic-curve settings (target / min / max) ────────────────────

        // ── Core GPU temperature target ──────────────────────────────────────
        var targetLabel = new Label
        {
            Text = "Core GPU temperature target (°C):",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 12, 6)
        };

        _targetTempControl = new NumericUpDown
        {
            Minimum = AppSettings.MinTargetTempC,
            Maximum = AppSettings.MaxTargetTempC,
            Value = settings.TargetTempC,
            Width = 70,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 3)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(targetLabel, 0, row);
        table.Controls.Add(_targetTempControl, 1, row);
        row++;

        // ── Minimum fan speed ────────────────────────────────────────────────
        var minFanLabel = new Label
        {
            Text = "Minimum fan speed (%):",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 12, 6)
        };

        _minFanControl = new NumericUpDown
        {
            Minimum = AppSettings.MinFanFloor,  // 20
            Maximum = 70,
            Value = MathCompat.Clamp(settings.MinFanPercent, AppSettings.MinFanFloor, 70),
            Width = 70,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 3)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(minFanLabel, 0, row);
        table.Controls.Add(_minFanControl, 1, row);
        row++;

        // ── Maximum fan speed ────────────────────────────────────────────────
        var maxFanLabel = new Label
        {
            Text = "Maximum fan speed (%):",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 12, 6)
        };

        _maxFanControl = new NumericUpDown
        {
            Minimum = 40,
            Maximum = AppSettings.MaxFanCeiling,  // 85
            Value = MathCompat.Clamp(settings.MaxFanPercent, 40, AppSettings.MaxFanCeiling),
            Width = 70,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 3)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(maxFanLabel, 0, row);
        table.Controls.Add(_maxFanControl, 1, row);
        row++;

        var maxFanHint = new Label
        {
            Text = "85% is the manufacturer-recommended maximum.",
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(5, 0, 3, 6)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(maxFanHint, 0, row);
        table.SetColumnSpan(maxFanHint, 2);
        row++;

        // ── Start with Windows ───────────────────────────────────────────────
        _startWithWindowsCheck = new CheckBox
        {
            Text = "Start with Windows",
            // Reflect the ACTUAL Run-key state (the installer can set it directly), not the
            // settings.json value — otherwise the box shows unchecked after an install that
            // enabled startup, and clicking OK would silently remove the installer's Run key.
            Checked = _startupService.IsEnabled,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 9)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(_startWithWindowsCheck, 0, row);
        table.SetColumnSpan(_startWithWindowsCheck, 2);
        row++;

        // ── GPU list ─────────────────────────────────────────────────────────
        var gpuLabel = new Label
        {
            Text = "Controlled GPUs:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 3)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(gpuLabel, 0, row);
        table.SetColumnSpan(gpuLabel, 2);
        row++;

        _gpuList = new CheckedListBox
        {
            Size = new Size(385, 80),
            CheckOnClick = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(3, 3, 3, 9)
        };
        PopulateGpuList(settings);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(_gpuList, 0, row);
        table.SetColumnSpan(_gpuList, 2);
        row++;

        // ── Live status ──────────────────────────────────────────────────────
        _statusLabel = new Label
        {
            AutoSize = true,
            // Reserve ~3 lines so the dialog doesn't resize as the 1s status timer flips
            // between "Starting…", per-GPU lines, and "Paused…". Wraps at 385px; grows if
            // more GPUs need more lines.
            MinimumSize = new Size(385, 52),
            MaximumSize = new Size(385, 0),
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f),
            Margin = new Padding(3, 3, 3, 9)
        };
        RefreshStatus();
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(_statusLabel, 0, row);
        table.SetColumnSpan(_statusLabel, 2);
        row++;

        // ── Feedback link ────────────────────────────────────────────────────
        var feedbackNote = new Label
        {
            Text = "Feature requests welcome.",
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 0)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(feedbackNote, 0, row);
        table.SetColumnSpan(feedbackNote, 2);
        row++;

        var feedbackLink = new LinkLabel
        {
            Text = "Send feedback / request a feature →",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 0, 3, 9)
        };
        feedbackLink.LinkClicked += (_, _) => AppLinks.Open(AppLinks.ContactFormUrl);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(feedbackLink, 0, row);
        table.SetColumnSpan(feedbackLink, 2);
        row++;

        // ── Buttons row ──────────────────────────────────────────────────────
        // About sits on the left (column 0); OK / Cancel / Apply are right-aligned (column 1).
        var aboutButton = new Button
        {
            Text = "About…",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3)
        };
        aboutButton.Click += (_, _) => new AboutForm().ShowDialog(this);

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Margin = new Padding(3)
        };
        // This form is shown modeless (Show(), not ShowDialog) so the tray keeps running and the
        // status timer stays live — which means a button's DialogResult does NOT auto-close it.
        // Close explicitly. Cancel discards (no ApplySettings).
        cancelButton.Click += (_, _) => Close();

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Margin = new Padding(3)
        };
        // OK applies the pending settings, then closes the pane. (Apply applies and stays open.)
        okButton.Click += (_, _) => { ApplySettings(); Close(); };

        var applyButton = new Button
        {
            Text = "Apply",
            DialogResult = DialogResult.None,
            AutoSize = true,
            Margin = new Padding(3)
        };
        applyButton.Click += (_, _) => ApplySettings();

        // Right-aligned button flow; controls are added right-to-left so the
        // resulting visual order reads OK  Cancel  Apply.
        var commitFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0)
        };
        commitFlow.Controls.Add(applyButton);
        commitFlow.Controls.Add(cancelButton);
        commitFlow.Controls.Add(okButton);

        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(aboutButton, 0, row);
        table.Controls.Add(commitFlow, 1, row);

        _table = table;
        Controls.Add(table);

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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Size to the fully laid-out, DPI-scaled table (un-docked, so its bounds are real), then re-center.
        ClientSize = new Size(_table.Right + Padding.Right, _table.Bottom + Padding.Bottom);
        CenterToScreen();
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

    private void ApplySettings()
    {
        var settings = _settingsService.Load();
        settings.Mode = _manualModeRadio.Checked ? FanMode.Manual : FanMode.Auto;
        settings.ManualFanPercent = (int)_manualFanControl.Value;
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
