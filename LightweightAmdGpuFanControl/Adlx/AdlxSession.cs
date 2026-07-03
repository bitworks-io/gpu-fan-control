using ADLXWrapper;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl.Adlx;

/// <summary>
/// Owns a single ADLX session (init + system services + a shared monitor) that per-GPU
/// fan-control channels borrow. Exactly one of these exists while the app is controlling fans;
/// the channels never tear it down. Disposing the session terminates ADLX.
/// </summary>
public sealed class AdlxSession : IDisposable
{
    private readonly AdlxInitializer _initializer;
    private bool _disposed;

    private AdlxSession(AdlxInitializer initializer, SystemServices systemServices)
    {
        _initializer = initializer;
        SystemServices = systemServices;
        Monitor = new GpuMonitor(systemServices);
    }

    /// <summary>Shared ADLX system services, used to build per-GPU fan controllers.</summary>
    public SystemServices SystemServices { get; }

    /// <summary>Shared monitor for temperature/fan reads (device-addressed by GPU handle).</summary>
    public GpuMonitor Monitor { get; }

    /// <summary>Number of AMD GPUs visible to this ADLX session.</summary>
    public int GpuCount => Monitor.GetGpuCount();

    /// <summary>
    /// Attempts to initialize ADLX and open a session. Returns null when ADLX is unavailable
    /// (no driver, unsupported GPU, or the native binding failed to load).
    /// </summary>
    public static AdlxSession? TryCreate(LogService logService)
    {
        var initializer = new AdlxInitializer();
        if (!initializer.Initialize())
        {
            initializer.Dispose();
            return null;
        }

        try
        {
            var systemServices = initializer.GetSystemServices();
            return new AdlxSession(initializer, systemServices);
        }
        catch (Exception ex)
        {
            logService.Log("ADLX session initialization failed.", ex);
            initializer.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initializer.Dispose();
    }
}
