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
        var action = LaunchDecision.Decide(args);
        if (action == LaunchAction.Stop)
            return StopCommand.Run(new LogService());
        if (action == LaunchAction.Unsupported)
        {
            // Options written for newer builds: exit at once, with no mutex, tray or dialog.
            try { new LogService().Log("Unsupported arguments for 1.0.x: " + string.Join(" ", args) + "; exit 3"); } catch { }
            return ExitCodes.NothingToDo;
        }

        _instanceMutex = new Mutex(true, "Local\\LightweightAmdGpuFanControl.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Lightweight AMD GPU Fan Control is already running.", "Already running",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.Run(new SystrayApplicationContext());
        return 0;
    }
}
