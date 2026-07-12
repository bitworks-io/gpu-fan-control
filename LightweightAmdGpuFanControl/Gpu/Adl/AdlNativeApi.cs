using System.Runtime.InteropServices;

namespace LightweightAmdGpuFanControl.Gpu.Adl;

/// <summary>
/// net48 has no System.Runtime.InteropServices.NativeLibrary (added in .NET Core 3.0). Minimal
/// LoadLibrary/GetProcAddress/FreeLibrary shim covering the three members AdlNativeApi needs.
/// </summary>
internal static class NativeLibrary
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);

    public static bool TryLoad(string libraryPath, out IntPtr handle)
    {
        handle = LoadLibrary(libraryPath);
        return handle != IntPtr.Zero;
    }

    public static bool TryGetExport(IntPtr handle, string name, out IntPtr address)
    {
        address = GetProcAddress(handle, name);
        return address != IntPtr.Zero;
    }

    public static void Free(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            FreeLibrary(handle);
    }
}

internal sealed class AdlNativeApi : IDisposable
{
    public const int Ok = 0;
    public const int FanSpeedTypePercent = 1;
    public const int FanSpeedTypeRpm = 2;
    public const int FanFlagUserDefinedSpeed = 1;

    private readonly IntPtr _library;
    private readonly AdlMainMemoryAlloc _memoryAlloc = Marshal.AllocHGlobal;
    private IntPtr _context;

    private AdlNativeApi(IntPtr library)
    {
        _library = library;
        MainControlCreate = GetDelegate<ADL2_Main_Control_Create>();
        MainControlDestroy = GetDelegate<ADL2_Main_Control_Destroy>();
        AdapterNumberOfAdaptersGet = GetDelegate<ADL2_Adapter_NumberOfAdapters_Get>();
        OverdriveCaps = GetDelegate<ADL2_Overdrive_Caps>();
        OverdriveNTemperatureGet = GetDelegate<ADL2_OverdriveN_Temperature_Get>();
        OverdriveNFanControlGet = GetDelegate<ADL2_OverdriveN_FanControl_Get>();
        OverdriveNFanControlSet = GetDelegate<ADL2_OverdriveN_FanControl_Set>();
        OverdriveNZeroRpmFanGet = GetDelegate<ADL2_OverdriveN_ZeroRPMFan_Get>(required: false);
        OverdriveNZeroRpmFanSet = GetDelegate<ADL2_OverdriveN_ZeroRPMFan_Set>(required: false);
        CustomFanCaps = GetDelegate<ADL2_CustomFan_Caps>(required: false);
        CustomFanGet = GetDelegate<ADL2_CustomFan_Get>(required: false);
        CustomFanSet = GetDelegate<ADL2_CustomFan_Set>(required: false);
        Overdrive5TemperatureGet = GetDelegate<ADL2_Overdrive5_Temperature_Get>(required: false);
        Overdrive5FanSpeedGet = GetDelegate<ADL2_Overdrive5_FanSpeed_Get>(required: false);
        Overdrive5FanSpeedSet = GetDelegate<ADL2_Overdrive5_FanSpeed_Set>(required: false);
        Overdrive5FanSpeedToDefault = GetDelegate<ADL2_Overdrive5_FanSpeedToDefault_Set>(required: false);
    }

    public IntPtr Context => _context;

    private ADL2_Main_Control_Create MainControlCreate { get; }
    private ADL2_Main_Control_Destroy MainControlDestroy { get; }
    public ADL2_Adapter_NumberOfAdapters_Get AdapterNumberOfAdaptersGet { get; }
    public ADL2_Overdrive_Caps OverdriveCaps { get; }
    public ADL2_OverdriveN_Temperature_Get OverdriveNTemperatureGet { get; }
    public ADL2_OverdriveN_FanControl_Get OverdriveNFanControlGet { get; }
    public ADL2_OverdriveN_FanControl_Set OverdriveNFanControlSet { get; }
    public ADL2_OverdriveN_ZeroRPMFan_Get? OverdriveNZeroRpmFanGet { get; }
    public ADL2_OverdriveN_ZeroRPMFan_Set? OverdriveNZeroRpmFanSet { get; }
    public ADL2_CustomFan_Caps? CustomFanCaps { get; }
    public ADL2_CustomFan_Get? CustomFanGet { get; }
    public ADL2_CustomFan_Set? CustomFanSet { get; }
    public ADL2_Overdrive5_Temperature_Get? Overdrive5TemperatureGet { get; }
    public ADL2_Overdrive5_FanSpeed_Get? Overdrive5FanSpeedGet { get; }
    public ADL2_Overdrive5_FanSpeed_Set? Overdrive5FanSpeedSet { get; }
    public ADL2_Overdrive5_FanSpeedToDefault_Set? Overdrive5FanSpeedToDefault { get; }

