using System.Reflection;

namespace LightweightAmdGpuFanControl.Forms;

/// <summary>
/// About dialog. Surfaces the publisher, version, and a prominent link to the Bitworks
/// contact form so users know how to send feedback or request features.
/// </summary>
public sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About Lightweight AMD GPU Fan Control";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(430, 270);

        // --- App title ---
        var titleLabel = new Label
        {
            Text = "Lightweight AMD GPU Fan Control",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 11f, FontStyle.Bold),
            Location = new Point(20, 20),
            AutoSize = true
        };

        // --- Version ---
        var versionText = GetVersion();
        var versionLabel = new Label
        {
            Text = $"Version {versionText}",
            Location = new Point(20, 50),
            AutoSize = true
        };

        // --- Publisher ---
        var publisherLabel = new Label
        {
            Text = "by Bitworks",
            ForeColor = SystemColors.GrayText,
            Location = new Point(20, 70),
            AutoSize = true
        };

        // --- Feedback blurb ---
        var blurbLabel = new Label
        {
            Text = "Your feedback shapes this tool. Found a bug, or want a feature?\nWe'd genuinely love to hear from you.",
            Size = new Size(385, 46),
            Location = new Point(20, 102)
        };

        // --- Feedback button ---
        var feedbackButton = new Button
        {
            Text = "Send feedback / request a feature",
            Location = new Point(20, 158),
            Width = 240,
            Height = 30
        };
        feedbackButton.Click += (_, _) => AppLinks.Open(AppLinks.ContactFormUrl);

        // --- Website link ---
        var websiteLink = new LinkLabel
        {
            Text = "bitworks.io",
            Location = new Point(272, 166),
            AutoSize = true
        };
        websiteLink.LinkClicked += (_, _) => AppLinks.Open(AppLinks.WebsiteUrl);

        // --- OK button ---
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(330, 200),
            Width = 75
        };

        Controls.AddRange(new System.Windows.Forms.Control[]
        {
            titleLabel, versionLabel, publisherLabel, blurbLabel,
            feedbackButton, websiteLink, okButton
        });

        AcceptButton = okButton;
    }

    private static string GetVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v == null) return "1.0.0";
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
