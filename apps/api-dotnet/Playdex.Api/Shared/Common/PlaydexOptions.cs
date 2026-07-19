using Microsoft.Extensions.Configuration;

namespace Playdex.Api.Shared.Common;

public sealed record PlaydexOptions(
    string DatabaseUrl,
    string JwtSecret,
    int PollIntervalSeconds,
    string AccessTokenTtl,
    IReadOnlyList<string> CorsOrigins,
    string? SteamApiKey,
    string? IgdbClientId,
    string? IgdbClientSecret)
{
    private const int DefaultPollIntervalSeconds = 60;
    private const int MinimumPollIntervalSeconds = 10;
    private const int MaximumPollIntervalSeconds = 3600;

    public static PlaydexOptions FromConfiguration(IConfiguration configuration)
    {
        var databaseUrl = configuration["DATABASE_URL"]?.Trim();
        if (string.IsNullOrEmpty(databaseUrl))
        {
            throw new OptionsValidationException("DATABASE_URL is required.");
        }

        var jwtSecret = configuration["JWT_SECRET"]?.Trim();
        if (string.IsNullOrEmpty(jwtSecret))
        {
            throw new OptionsValidationException("JWT_SECRET is required.");
        }

        return new PlaydexOptions(
            databaseUrl,
            jwtSecret,
            ResolvePollIntervalSeconds(configuration["POLL_INTERVAL_SECONDS"]),
            configuration["ACCESS_TOKEN_TTL"]?.Trim() ?? "15m",
            ResolveCorsOrigins(configuration["CORS_ORIGINS"]),
            configuration["STEAM_API_KEY"]?.Trim(),
            configuration["IGDB_CLIENT_ID"]?.Trim(),
            configuration["IGDB_CLIENT_SECRET"]?.Trim());
    }

    private static int ResolvePollIntervalSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultPollIntervalSeconds;
        }

        if (!int.TryParse(value, out var interval))
        {
            throw new OptionsValidationException("POLL_INTERVAL_SECONDS must be a number.");
        }

        return Math.Clamp(interval, MinimumPollIntervalSeconds, MaximumPollIntervalSeconds);
    }

    private static IReadOnlyList<string> ResolveCorsOrigins(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(origin => origin != "*")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}

public sealed class OptionsValidationException(string message) : Exception(message);
