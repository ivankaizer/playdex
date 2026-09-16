namespace Web.Infrastructure.Models;

public sealed record DraftCapabilities(bool Available, string Provider, int MaxTextCharacters, long MaxImageBytes, long MaxImagePixels, int MaxRows, int HistoryRetentionDays = 30);
public sealed record DraftBatchDto(Guid Id, DateTimeOffset CreatedAt, IReadOnlyList<DraftRowDto> Rows);
public sealed record DraftRowDto(Guid Id, int Revision, string Status, string Title, string? Date, int? DurationMinutes, string PlatformKey, ManualMappingSearchResult? Selection, IReadOnlyList<string> Warnings, bool Ready, long? AcceptedSessionId);
public sealed record DraftRowUpdate(int Revision, string Title, string? Date, int? DurationMinutes, string PlatformKey, string? GameId, string? IgdbId);
public sealed record DraftAcceptRow(Guid Id, int Revision);
public sealed record DraftAcceptRequest(IReadOnlyList<DraftAcceptRow> Rows);
public sealed record DraftAcceptResult(Guid Id, long? SessionId, string? Error);
public sealed record DraftAcceptResponse(IReadOnlyList<DraftAcceptResult> Results);
