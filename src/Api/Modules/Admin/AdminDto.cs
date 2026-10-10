namespace Api.Modules.Admin;

public sealed record AdminDashboardCountsDto(int LiveSessions, int LiveUsers, int SessionsToday, int AiErrors);

public sealed record AdminDashboardPlatformDto(string Key, string Name);

public sealed record AdminDashboardSessionDto(
    long Id,
    string Email,
    string Game,
    string? CapsuleUrl,
    string PlatformKey,
    string Platform,
    string? AccountId,
    string Source,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? LastPolledAt);

public sealed record AdminDashboardGenerationDto(
    Guid Id,
    string Email,
    string Model,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record AdminDashboardDto(
    DateTimeOffset UpdatedAt,
    AdminDashboardCountsDto Counts,
    IReadOnlyList<AdminDashboardPlatformDto> Platforms,
    IReadOnlyList<AdminDashboardSessionDto> LiveSessions,
    IReadOnlyList<AdminDashboardSessionDto> EndedSessions,
    IReadOnlyList<AdminDashboardGenerationDto> FailedGenerations);

public sealed record AdminPlatformSummaryDto(string Key, string Name, int Total, int Active)
{
    public bool Enabled => Active > 0;
}

public sealed record AdminSetPlatformEnabledRequest(bool? Enabled);

public sealed record AdminPlatformUpdateResultDto(int Updated);

public sealed record AdminIntegrationDto(
    long Id,
    string Email,
    string Platform,
    string? AccountId,
    bool Active,
    string? ApiKey);

public sealed record AdminUpdateIntegrationRequest(
    bool ApiKeyChanged,
    string? ApiKey,
    bool AccountIdChanged,
    string? AccountId);

public sealed record AdminSessionDto(
    long Id,
    string Email,
    string Platform,
    string? AccountId,
    string Game,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt)
{
    public bool Live => EndedAt is null;
}

public sealed record AdminSessionPageDto(IReadOnlyList<AdminSessionDto> Sessions, int Total, int Page, int PageSize);

public sealed record AdminTransferSessionsRequest(IReadOnlyList<long>? SessionIds, long? TargetIntegrationId);

public sealed record AdminTransferSessionsResultDto(int Transferred);

public sealed record AdminGameDto(long Id, string Title, string? CapsuleUrl, bool TitleLocked, bool CapsuleUrlLocked);

public sealed record AdminUpdateGameRequest(
    string? Title,
    bool TitleChanged,
    string? CapsuleUrl,
    bool CapsuleUrlChanged,
    bool? TitleLocked,
    bool? CapsuleUrlLocked);

public sealed record AdminMergeGamesRequest(long? SourceId, long? TargetId);

public sealed record AdminGenerationSummaryDto(
    Guid Id,
    string Email,
    DateTimeOffset CreatedAt,
    string Provider,
    string Model,
    string Status,
    string? InputPreview,
    bool HasImage);

public sealed record AdminGenerationPageDto(IReadOnlyList<AdminGenerationSummaryDto> Items, int Total, int Page, int PageSize);

public sealed record AdminGenerationResultRowDto(string Title, string? Date, int? DurationMinutes, string PlatformKey);

public sealed record AdminGenerationResponseDto(int HttpStatus, string Body, bool Truncated);

public sealed record AdminGenerationDetailDto(
    Guid Id,
    string Email,
    Guid RequestId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string Provider,
    string Model,
    string Timezone,
    string ReferenceDate,
    string? InputText,
    bool HasImage,
    string Status,
    string? Error,
    string? Result,
    IReadOnlyList<AdminGenerationResultRowDto> Rows,
    IReadOnlyList<AdminGenerationResponseDto> Responses);

public sealed record AdminGenerationImage(byte[] Bytes, string ContentType);
