using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Launcher.Host;

internal sealed record AudioOutput(string Id, string Name, bool IsDefault);

internal sealed record DisplayState(string Id, string Name, bool HdrSupported, bool HdrOn);

/// <summary>What the quick settings panel shows; posted to the page with type "quick-settings".</summary>
internal sealed record QuickSettingsState(string Type, IReadOnlyList<AudioOutput> Outputs, IReadOnlyList<DisplayState> Displays, string? Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(QuickSettingsState))]
internal sealed partial class QuickSettingsJson : JsonSerializerContext;

/// <summary>
/// The quick settings panel's system controls: the default sound output, HDR per display, and sleep, restart and shut
/// down. Every call blocks briefly (COM, display configuration), so callers run it off the UI thread; calls are
/// serialized so two toggles never overlap.
/// </summary>
internal static class QuickSettings
{
    private static readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Run <paramref name="change"/> (null: just read), then return the state as JSON for the page.</summary>
    public static async Task<string> ApplyAsync(Action? change, string description)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                string? error = null;
                if (change is not null)
                {
                    try
                    {
                        change();
                        Log.Info($"quick settings: {description}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"quick settings: {description} failed", ex);
                        error = $"Couldn't {description}.";
                    }
                }
                return JsonSerializer.Serialize(new QuickSettingsState("quick-settings", ReadOutputs(), ReadDisplays(), error), QuickSettingsJson.Default.QuickSettingsState);
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    private static List<AudioOutput> ReadOutputs()
    {
        try
        {
            return AudioOutputs.List();
        }
        catch (Exception ex)
        {
            Log.Error("quick settings: sound outputs unreadable", ex);
            return [];
        }
    }

    private static List<DisplayState> ReadDisplays()
    {
        try
        {
            return Displays.List();
        }
        catch (InvalidOperationException ex)
        {
            Log.Error("quick settings: displays unreadable", ex);
            return [];
        }
    }
}

/// <summary>
/// Sound outputs through Core Audio: IMMDeviceEnumerator lists active render endpoints, and IPolicyConfig (undocumented
/// but stable since Windows 7; the Sound settings page and tools such as EarTrumpet use it) sets the default. COM is
/// called through vtable slots, which works under Native AOT without generated interop.
/// </summary>
internal static unsafe class AudioOutputs
{
    private static readonly Guid _deviceEnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid _deviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid _policyConfigClass = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    private static readonly Guid _policyConfig = new("F8679F50-850A-41CF-9C72-430F290290C8");

    private const int Render = 0; // eRender
    private const uint DeviceStateActive = 0x1;
    private const int ErrorNotFound = unchecked((int)0x80070490);

    // Roles: eConsole, eMultimedia, eCommunications. Sound settings sets all three.
    private static readonly int[] _roles = [0, 1, 2];

    public static List<AudioOutput> List()
    {
        using var com = new ComScope();
        using var enumerator = Create(_deviceEnumeratorClass, _deviceEnumerator);

        string? defaultId = null;
        nint defaultDevice;
        // IMMDeviceEnumerator::GetDefaultAudioEndpoint(flow, role, device)
        var hr = ((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)Slot(enumerator, 4))(enumerator, Render, 1, &defaultDevice);
        if (hr == 0)
        {
            using var device = new ComPtr(defaultDevice);
            defaultId = DeviceId(device);
        }
        else if (hr != ErrorNotFound)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        nint collection;
        // IMMDeviceEnumerator::EnumAudioEndpoints(flow, stateMask, devices)
        Check(((delegate* unmanaged[Stdcall]<nint, int, uint, nint*, int>)Slot(enumerator, 3))(enumerator, Render, DeviceStateActive, &collection));
        using var devices = new ComPtr(collection);
        uint count;
        Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(devices, 3))(devices, &count));

