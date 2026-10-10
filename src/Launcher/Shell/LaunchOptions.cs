namespace Playdex.Shell;

internal sealed record LaunchOptions(
    bool Dev,
    string? UiUrl,
    bool ExitOnReady,
    int BenchInput,
    int BenchIntervalMs,
    bool ExitWhenDone,
    string? LaunchGameId,
    bool SuspendWhileGaming)
{
    /// <summary>
    /// --dev / --ui-url=URL           load the Vite dev server instead of the packaged ui/ folder
    /// --exit-on-ready                close once the UI is interactive (cold-start measurement)
    /// --bench-input=N [--bench-interval=MS] [--exit-when-done]
    ///                                inject N synthetic presses through the input pipeline and log latency
    /// --launch=GAMEID                launch a game once the UI is ready (also playdex-launcher://launch/GAMEID)
    /// --no-suspend                   keep the page running while a game is in front
    /// </summary>
    public static LaunchOptions Parse(string[] args)
    {
        string? Value(string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}=", StringComparison.Ordinal))?[(name.Length + 3)..];
        bool Flag(string name) => args.Contains($"--{name}");
        var uiUrl = Value("ui-url") ?? Environment.GetEnvironmentVariable("PLAYDEX_LAUNCHER_UI_URL");
#if DEBUG
        var dev = !Flag("dist");
#else
        var dev = Flag("dev") || uiUrl is not null;
#endif
        return new LaunchOptions(
            dev,
            uiUrl,
            Flag("exit-on-ready"),
            int.TryParse(Value("bench-input"), out var n) ? n : 0,
            int.TryParse(Value("bench-interval"), out var ms) ? ms : 80,
            Flag("exit-when-done"),
            Value("launch") ?? ProtocolGameId(args.FirstOrDefault(a => a.StartsWith("playdex-launcher:", StringComparison.OrdinalIgnoreCase))),
            !Flag("no-suspend"));
    }

    /// <summary>playdex-launcher://launch/GAMEID → GAMEID.</summary>
    public static string? ProtocolGameId(string? uri)
    {
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Host != "launch")
        {
            return null;
        }
        var id = parsed.AbsolutePath.Trim('/');
        return id.Length > 0 ? Uri.UnescapeDataString(id) : null;
    }
}
