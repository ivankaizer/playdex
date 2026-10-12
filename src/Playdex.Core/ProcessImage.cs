using System.Diagnostics;

namespace Playdex.Core;

/// <summary>Which processes run an exe from a folder: the setup closes the Playdex it replaces, not another copy.</summary>
public static class ProcessImage
{
    /// <summary>The full path of the process's exe, or null when it can't be read (gone, or protected).</summary>
    public static string? PathOf(int processId)
    {
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == 0)
        {
            return null;
        }
        try
        {
            return Native.ProcessImagePath(handle);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    /// <summary>Whether a process named like <paramref name="executable"/> runs from inside <paramref name="directory"/>.</summary>
    public static bool IsRunningFrom(string directory, string executable)
    {
        var root = Path.GetFullPath(directory).TrimEnd('\\') + "\\";
        var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
        try
        {
            return running.Any(p => PathOf(p.Id) is { } path && path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            foreach (var process in running)
            {
                process.Dispose();
            }
        }
    }
}