        var outputs = new List<AudioOutput>((int)count);
        for (uint i = 0; i < count; i++)
        {
            nint item;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(devices, 4))(devices, i, &item));
            using var device = new ComPtr(item);
            var id = DeviceId(device);
            outputs.Add(new AudioOutput(id, FriendlyName(device) ?? "Sound output", id == defaultId));
        }
        return outputs;
    }

    public static void SetDefault(string id)
    {
        if (!List().Any(o => o.Id == id))
        {
            throw new ArgumentException("No such sound output.", nameof(id));
        }
        using var com = new ComScope();
        using var policy = Create(_policyConfigClass, _policyConfig);
        fixed (char* deviceId = id)
        {
            foreach (var role in _roles)
            {
                // IPolicyConfig::SetDefaultEndpoint(deviceId, role)
                Check(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Slot(policy, 13))(policy, deviceId, role));
            }
        }
    }

    private static string DeviceId(ComPtr device)
    {
        char* id;
        // IMMDevice::GetId(id)
        Check(((delegate* unmanaged[Stdcall]<nint, char**, int>)Slot(device, 5))(device, &id));
        try
        {
            return new string(id);
        }
        finally
        {
            Native.CoTaskMemFree((nint)id);
        }
    }

    private static string? FriendlyName(ComPtr device)
    {
        nint storePointer;
        // IMMDevice::OpenPropertyStore(STGM_READ, store)
        if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(device, 4))(device, 0, &storePointer) != 0)
        {
            return null;
        }
        using var store = new ComPtr(storePointer);
        // PKEY_Device_FriendlyName
        var key = new Native.PropertyKey { FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
        Native.PropVariant value;
        // IPropertyStore::GetValue(key, value)
        if (((delegate* unmanaged[Stdcall]<nint, Native.PropertyKey*, Native.PropVariant*, int>)Slot(store, 5))(store, &key, &value) != 0)
        {
            return null;
        }
        try
        {
            return value.Type == Native.VtLpwstr && value.Pointer != 0 ? new string((char*)value.Pointer) : null;
        }
        finally
        {
            _ = Native.PropVariantClear(&value);
        }
    }

    private static ComPtr Create(Guid classId, Guid interfaceId)
    {
        Check(Native.CoCreateInstance(classId, 0, Native.ClsctxAll, interfaceId, out var instance));
        return new ComPtr(instance);
    }

    private static nint Slot(nint instance, int index) => (*(nint**)instance)[index];

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    /// <summary>One COM reference, released on dispose (IUnknown::Release is slot 2).</summary>
    private readonly struct ComPtr(nint pointer) : IDisposable
    {
        public static implicit operator nint(ComPtr ptr) => ptr._pointer;

        private readonly nint _pointer = pointer;

        public void Dispose()
        {
            if (_pointer != 0)
            {
                ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(_pointer, 2))(_pointer);
            }
        }
    }

    /// <summary>COM for this thread-pool thread (MTA), balanced on dispose.</summary>
    private readonly struct ComScope : IDisposable
    {
        private readonly bool _initialized;

        public ComScope() => _initialized = Native.CoInitializeEx(0, Native.CoinitMultithreaded) >= 0;

        public void Dispose()
        {
            if (_initialized)
            {
                Native.CoUninitialize();
            }
        }
    }
}

/// <summary>
/// HDR per display through the display configuration API: DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 and
/// DISPLAYCONFIG_SET_HDR_STATE on Windows 11 24H2, the older advanced color calls before that. The same switch as
/// Settings → Display → Use HDR.
/// </summary>
internal static unsafe class Displays
{
    private const uint HdrSupported = 1 << 4;
    private const uint HdrUserEnabled = 1 << 5;
    private const uint AdvancedColorSupported = 1 << 0;
    private const uint AdvancedColorEnabled = 1 << 1;
    private const uint WideColorEnforced = 1 << 2;

