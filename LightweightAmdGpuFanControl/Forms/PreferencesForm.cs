using LightweightAmdGpuFanControl.Models;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl.Forms;

public class PreferencesForm : Form
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private NumericUpDown _targetTempControl = null!;
    private CheckBox _startWithWindowsCheck = null!;

    public PreferencesForm(SettingsService settingsService, StartupService startupService)
    {
        _settingsService = settingsService;
        _startupService = startupService;

        Text = "Lightweight AMD GPU Fan Control - Preferences";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(360, 180);
        ShowInTaskbar = false;

        var settings = _settingsService.Load();

        var targetLabel = new Label
        {
            Text = "Target GPU temperature (°C):",
            Location = new Point(20, 24),
            AutoSize = true
        };

        _targetTempControl = new NumericUpDown
        {
            Minimum = AppSettings.MinTargetTempC,
            Maximum = AppSettings.MaxTargetTempC,
            Value = settings.TargetTempC,
            Location = new Point(220, 20),
            Width = 80
        };

        _startWithWindowsCheck = new CheckBox
        {
            Text = "Start with Windows",
            Checked = settings.StartWithWindows,
            Location = new Point(20, 60),
            AutoSize = true
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(140, 100),
            Width = 80
        };
        okButton.Click += OkButton_Click;

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(230, 100),
            Width = 80
        };

        Controls.Add(targetLabel);
        Controls.Add(_targetTempControl);
        Controls.Add(_startWithWindowsCheck);
        Controls.Add(okButton);
        Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        // Load-modify-save so we preserve fields this form doesn't expose (min/max, GPU list, mode).
        var settings = _settingsService.Load();
        settings.TargetTempC = (int)_targetTempControl.Value;
        settings.StartWithWindows = _startWithWindowsCheck.Checked;
        _settingsService.Save(settings);
        _startupService.SetEnabled(settings.StartWithWindows);
    }
}
