using Microsoft.Extensions.Logging;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensibility;

namespace Api.Observability;

public sealed record PlaydexSentrySettings(
    string? ApiDsn,
    string? WebDsn,
    string Environment,
    string? Release,
    double TracesSampleRate)
{
    public bool IsEnabled => ApiDsn is not null;

    public static PlaydexSentrySettings From(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var apiDsn = Extensions.GetSentryDsn(configuration);
        var webDsn = Normalize(configuration["SENTRY_WEB_DSN"])
            ?? Normalize(configuration["Sentry:WebDsn"])
            ?? apiDsn;
        var environment = Normalize(configuration["SENTRY_ENVIRONMENT"])
            ?? Normalize(configuration["Sentry:Environment"])
            ?? hostEnvironment.EnvironmentName;
        var release = Normalize(configuration["SENTRY_RELEASE"])
            ?? Normalize(configuration["Sentry:Release"]);

        return new PlaydexSentrySettings(
            apiDsn,
            webDsn,
            environment,
            release,
            Extensions.GetTraceSampleRate(configuration));
    }

    public void ConfigureApi(SentryAspNetCoreOptions options)
    {
        options.Dsn = ApiDsn;
        options.Environment = Environment;
        options.Release = Release;
        options.TracesSampleRate = TracesSampleRate;
        options.EnableLogs = true;
        options.MinimumBreadcrumbLevel = LogLevel.Information;
        options.MinimumEventLevel = LogLevel.Error;
        options.SendDefaultPii = false;
        options.MaxRequestBodySize = RequestSize.None;
        options.SetBeforeBreadcrumb(SensitiveUrls.Redact);
        options.DefaultTags["component"] = "api";
        options.AddExceptionFilterForType<OperationCanceledException>();
        options.UseOtlp();
    }

    public BrowserSentrySettings ToBrowserSettings() => new(
        WebDsn,
        Environment,
        Release,
        TracesSampleRate);

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
}

public sealed record BrowserSentrySettings(
    string? Dsn,
    string Environment,
    string? Release,
    double TracesSampleRate);
