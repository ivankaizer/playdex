namespace Api.Modules.Privacy;

public sealed record DeleteAccountRequest(string? Password);

/// <summary>Everything Playdex stores about one user, for <c>GET /me/export</c>.</summary>
public sealed record AccountExportDto(
    DateTimeOffset ExportedAt,
    ExportProfileDto Profile,
    ExportSettingsDto Settings,
    IReadOnlyList<ExportNotificationChannelDto> NotificationChannels,
    IReadOnlyList<ExportIntegrationDto> Integrations,
    IReadOnlyList<ExportSessionDto> Sessions,
    IReadOnlyList<ExportGameRatingDto> Ratings,
    IReadOnlyList<ExportGameDto> IgnoredGames,
    IReadOnlyList<ExportGameConnectionDto> GameConnections,
    IReadOnlyList<ExportDraftBatchDto> DraftBatches,
    IReadOnlyList<ExportAiGenerationDto> AiGenerations);

public sealed record ExportProfileDto(string Email, DateTimeOffset CreatedAt);

public sealed record ExportSettingsDto(int IgnoreShortSessionsMinutes);

public sealed record ExportNotificationChannelDto(string Transport, string Destination);

/// <summary>Credentials are reported as present or not; the secret itself is never exported.</summary>
public sealed record ExportIntegrationDto(
    string Type, string? Platform, string? AccountId, bool HasApiKey, bool Active, DateTimeOffset CreatedAt);

public sealed record ExportSessionDto(
    string Game, string Platform, string CaptureMethod, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

public sealed record ExportGameDto(string Game);

public sealed record ExportGameRatingDto(string Game, short Rating);

public sealed record ExportGameConnectionDto(string Game, string ConnectedGame);

public sealed record ExportDraftBatchDto(
    DateTimeOffset CreatedAt, string Timezone, DateOnly ReferenceDate, IReadOnlyList<ExportDraftRowDto> Rows);

public sealed record ExportDraftRowDto(string Title, string? Date, int? DurationMinutes, string PlatformKey, string Status);

/// <summary>An AI batch request still within the history retention window; the screenshot is base64.</summary>
public sealed record ExportAiGenerationDto(
    DateTimeOffset CreatedAt,
    string Provider,
    string Model,
    string Status,
    string? InputText,
    string? ImageContentType,
    string? ImageBase64,
    string? ResultJson);
