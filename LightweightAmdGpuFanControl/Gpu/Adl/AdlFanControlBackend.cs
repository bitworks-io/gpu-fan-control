using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl.Gpu.Adl;

/// <summary>
/// Legacy ADL backend for Polaris-era and older Radeon GPUs where ADLX fan tuning is unavailable.
/// </summary>
public sealed class AdlFanControlBackend : IFanControlBackend
{
    private const int MinFanPercent = 20;
    private const int MaxFanPercent = 85;
    private const int ManualMode = 1;
    private const int AutomaticMode = 0; // ODNControlType_Default
    private const int TemperatureTypeEdge = 1;
    private const int ThermalControllerIndex = 0;

    private readonly LogService _logService;
    private AdlNativeApi? _api;
    private int _adapterIndex = -1;
    private int _overdriveVersion;
    private bool _useCustomFan;

    public AdlFanControlBackend(LogService logService)
    {
        _logService = logService;
    }

    public string BackendName => _overdriveVersion == 5 ? "ADL Overdrive5" : "ADL OverdriveN";
    public string AdapterName => _adapterIndex >= 0 ? $"AMD ADL adapter {_adapterIndex}" : "AMD ADL adapter";
    public string GpuId => $"ADL:{_adapterIndex}";

    public bool Initialize()
    {
        if (!AdlNativeApi.TryLoad(out _api, out var error) || _api == null)
        {
            _logService.Log($"ADL backend unavailable: {error}");
            return false;
        }

        if (_api.AdapterNumberOfAdaptersGet(_api.Context, out var adapterCount) != AdlNativeApi.Ok || adapterCount <= 0)
        {
            _logService.Log("ADL backend found no adapters.");
            Dispose();
            return false;
        }

        for (var adapterIndex = 0; adapterIndex < adapterCount; adapterIndex++)
        {
            if (_api.OverdriveCaps(_api.Context, adapterIndex, out var supported, out _, out var version) != AdlNativeApi.Ok || supported == 0)
                continue;

            if (version >= 7 && IsOverdriveNFanSupported(adapterIndex))
            {
                _adapterIndex = adapterIndex;
                _overdriveVersion = version;
                _logService.Log($"Selected ADL OverdriveN backend for adapter {adapterIndex} (Overdrive {version}).");
                return true;
            }

            if (version == 5 && _api.Overdrive5FanSpeedGet != null && _api.Overdrive5FanSpeedSet != null)
            {
                _adapterIndex = adapterIndex;
                _overdriveVersion = version;
                _logService.Log($"Selected ADL Overdrive5 backend for adapter {adapterIndex}.");
                return true;
            }
        }

        _logService.Log("ADL backend found adapters but no supported fan-control interface.");
        Dispose();
        return false;
    }

    public double? GetTemperatureC()
    {
        if (_api == null || _adapterIndex < 0)
            return null;

        try
        {
            if (_overdriveVersion == 5 && _api.Overdrive5TemperatureGet != null)
            {
                var temp = new ADLTemperature { iSize = System.Runtime.InteropServices.Marshal.SizeOf<ADLTemperature>() };
                return _api.Overdrive5TemperatureGet(_api.Context, _adapterIndex, ThermalControllerIndex, ref temp) == AdlNativeApi.Ok
                    ? temp.iTemperature / 1000.0
                    : null;
            }

            return _api.OverdriveNTemperatureGet(_api.Context, _adapterIndex, TemperatureTypeEdge, out var temperature) == AdlNativeApi.Ok
                ? temperature / 1000.0
                : null;
        }
        catch (Exception ex)
        {
            _logService.Log("ADL temperature read failed.", ex);
            return null;
        }
    }

    public FanTelemetry GetFanTelemetry()
    {
        if (_api == null || _adapterIndex < 0)
            return new FanTelemetry(null, null, null);

        try
        {
            if (_overdriveVersion == 5 && _api.Overdrive5FanSpeedGet != null)
                return GetOverdrive5FanTelemetry();

            var fan = new ADLODNFanControl();
            var result = _useCustomFan && _api.CustomFanGet != null
                ? _api.CustomFanGet(_api.Context, _adapterIndex, ref fan)
                : _api.OverdriveNFanControlGet(_api.Context, _adapterIndex, ref fan);

            if (result != AdlNativeApi.Ok)
                return new FanTelemetry(null, null, null);

            var current = fan.iCurrentFanSpeed;
            int? target = fan.iTargetFanSpeed is >= 0 and <= 100 ? fan.iTargetFanSpeed : null;
            if (current is >= 0 and <= 100)
                return new FanTelemetry(target, current, null);

            return new FanTelemetry(target, null, current > 100 ? current : null);
        }
        catch (Exception ex)
        {
            _logService.Log("ADL fan telemetry read failed.", ex);
            return new FanTelemetry(null, null, null);
        }
    }

    public void DisableZeroRpm()
    {
        if (_api?.OverdriveNZeroRpmFanGet == null || _api.OverdriveNZeroRpmFanSet == null || _adapterIndex < 0)
            return;

        try
        {
            if (_api.OverdriveNZeroRpmFanGet(_api.Context, _adapterIndex, out var supported, out var current, out _) == AdlNativeApi.Ok &&
                supported != 0 &&
                current != 0)
            {
                _api.OverdriveNZeroRpmFanSet(_api.Context, _adapterIndex, 0);
            }
        }
        catch (Exception ex)
        {
            _logService.Log("ADL Zero RPM disable failed.", ex);
        }
    }

