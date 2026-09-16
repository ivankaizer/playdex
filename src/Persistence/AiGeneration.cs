namespace Playdex.Persistence;

public sealed class AiGeneration
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public Guid RequestId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public required string Timezone { get; set; }
    public DateOnly ReferenceDate { get; set; }
    public string? InputText { get; set; }
    public byte[]? Image { get; set; }
    public string? ImageContentType { get; set; }
    public string Status { get; set; } = "running";
    public string? Error { get; set; }
    public string? ResultJson { get; set; }
    public string ResponsesJson { get; set; } = "[]";
}
