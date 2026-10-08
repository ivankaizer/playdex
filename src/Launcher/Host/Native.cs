using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Playdex.Launcher.Host;

#pragma warning disable CA1069 // Win32 constants

/// <summary>Win32 imports. Source-generated (LibraryImport) so they work under Native AOT.</summary>
internal static unsafe partial class Native
{
    // ---- XInput ----

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    public const uint ErrorSuccess = 0;

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadLibrary(string name);

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress")]
    public static partial nint GetProcAddress(nint module, nint ordinal);

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetProcAddress(nint module, string name);

    // ---- Timers and waits ----

    public const uint CreateWaitableTimerHighResolution = 0x2;
    public const uint TimerAllAccess = 0x1F0003;
    public const uint Infinite = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW")]
    public static partial nint CreateWaitableTimerEx(nint attributes, nint name, uint flags, uint access);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWaitableTimer(nint timer, long* dueTime, int period, nint completion, nint arg, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("kernel32.dll")]
    public static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    public static partial void GetSystemTimePreciseAsFileTime(out long fileTime);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    // ---- Windows and focus ----

    public const int SwRestore = 9;
    public const int SwShow = 5;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint processId);

    // ---- Registry change notification ----

    public const uint RegNotifyChangeLastSet = 0x4;
    public const uint RegNotifyThreadAgnostic = 0x10000000;

    [LibraryImport("advapi32.dll")]
    public static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle key,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint filter,
        SafeWaitHandle evt,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    // ---- Processes and job objects ----

    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint Synchronize = 0x00100000;
    public const uint CreateSuspended = 0x4;
    public const uint CreateUnicodeEnvironment = 0x400;
    public const int JobObjectAssociateCompletionPortInformation = 7;
    public const uint JobObjectMsgActiveProcessZero = 4;
    public const uint JobObjectMsgNewProcess = 6;
    public const uint JobObjectMsgExitProcess = 7;
    public const uint JobObjectMsgAbnormalExitProcess = 8;

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfo
    {
        public uint Cb;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public nint Reserved2;
        public nint StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JobAssociateCompletionPort
    {
        public nint CompletionKey;
        public nint CompletionPort;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateProcess(
        char* applicationName, char* commandLine, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, nint environment,
        char* currentDirectory, StartupInfo* startupInfo, ProcessInformation* processInformation);

    [LibraryImport("kernel32.dll")]
    public static partial uint ResumeThread(nint thread);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    public static partial nint CreateJobObject(nint attributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetInformationJobObject(nint job, int infoClass, void* info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint CreateIoCompletionPort(nint fileHandle, nint existingPort, nuint completionKey, uint threads);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetQueuedCompletionStatus(nint port, out uint bytes, out nuint key, out nint overlapped, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostQueuedCompletionStatus(nint port, uint bytes, nuint key, nint overlapped);

    [LibraryImport("kernel32.dll")]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(nint process, uint flags, char* buffer, ref uint size);

    /// <summary>Wall-clock now as a FILETIME (100 ns ticks), comparable with GetProcessTimes.</summary>
    public static long PreciseNow()
    {
        GetSystemTimePreciseAsFileTime(out var now);
        return now;
    }

    public static string? ProcessImagePath(nint process)
    {
        var buffer = stackalloc char[1024];
        uint size = 1024;
        return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    }
}
