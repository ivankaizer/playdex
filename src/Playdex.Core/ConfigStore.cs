using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Core;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(LegacyLauncherConfig))]
internal sealed partial class ConfigJson : JsonSerializerContext;

/// <summary>The launcher's config.json before the merge.</summary>
internal sealed record LegacyLauncherConfig(string? ApiKey, string? ApiBaseUrl);

/// <summary>
/// Reads and writes a <see cref="AppConfig"/>. Saves are atomic (a temporary file, then a move). The install's store,
/// <see cref="Default"/>, is created on first use from the Wrapper's and the launcher's configs from before the merge,
/// which are left in place.
/// </summary>
public sealed class ConfigStore
{
    /// <summary>
    /// API URLs that used to be the default and were written into configs: the Wrapper's (it answers POSTs with 405) and
    /// playdex-api.ejwan.dev (retired). A config that still has one gets <see cref="ApiConfig.DefaultBaseUrl"/>.
    /// </summary>
    private static readonly string[] _retiredDefaultBaseUrls = ["https://playdex.ejwan.dev/api", "https://playdex-api.ejwan.dev/api"];

    private readonly string? _legacyWrapperConfig;
    private readonly string? _legacyLauncherConfig;

    /// <summary>A store at <paramref name="path"/> that starts empty when the file doesn't exist.</summary>
    public ConfigStore(string path)
        : this(path, null, null)
    {
    }

    internal ConfigStore(string path, string? legacyWrapperConfig, string? legacyLauncherConfig)
    {
        PathToConfig = Path.GetFullPath(path);
        _legacyWrapperConfig = legacyWrapperConfig;
        _legacyLauncherConfig = legacyLauncherConfig;
    }

    public string PathToConfig { get; }

    /// <summary>The install's config, %LOCALAPPDATA%\Playdex\config.json.</summary>
    public static ConfigStore Default() => new(
        Paths.ConfigFile,
        Path.Combine(Paths.LegacyWrapperDirectory, "config.json"),
        Path.Combine(Paths.LegacyLauncherDirectory, "config.json"));

    /// <summary>
    /// True for the install's config file and for the Wrapper's from before the merge, which generated launchers made
    /// by an older Wrapper still name.
    /// </summary>
    public static bool IsDefaultPath(string path) =>
        string.Equals(Path.GetFullPath(path), Paths.ConfigFile, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetFullPath(path), Path.Combine(Paths.LegacyWrapperDirectory, "config.json"), StringComparison.OrdinalIgnoreCase);

    public AppConfig LoadOrCreate()
    {
        if (!File.Exists(PathToConfig))
        {
            var created = Migrate() ?? new AppConfig();
            Save(created);
            return created;
        }

        using var stream = File.OpenRead(PathToConfig);
        return Normalize(JsonSerializer.Deserialize(stream, ConfigJson.Default.AppConfig) ?? new AppConfig());
    }

    /// <summary>
    /// <see cref="LoadOrCreate"/> for the app's start: a config that isn't valid JSON doesn't stop the app. It is moved
    /// aside (config.json.corrupt-…, see <see cref="CorruptFile"/>) and a default config takes its place, so the app
    /// opens and the games, keys and settings can be set up again or copied back from the old file.
    /// </summary>
    public AppConfig LoadOrRecover()
    {
        try
        {
            return LoadOrCreate();
        }
        catch (JsonException ex)
        {
            Log.Error($"config: {PathToConfig} is not valid JSON; starting with a default config", ex);
            var config = new AppConfig();
            if (CorruptFile.MoveAside(PathToConfig) is not null)
            {
                Save(config);
                return config;
            }
            // Not moved: the default config stays in memory, and the next save replaces the unreadable file.
            return Normalize(config);
        }
    }

