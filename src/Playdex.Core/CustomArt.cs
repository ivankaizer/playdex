namespace Playdex.Core;

/// <summary>
/// Artwork the user picked for a game from SteamGridDB, on the game's page. Playdex keeps it, not Steam, so it works for
/// every game in the library, with or without a Steam shortcut, and wins over all other art (<see cref="Library.Scan"/>).
/// Files are &lt;state&gt;\art\&lt;library id&gt;\&lt;kind&gt;.&lt;SteamGridDB image id&gt;.&lt;ext&gt;: each image its own name, so
/// the page never shows a cached copy of the one it replaced. Paths returned are "&lt;library id&gt;/&lt;file&gt;", served
/// from <see cref="Library.CustomArtHost"/>.
/// </summary>
public static class CustomArt
{
    /// <summary>The kinds the launcher shows: <see cref="SteamArt"/>'s images.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["portrait", "capsule", "hero", "logo"];

    private static readonly string[] _extensions = [".png", ".jpg", ".jpeg", ".webp"];

    public static string Root(StorageConfig storage) => Path.Combine(storage.StateDirectory, "art");

    public static string NormalizeKind(string? kind) =>
        Kinds.FirstOrDefault(k => string.Equals(k, kind, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Unknown artwork kind '{kind}'.");

    /// <summary>The picked art of a game, or null when it has none.</summary>
    public static SteamArt? Find(string root, string libraryId)
    {
        var folder = GameFolder(root, libraryId);
        if (!Directory.Exists(folder))
        {
            return null;
        }
        string? Pick(string kind) => Current(folder, kind) is { } file ? $"{libraryId}/{Path.GetFileName(file)}" : null;
        var art = new SteamArt(Pick("hero"), Pick("logo"), Pick("portrait"), Pick("capsule"));
        return art is { Hero: null, Logo: null, Portrait: null, Capsule: null } ? null : art;
    }

    /// <summary>The kinds the user picked for a game, with the SteamGridDB image each one is.</summary>
    public static IReadOnlyDictionary<string, long> Picked(string root, string libraryId)
    {
        var picked = new Dictionary<string, long>(StringComparer.Ordinal);
        var folder = GameFolder(root, libraryId);
        if (!Directory.Exists(folder))
        {
            return picked;
        }
        foreach (var kind in Kinds)
        {
            // "<kind>.<image id>.<ext>"
            if (Current(folder, kind) is { } file && long.TryParse(Path.GetFileNameWithoutExtension(file).AsSpan(kind.Length + 1), out var imageId))
            {
                picked[kind] = imageId;
            }
        }
        return picked;
    }

    /// <summary>Download a SteamGridDB image as the game's <paramref name="kind"/>, replacing the one picked before.</summary>
    public static async Task SetAsync(
        string root,
        string libraryId,
        string kind,
        long imageId,
        string url,
        SteamGridDbClient client,
        CancellationToken cancellationToken = default)
    {
        kind = NormalizeKind(kind);
        if (!SteamGridDbClient.IsImageUrl(url))
        {
            throw new InvalidOperationException("Only SteamGridDB images can be used as artwork.");
        }
        var folder = GameFolder(root, libraryId);
        Directory.CreateDirectory(folder);
        var extension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        var name = $"{kind}.{imageId}{(_extensions.Contains(extension) ? extension : ".png")}";
        var destination = Path.Combine(folder, name);
        await client.DownloadImageAsync(url, destination, cancellationToken);
        Delete(folder, kind, keep: destination);
    }

    /// <summary>Back to the game's own art for <paramref name="kind"/>.</summary>
    public static void Reset(string root, string libraryId, string kind)
    {
        kind = NormalizeKind(kind);
        var folder = GameFolder(root, libraryId);
        if (Directory.Exists(folder))
        {
            Delete(folder, kind, keep: null);
        }
    }

    /// <summary>Forget all of a game's picked art (the game was removed from Playdex).</summary>
    public static void Remove(string root, string libraryId)
    {
        var folder = GameFolder(root, libraryId);
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"custom art: {folder} not removed: {ex.Message}");
        }
    }

    /// <summary>A library id is a folder name here, so one that could leave the art folder is refused.</summary>
    private static string GameFolder(string root, string libraryId)
    {
        if (string.IsNullOrWhiteSpace(libraryId)
            || libraryId is "." or ".."
            || libraryId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException($"'{libraryId}' is not a library game.");
        }
        return Path.Combine(root, libraryId);
    }

    /// <summary>The newest file picked for <paramref name="kind"/> (an older one stays only while it could not be deleted).</summary>
    private static string? Current(string folder, string kind) =>
        Files(folder, kind).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

    private static IEnumerable<string> Files(string folder, string kind) =>
        Directory.EnumerateFiles(folder, $"{kind}.*")
            .Where(f => _extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

    private static void Delete(string folder, string kind, string? keep)
    {
        foreach (var file in Files(folder, kind).Where(f => !string.Equals(f, keep, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Info($"custom art: {file} not deleted: {ex.Message}");
            }
        }
    }
}
