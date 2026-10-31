namespace Api.Observability;

public static partial class TelemetryLog
{
    /// <summary>Says at startup where errors and telemetry go, so a missing DSN or endpoint is not a silent gap.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Telemetry: Sentry {SentryState}, OTLP export {OtlpState}")]
    public static partial void LogTelemetryTargets(this ILogger logger, string sentryState, string otlpState);
}
