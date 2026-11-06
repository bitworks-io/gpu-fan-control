using System.Threading;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>The process-wide single-instance mutex shared by the tray app, --probe and --restore-auto.</summary>
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

    private const string OneShotMarkerName = "Local\\LightweightAmdGpuFanControl.OneShot";
    private const int TrayWaitBehindOneShotMs = 30000;

    /// <summary>
    /// Advertises a short one-shot run (--probe, --restore-auto, --stop) for its lifetime, so a tray start
    /// that finds the mutex held waits for it instead of reporting "already running". Dispose to end the
    /// marker. Never throws; returns null if the marker could not be created.
    /// </summary>
    public static IDisposable? MarkOneShot()
    {
        try { return new EventWaitHandle(false, EventResetMode.ManualReset, OneShotMarkerName); }
        catch { return null; }
    }

    /// <summary>
    /// Tray-mode acquire: interactive starts stay instant (as in v1.0) and automation waits up to 15 s, unless
    /// a one-shot run holds the mutex, in which case the tray start waits up to 30 s for it to finish.
    /// </summary>
    public static bool TryAcquireForTray(out Mutex? mutex, bool automation)
    {
        if (TryAcquire(out mutex, automation ? 15000 : 0))
            return true;
        if (!OneShotActive())
            return false;
        return TryAcquire(out mutex, TrayWaitBehindOneShotMs);
    }

    private static bool OneShotActive()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(OneShotMarkerName, out var marker))
                return false;
            marker.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Releases and disposes the mutex. Never throws.</summary>
    public static void Release(Mutex? mutex)
    {
        if (mutex == null) return;
        try { mutex.ReleaseMutex(); } catch { }
        try { mutex.Dispose(); } catch { }
    }
}
