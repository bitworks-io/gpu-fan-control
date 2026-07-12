using System.Threading;

namespace LightweightAmdGpuFanControl;

static class Program
{
    // Kept alive for the process lifetime so the named mutex isn't released early.
    private static Mutex? _instanceMutex;

    [STAThread]
    static void Main()
    {
        _instanceMutex = new Mutex(true, "Local\\LightweightAmdGpuFanControl.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Lightweight AMD GPU Fan Control is already running.", "Already running",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.Run(new SystrayApplicationContext());
    }
}
