namespace LightweightAmdGpuFanControl.Gpu;

public interface IFanControlBackend : IDisposable
{
    string BackendName { get; }
    string AdapterName { get; }

    bool Initialize();
    double? GetTemperatureC();
    FanTelemetry GetFanTelemetry();
    void DisableZeroRpm();
    void SetFanPercent(int percent);
}
