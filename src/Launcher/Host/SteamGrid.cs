using System.Buffers.Binary;
using System.Text;
using Microsoft.Win32;

namespace Playdex.Launcher.Host;

/// <summary>
/// Steam's custom artwork: userdata\&lt;user&gt;\config\grid\&lt;id&gt;{p,,_hero,_logo}.{png,jpg,...}. Non-Steam
/// shortcuts (the Playdex Wrapper adds one per game and fills it from SteamGridDB) only have art here, under the
/// shortcut's app id from shortcuts.vdf; Steam apps have art here when the user set custom art. Paths returned are
/// "&lt;user id&gt;/&lt;file&gt;", served from &lt;user id&gt;.steam-grid.playdex (see <see cref="GridFolders"/>).
/// </summary>
internal static class SteamGrid
{
    private static readonly string[] _extensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>Steam\userdata, or null without Steam.</summary>
    public static string? UserdataDirectory { get; } =
        Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") is string steamPath
        && Path.Combine(steamPath, "userdata") is var dir && Directory.Exists(dir)
            ? Path.GetFullPath(dir)
            : null;

    /// <summary>Steam users' config folders, most recently used first.</summary>
    private static readonly Lazy<List<string>> _userConfigs = new(() =>
    {
        if (UserdataDirectory is null)
        {
            return [];
        }
        try
        {
            return [.. Directory.EnumerateDirectories(UserdataDirectory)
                .Where(d => uint.TryParse(Path.GetFileName(d), out _))
                .Select(d => Path.Combine(d, "config"))
                .Where(Directory.Exists)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"steam grid: userdata unreadable: {ex.Message}");
            return [];
        }
    });

    /// <summary>Non-Steam shortcuts by executable (full path, unquoted): the shortcut's app id and its user's config folder.</summary>
    private static readonly Lazy<Dictionary<string, (uint AppId, string UserConfig)>> _shortcuts = new(() =>
    {
        var shortcuts = new Dictionary<string, (uint, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in _userConfigs.Value)
        {
            var path = Path.Combine(config, "shortcuts.vdf");
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                foreach (var (appId, exe) in ReadShortcuts(File.ReadAllBytes(path)))
                {
                    if (exe.Trim().Trim('"') is { Length: > 0 } executable && Path.IsPathFullyQualified(executable))
                    {
                        shortcuts.TryAdd(Path.GetFullPath(executable), (appId, config));
                    }
                }
            }
            catch (IOException ex)
            {
                Log.Info($"steam grid: {path} unreadable: {ex.Message}");
            }
        }
        return shortcuts;
    });

    /// <summary>Each Steam user's grid folder by user id; only these folders are served to the page.</summary>
    public static IEnumerable<(string UserId, string Folder)> GridFolders =>
        _userConfigs.Value.Select(c => Path.Combine(c, "grid")).Where(Directory.Exists).Select(g => (UserId(g), g));

    /// <summary>userdata\&lt;id&gt;\config\grid → &lt;id&gt;.</summary>
    private static string UserId(string grid) => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(grid)))!;

    /// <summary>Grid art of the non-Steam shortcut that starts <paramref name="executable"/>, if there is one.</summary>
    public static SteamArt? ForShortcut(string executable) =>
        _shortcuts.Value.TryGetValue(Path.GetFullPath(executable), out var shortcut) ? Find([shortcut.UserConfig], shortcut.AppId) : null;

    /// <summary>Custom grid art a Steam user set for a Steam app.</summary>
    public static SteamArt? ForApp(int appId) => Find(_userConfigs.Value, (uint)appId);

    private static SteamArt? Find(IEnumerable<string> userConfigs, uint id)
    {
        string? hero = null, logo = null, portrait = null, capsule = null;
        foreach (var config in userConfigs)
        {
            var grid = Path.Combine(config, "grid");
            if (!Directory.Exists(grid))
            {
                continue;
            }
            hero ??= GridFile(grid, $"{id}_hero");
            logo ??= GridFile(grid, $"{id}_logo");
            portrait ??= GridFile(grid, $"{id}p");
            capsule ??= GridFile(grid, $"{id}");
        }
        return hero is null && logo is null && portrait is null && capsule is null ? null : new SteamArt(hero, logo, portrait, capsule);
    }

    private static string? GridFile(string grid, string stem)
    {
        foreach (var extension in _extensions)
        {
            var path = Path.Combine(grid, stem + extension);
            if (File.Exists(path))
            {
                // "<user id>/<file>": each user's grid folder is its own virtual host (see GridFolders).
                return $"{UserId(grid)}/{Path.GetFileName(path)}";
            }
        }
        return null;
    }

    /// <summary>
    /// (appid, exe) of every shortcut in a binary shortcuts.vdf: typed fields (0 object, 1 string, 2 int32, 3 float,
    /// 4 pointer, 6 color, 7/10 64-bit), each "type name\0 value", objects closed by 8. A damaged tail keeps what was read.
    /// </summary>
    internal static List<(uint AppId, string Exe)> ReadShortcuts(byte[] data)
    {
        var found = new List<(uint, string)>();
        var pos = 0;
        try
        {
            ReadObject(data, ref pos, found);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidDataException)
        {
            Log.Info($"steam grid: shortcuts.vdf unreadable at byte {pos}");
        }
        return found;
    }

    private static void ReadObject(byte[] data, ref int pos, List<(uint, string)> found)
    {
        uint? appId = null;
        string? exe = null;
        while (pos < data.Length)
        {
            var type = data[pos++];
            if (type == 8)
            {
                break;
            }
            var name = ReadString(data, ref pos);
            switch (type)
            {
                case 0:
                    ReadObject(data, ref pos, found);
                    break;
                case 1:
                    var value = ReadString(data, ref pos);
                    if (name.Equals("exe", StringComparison.OrdinalIgnoreCase))
                    {
                        exe = value;
                    }
                    break;
                case 2:
                    var number = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos, 4));
                    pos += 4;
                    if (name.Equals("appid", StringComparison.OrdinalIgnoreCase))
                    {
                        appId = number;
                    }
                    break;
                case 3 or 4 or 6:
                    pos += 4;
                    break;
                case 7 or 10:
                    pos += 8;
                    break;
                default:
                    throw new InvalidDataException($"field type {type}");
            }
        }
        if (appId is { } id && exe is { } executable)
        {
            found.Add((id, executable));
        }
    }

    private static string ReadString(byte[] data, ref int pos)
    {
        var end = Array.IndexOf(data, (byte)0, pos);
        if (end < 0)
        {
            throw new InvalidDataException("unterminated string");
        }
        var value = Encoding.UTF8.GetString(data, pos, end - pos);
        pos = end + 1;
        return value;
    }
}
