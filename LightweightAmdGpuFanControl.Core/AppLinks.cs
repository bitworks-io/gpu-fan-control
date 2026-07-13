using System.Diagnostics;

namespace LightweightAmdGpuFanControl;

/// <summary>
/// Central place for the app's outbound links and a safe helper to open them in the default browser.
/// Encourages user feedback: the contact form is surfaced from both Preferences and About.
/// </summary>
public static class AppLinks
{
    /// <summary>Bitworks contact form — used for feedback and feature requests.</summary>
    public const string ContactFormUrl = "https://bitworks.io/contact-us/";

    /// <summary>Bitworks website.</summary>
    public const string WebsiteUrl = "https://bitworks.io";

    /// <summary>GitHub Issues — where users report bugs / problems.</summary>
    public const string IssuesUrl = "https://github.com/bitworks-io/gpu-fan-control/issues";

    /// <summary>Fan-control troubleshooting help — the project README's troubleshooting section.</summary>
    public const string FanHelpUrl = "https://github.com/bitworks-io/gpu-fan-control#troubleshooting-fan-control-not-working";

    /// <summary>Opens a URL in the user's default browser. Best-effort; never throws.</summary>
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Best-effort: nothing actionable if the shell cannot launch a browser.
        }
    }
}