    public static List<DisplayState> List()
    {
        var displays = new List<DisplayState>();
        var seen = new HashSet<string>();
        foreach (var path in ActivePaths())
        {
            var id = Id(path.TargetAdapterId, path.TargetId);
            if (!seen.Add(id))
            {
                continue; // Duplicated (clone) displays share a target.
            }
            var (supported, on) = Hdr(path.TargetAdapterId, path.TargetId);
            displays.Add(new DisplayState(id, Name(path.TargetAdapterId, path.TargetId, displays.Count + 1), supported, on));
        }
        return displays;
    }

    public static void SetHdr(string id, bool on)
    {
        var paths = ActivePaths();
        var index = Array.FindIndex(paths, p => Id(p.TargetAdapterId, p.TargetId) == id);
        if (index < 0)
        {
            throw new ArgumentException("No such display.", nameof(id));
        }
        var path = paths[index];
        var request = new Native.DisplayConfigSetState
        {
            Header = Header(Native.DisplayConfigSetHdrState, sizeof(Native.DisplayConfigSetState), path.TargetAdapterId, path.TargetId),
            Value = on ? 1u : 0u,
        };
        var status = Native.DisplayConfigSetDeviceInfo(&request.Header);
        if (status != 0)
        {
            // Before 24H2: advanced color is HDR.
            request.Header.Type = Native.DisplayConfigSetAdvancedColorState;
            status = Native.DisplayConfigSetDeviceInfo(&request.Header);
        }
        if (status != 0)
        {
            throw new InvalidOperationException($"DisplayConfigSetDeviceInfo failed: {status}");
        }
    }

    private static (bool Supported, bool On) Hdr(Native.Luid adapter, uint target)
    {
        var info = new Native.DisplayConfigAdvancedColorInfo2
        {
            Header = Header(Native.DisplayConfigGetAdvancedColorInfo2, sizeof(Native.DisplayConfigAdvancedColorInfo2), adapter, target),
        };
        if (Native.DisplayConfigGetDeviceInfo(&info.Header) == 0)
        {
            return ((info.Value & HdrSupported) != 0, (info.Value & HdrUserEnabled) != 0);
        }
        var legacy = new Native.DisplayConfigAdvancedColorInfo
        {
            Header = Header(Native.DisplayConfigGetAdvancedColorInfo, sizeof(Native.DisplayConfigAdvancedColorInfo), adapter, target),
        };
        if (Native.DisplayConfigGetDeviceInfo(&legacy.Header) != 0)
        {
            return (false, false);
        }
        // Wide color forced on means a non-HDR display using advanced color for color management, not HDR.
        var supported = (legacy.Value & AdvancedColorSupported) != 0 && (legacy.Value & WideColorEnforced) == 0;
        return (supported, supported && (legacy.Value & AdvancedColorEnabled) != 0);
    }

    private static string Name(Native.Luid adapter, uint target, int number)
    {
        var name = new Native.DisplayConfigTargetDeviceName
        {
            Header = Header(Native.DisplayConfigGetTargetName, sizeof(Native.DisplayConfigTargetDeviceName), adapter, target),
        };
        if (Native.DisplayConfigGetDeviceInfo(&name.Header) == 0)
        {
            var friendly = new string(name.MonitorFriendlyDeviceName);
            if (friendly.Length > 0)
            {
                return friendly;
            }
            if (name.OutputTechnology == Native.OutputTechnologyInternal)
            {
                return "Built-in display";
            }
        }
        return $"Display {number}";
    }

    private static Native.DisplayConfigPathInfo[] ActivePaths()
    {
        while (true)
        {
            Check(Native.GetDisplayConfigBufferSizes(Native.QdcOnlyActivePaths, out var pathCount, out var modeCount), "GetDisplayConfigBufferSizes");
            var paths = new Native.DisplayConfigPathInfo[pathCount];
            var modes = new byte[modeCount * 64]; // DISPLAYCONFIG_MODE_INFO is 64 bytes.
            int status;
            fixed (Native.DisplayConfigPathInfo* pathBuffer = paths)
            fixed (byte* modeBuffer = modes)
            {
                status = Native.QueryDisplayConfig(Native.QdcOnlyActivePaths, ref pathCount, pathBuffer, ref modeCount, modeBuffer, 0);
            }
            if (status == Native.ErrorInsufficientBuffer)
            {
                continue; // A display was connected in between.
            }
            Check(status, "QueryDisplayConfig");
            return paths[..(int)pathCount];
        }
    }

