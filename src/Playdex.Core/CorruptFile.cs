using System.Globalization;

namespace Playdex.Core;

/// <summary>
/// A state file that can't be read (not valid JSON) is moved aside rather than deleted or left to stop the app: the app
/// starts afresh and the old file stays next to it, name.json.corrupt-yyyyMMdd-HHmmss, to look at or copy from.
/// </summary>
internal static class CorruptFile
{
    /// <summary>The path <paramref name="path"/> was moved to, or null when it could not be moved (it stays as it is).</summary>
    public static string? MoveAside(string path, DateTimeOffset? now = null)
    {
        var aside = $"{path}.corrupt-{(now ?? DateTimeOffset.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        try
        {
            File.Move(path, aside, overwrite: true);
            Log.Info($"moved the unreadable {path} to {aside}");
            return aside;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"could not move the unreadable {path} aside", ex);
            return null;
        }
    }
}
