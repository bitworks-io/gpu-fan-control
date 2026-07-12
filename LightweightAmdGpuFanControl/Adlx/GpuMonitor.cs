using ADLXWrapper;

namespace LightweightAmdGpuFanControl.Adlx;

/// <summary>
/// Reads GPU temperature and fan metrics via ADLX.
/// </summary>
public sealed class GpuMonitor
{
    private readonly SystemServices _systemServices;
    private PerformanceMonitor? _performanceMonitor;
    private GPU? _gpu;

    public GpuMonitor(SystemServices systemServices)
    {
        _systemServices = systemServices;
    }

    /// <summary>
    /// Gets the first AMD GPU from the system.
    /// </summary>
    public GPU? GetPrimaryGpu()
    {
        var gpus = _systemServices.GetGPUs();
        return gpus.Count > 0 ? gpus[0] : null;
    }

    /// <summary>
    /// Gets all AMD GPUs reported by ADLX.
    /// </summary>
    public IReadOnlyList<GPU> GetAllGpus()
    {
        var gpus = _systemServices.GetGPUs();
        var result = new List<GPU>(gpus.Count);
        for (int i = 0; i < gpus.Count; i++)
            result.Add(gpus[i]);
        return result;
    }

    /// <summary>
    /// Gets the GPU at the specified index, or null if the index is out of range.
    /// </summary>
    public GPU? GetGpuAtIndex(int index)
    {
        var gpus = _systemServices.GetGPUs();
        return index >= 0 && index < gpus.Count ? gpus[index] : null;
    }

    /// <summary>Returns the number of AMD GPUs visible to ADLX.</summary>
    public int GetGpuCount() => _systemServices.GetGPUs().Count;

    /// <summary>
    /// Gets the current GPU temperature in Celsius.
    /// </summary>
    /// <param name="gpu">The GPU to query. Use GetPrimaryGpu() if null.</param>
    /// <returns>Temperature in °C, or null if unavailable.</returns>
    public double? GetGpuTemperature(GPU? gpu = null)
    {
        gpu ??= GetPrimaryGpu();
        if (gpu == null) return null;

        try
        {
            _performanceMonitor ??= _systemServices.GetPerformanceMonitor();
            var metrics = _performanceMonitor.GetGPUMetricsStruct(gpu);
            return metrics.GPUTemperature;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the current fan speed (percentage or RPM depending on GPU).
    /// </summary>
    public int? GetFanSpeed(GPU? gpu = null)
    {
        gpu ??= GetPrimaryGpu();
        if (gpu == null) return null;

        try
        {
            _performanceMonitor ??= _systemServices.GetPerformanceMonitor();
            var metrics = _performanceMonitor.GetGPUMetricsStruct(gpu);
            return metrics.GPUFanSpeed;
        }
        catch
        {
            return null;
        }
    }
}
