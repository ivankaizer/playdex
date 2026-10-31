using System.Globalization;

namespace Playdex.Core;

/// <summary>
/// A launcher game's own key art (<see cref="StoreGame.KeyArt"/>), copied out of its install so the page is served this
/// folder rather than the game's. Files are &lt;state&gt;\store-art\&lt;library id&gt;\keyart.&lt;stamp&gt;.&lt;ext&gt;, the
/// stamp from the source's size and time: an update that changes the image gets a new name, so the page never shows a
/// cached copy of the old one. Paths returned are "&lt;library id&gt;/&lt;file&gt;", served from
/// <see cref="Library.StoreArtHost"/>.
/// </summary>
public static class StoreArt
{
    public static string Root(StorageConfig storage) => Path.Combine(storage.StateDirectory, "store-art");

    /// <summary>
    /// The game's key art as every kind but the logo (it has the logo in it); null when it has none or it can't be
    /// copied.
    /// </summary>
    public static SteamArt? Find(string root, string libraryId, StoreGame found)
    {
        if (found.KeyArt is not { } source)
        {
            return null;
        }
        try
        {
            var info = new FileInfo(source);
            if (!info.Exists)
            {
                return null;
            }
            var stamp = string.Create(CultureInfo.InvariantCulture, $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}");
            var name = $"keyart.{stamp}{info.Extension.ToLowerInvariant()}";
            var folder = Path.Combine(root, libraryId);
            var copy = Path.Combine(folder, name);
            if (!File.Exists(copy))
            {
                Directory.CreateDirectory(folder);
                foreach (var old in Directory.EnumerateFiles(folder, "keyart.*"))
                {
                    File.Delete(old);
                }
                // Copied under a temporary name first: a scan that stops halfway leaves no broken image behind.
                var partial = copy + ".partial";
                File.Copy(source, partial, overwrite: true);
                File.Move(partial, copy, overwrite: true);
            }
            var path = $"{libraryId}/{name}";
            return new SteamArt(path, null, path, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"store art: {libraryId}'s key art not copied ({ex.Message})");
            return null;
        }
    }
}
