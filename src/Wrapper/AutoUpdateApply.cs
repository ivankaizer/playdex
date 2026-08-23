namespace Wrapper;

internal static class UpdateCommandLine
{
    private const string ApplyArgument = "--apply-update";
    private const string HealthArgument = "--update-health";

    public static bool TryGetApply(IReadOnlyList<string> arguments, out string manifestPath, out int parentProcessId)
    {
        if (arguments.Count == 3 &&
            string.Equals(arguments[0], ApplyArgument, StringComparison.Ordinal) &&
            Path.IsPathFullyQualified(arguments[1]) &&
            int.TryParse(arguments[2], out parentProcessId) &&
            parentProcessId > 0)
        {
            manifestPath = Path.GetFullPath(arguments[1]);
            return true;
        }

        manifestPath = "";
        parentProcessId = 0;
        return false;
    }

    public static bool TryGetHealthSignal(IReadOnlyList<string> arguments, out string healthSignalPath)
    {
        if (arguments.Count == 2 &&
            string.Equals(arguments[0], HealthArgument, StringComparison.Ordinal) &&
            Path.IsPathFullyQualified(arguments[1]) &&
            UpdatePathSafety.IsUnderRoot(arguments[1], AppBrand.DefaultUpdateDirectory))
        {
            healthSignalPath = Path.GetFullPath(arguments[1]);
            return true;
        }

        healthSignalPath = "";
        return false;
    }

    public static ProcessStartInfo CreateHelperStartInfo(string helperPath, string manifestPath, int parentProcessId)
    {
        var info = CreateHiddenStartInfo(helperPath);
        info.ArgumentList.Add(ApplyArgument);
        info.ArgumentList.Add(Path.GetFullPath(manifestPath));
        info.ArgumentList.Add(parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return info;
    }

    public static ProcessStartInfo CreateUpdatedStartInfo(string targetPath, string healthSignalPath)
    {
        var info = CreateHiddenStartInfo(targetPath);
        info.ArgumentList.Add(HealthArgument);
        info.ArgumentList.Add(Path.GetFullPath(healthSignalPath));
        return info;
    }

    private static ProcessStartInfo CreateHiddenStartInfo(string executablePath) => new(Path.GetFullPath(executablePath))
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!
    };
}

internal static class UpdatePathSafety
{
    public static bool IsUnderRoot(string path, string root)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}