    private static Native.DisplayConfigDeviceInfoHeader Header(int type, int size, Native.Luid adapter, uint target) =>
        new() { Type = type, Size = (uint)size, AdapterId = adapter, Id = target };

    private static string Id(Native.Luid adapter, uint target) => $"{adapter.HighPart:x}.{adapter.LowPart:x}.{target:x}";

    private static void Check(int status, string call)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"{call} failed: {status}");
        }
    }
}

/// <summary>Sleep, restart and shut down, as the Start menu does them (apps are asked to close, never forced).</summary>
internal static unsafe class Power
{
    /// <summary>
    /// Modern Standby PCs (most handhelds) have no S3 sleep: turning the display off is what puts them to sleep, as the
    /// power button does. Others suspend. Returns once the PC is back (or at once on Modern Standby).
    /// </summary>
    public static void Sleep()
    {
        var capabilities = stackalloc byte[128];
        var known = Native.GetPwrCapabilities(capabilities);
        var modernStandby = known && (capabilities[Native.PowerCapabilitiesAoAc] != 0 || capabilities[Native.PowerCapabilitiesSystemS3] == 0);
        if (modernStandby)
        {
            if (!Native.PostMessage(Native.HwndBroadcast, Native.WmSysCommand, Native.ScMonitorPower, Native.MonitorOff))
            {
                throw new InvalidOperationException("Could not turn the display off.");
            }
            return;
        }
        if (!Native.SetSuspendState(hibernate: false, force: false, wakeupEventsDisabled: false))
        {
            throw new InvalidOperationException($"SetSuspendState failed: {Marshal.GetLastPInvokeError()}");
        }
    }

    public static void Restart() => Shutdown(Native.ShutdownRestart);

    /// <summary>With Fast startup on (hiberboot), a hybrid shutdown, as the Start menu does.</summary>
    public static void ShutDown()
    {
        var capabilities = stackalloc byte[128];
        var hiberboot = Native.GetPwrCapabilities(capabilities) && capabilities[Native.PowerCapabilitiesHiberboot] != 0;
        Shutdown(Native.ShutdownPowerOff | (hiberboot ? Native.ShutdownHybrid : 0));
    }

    private static void Shutdown(uint flags)
    {
        EnableShutdownPrivilege();
        var status = Native.InitiateShutdown(null, null, 0, flags, Native.ShutdownReasonPlanned);
        if (status != 0)
        {
            throw new InvalidOperationException($"InitiateShutdown failed: {status}");
        }
    }

    /// <summary>Every user has SeShutdownPrivilege, but it is disabled in the token until enabled.</summary>
    private static void EnableShutdownPrivilege()
    {
        if (!Native.OpenProcessToken(Native.GetCurrentProcess(), Native.TokenAdjustPrivileges | Native.TokenQuery, out var token))
        {
            throw new InvalidOperationException($"OpenProcessToken failed: {Marshal.GetLastPInvokeError()}");
        }
        try
        {
            if (!Native.LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid))
            {
                throw new InvalidOperationException($"LookupPrivilegeValue failed: {Marshal.GetLastPInvokeError()}");
            }
            var privileges = new Native.TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = Native.SePrivilegeEnabled };
            if (!Native.AdjustTokenPrivileges(token, false, &privileges, 0, 0, 0) || Marshal.GetLastPInvokeError() == Native.ErrorNotAllAssigned)
            {
                throw new InvalidOperationException("The shutdown privilege is not available.");
            }
        }
        finally
        {
            Native.CloseHandle(token);
        }
    }
}
