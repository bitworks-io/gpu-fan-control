using System.Threading;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>The process-wide single-instance mutex shared by the tray app and --stop.</summary>
internal static class SingleInstance
{
    private const string Name = "Local\\LightweightAmdGpuFanControl.SingleInstance";

    /// <summary>
    /// Tries to own the mutex, waiting up to <paramref name="waitMs"/>. An abandoned mutex (the previous
    /// owner was killed) counts as acquired: we now own it.
    /// </summary>
    public static bool TryAcquire(out Mutex? mutex, int waitMs = 0)
    {
        var m = new Mutex(false, Name);
        bool got;
        try
        {
            got = m.WaitOne(waitMs);
        }
        catch (AbandonedMutexException)
        {
            got = true;
        }

        if (!got)
        {
            m.Dispose();
            mutex = null;
            return false;
        }

        mutex = m;
        return true;
    }

    /// <summary>Releases and disposes the mutex. Never throws.</summary>
    public static void Release(Mutex? mutex)
    {
        if (mutex == null) return;
        try { mutex.ReleaseMutex(); } catch { }
        try { mutex.Dispose(); } catch { }
    }
}
