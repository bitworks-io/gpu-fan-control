using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LightweightAmdGpuFanControl.Gpu.Native;

/// <summary>
/// Every P/Invoke used to locate, verify and load the AMD driver libraries. All imports use
/// Unicode "W" entry points and resolve their own DLL from System32 only.
/// </summary>
internal static class Win32
{
    public const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x100;
    public const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x800;
    public const uint DIGCF_PRESENT = 0x2;
    public const uint DICS_FLAG_GLOBAL = 1;
    public const uint DIREG_DRV = 2;
    public const int KEY_READ = 0x20019;

    public const uint WTD_UI_NONE = 2;
    public const uint WTD_REVOKE_NONE = 0;
    public const uint WTD_CHOICE_FILE = 1;
    public const uint WTD_STATEACTION_VERIFY = 1;
    public const uint WTD_STATEACTION_CLOSE = 2;

    public static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

    /// <summary>GUID_DEVCLASS_DISPLAY {4d36e968-e325-11ce-bfc1-08002be10318}.</summary>
    public static readonly Guid DisplayClassGuid = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");

    /// <summary>WINTRUST_ACTION_GENERIC_VERIFY_V2.</summary>
    public static readonly Guid WintrustActionGenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    // kernel32

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr LoadLibraryExW(string path, IntPtr hFile, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetModuleHandleExW(uint flags, string name, out IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr GetModuleHandleW(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int GetModuleFileNameW(IntPtr module, StringBuilder buffer, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr GetProcAddress(IntPtr module, string procName);

    // setupapi

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInstanceIdW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr SetupDiOpenDevRegKey(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint scope = DICS_FLAG_GLOBAL, uint hwProfile = 0, uint keyType = DIREG_DRV, int samDesired = KEY_READ);

    [DllImport("setupapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupGetInfDriverStoreLocationW(string fileName, IntPtr alternatePlatformInfo, string? localeName, StringBuilder returnBuffer, int returnBufferSize, out int requiredSize);

    // wintrust

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public int cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public int cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);

    /// <summary>
    /// Verifies the Authenticode signature of <paramref name="path"/> without UI or revocation
    /// checks. Returns the WinVerifyTrust result (0 = trusted). Never throws.
    /// </summary>
    public static int VerifyFileTrust(string path)
    {
        IntPtr pathPtr = IntPtr.Zero, filePtr = IntPtr.Zero, dataPtr = IntPtr.Zero;
        bool verified = false;
        try
        {
            pathPtr = Marshal.StringToHGlobalUni(path);
            filePtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            Marshal.StructureToPtr(new WINTRUST_FILE_INFO
            {
                cbStruct = Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
                pcwszFilePath = pathPtr,
            }, filePtr, false);

            var data = new WINTRUST_DATA
            {
                cbStruct = Marshal.SizeOf(typeof(WINTRUST_DATA)),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = filePtr,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = 0,
            };
            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA)));
            Marshal.StructureToPtr(data, dataPtr, false);

            verified = true;
            return WinVerifyTrust(InvalidHandleValue, WintrustActionGenericVerifyV2, dataPtr);
        }
        catch
        {
            return unchecked((int)0x80004005); // E_FAIL-style: treat as untrusted
        }
        finally
        {
            try
            {
                if (verified && dataPtr != IntPtr.Zero)
                {
                    var closing = (WINTRUST_DATA)Marshal.PtrToStructure(dataPtr, typeof(WINTRUST_DATA))!;
                    closing.dwStateAction = WTD_STATEACTION_CLOSE;
                    Marshal.StructureToPtr(closing, dataPtr, false);
                    WinVerifyTrust(InvalidHandleValue, WintrustActionGenericVerifyV2, dataPtr);
                }
            }
            catch
            {
                // best-effort state close
            }
            if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
            if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
            if (pathPtr != IntPtr.Zero) Marshal.FreeHGlobal(pathPtr);
        }
    }

    // Helpers

    /// <summary>Full path of an already-loaded module, or null when it is not loaded.</summary>
    public static string? ModulePath(string baseName)
    {
        try
        {
            var h = GetModuleHandleW(baseName);
            if (h == IntPtr.Zero) return null;
            var sb = new StringBuilder(1024);
            return GetModuleFileNameW(h, sb, sb.Capacity) > 0 ? sb.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>File version as major.minor.build.private, or null when unreadable.</summary>
    public static string? BinaryVersion(string path)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            return $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart}";
        }
        catch
        {
            return null;
        }
    }
}
