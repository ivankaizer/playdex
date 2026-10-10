using Playdex.Core;
using System.Runtime.InteropServices;

namespace Playdex.Shell;

/// <summary>Win32 imports for quick settings: Core Audio, display configuration (HDR, refresh rate) and power.</summary>
internal static unsafe partial class Native
{
    // ---- COM ----

    public const uint ClsctxAll = 0x17;
    public const uint CoinitMultithreaded = 0x0;

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(nint memory);

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(PropVariant* value);

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    /// <summary>PROPVARIANT: a 2-byte type tag, 6 reserved bytes, then the value (a pointer for VT_LPWSTR).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
    }

    public const ushort VtLpwstr = 31;

    // ---- Display configuration ----

    public const uint QdcOnlyActivePaths = 0x2;
    public const int ErrorInsufficientBuffer = 122;
    public const int DisplayConfigGetSourceName = 1;
    public const int DisplayConfigGetTargetName = 2;
    public const int DisplayConfigGetAdvancedColorInfo = 9;
    public const int DisplayConfigSetAdvancedColorState = 10;
    public const int DisplayConfigGetAdvancedColorInfo2 = 15;
    public const int DisplayConfigSetHdrState = 16;
    public const uint OutputTechnologyInternal = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    /// <summary>DISPLAYCONFIG_PATH_INFO. Only the source's and the target's adapter and id are read.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct DisplayConfigPathInfo
    {
        [FieldOffset(0)] public Luid SourceAdapterId;
        [FieldOffset(8)] public uint SourceId;
        [FieldOffset(20)] public Luid TargetAdapterId;
        [FieldOffset(28)] public uint TargetId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigDeviceInfoHeader
    {
        public int Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    /// <summary>DISPLAYCONFIG_SOURCE_DEVICE_NAME: the GDI name (\\.\DISPLAY1) that the display mode APIs take.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public fixed char ViewGdiDeviceName[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        public fixed char MonitorFriendlyDeviceName[64];
        public fixed char MonitorDevicePath[128];
    }

    /// <summary>
    /// DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 (Windows 11 24H2). Bits of <see cref="Value"/>: 0 advanced color
    /// supported, 1 active, 3 limited by policy, 4 HDR supported, 5 HDR enabled by the user, 6 wide color supported,
    /// 7 wide color enabled by the user.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigAdvancedColorInfo2
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
        public uint ActiveColorMode;
    }

    /// <summary>
    /// DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO (before 24H2). Bits of <see cref="Value"/>: 0 advanced color supported,
    /// 1 enabled, 2 wide color enforced, 3 force disabled by policy.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigAdvancedColorInfo
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    /// <summary>DISPLAYCONFIG_SET_HDR_STATE and DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE: bit 0 enables.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigSetState
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
    }

    [LibraryImport("user32.dll")]
    public static partial int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [LibraryImport("user32.dll")]
    public static partial int QueryDisplayConfig(
        uint flags, ref uint pathCount, DisplayConfigPathInfo* paths, ref uint modeCount, byte* modes, nint topology);

    [LibraryImport("user32.dll")]
    public static partial int DisplayConfigGetDeviceInfo(DisplayConfigDeviceInfoHeader* request);

    [LibraryImport("user32.dll")]
    public static partial int DisplayConfigSetDeviceInfo(DisplayConfigDeviceInfoHeader* request);

    // ---- Display modes (refresh rate) ----

    public const int EnumCurrentSettings = -1;
    public const uint DmBitsPerPel = 0x40000;
    public const uint DmPelsWidth = 0x80000;
    public const uint DmPelsHeight = 0x100000;
    public const uint DmDisplayFrequency = 0x400000;
    public const uint DmInterlaced = 0x2;
    public const uint CdsUpdateRegistry = 0x1;
    public const int DispChangeSuccessful = 0;

    /// <summary>DEVMODEW, display fields only (220 bytes).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 220)]
    public struct DevMode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(72)] public uint Fields;
        [FieldOffset(168)] public uint BitsPerPel;
        [FieldOffset(172)] public uint PelsWidth;
        [FieldOffset(176)] public uint PelsHeight;
        [FieldOffset(180)] public uint DisplayFlags;
        [FieldOffset(184)] public uint DisplayFrequency;
    }

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplaySettingsEx(string deviceName, int modeNumber, DevMode* mode, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int ChangeDisplaySettingsEx(string deviceName, DevMode* mode, nint hwnd, uint flags, nint parameter);

    // ---- Power ----

    public const nint HwndBroadcast = 0xFFFF;
    public const uint WmSysCommand = 0x0112;
    public const nint ScMonitorPower = 0xF170;
    public const nint MonitorOff = 2;
    public const uint ShutdownRestart = 0x4;
    public const uint ShutdownPowerOff = 0x8;
    public const uint ShutdownHybrid = 0x200;
    public const uint ShutdownReasonPlanned = 0x80000000;
    public const uint TokenAdjustPrivileges = 0x20;
    public const uint TokenQuery = 0x8;
    public const uint SePrivilegeEnabled = 0x2;
    public const int ErrorNotAllAssigned = 1300;

    /// <summary>The SYSTEM_POWER_CAPABILITIES fields read here, by byte offset; the struct is 76 bytes.</summary>
    public const int PowerCapabilitiesSize = 76;
    public const int PowerCapabilitiesSystemS3 = 5;
    public const int PowerCapabilitiesHiberboot = 18;
    public const int PowerCapabilitiesAoAc = 20;

    [StructLayout(LayoutKind.Sequential)]
    public struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GetPwrCapabilities(byte* capabilities);

    [LibraryImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool force,
        [MarshalAs(UnmanagedType.U1)] bool wakeupEventsDisabled);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("advapi32.dll", EntryPoint = "InitiateShutdownW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint InitiateShutdown(string? machineName, string? message, uint gracePeriod, uint flags, uint reason);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(
        nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, TokenPrivileges* newState, uint length, nint previous, nint returnLength);
}