    public static bool TryLoad(out AdlNativeApi? api, out string error)
    {
        api = null;
        foreach (var dll in new[] { "atiadlxx.dll", "atiadlxy.dll" })
        {
            if (!NativeLibrary.TryLoad(dll, out var library))
                continue;

            try
            {
                api = new AdlNativeApi(library);
                var result = api.MainControlCreate(api._memoryAlloc, 1, out api._context);
                if (result == Ok)
                {
                    error = string.Empty;
                    return true;
                }

                api.Dispose();
                error = $"ADL initialization failed with code {result}.";
                return false;
            }
            catch (Exception ex)
            {
                NativeLibrary.Free(library);
                error = ex.Message;
            }
        }

        error = "Could not load atiadlxx.dll or atiadlxy.dll.";
        return false;
    }

    private T? GetDelegate<T>(bool required = true) where T : Delegate
    {
        var name = typeof(T).Name;
        if (!NativeLibrary.TryGetExport(_library, name, out var proc))
        {
            if (required)
                throw new MissingMethodException($"ADL export {name} was not found.");
            return null;
        }

        return Marshal.GetDelegateForFunctionPointer<T>(proc);
    }

    public void Dispose()
    {
        if (_context != IntPtr.Zero)
        {
            MainControlDestroy(_context);
            _context = IntPtr.Zero;
        }

        if (_library != IntPtr.Zero)
            NativeLibrary.Free(_library);
    }

    internal delegate IntPtr AdlMainMemoryAlloc(int size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_Main_Control_Create(AdlMainMemoryAlloc callback, int enumConnectedAdapters, out IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_Main_Control_Destroy(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int numberOfAdapters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_Overdrive_Caps(IntPtr context, int adapterIndex, out int supported, out int enabled, out int version);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_OverdriveN_Temperature_Get(IntPtr context, int adapterIndex, int temperatureType, out int temperature);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_OverdriveN_FanControl_Get(IntPtr context, int adapterIndex, ref ADLODNFanControl fanControl);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_OverdriveN_FanControl_Set(IntPtr context, int adapterIndex, ref ADLODNFanControl fanControl);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_OverdriveN_ZeroRPMFan_Get(IntPtr context, int adapterIndex, out int support, out int currentValue, out int defaultValue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_OverdriveN_ZeroRPMFan_Set(IntPtr context, int adapterIndex, int currentValue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_CustomFan_Caps(IntPtr context, int adapterIndex, out int supported);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_CustomFan_Get(IntPtr context, int adapterIndex, ref ADLODNFanControl fanControl);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_CustomFan_Set(IntPtr context, int adapterIndex, ref ADLODNFanControl fanControl);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_Overdrive5_Temperature_Get(IntPtr context, int adapterIndex, int thermalControllerIndex, ref ADLTemperature temperature);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_Overdrive5_FanSpeed_Get(IntPtr context, int adapterIndex, int thermalControllerIndex, ref ADLFanSpeedValue fanSpeed);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_Overdrive5_FanSpeed_Set(IntPtr context, int adapterIndex, int thermalControllerIndex, ref ADLFanSpeedValue fanSpeed);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ADL2_Overdrive5_FanSpeedToDefault_Set(IntPtr context, int adapterIndex, int thermalControllerIndex);
}

[StructLayout(LayoutKind.Sequential)]
internal struct ADLODNFanControl
{
    public int iMode;
    public int iFanControlMode;
    public int iCurrentFanSpeedMode;
    public int iCurrentFanSpeed;
    public int iTargetFanSpeed;
    public int iTargetTemperature;
    public int iMinPerformanceClock;
    public int iMinFanLimit;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ADLTemperature
{
    public int iSize;
    public int iTemperature;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ADLFanSpeedValue
{
    public int iSize;
    public int iSpeedType;
    public int iFanSpeed;
    public int iFlags;
}
