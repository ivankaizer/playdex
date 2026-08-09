using System.Globalization;
using System.Net.Http.Json;

namespace Web.Observability;

public sealed record BrowserSentrySettings(
    string? Dsn,
    string Environment,
    string? Release,
    double TracesSampleRate)
{
    public bool IsEnabled => Normalize(Dsn) is not null;

    public static async Task<BrowserSentrySettings> ResolveAsync(
        IConfiguration configuration,
        string hostBaseAddress,
        string apiBase,
        string fallbackEnvironment)
    {
        var localDsn = Normalize(configuration["Sentry:Dsn"]);
        if (localDsn is not null)
        {
            return new BrowserSentrySettings(
                localDsn,
                Normalize(configuration["Sentry:Environment"]) ?? fallbackEnvironment,
                Normalize(configuration["Sentry:Release"]),
                ParseSampleRate(configuration["Sentry:TracesSampleRate"]));
        }

        try
        {
            var apiUri = new Uri(new Uri(hostBaseAddress), apiBase.TrimEnd('/') + "/");
            using var client = new HttpClient { BaseAddress = apiUri };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var envelope = await client.GetFromJsonAsync<ApiEnvelope<BrowserSentrySettings>>(
                "observability/sentry",
                timeout.Token);

            return envelope?.Data is { } remote
                ? remote with { Dsn = Normalize(remote.Dsn) }
                : Disabled(fallbackEnvironment);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Sentry bootstrap unavailable: {exception.Message}");
            return Disabled(fallbackEnvironment);
        }
    }

    private static BrowserSentrySettings Disabled(string environment) =>
        new(null, environment, null, 0);

    private static double ParseSampleRate(string? configured) =>
        double.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        value is >= 0 and <= 1
            ? value
            : 0.2;

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith("__", StringComparison.Ordinal) &&
            normalized.EndsWith("__", StringComparison.Ordinal))
        {
            return null;
        }

        return normalized;
    }

    private sealed record ApiEnvelope<T>(T? Data);
}
