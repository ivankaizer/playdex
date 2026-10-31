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
        var webDsn = Extensions.NormalizeSetting(configuration["SENTRY_WEB_DSN"])
            ?? Extensions.NormalizeSetting(configuration["Sentry:WebDsn"])
            ?? apiDsn;
        var environment = Extensions.NormalizeSetting(configuration["SENTRY_ENVIRONMENT"])
            ?? Extensions.NormalizeSetting(configuration["Sentry:Environment"])
            ?? hostEnvironment.EnvironmentName;
        var release = Extensions.NormalizeSetting(configuration["SENTRY_RELEASE"])
            ?? Extensions.NormalizeSetting(configuration["Sentry:Release"]);

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
        // Errors and their breadcrumbs only: logs are exported over OTLP (OTLP_ENDPOINT), not paid for twice.
        options.EnableLogs = false;
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
}

public sealed record BrowserSentrySettings(
    string? Dsn,
    string Environment,
    string? Release,
    double TracesSampleRate);
