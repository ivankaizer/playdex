namespace Playdex.Persistence;

public sealed class SessionDraftBatch
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public Guid RequestId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string Timezone { get; set; }
    public DateOnly ReferenceDate { get; set; }
    public List<SessionDraftRow> Rows { get; set; } = [];
}

public sealed class SessionDraftRow
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public int Position { get; set; }
    public required string Title { get; set; }
    public string? Date { get; set; }
    public int? DurationMinutes { get; set; }
    public string PlatformKey { get; set; } = "unknown";
    public string? GameId { get; set; }
    public string? IgdbId { get; set; }
    public string? SelectionJson { get; set; }
    public string[] Warnings { get; set; } = [];
    public int Revision { get; set; } = 1;
    public string Status { get; set; } = "pending";
    // A retry tombstone, deliberately retained when the session is deleted.
    public long? AcceptedSessionId { get; set; }
}
