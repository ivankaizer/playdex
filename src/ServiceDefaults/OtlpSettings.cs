using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Where traces, metrics and logs go besides Sentry: an OTLP/HTTP endpoint such as Grafana Cloud's, from
/// <c>OTLP_ENDPOINT</c> and <c>OTLP_HEADERS</c>. Not the standard <c>OTEL_EXPORTER_OTLP_*</c> variables: every OTLP
/// exporter reads those, Sentry's included, which would send this endpoint's credentials to Sentry.
/// </summary>
/// <param name="Endpoint">The base URL; each signal is sent to its <c>/v1/…</c> path under it.</param>
/// <param name="Headers">Comma-separated <c>name=value</c> pairs, as in <c>OTEL_EXPORTER_OTLP_HEADERS</c>.</param>
public sealed record OtlpSettings(Uri Endpoint, string? Headers)
{
    /// <summary>The exporters' options name, apart from the default ones Sentry's exporter configures.</summary>
    private const string OptionsName = "playdex-otlp";

    /// <returns>Null when <c>OTLP_ENDPOINT</c> is not set.</returns>
    public static OtlpSettings? From(IConfiguration configuration)
    {
        if (Extensions.NormalizeSetting(configuration["OTLP_ENDPOINT"]) is not { } endpoint)
        {
            return null;
        }

        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("OTLP_ENDPOINT must be an absolute http(s) URL.");
        }

        return new OtlpSettings(uri, Extensions.NormalizeSetting(configuration["OTLP_HEADERS"]));
    }

    public void AddTo(TracerProviderBuilder tracing) =>
        tracing.AddOtlpExporter(OptionsName, options => Configure(options, "v1/traces"));

    public void AddTo(MeterProviderBuilder metrics) =>
        metrics.AddOtlpExporter(OptionsName, (options, reader) =>
        {
            Configure(options, "v1/metrics");
            // Grafana Cloud stores cumulative series; one export a minute keeps their count and cost down.
            reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
            reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 60_000;
        });

    public void AddTo(LoggerProviderBuilder logging) =>
        logging.AddOtlpExporter(OptionsName, options => Configure(options, "v1/logs"));

    private void Configure(OtlpExporterOptions options, string signalPath)
    {
        options.Endpoint = new Uri(Endpoint, signalPath);
        options.Protocol = OtlpExportProtocol.HttpProtobuf;
        options.Headers = Headers;
    }
}
