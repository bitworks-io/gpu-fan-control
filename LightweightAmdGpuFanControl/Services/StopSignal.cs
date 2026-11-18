using System.Threading;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Tray side of <c>--stop</c>: the running app creates the named stop event and exits gracefully when it is set.
/// The <c>--stop</c> command (<see cref="StopCommand"/>) only opens this event; it never creates it.
/// </summary>
internal static class StopSignal
{
    public const string EventName = "Local\\LightweightAmdGpuFanControl.Stop";

    /// <summary>
    /// Starts listening for a stop request; <paramref name="onStop"/> runs on a thread-pool thread.
    /// Returns a handle that stops listening, or null if the listener could not be created. Never throws.
    /// </summary>
    public static IDisposable? Listen(Action onStop, LogService log)
    {
        try
        {
            var handle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            var registered = ThreadPool.RegisterWaitForSingleObject(handle, (_, _) =>
            {
                try { onStop(); }
                catch (Exception ex) { try { log.Log("stop listener: handler failed", ex); } catch { } }
            }, null, Timeout.Infinite, true);
            return new Listener(registered, handle);
        }
        catch (Exception ex)
        {
            try { log.Log("stop listener unavailable", ex); } catch { }
            return null;
        }
    }

    private sealed class Listener : IDisposable
    {
        private readonly RegisteredWaitHandle _registered;
        private readonly EventWaitHandle _handle;

        public Listener(RegisteredWaitHandle registered, EventWaitHandle handle)
        {
            _registered = registered;
            _handle = handle;
        }

        public void Dispose()
        {
            try { _registered.Unregister(null); } catch { }
            try { _handle.Dispose(); } catch { }
        }
    }
}
