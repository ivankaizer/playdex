using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Playdex.Core;

public sealed record SteamShortcutResult(uint AppId, string UserConfigDirectory, bool WasAdded);

public static class SteamIntegration
{
    public static SteamShortcutResult AddShortcut(GameConfig game, string launcherPath)
    {
        var steamProcesses = Process.GetProcessesByName("steam");
        try
        {
            if (steamProcesses.Length > 0)
            {
                throw new InvalidOperationException("Close Steam completely before adding or updating a non-Steam game.");
            }
        }
        finally
        {
            foreach (var process in steamProcesses)
            {
                process.Dispose();
            }
        }

        return AddShortcut(game, launcherPath, SteamInstallation.FindActiveUserConfigDirectory());
    }

    internal static SteamShortcutResult AddShortcut(
        GameConfig game,
        string launcherPath,
        string userConfigDirectory)
    {
        launcherPath = Path.GetFullPath(launcherPath);
        if (!File.Exists(launcherPath))
        {
            throw new FileNotFoundException("The generated game launcher was not found.", launcherPath);
        }

        Directory.CreateDirectory(userConfigDirectory);
        var shortcutsPath = Path.Combine(userConfigDirectory, "shortcuts.vdf");
        var executable = Quote(launcherPath);
        var appName = GameCatalog.NormalizeDisplayName(game.DisplayName);
        var existing = SteamShortcutsFile.FindByExecutable(shortcutsPath, executable);
        if (existing is not null)
        {
            return new SteamShortcutResult(existing.Value, userConfigDirectory, false);
        }

        var appId = SteamShortcutId.Calculate(executable, appName);
        SteamShortcutsFile.Append(shortcutsPath, appId, appName, executable, game.Launch.ExecutablePath);
        return new SteamShortcutResult(appId, userConfigDirectory, true);
    }

    private static string Quote(string value) => $"\"{value}\"";
}

public static class SteamInstallation
{
    public static string FindActiveUserConfigDirectory()
    {
        using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steamPath = steamKey?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            throw new InvalidOperationException("Steam is not installed for the current Windows user.");
        }

        var userdata = Path.Combine(Path.GetFullPath(steamPath), "userdata");
        if (!Directory.Exists(userdata))
        {
            throw new InvalidOperationException("Steam user data could not be found. Sign in to Steam once and try again.");
        }

        var activeUser = ReadActiveUserId(steamKey!);
        if (activeUser is not null)
        {
            var activeConfig = Path.Combine(userdata, activeUser, "config");
            if (Directory.Exists(Path.GetDirectoryName(activeConfig)!))
            {
                return activeConfig;
            }
        }

        var recentUser = ReadMostRecentUserId(Path.Combine(steamPath, "config", "loginusers.vdf"));
        if (recentUser is not null)
        {
            var recentConfig = Path.Combine(userdata, recentUser, "config");
            if (Directory.Exists(Path.GetDirectoryName(recentConfig)!))
            {
                return recentConfig;
            }
        }

