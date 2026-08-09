namespace Wrapper;

internal sealed record LauncherManifest(string ConfigPath, string GameId)
{
    public static string GetPath(string executablePath) => Path.ChangeExtension(executablePath, AppBrand.LauncherManifestExtension);

    public static LauncherManifest Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var manifest = JsonSerializer.Deserialize<LauncherManifest>(stream, JsonOptions.Create());
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.ConfigPath) || string.IsNullOrWhiteSpace(manifest.GameId))
            {
                throw new InvalidDataException("The launcher manifest must contain configPath and gameId.");
            }

            return !Path.IsPathFullyQualified(manifest.ConfigPath)
                ? throw new InvalidDataException("The launcher manifest configPath must be an absolute path.")
                : manifest;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Launcher manifest '{path}' is invalid: {ex.Message}", ex);
        }
    }
}

internal static class LauncherRouting
{
    public const string GeneratedLauncherPrefix = AppBrand.LauncherPrefix;

    public static LauncherManifest? Resolve(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        var manifestPath = LauncherManifest.GetPath(processPath);
        if (File.Exists(manifestPath))
        {
            return LauncherManifest.Load(manifestPath);
        }

        return Path.GetFileName(processPath).StartsWith(GeneratedLauncherPrefix, StringComparison.OrdinalIgnoreCase)
            ? throw new InvalidDataException($"Generated launcher manifest is missing: {manifestPath}")
            : null;
    }
}
