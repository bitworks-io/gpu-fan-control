using System.Reflection;

namespace LightweightAmdGpuFanControl.Forms;

/// <summary>
/// About dialog. Surfaces the publisher, version, and a prominent link to the Bitworks
/// contact form so users know how to send feedback or request features.
/// </summary>
public sealed class AboutForm : Form
{
    private TableLayoutPanel _table = null!;

    public AboutForm()
    {
        Text = "About Lightweight AMD GPU Fan Control";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(16);
        // NOTE: form size is set explicitly in OnShown from the table's actual (laid-out,
        // DPI-scaled) bounds — see PreferencesForm for why a fixed Size clips at high DPI.

        var table = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Location = new Point(Padding.Left, Padding.Top)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int row = 0;

        // --- App title ---
        var titleLabel = new Label
        {
            Text = "Lightweight AMD GPU Fan Control",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 11f, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 6)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(titleLabel, 0, row);
        row++;

        // --- Version ---
        var versionText = GetVersion();
        var versionLabel = new Label
        {
            Text = $"Version {versionText}",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 3)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(versionLabel, 0, row);
        row++;

        // --- Publisher ---
        var publisherLabel = new Label
        {
            Text = "by Bitworks",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 9)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(publisherLabel, 0, row);
        row++;

        // --- Feedback blurb ---
        var blurbLabel = new Label
        {
            Text = "Your feedback shapes this tool. Found a bug, or want a feature?\nWe'd genuinely love to hear from you.",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 3, 3, 9)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(blurbLabel, 0, row);
        row++;

        // --- Feedback button + website link ---
        var feedbackButton = new Button
        {
            Text = "Send feedback / request a feature",
            AutoSize = true,
            Margin = new Padding(3, 3, 12, 3)
        };
        feedbackButton.Click += (_, _) => AppLinks.Open(AppLinks.ContactFormUrl);

        var websiteLink = new LinkLabel
        {
            Text = "bitworks.io",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 3, 3)
        };
        websiteLink.LinkClicked += (_, _) => AppLinks.Open(AppLinks.WebsiteUrl);

        var feedbackFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, 12)
        };
        feedbackFlow.Controls.Add(feedbackButton);
        feedbackFlow.Controls.Add(websiteLink);

        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(feedbackFlow, 0, row);
        row++;

        // --- OK button ---
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(okButton, 0, row);

        _table = table;
        Controls.Add(table);

        AcceptButton = okButton;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ClientSize = new Size(_table.Right + Padding.Right, _table.Bottom + Padding.Bottom);
        CenterToScreen();
    }

    private static string GetVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v == null) return "1.0.0";
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
