using System.Threading;
using LightweightAmdGpuFanControl.Cli;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// <c>--stop</c>: asks a running instance to exit gracefully and waits for it. It first confirms that an
/// instance actually holds the single-instance mutex, then only opens the stop event that instance created
/// (see <see cref="StopSignal"/>); it never creates the event and never ends a process. On a timeout it logs
/// and returns 1: forcing the exit is the caller's decision.
/// </summary>
internal static class StopCommand
{
    public static int Run(LogService log)
    {
        try
        {
            // Not running: nothing holds the mutex. A stop event that exists anyway is not ours to trust.
            if (SingleInstance.TryAcquire(out var probe, 0))
            {
                SingleInstance.Release(probe);
                log.Log("stop: not running");
                return StopOutcome.ExitCode(instanceRunning: false, eventExisted: false, exitedInTime: false);
            }

            if (!EventWaitHandle.TryOpenExisting(StopSignal.EventName, out var ev))
            {
                log.Log("stop: a running instance has no stop listener (older build)");
                return StopOutcome.ExitCode(instanceRunning: true, eventExisted: false, exitedInTime: false);
            }

            using (ev)
            {
                ev.Set();
                bool exited = SingleInstance.TryAcquire(out var m, 20000);
                SingleInstance.Release(m);
                log.Log(exited ? "stop: stopped" : "stop: timed out waiting for exit");
                return StopOutcome.ExitCode(instanceRunning: true, eventExisted: true, exitedInTime: exited);
            }
        }
        catch (Exception ex)
        {
            try { log.Log("stop: unexpected error", ex); } catch { }
            return ExitCodes.Error;
        }
    }
}
