namespace Wrapper;

internal sealed partial class LauncherManager(ILogger<LauncherManager> logger)
{
    private const int MaximumReadableIdLength = 48;

    public IReadOnlyList<string> SynchronizeAll(AppConfig config, string configPath, string sourceExecutable)
    {
        var paths = new List<string>();
        foreach (var game in config.Games.Values.OrderBy(game => game.GameId, StringComparer.OrdinalIgnoreCase))
        {
            paths.Add(SynchronizeGame(config.Storage, game, configPath, sourceExecutable));
        }

        return paths;
    }

    public string SynchronizeGame(StorageConfig storage, GameConfig game, string configPath, string sourceExecutable)
    {
        var requestedSource = Path.GetFullPath(sourceExecutable);
        sourceExecutable = ResolveSingleFileSource(requestedSource);
        if (!string.Equals(requestedSource, sourceExecutable, StringComparison.OrdinalIgnoreCase))
        {
            LogUsingSingleFileSource(logger, sourceExecutable);
        }

        var launcherPath = GetLauncherPath(storage, game);
        Directory.CreateDirectory(Path.GetDirectoryName(launcherPath)!);
        RefreshExecutable(sourceExecutable, launcherPath);

        var manifest = new LauncherManifest(Path.GetFullPath(configPath), game.GameId);
        WriteManifestAtomically(LauncherManifest.GetPath(launcherPath), manifest);
        LogLauncherSynchronized(logger, game.GameId, launcherPath);
        return launcherPath;
    }

    public static string ResolveSingleFileSource(string sourceExecutable)
    {
        sourceExecutable = Path.GetFullPath(sourceExecutable);
        if (!File.Exists(sourceExecutable))
        {
            throw new FileNotFoundException("The wrapper executable used to create launchers was not found.", sourceExecutable);
        }

        if (!File.Exists(Path.ChangeExtension(sourceExecutable, ".dll")))
        {
            return sourceExecutable;
        }

        var sourceDirectory = Path.GetDirectoryName(sourceExecutable)!;
        var executableName = Path.GetFileName(sourceExecutable);
        var parentDirectory = Directory.GetParent(sourceDirectory)?.FullName;
        var candidates = new List<string>
        {
            Path.Combine(sourceDirectory, "single-file", executableName)
        };
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            candidates.Add(Path.Combine(parentDirectory, "single-file", executableName));
        }

        var binDirectory = FindAncestorDirectory(sourceDirectory, "bin");
        var releaseDirectory = binDirectory is null ? null : Path.Combine(binDirectory, "Release");
        if (releaseDirectory is not null && Directory.Exists(releaseDirectory))
        {
            try
            {
                candidates.AddRange(
                    Directory.EnumerateFiles(releaseDirectory, executableName, SearchOption.AllDirectories)
                        .Where(path => string.Equals(
                            Path.GetFileName(Path.GetDirectoryName(path)),
                            "single-file",
                            StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(File.GetLastWriteTimeUtc));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Direct sibling candidates can still be used when the bounded build-tree search is unavailable.
            }
        }

        var candidate = candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(path => File.Exists(path) && !File.Exists(Path.ChangeExtension(path, ".dll")));
        if (candidate is not null)
        {
            return candidate;
        }

        throw new InvalidOperationException(
            $"Per-game launchers require the published single-file wrapper. Started from '{sourceExecutable}' and no sibling single-file build was found.");
    }

    private static string? FindAncestorDirectory(string startPath, string directoryName)
    {
        for (var current = new DirectoryInfo(startPath); current is not null; current = current.Parent)
        {
            if (string.Equals(current.Name, directoryName, StringComparison.OrdinalIgnoreCase))
            {
                return current.FullName;
            }
        }

        return null;
    }

    public static string GetLauncherDirectory(StorageConfig storage) =>
        storage.LauncherDirectory;

    public static string GetLauncherPath(StorageConfig storage, GameConfig game)
    {
        var readableId = SanitizeFileName(game.GameId);
        if (readableId.Length > MaximumReadableIdLength)
        {
            readableId = readableId[..MaximumReadableIdLength];
        }

        if (string.IsNullOrWhiteSpace(readableId))
        {
            readableId = "game";
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(game.GameId)))[..8].ToLowerInvariant();
        return Path.Combine(GetLauncherDirectory(storage), $"{LauncherRouting.GeneratedLauncherPrefix}{readableId}-{hash}.exe");
    }

    public void RemoveLauncherArtifacts(StorageConfig storage, GameConfig game)
    {
        var launcherPath = GetLauncherPath(storage, game);
        TryDeleteGeneratedFile(launcherPath);
        TryDeleteGeneratedFile(LauncherManifest.GetPath(launcherPath));
    }

    private void RefreshExecutable(string sourceExecutable, string launcherPath)
    {
        if (string.Equals(sourceExecutable, launcherPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.Delete(launcherPath);
        if (CreateHardLink(launcherPath, sourceExecutable, IntPtr.Zero))
        {
            LogHardLinkCreated(logger, launcherPath);
            return;
        }

        var error = Marshal.GetLastWin32Error();
        File.Copy(sourceExecutable, launcherPath, overwrite: true);
        LogHardLinkUnavailable(logger, error, launcherPath);
    }

    private void TryDeleteGeneratedFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                LogArtifactRemoved(logger, path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogArtifactRemovalFailed(logger, path, ex);
        }
    }

    private static void WriteManifestAtomically(string path, LauncherManifest manifest)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, manifest, JsonOptions.Create());
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character)).Trim(' ', '.');
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Using single-file launcher source: {SourceExecutable}")]
    private static partial void LogUsingSingleFileSource(ILogger logger, string sourceExecutable);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Synchronized launcher for {GameId}: {LauncherPath}")]
    private static partial void LogLauncherSynchronized(ILogger logger, string gameId, string launcherPath);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Created launcher hard link: {LauncherPath}")]
    private static partial void LogHardLinkCreated(ILogger logger, string launcherPath);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Hard link unavailable ({Error}); copied launcher executable: {LauncherPath}")]
    private static partial void LogHardLinkUnavailable(ILogger logger, int error, string launcherPath);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Removed generated launcher artifact: {Path}")]
    private static partial void LogArtifactRemoved(ILogger logger, string path);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Could not remove generated launcher artifact: {Path}")]
    private static partial void LogArtifactRemovalFailed(ILogger logger, string path, Exception exception);
}
