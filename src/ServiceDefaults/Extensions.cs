using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        var sentryDsn = GetSentryDsn(builder.Configuration);
        var otlp = OtlpSettings.From(builder.Configuration);
        var traceSampleRate = GetTraceSampleRate(builder.Configuration);
        var serviceName = NormalizeSetting(builder.Configuration["OTEL_SERVICE_NAME"]) ?? builder.Environment.ApplicationName;
        var environment = NormalizeSetting(builder.Configuration["SENTRY_ENVIRONMENT"]) ?? builder.Environment.EnvironmentName;

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName)
                .AddAttributes([new("deployment.environment.name", environment)]))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddNpgsql();

                if (sentryDsn is not null || otlp is not null)
                {
                    tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(traceSampleRate)));
                }

                if (sentryDsn is not null)
                {
                    tracing.AddSentryOtlpExporter(sentryDsn);
                }

                otlp?.AddTo(tracing);
            })
            // Modules add their meters; without an OTLP endpoint nothing reads them.
            .WithMetrics(metrics => otlp?.AddTo(metrics));

        if (otlp is not null)
        {
            telemetry.WithLogging(otlp.AddTo, options =>
            {
                options.IncludeScopes = true;
                options.IncludeFormattedMessage = true;
            });
            // Framework chatter (a line per request, per SQL command, per outgoing HTTP call, per retry) stays on the
            // console: what failed is logged by the code that gave up.
            builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("Microsoft", LogLevel.Warning);
            builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("System", LogLevel.Warning);
            builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("Polly", LogLevel.Error);
            builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("Microsoft.Hosting.Lifetime", LogLevel.Information);
        }

        return builder;
    }

    public static string? GetSentryDsn(IConfiguration configuration)
    {
        return NormalizeSetting(configuration["SENTRY_DSN"])
            ?? NormalizeSetting(configuration["Sentry:Dsn"]);
    }

    public static double GetTraceSampleRate(IConfiguration configuration)
    {
        var configured = configuration["SENTRY_TRACES_SAMPLE_RATE"]
            ?? configuration["Sentry:TracesSampleRate"];
        return double.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
               value is >= 0 and <= 1
            ? value
            : 0.2;
    }

    /// <summary>
    /// A configuration value trimmed, or null when it is blank or still a <c>__PLACEHOLDER__</c> left by a
    /// container entrypoint.
    /// </summary>
    public static string? NormalizeSetting(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ||
               normalized.StartsWith("__", StringComparison.Ordinal) &&
               normalized.EndsWith("__", StringComparison.Ordinal)
            ? null
            : normalized;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");
            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live"),
            });
        }

        return app;
    }
}
