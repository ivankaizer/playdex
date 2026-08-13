namespace Wrapper;

internal sealed class ConfigStore(string path)
{
    private static readonly JsonSerializerOptions _options = JsonOptions.Create();

    public static string DefaultPath { get; } = Path.Combine(AppBrand.DefaultStorageDirectory, "config.json");

    public string PathToConfig { get; } = path;

    public async Task<WrapperConfig> LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PathToConfig))
        {
            var newConfig = new WrapperConfig();
            await SaveAsync(newConfig, cancellationToken);
            return newConfig;
        }

        await using var stream = File.OpenRead(PathToConfig);
        var config = await JsonSerializer.DeserializeAsync<WrapperConfig>(stream, _options, cancellationToken)
            ?? new WrapperConfig();

        Normalize(config);
        return config;
    }

    public async Task SaveAsync(WrapperConfig config, CancellationToken cancellationToken = default)
    {
        Normalize(config);

        var directory = Path.GetDirectoryName(PathToConfig);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{PathToConfig}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, config, _options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, PathToConfig, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void Normalize(WrapperConfig config)
    {
        config.Games = new Dictionary<string, GameConfig>(
            config.Games ?? new Dictionary<string, GameConfig>(),
            StringComparer.OrdinalIgnoreCase);
        config.Api ??= new ApiConfig();
        config.SteamGridDb ??= new SteamGridDbConfig();
        config.SteamGridDb.ApiKey = SteamGridDbConfig.NormalizeApiKey(config.SteamGridDb.ApiKey);
        config.Ludusavi ??= new LudusaviConfig();
        config.Ludusavi.ExecutablePath = LudusaviConfig.NormalizeExecutablePath(
            config.Ludusavi.ExecutablePath,
            requireExisting: false);
        var storage = config.Storage ?? new StorageConfig();
        config.Storage = new StorageConfig
        {
            StateDirectory = NormalizeStoragePath(storage.StateDirectory, AppBrand.DefaultStateDirectory),
            LogDirectory = NormalizeStoragePath(storage.LogDirectory, AppBrand.DefaultLogDirectory),
            LauncherDirectory = NormalizeStoragePath(storage.LauncherDirectory, AppBrand.DefaultLauncherDirectory)
        };

        foreach (var game in config.Games.Values)
        {
            game.Launch ??= new LaunchConfig();
            game.Launch.Arguments ??= [];
            game.Monitor ??= new MonitorConfig();
            game.Backup ??= new BackupConfig();
            game.Backup.LudusaviGameTitle = string.IsNullOrWhiteSpace(game.Backup.LudusaviGameTitle)
                ? null
                : game.Backup.LudusaviGameTitle.Trim();
        }
    }

    private static string NormalizeStoragePath(string? configuredPath, string defaultPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? defaultPath
            : configuredPath;

        return Path.GetFullPath(path);
    }
}

internal static class JsonOptions
{
    public static JsonSerializerOptions Create() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
