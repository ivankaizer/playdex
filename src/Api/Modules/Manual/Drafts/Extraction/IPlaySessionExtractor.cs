namespace Api.Modules.Manual.Drafts.Extraction;

public sealed class AiDraftOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "openai";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/responses";
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public int MaxTextCharacters { get; set; } = 10_000;
    public int MaxImageBytes { get; set; } = 10_485_760;
    public long MaxImagePixels { get; set; } = 20_000_000;
    public int MaxRows { get; set; } = 50;
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Budget for the IGDB game searches after a successful extraction. When it runs out, the remaining rows are saved
    /// without IGDB candidates rather than failing the request (and discarding the paid extraction).
    /// </summary>
    public int SearchTimeoutSeconds { get; set; } = 20;
    public int RequestsPerHour { get; set; } = 10;
    public int HistoryRetentionDays { get; set; } = 30;
    public bool IsAvailable => Enabled && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model)
        && MaxTextCharacters > 0 && MaxImageBytes > 0 && MaxImagePixels > 0 && MaxRows is > 0 and <= 50
        && TimeoutSeconds > 0 && RequestsPerHour > 0;
}

public sealed record ExtractionInput(string? Text, byte[]? Image, string? ImageContentType,
    DateOnly ReferenceDate, IReadOnlyList<string> PlatformKeys)
{
    public Func<ExtractionResponse, Task>? ObserveResponse { get; init; }
}

public sealed record ExtractionResponse(int HttpStatus, string Body, bool Truncated);

public sealed record ExtractedSession(string Title, string? Date, int? DurationMinutes,
    string? PlatformKey, IReadOnlyList<string> Warnings);

public interface IPlaySessionExtractor
{
    Task<IReadOnlyList<ExtractedSession>> Extract(ExtractionInput input, CancellationToken cancellationToken);
}
