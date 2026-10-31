using System.Diagnostics.Metrics;
using System.Globalization;

namespace Api.Modules.Sessions;

/// <summary><c>playdex.sessions</c>: sessions an integration started and ended, so an account that polls fine but
/// never detects play stands out.</summary>
public static class SessionMetrics
{
    public const string MeterName = "Playdex.Api.Sessions";

    private static readonly Meter _meter = new(MeterName);
    private static readonly Counter<long> _sessions = _meter.CreateCounter<long>(
        "playdex.sessions",
        description: "Sessions started and ended by an integration (polling or the Playdex app).");

    public static void Record(string @event, long integrationId) =>
        _sessions.Add(
            1,
            new("event", @event),
            new("integration_id", integrationId.ToString(CultureInfo.InvariantCulture)));
}
