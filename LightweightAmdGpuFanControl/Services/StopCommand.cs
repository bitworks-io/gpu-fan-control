using System.Threading;
using LightweightAmdGpuFanControl.Cli;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// <c>--stop</c>: asks a running instance to exit gracefully and waits for it. It only opens the stop event the
/// running app created (see <see cref="StopSignal"/>); it never creates it and never ends a process. On a
/// timeout it logs and returns 1: forcing the exit is the caller's decision.
/// </summary>
internal static class StopCommand
{
    public static int Run(LogService log)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(StopSignal.EventName, out var ev))
            {
                bool free = SingleInstance.TryAcquire(out var m, 0);
                SingleInstance.Release(m);
                int code = StopOutcome.ExitCode(false, free, false);
                log.Log(free ? "stop: not running" : "stop: a running instance has no stop listener (older build)");
                return code;
            }

            using (ev)
            {
                ev.Set();
                bool exited = SingleInstance.TryAcquire(out var m, 20000);
                SingleInstance.Release(m);
                log.Log(exited ? "stop: stopped" : "stop: timed out waiting for exit");
                return StopOutcome.ExitCode(true, false, exited);
            }
        }
        catch (Exception ex)
        {
            try { log.Log("stop: unexpected error", ex); } catch { }
            return ExitCodes.Error;
        }
    }
}
