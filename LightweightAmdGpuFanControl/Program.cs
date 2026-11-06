using System.Threading;
using LightweightAmdGpuFanControl.Cli;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl;

static class Program
{
    // Kept alive for the process lifetime so the named mutex isn't released early.
    private static Mutex? _instanceMutex;

    [STAThread]
    static int Main(string[] args)
    {
        var log = new LogService();
        var options = LaunchOptions.Parse(args);

        if (options.Ignored.Count > 0)
            log.Log("Ignoring unknown arguments: " + string.Join(" ", options.Ignored));
        if (options.StatusFileError != null)
            log.Log($"--status-file: {options.StatusFileError}; using the default path");

        if (options.Error != null)
        {
            log.Log("Usage error: " + options.Error);
            return ExitCodes.Usage;
        }

        if (options.Mode == LaunchMode.Probe)
            return CapabilityProbe.Run(log, options);

        if (options.Mode == LaunchMode.RestoreAuto)
            return RestoreAutoRunner.Run(log);

        if (options.Mode == LaunchMode.Stop)
            return StopCommand.Run(log);

        // Automation waits for a previous instance to finish exiting; interactive stays instant, as in v1.0.
        if (!SingleInstance.TryAcquire(out _instanceMutex, options.Automation ? 15000 : 0))
        {
            if (options.Automation)
            {
                log.Log("Another instance is running; exiting (automation).");
                return ExitCodes.AlreadyRunning;
            }

            MessageBox.Show("Lightweight AMD GPU Fan Control is already running.", "Already running",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        AutomationStatusWriter? status = null;
        if (options.Automation)
        {
            status = new AutomationStatusWriter(AutomationStatusWriter.ResolvePath(options, log), log);
            status.Starting();
        }

        var context = new SystrayApplicationContext(log, options, status);
        if (context.ExitCode.HasValue)
        {
            // Automation with no backend: nothing to run, so skip the message loop.
            context.Dispose();
            return context.ExitCode.Value;
        }

        Application.Run(context);
        return context.ExitCode ?? 0;
    }
}
