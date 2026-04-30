using ADLXWrapper;
using AdlxSdk = ADLXWrapper.ADLXWrapper;

namespace LightweightAmdGpuFanControl.Adlx;

/// <summary>
/// Manages ADLX lifecycle (init/terminate) and provides access to system services.
/// </summary>
public sealed class AdlxInitializer : IDisposable
{
    private AdlxSdk? _wrapper;
    private bool _disposed;

    public bool IsInitialized => _wrapper != null;

    /// <summary>
    /// Initialize ADLX. Call before using GpuMonitor or FanController.
    /// </summary>
    /// <returns>True if successful.</returns>
    public bool Initialize()
    {
        if (_wrapper != null)
            return true;

        try
        {
            _wrapper = new AdlxSdk();
            return true;
        }
        catch (Exception)
        {
            _wrapper = null;
            return false;
        }
    }

    public SystemServices GetSystemServices()
    {
        if (_wrapper == null)
            throw new InvalidOperationException("ADLX not initialized. Call Initialize() first.");
        return _wrapper.GetSystemServices();
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            _wrapper?.Dispose();
        }
        finally
        {
            _wrapper = null;
            _disposed = true;
        }
    }
}