        var users = Directory.EnumerateDirectories(userdata)
            .Where(path => uint.TryParse(Path.GetFileName(path), out _))
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ToList();
        return users.Count switch
        {
            0 => throw new InvalidOperationException("No Steam user profile was found. Sign in to Steam once and try again."),
            1 => Path.Combine(users[0], "config"),
            _ => throw new InvalidOperationException("The active Steam user could not be determined. Start Steam, sign in to the intended account, then close Steam and try again.")
        };
    }

    private static string? ReadActiveUserId(RegistryKey steamKey)
    {
        using var activeProcess = steamKey.OpenSubKey("ActiveProcess");
        var value = activeProcess?.GetValue("ActiveUser");
        try
        {
            var id = Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            return id == 0 ? null : id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static string? ReadMostRecentUserId(string loginUsersPath)
    {
        try
        {
            if (!File.Exists(loginUsersPath))
            {
                return null;
            }

            var contents = File.ReadAllText(loginUsersPath);
            var users = Regex.Matches(
                    contents,
                    "\"(?<steamId>7656119[0-9]+)\"\\s*\\{(?<settings>.*?)\\n\\s*\\}",
                    RegexOptions.Singleline)
                .Select(match => new
                {
                    SteamId = ulong.Parse(match.Groups["steamId"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    IsMostRecent = Regex.IsMatch(match.Groups["settings"].Value, "\"MostRecent\"\\s+\"1\"", RegexOptions.IgnoreCase),
                    Timestamp = ReadTimestamp(match.Groups["settings"].Value)
                })
                .OrderByDescending(user => user.IsMostRecent)
                .ThenByDescending(user => user.Timestamp)
                .ToList();
            if (users.Count == 0)
            {
                return null;
            }

            const ulong individualAccountBase = 76561197960265728ul;
            return users[0].SteamId >= individualAccountBase
                ? (users[0].SteamId - individualAccountBase).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static ulong ReadTimestamp(string settings)
    {
        var match = Regex.Match(settings, "\"Timestamp\"\\s+\"(?<value>[0-9]+)\"", RegexOptions.IgnoreCase);
        return match.Success && ulong.TryParse(match.Groups["value"].Value, out var value) ? value : 0;
    }
}

public static class SteamShortcutId
{
    public static uint Calculate(string quotedExecutable, string appName)
    {
        var bytes = Encoding.UTF8.GetBytes(quotedExecutable + appName);
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
            }
        }

        return (~crc) | 0x80000000u;
    }
}

public static class SteamShortcutsFile
{
    private const byte ObjectType = 0;
    private const byte StringType = 1;
    private const byte Int32Type = 2;
    private const byte FloatType = 3;
    private const byte PointerType = 4;
    private const byte WideStringType = 5;
    private const byte ColorType = 6;
    private const byte UInt64Type = 7;
    private const byte EndType = 8;
    private const byte Int64Type = 10;

    public static uint? FindByExecutable(string path, string executable)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var data = File.ReadAllBytes(path);
        var root = ReadRoot(data);
        foreach (var entry in root.Entries)
        {
            if (string.Equals(entry.Executable, executable, StringComparison.OrdinalIgnoreCase))
            {
                return entry.AppId;
            }
        }

        return null;
    }

    public static void Append(
        string path,
        uint appId,
        string appName,
        string executable,
        string? iconPath)
    {
        byte[] data;
        RootInfo root;
        if (File.Exists(path))
        {
            data = File.ReadAllBytes(path);
            root = ReadRoot(data);
        }
        else
        {
            using var initial = new MemoryStream();
            initial.WriteByte(ObjectType);
            WriteCString(initial, "shortcuts");
            initial.WriteByte(EndType);
            data = initial.ToArray();
            root = ReadRoot(data);
        }

        var index = root.Entries.Count == 0 ? 0 : root.Entries.Max(entry => entry.Index) + 1;
        using var output = new MemoryStream(data.Length + 512);
        output.Write(data, 0, root.EndOffset);
        WriteEntry(output, index, appId, appName, executable, iconPath);
        output.Write(data, root.EndOffset, data.Length - root.EndOffset);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, output.ToArray());
            if (File.Exists(path))
            {
                File.Copy(path, $"{path}.playdex.bak", overwrite: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static RootInfo ReadRoot(byte[] data)
    {
        var offset = 0;
        if (ReadByte(data, ref offset) != ObjectType ||
            !string.Equals(ReadCString(data, ref offset), "shortcuts", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Steam shortcuts.vdf has an unexpected format.");
        }

        var entries = new List<EntryInfo>();
        while (true)
        {
            var typeOffset = offset;
            var type = ReadByte(data, ref offset);
            if (type == EndType)
            {
                return new RootInfo(typeOffset, entries);
            }

            if (type != ObjectType)
            {
                throw new InvalidDataException("Steam shortcuts.vdf contains an unexpected root value.");
            }

            var name = ReadCString(data, ref offset);
            var entryIndex = int.TryParse(name, out var parsedIndex) ? parsedIndex : entries.Count;
            uint? appId = null;
            string? executable = null;
            ReadEntryObject(data, ref offset, ref appId, ref executable);
            if (appId is not null)
            {
                entries.Add(new EntryInfo(entryIndex, appId.Value, executable));
            }
        }
    }

    private static void ReadEntryObject(byte[] data, ref int offset, ref uint? appId, ref string? executable)
    {
        while (true)
        {
            var type = ReadByte(data, ref offset);
            if (type == EndType)
            {
                return;
            }

            var name = ReadCString(data, ref offset);
            if (type == ObjectType)
            {
                SkipObject(data, ref offset);
            }
            else if (type == StringType)
            {
                var value = ReadCString(data, ref offset);
                if (string.Equals(name, "Exe", StringComparison.OrdinalIgnoreCase))
                {
                    executable = value;
                }
            }
            else if (type == Int32Type)
            {
                var value = ReadUInt32(data, ref offset);
                if (string.Equals(name, "appid", StringComparison.OrdinalIgnoreCase))
                {
                    appId = value;
                }
            }
            else
            {
                SkipValue(data, ref offset, type);
            }
        }
    }

    private static void SkipObject(byte[] data, ref int offset)
    {
        while (true)
        {
            var type = ReadByte(data, ref offset);
            if (type == EndType)
            {
                return;
            }

            _ = ReadCString(data, ref offset);
            if (type == ObjectType)
            {
                SkipObject(data, ref offset);
            }
            else
            {
                SkipValue(data, ref offset, type);
            }
        }
    }

    private static void SkipValue(byte[] data, ref int offset, byte type)
    {
        switch (type)
        {
            case StringType:
                _ = ReadCString(data, ref offset);
                break;
            case WideStringType:
                while (ReadUInt16(data, ref offset) != 0) { }
                break;
            case Int32Type:
            case FloatType:
            case PointerType:
            case ColorType:
                EnsureAvailable(data, offset, 4);
                offset += 4;
                break;
            case UInt64Type:
            case Int64Type:
                EnsureAvailable(data, offset, 8);
                offset += 8;
                break;
            default:
                throw new InvalidDataException($"Steam shortcuts.vdf contains unsupported value type {type}.");
        }
    }

    private static void WriteEntry(
        Stream stream,
        int index,
        uint appId,
        string appName,
        string executable,
        string? iconPath)
    {
        stream.WriteByte(ObjectType);
        WriteCString(stream, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteInt32(stream, "appid", appId);
        WriteString(stream, "AppName", appName);
        WriteString(stream, "Exe", executable);
        WriteString(stream, "StartDir", $"\"{Path.GetDirectoryName(executable.Trim('\"'))}\"");
        WriteString(stream, "icon", iconPath ?? "");
        WriteString(stream, "ShortcutPath", "");
        WriteString(stream, "LaunchOptions", "");
        WriteInt32(stream, "IsHidden", 0);
        WriteInt32(stream, "AllowDesktopConfig", 1);
        WriteInt32(stream, "AllowOverlay", 1);
        WriteInt32(stream, "OpenVR", 0);
        WriteInt32(stream, "Devkit", 0);
        WriteString(stream, "DevkitGameID", "");
        WriteInt32(stream, "DevkitOverrideAppID", 0);
        WriteInt32(stream, "LastPlayTime", 0);
        WriteString(stream, "FlatpakAppID", "");
        stream.WriteByte(ObjectType);
        WriteCString(stream, "tags");
        stream.WriteByte(EndType);
        stream.WriteByte(EndType);
    }

    private static void WriteString(Stream stream, string name, string value)
    {
        stream.WriteByte(StringType);
        WriteCString(stream, name);
        WriteCString(stream, value);
    }

    private static void WriteInt32(Stream stream, string name, uint value)
    {
        stream.WriteByte(Int32Type);
        WriteCString(stream, name);
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteCString(Stream stream, string value)
    {
        stream.Write(Encoding.UTF8.GetBytes(value));
        stream.WriteByte(0);
    }

    private static byte ReadByte(byte[] data, ref int offset)
    {
        EnsureAvailable(data, offset, 1);
        return data[offset++];
    }

    private static string ReadCString(byte[] data, ref int offset)
    {
        var end = Array.IndexOf(data, (byte)0, offset);
        if (end < 0)
        {
            throw new InvalidDataException("Steam shortcuts.vdf contains an unterminated string.");
        }

        var value = Encoding.UTF8.GetString(data, offset, end - offset);
        offset = end + 1;
        return value;
    }

    private static ushort ReadUInt16(byte[] data, ref int offset)
    {
        EnsureAvailable(data, offset, 2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        offset += 2;
        return value;
    }

    private static uint ReadUInt32(byte[] data, ref int offset)
    {
        EnsureAvailable(data, offset, 4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    private static void EnsureAvailable(byte[] data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
        {
            throw new InvalidDataException("Steam shortcuts.vdf is truncated.");
        }
    }

    private sealed record RootInfo(int EndOffset, List<EntryInfo> Entries);
    private sealed record EntryInfo(int Index, uint AppId, string? Executable);
}
