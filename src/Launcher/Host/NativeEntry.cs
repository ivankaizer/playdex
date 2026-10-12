using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Playdex.Launcher.Host;

/// <summary>Win32 imports for the Full Screen Experience entry. Source-generated (LibraryImport) for Native AOT.</summary>
internal static unsafe partial class NativeEntry
{
    public const uint AnyProcess = unchecked((uint)-1); // ASFW_ANY

    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const nint DesktopAppPolicyAttribute = 0x00020012; // PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY
    private const uint DesktopAppBreakawayOverride = 0x04; // PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_OVERRIDE

    /// <summary>
    /// Start <paramref name="path"/> as an ordinary unpackaged process. A child of a packaged app runs in the package's
    /// container by default (its identity, its single-instance keys); the desktop app policy breaks it away.
    /// </summary>
    /// <returns>The new process id.</returns>
    public static int StartOutsidePackage(string path, string arguments)
    {
        nuint size = 0;
        InitializeProcThreadAttributeList(null, 1, 0, &size);
        var list = (void*)Marshal.AllocHGlobal((nint)size);
        try
        {
            if (!InitializeProcThreadAttributeList(list, 1, 0, &size))
            {
                throw new Win32Exception();
            }
            try
            {
                var policy = DesktopAppBreakawayOverride;
                if (!UpdateProcThreadAttribute(list, 0, DesktopAppPolicyAttribute, &policy, sizeof(uint), null, null))
                {
                    throw new Win32Exception();
                }
                var startup = new StartupInfoEx { AttributeList = list };
                startup.StartupInfo.Size = (uint)sizeof(StartupInfoEx);
                // CreateProcessW may write to the command line, so it gets its own buffer.
                var commandLine = $"\"{path}\" {arguments}\0".ToCharArray();
                ProcessInformation info;
                fixed (char* app = path)
                fixed (char* line = commandLine)
                fixed (char* directory = Path.GetDirectoryName(path))
                {
                    if (!CreateProcess(app, line, null, null, false, ExtendedStartupInfoPresent, null, directory, &startup, &info))
                    {
                        throw new Win32Exception();
                    }
                }
                CloseHandle(info.Thread);
                CloseHandle(info.Process);
                return (int)info.ProcessId;
            }
            finally
            {
                DeleteProcThreadAttributeList(list);
            }
        }
        finally
        {
            Marshal.FreeHGlobal((nint)list);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
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
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public void* AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(void* list, uint count, uint flags, nuint* size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(void* list, uint flags, nint attribute, void* value, nuint size, void* previous, nuint* returnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(void* list);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcess(
        char* application, char* commandLine, void* processAttributes, void* threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, void* environment, char* currentDirectory,
        StartupInfoEx* startupInfo, ProcessInformation* processInformation);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(nint owner, string text, string caption, uint type);
}