    public async Task<AppConfig> LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PathToConfig))
        {
            var created = Migrate() ?? new AppConfig();
            await SaveAsync(created, cancellationToken);
            return created;
        }

        await using var stream = File.OpenRead(PathToConfig);
        var config = await JsonSerializer.DeserializeAsync(stream, ConfigJson.Default.AppConfig, cancellationToken);
        return Normalize(config ?? new AppConfig());
    }

    public void Save(AppConfig config)
    {
        Normalize(config);
        var temporary = TemporaryPath();
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, config, ConfigJson.Default.AppConfig);
            }

            File.Move(temporary, PathToConfig, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        Normalize(config);
        var temporary = TemporaryPath();
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, config, ConfigJson.Default.AppConfig, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, PathToConfig, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private string TemporaryPath()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathToConfig)!);
        return $"{PathToConfig}.{Guid.NewGuid():N}.tmp";
    }

    /// <summary>
    /// The first config of a merged install: the Wrapper's games and settings, its storage folders kept where they are
    /// (Steam shortcuts point at its launchers), and the launcher's API key and URL where the Wrapper has none.
    /// Per-game API keys are dropped: one key per install. Null when there is nothing to migrate.
    /// </summary>
    private AppConfig? Migrate()
    {
        AppConfig? config = null;
        if (_legacyWrapperConfig is not null && File.Exists(_legacyWrapperConfig))
        {
            try
            {
                var json = File.ReadAllText(_legacyWrapperConfig);
                config = JsonSerializer.Deserialize(json, ConfigJson.Default.AppConfig) ?? new AppConfig();
                using var document = JsonDocument.Parse(json);
                KeepLegacyStorage(config, document.RootElement);
                DropGameApiKeys(config, document.RootElement);
                Log.Info($"config: migrated {config.Games.Count} game(s) from {_legacyWrapperConfig}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Error($"config: could not migrate {_legacyWrapperConfig}", ex);
                config = null;
            }
        }

        if (_legacyLauncherConfig is not null && File.Exists(_legacyLauncherConfig))
        {
            try
            {
                var launcher = JsonSerializer.Deserialize(File.ReadAllText(_legacyLauncherConfig), ConfigJson.Default.LegacyLauncherConfig);
                var fromWrapper = config is not null;
                config ??= new AppConfig();
                if (string.IsNullOrWhiteSpace(config.Api.ApiKey) && !string.IsNullOrWhiteSpace(launcher?.ApiKey))
                {
                    config.Api.ApiKey = launcher.ApiKey.Trim();
                }
                if (!fromWrapper && !string.IsNullOrWhiteSpace(launcher?.ApiBaseUrl))
                {
                    config.Api.BaseUrl = launcher.ApiBaseUrl.Trim();
                }
                Log.Info($"config: migrated the API settings from {_legacyLauncherConfig}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Error($"config: could not migrate {_legacyLauncherConfig}", ex);
            }
        }

        return config;
    }

    /// <summary>A storage folder the old config left out was the Wrapper's default, under its own folder.</summary>
    private static void KeepLegacyStorage(AppConfig config, JsonElement root)
    {
        string Folder(string property, string folder) =>
            root.TryGetProperty("storage", out var storage) && storage.ValueKind == JsonValueKind.Object
            && storage.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : Path.Combine(Paths.LegacyWrapperDirectory, folder);

        config.Storage = new StorageConfig
        {
            StateDirectory = Folder("stateDirectory", "state"),
            LogDirectory = Folder("logDirectory", "logs"),
            LauncherDirectory = Folder("launcherDirectory", "launchers"),
        };
    }

    /// <summary>Per-game keys go away. If the install has no key of its own, the first game's key becomes it.</summary>
    private static void DropGameApiKeys(AppConfig config, JsonElement root)
    {
        if (!root.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        var keys = games.EnumerateObject()
            .Select(g => g.Value.ValueKind == JsonValueKind.Object && g.Value.TryGetProperty("apiKey", out var key)
                && key.ValueKind == JsonValueKind.String ? key.GetString()?.Trim() : null)
            .OfType<string>()
            .Where(k => k.Length > 0)
            .ToList();
        if (keys.Count == 0)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(config.Api.ApiKey))
        {
            config.Api.ApiKey = keys[0];
        }
        Log.Info($"config: dropped the per-game API key of {keys.Count} game(s); every game now uses the install's key");
    }

    private static AppConfig Normalize(AppConfig config)
    {
        config.Games = new Dictionary<string, GameConfig>(
            config.Games ?? new Dictionary<string, GameConfig>(),
            StringComparer.OrdinalIgnoreCase);
        config.Api ??= new ApiConfig();
        if (_retiredDefaultBaseUrls.Contains(config.Api.BaseUrl?.Trim().TrimEnd('/'), StringComparer.OrdinalIgnoreCase))
        {
            config.Api.BaseUrl = ApiConfig.DefaultBaseUrl;
        }
        config.Api.Platform = ApiConfig.NormalizePlatform(config.Api.Platform);
        config.SteamGridDb ??= new SteamGridDbConfig();
        config.SteamGridDb.ApiKey = SteamGridDbConfig.NormalizeApiKey(config.SteamGridDb.ApiKey);
        config.Ludusavi ??= new LudusaviConfig();
        config.Ludusavi.ExecutablePath = LudusaviConfig.NormalizeExecutablePath(
            config.Ludusavi.ExecutablePath,
            requireExisting: false);
        var storage = config.Storage ?? new StorageConfig();
        config.Storage = new StorageConfig
        {
            StateDirectory = NormalizeStoragePath(storage.StateDirectory, Paths.DefaultStateDirectory),
            LogDirectory = NormalizeStoragePath(storage.LogDirectory, Paths.DefaultLogDirectory),
            LauncherDirectory = NormalizeStoragePath(storage.LauncherDirectory, Paths.DefaultLauncherDirectory),
        };
        config.Shell ??= new ShellConfig();
        config.Shell.Mode = ShellConfig.NormalizeMode(config.Shell.Mode);
        config.Apps = AppShortcuts.Normalize(config.Apps ?? []);

        foreach (var game in config.Games.Values)
        {
            game.Platform = ApiConfig.NormalizePlatformOverride(game.Platform);
            game.Launch ??= new LaunchConfig();
            game.Launch.Arguments ??= [];
            game.Monitor ??= new MonitorConfig();
            game.Backup ??= new BackupConfig();
            game.Backup.LudusaviGameTitle = string.IsNullOrWhiteSpace(game.Backup.LudusaviGameTitle)
                ? null
                : game.Backup.LudusaviGameTitle.Trim();
        }

        return config;
    }

    private static string NormalizeStoragePath(string? configuredPath, string defaultPath) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath) ? defaultPath : configuredPath);
}