    public void SetFanPercent(int percent)
    {
        if (_api == null || _adapterIndex < 0)
            throw new InvalidOperationException("ADL backend is not initialized.");

        percent = Math.Clamp(percent, MinFanPercent, MaxFanPercent);

        if (_overdriveVersion == 5)
        {
            SetOverdrive5FanPercent(percent);
            return;
        }

        var fan = new ADLODNFanControl();
        var getResult = _useCustomFan && _api.CustomFanGet != null
            ? _api.CustomFanGet(_api.Context, _adapterIndex, ref fan)
            : _api.OverdriveNFanControlGet(_api.Context, _adapterIndex, ref fan);

        if (getResult != AdlNativeApi.Ok)
            throw new InvalidOperationException($"ADL fan-control read failed with code {getResult}.");

        fan.iMode = ManualMode;
        fan.iFanControlMode = ManualMode;
        fan.iCurrentFanSpeedMode = AdlNativeApi.FanSpeedTypePercent;
        fan.iTargetFanSpeed = percent;
        fan.iMinFanLimit = Math.Max(fan.iMinFanLimit, MinFanPercent);

        var setResult = _useCustomFan && _api.CustomFanSet != null
            ? _api.CustomFanSet(_api.Context, _adapterIndex, ref fan)
            : _api.OverdriveNFanControlSet(_api.Context, _adapterIndex, ref fan);

        if (setResult != AdlNativeApi.Ok)
            throw new InvalidOperationException($"ADL fan-control set failed with code {setResult}.");
    }

    private bool IsOverdriveNFanSupported(int adapterIndex)
    {
        if (_api == null)
            return false;

        if (_api.CustomFanCaps != null &&
            _api.CustomFanCaps(_api.Context, adapterIndex, out var customFanSupported) == AdlNativeApi.Ok &&
            customFanSupported != 0 &&
            _api.CustomFanGet != null &&
            _api.CustomFanSet != null)
        {
            _useCustomFan = true;
            return true;
        }

        var fan = new ADLODNFanControl();
        return _api.OverdriveNFanControlGet(_api.Context, adapterIndex, ref fan) == AdlNativeApi.Ok;
    }

    private FanTelemetry GetOverdrive5FanTelemetry()
    {
        if (_api?.Overdrive5FanSpeedGet == null)
            return new FanTelemetry(null, null, null);

        var fan = new ADLFanSpeedValue
        {
            iSize = System.Runtime.InteropServices.Marshal.SizeOf<ADLFanSpeedValue>(),
            iSpeedType = AdlNativeApi.FanSpeedTypePercent
        };

        if (_api.Overdrive5FanSpeedGet(_api.Context, _adapterIndex, ThermalControllerIndex, ref fan) != AdlNativeApi.Ok)
            return new FanTelemetry(null, null, null);

        return fan.iSpeedType == AdlNativeApi.FanSpeedTypeRpm
            ? new FanTelemetry(null, null, fan.iFanSpeed)
            : new FanTelemetry(fan.iFanSpeed, fan.iFanSpeed, null);
    }

    private void SetOverdrive5FanPercent(int percent)
    {
        if (_api?.Overdrive5FanSpeedSet == null)
            throw new InvalidOperationException("ADL Overdrive5 fan set API is unavailable.");

        var fan = new ADLFanSpeedValue
        {
            iSize = System.Runtime.InteropServices.Marshal.SizeOf<ADLFanSpeedValue>(),
            iSpeedType = AdlNativeApi.FanSpeedTypePercent,
            iFanSpeed = percent,
            iFlags = AdlNativeApi.FanFlagUserDefinedSpeed
        };

        var result = _api.Overdrive5FanSpeedSet(_api.Context, _adapterIndex, ThermalControllerIndex, ref fan);
        if (result != AdlNativeApi.Ok)
            throw new InvalidOperationException($"ADL Overdrive5 fan set failed with code {result}.");
    }

    public void RestoreAutomaticFanControl()
    {
        if (_api == null || _adapterIndex < 0)
            return;

        try
        {
            if (_overdriveVersion == 5)
            {
                _api.Overdrive5FanSpeedToDefault?.Invoke(_api.Context, _adapterIndex, ThermalControllerIndex);
                return;
            }

            var fan = new ADLODNFanControl();
            var getResult = _useCustomFan && _api.CustomFanGet != null
                ? _api.CustomFanGet(_api.Context, _adapterIndex, ref fan)
                : _api.OverdriveNFanControlGet(_api.Context, _adapterIndex, ref fan);

            if (getResult != AdlNativeApi.Ok)
                return;

            fan.iMode = AutomaticMode;
            fan.iFanControlMode = AutomaticMode;

            if (_useCustomFan && _api.CustomFanSet != null)
                _api.CustomFanSet(_api.Context, _adapterIndex, ref fan);
            else
                _api.OverdriveNFanControlSet(_api.Context, _adapterIndex, ref fan);
        }
        catch (Exception ex)
        {
            _logService.Log("ADL restore-to-automatic failed.", ex);
        }
    }

    public void Dispose()
    {
        // Safety net: return the fan to the driver's automatic curve before unloading the API.
        RestoreAutomaticFanControl();
        _api?.Dispose();
        _api = null;
        _adapterIndex = -1;
        _overdriveVersion = 0;
    }
}
