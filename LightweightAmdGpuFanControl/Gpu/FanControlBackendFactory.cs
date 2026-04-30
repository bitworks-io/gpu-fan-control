using LightweightAmdGpuFanControl.Gpu.Adl;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl.Gpu;

public static class FanControlBackendFactory
{
    public static IFanControlBackend? Create(LogService logService)
    {
        var backends = new IFanControlBackend[]
        {
            new AdlxFanControlBackend(logService),
            new AdlFanControlBackend(logService)
        };

        foreach (var backend in backends)
        {
            if (backend.Initialize())
                return backend;

            backend.Dispose();
        }

        return null;
    }
}
