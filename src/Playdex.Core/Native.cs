using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Playdex.Core;

#pragma warning disable CA1069 // Win32 constants

/// <summary>Win32 imports. Source-generated (LibraryImport) so they work under Native AOT.</summary>
internal static unsafe partial class Native
{
    public const uint Infinite = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    public static partial void GetSystemTimePreciseAsFileTime(out long fileTime);

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

    // ---- Windows ----

    /// <summary>For <see cref="AllowSetForegroundWindow"/>: any process may take the foreground (until the next input).</summary>
    public const uint AsfwAny = unchecked((uint)-1);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint processId);

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