internal static class UpdateHealthSignal
{
    public static void Confirm(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidDataException("The update health path is invalid.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "healthy", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

internal static class UpdateFailureStore
{
    public static string? Consume(string updateRoot)
    {
        var path = Path.Combine(updateRoot, "last-failure.txt");
        try
        {
            if (!File.Exists(path)) return null;
            var message = File.ReadAllText(path).Trim();
            File.Delete(path);
            return string.IsNullOrWhiteSpace(message) ? "The previous update could not be installed." : message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "The previous update could not be installed.";
        }
    }
}

internal static class UpdateApplier
{
    internal static readonly TimeSpan ParentExitTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(30);

    public static async Task<bool> RunAsync(string manifestPath, int parentProcessId, CancellationToken cancellationToken = default)
    {
        StagedUpdateManifest? manifest = null;
        string? backupPath = null;
        Process? updatedProcess = null;
        try
        {
            manifest = await LoadManifestAsync(manifestPath, cancellationToken);
            ValidateApplyManifest(manifest, manifestPath);
            if (!string.Equals(
                Path.GetFullPath(manifest.UpdateRoot).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(AppBrand.DefaultUpdateDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update manifest does not use application update storage.");
            }
            await WaitForParentExitAsync(parentProcessId, ParentExitTimeout, cancellationToken);
            if (!await UpdatePackageStager.VerifyAsync(manifest, cancellationToken))
            {
                throw new InvalidDataException("The staged update failed verification before replacement.");
            }

            backupPath = ReplaceTarget(manifest);
            var startInfo = UpdateCommandLine.CreateUpdatedStartInfo(manifest.TargetExecutablePath, manifest.HealthSignalPath!);
            updatedProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("The updated wrapper did not start.");
            var healthy = await WaitForHealthAsync(updatedProcess, manifest.HealthSignalPath!, HealthTimeout, cancellationToken);
            if (!healthy)
            {
                throw new InvalidOperationException("The updated wrapper did not confirm healthy startup.");
            }

            TryDelete(backupPath);
            TryDelete(manifest.FailurePath!);
            TryDelete(manifestPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or TaskCanceledException)
        {
            if (updatedProcess is { HasExited: false })
            {
                try { updatedProcess.Kill(entireProcessTree: true); } catch (Exception killException) when (killException is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            if (manifest is not null && backupPath is not null)
            {
                TryRestore(manifest.TargetExecutablePath, backupPath);
                TryRelaunchRestored(manifest.TargetExecutablePath);
            }
            if (manifest?.FailurePath is not null)
            {
                WriteFailure(manifest.FailurePath, ex.Message);
            }
            return false;
        }
        finally
        {
            updatedProcess?.Dispose();
        }
    }

    internal static async Task<StagedUpdateManifest> LoadManifestAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length > 64 * 1024) throw new InvalidDataException("The update manifest is too large.");
        return await JsonSerializer.DeserializeAsync<StagedUpdateManifest>(stream, JsonOptions.Create(), cancellationToken)
            ?? throw new InvalidDataException("The update manifest is empty.");
    }

    internal static void ValidateApplyManifest(StagedUpdateManifest manifest, string manifestPath)
    {
        if (!UpdatePackageStager.ValidateManifestPaths(manifest) ||
            string.IsNullOrWhiteSpace(manifest.HealthSignalPath) ||
            string.IsNullOrWhiteSpace(manifest.FailurePath))
        {
            throw new InvalidDataException("The update manifest is invalid.");
        }

        var root = Path.GetFullPath(manifest.UpdateRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var path in new[] { manifestPath, manifest.HealthSignalPath, manifest.FailurePath })
        {
            if (!Path.GetFullPath(path!).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update manifest contains a path outside update storage.");
            }
        }
        if (string.Equals(Path.GetFullPath(manifest.TargetExecutablePath), Path.GetFullPath(Environment.ProcessPath ?? ""), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update helper cannot replace itself.");
        }
    }

    internal static async Task WaitForParentExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (ArgumentException)
        {
            // The parent already exited between helper launch and process lookup.
        }
    }

    internal static string ReplaceTarget(StagedUpdateManifest manifest)
    {
        var targetDirectory = Path.GetDirectoryName(manifest.TargetExecutablePath)
            ?? throw new InvalidDataException("The update target path is invalid.");
        var replacementPath = Path.Combine(targetDirectory, $".{Path.GetFileName(manifest.TargetExecutablePath)}.{Guid.NewGuid():N}.new");
        var backupPath = manifest.TargetExecutablePath + ".update-backup";
        TryDelete(replacementPath);
        TryDelete(backupPath);
        File.Copy(manifest.StagedExecutablePath, replacementPath);
        var copiedHash = UpdatePackageStager.HashFileAsync(replacementPath, CancellationToken.None).GetAwaiter().GetResult();
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(manifest.ExecutableHash),
            Convert.FromHexString(copiedHash)))
        {
            TryDelete(replacementPath);
            throw new InvalidDataException("The replacement executable changed while it was copied.");
        }
        File.Replace(replacementPath, manifest.TargetExecutablePath, backupPath, ignoreMetadataErrors: true);
        return backupPath;
    }

    internal static async Task<bool> WaitForHealthAsync(
        Process process,
        string healthSignalPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(healthSignalPath)) return true;
            if (process.HasExited) return false;
            await Task.Delay(100, cancellationToken);
        }
        return File.Exists(healthSignalPath);
    }

    internal static void TryRestore(string targetPath, string backupPath)
    {
        try
        {
            if (!File.Exists(backupPath)) return;
            if (File.Exists(targetPath)) File.Replace(backupPath, targetPath, null, ignoreMetadataErrors: true);
            else File.Move(backupPath, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryRelaunchRestored(string targetPath)
    {
        try { Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static void WriteFailure(string path, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"The update could not be installed: {message}", new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
