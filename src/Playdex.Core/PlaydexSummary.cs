namespace Playdex.Core;

/// <summary>
/// The user's Playdex data as the launcher shows it (GET /launcher/summary), passed to the page as raw JSON. The last
/// one received is kept in playdex-summary.json, so the page has it on its first frame and offline; <see cref="RefreshAsync"/>
/// replaces it. Without an API key nothing is fetched and the cache is cleared, so another account's data never shows.
/// </summary>
public sealed partial class PlaydexSummary(CaptureClient api, string? file = null) : IDisposable
{
    private readonly string _file = file ?? Path.Combine(Paths.DataDirectory, "playdex-summary.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _current;
    private bool _loaded;

    /// <summary>The last summary received (null: none yet, or no API key). Reads the cache file once.</summary>
    public string? Current
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _current = api.IsConfigured ? ReadCache() : null;
            }
            return _current;
        }
    }

    /// <summary>
    /// Playdex polls the user's Steam account (an active Steam integration), so it records Steam games itself. False
    /// without a summary: the launcher records them then, as an offline launcher with no history must.
    /// </summary>
    public bool SteamPolling
    {
        get
        {
            if (Current is not { } json)
            {
                return false;
            }
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(json);
                return document.RootElement.TryGetProperty("steamPolling", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.True;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>Fetch the summary; true when it changed (then <see cref="Current"/> is the new one).</summary>
    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var before = Current;
            if (!api.IsConfigured)
            {
                _current = null;
                TryDelete();
                return before is not null;
            }
            if (await api.GetLauncherSummaryAsync(ct) is not { } fresh)
            {
                return false; // Offline or failed: keep showing the last one.
            }
            // generatedAt changes on every response; compare the rest.
            if (WithoutGeneratedAt(fresh) == WithoutGeneratedAt(before))
            {
                return false;
            }
            _current = fresh;
            WriteCache(fresh);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? WithoutGeneratedAt(string? json) =>
        json is null ? null : GeneratedAt().Replace(json, "");

    [System.Text.RegularExpressions.GeneratedRegex("\"generatedAt\":\"[^\"]*\"")]
    private static partial System.Text.RegularExpressions.Regex GeneratedAt();

    private string? ReadCache()
    {
        try
        {
            return File.Exists(_file) ? File.ReadAllText(_file) : null;
        }
        catch (IOException ex)
        {
            Log.Info($"playdex: summary cache unreadable: {ex.Message}");
            return null;
        }
    }

    private void WriteCache(string json)
    {
        try
        {
            var temp = _file + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"playdex: summary cache not written: {ex.Message}");
        }
    }

    public void Dispose() => _gate.Dispose();

    private void TryDelete()
    {
        try
        {
            File.Delete(_file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"playdex: summary cache not removed: {ex.Message}");
        }
    }
}
