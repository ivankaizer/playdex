namespace Playdex.Api.Metrics;

public sealed record MetricsSnapshotDto(
    int PollSuccessTotal,
    int PollFailureTotal,
    Dictionary<string, int> PollSuccessByPlatform,
    Dictionary<string, int> PollFailureByPlatform,
    string? LastPollSuccessAt,
    string? LastPollFailureAt,
    long? LastCycleDurationMs
);
