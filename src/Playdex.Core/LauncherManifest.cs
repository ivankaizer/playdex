using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Core;

public sealed record LauncherManifest(string ConfigPath, string GameId)
{
    public static string GetPath(string executablePath) => Path.ChangeExtension(executablePath, Brand.LauncherManifestExtension);

    public static LauncherManifest Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var manifest = JsonSerializer.Deserialize(stream, LauncherJson.Default.LauncherManifest);
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

public static class LauncherRouting
{
    public const string GeneratedLauncherPrefix = Brand.LauncherPrefix;

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

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LauncherManifest))]
internal sealed partial class LauncherJson : JsonSerializerContext;
