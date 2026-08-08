using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Api.Modules.Telegram;

public sealed record TelegramUpdate(long UpdateId, TelegramMessage? Message);
public sealed record TelegramMessage(long MessageId, TelegramChat Chat, string? Text);
public sealed record TelegramChat(long Id);

public sealed class TelegramClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
    private readonly HttpClient _httpClient;
    private readonly string? _botToken;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramClient> _logger;

    public TelegramClient(
        HttpClient httpClient,
        IConfiguration configuration,
        IOptions<TelegramOptions> options,
        ILogger<TelegramClient> logger)
        : this(httpClient, configuration["TELEGRAM_BOT_TOKEN"], options, logger)
    {
    }

    internal TelegramClient(
        HttpClient httpClient,
        string? botToken,
        IOptions<TelegramOptions>? options = null,
        ILogger<TelegramClient>? logger = null)
    {
        _httpClient = httpClient;
        _botToken = string.IsNullOrWhiteSpace(botToken) ? null : botToken.Trim();
        _options = options?.Value ?? new TelegramOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TelegramClient>.Instance;
    }

    public bool IsEnabled => _botToken is not null || _options.UseMock;

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        long? offset,
        CancellationToken ct)
    {
        if (_options.UseMock)
        {
            _logger.LogInformation(
                "TELEGRAM_MOCK getUpdates offset={Offset} — returning no updates (mock).",
                offset);
            return [];
        }

        EnsureEnabled();
        var url = MethodUrl("getUpdates") + "?timeout=30";
        if (offset.HasValue) url += $"&offset={offset.Value}";

        using var response = await _httpClient.GetAsync(url, ct);
        var body = await ReadEnvelope<IReadOnlyList<TelegramApiUpdate>>(response, ct);
        return (body ?? [])
            .Select(update => new TelegramUpdate(
                update.UpdateId,
                update.Message is null
                    ? null
                    : new TelegramMessage(
                        update.Message.MessageId,
                        new TelegramChat(update.Message.Chat.Id),
                        update.Message.Text)))
            .ToList();
    }

    public async Task SendMessageAsync(long chatId, string text, CancellationToken ct)
    {
        EnsureEnabled();
        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["disable_web_page_preview"] = true,
        };
        if (_options.UseMock)
        {
            _logger.LogInformation(
                "TELEGRAM_MOCK sendMessage {Payload}",
                JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }
        using var response = await _httpClient.PostAsJsonAsync(
            MethodUrl("sendMessage"), payload, JsonOptions, ct);
        await ReadEnvelope<JsonElement>(response, ct);
    }

    public async Task SendPhotoAsync(
        long chatId,
        byte[] photo,
        string caption,
        CancellationToken ct)
    {
        EnsureEnabled();
        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = chatId,
            ["caption"] = caption,
            ["photo_file_name"] = "notification.webp",
            ["photo_content_type"] = "image/webp",
            ["photo_base64"] = Convert.ToBase64String(photo),
        };
        if (_options.UseMock)
        {
            _logger.LogInformation(
                "TELEGRAM_MOCK sendPhoto {Payload}",
                JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(chatId.ToString()), "chat_id");
        content.Add(new StringContent(caption), "caption");
        var image = new ByteArrayContent(photo);
        image.Headers.ContentType = new("image/webp");
        content.Add(image, "photo", "notification.webp");
        using var response = await _httpClient.PostAsync(MethodUrl("sendPhoto"), content, ct);
        await ReadEnvelope<JsonElement>(response, ct);
    }

    private async Task<T?> ReadEnvelope<T>(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        TelegramApiEnvelope<T>? envelope;
        try
        {
            envelope = await response.Content
                .ReadFromJsonAsync<TelegramApiEnvelope<T>>(JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new TelegramApiException(
                $"Telegram returned HTTP {(int)response.StatusCode} with an invalid response.", ex);
        }

        if (!response.IsSuccessStatusCode || envelope?.Ok != true)
        {
            var description = envelope?.Description;
            throw new TelegramApiException(string.IsNullOrWhiteSpace(description)
                ? $"Telegram request failed with HTTP {(int)response.StatusCode}."
                : description);
        }
        return envelope.Result;
    }

    private string MethodUrl(string method) => $"https://api.telegram.org/bot{_botToken}/{method}";

    private void EnsureEnabled()
    {
        if (!IsEnabled)
            throw new InvalidOperationException("TELEGRAM_BOT_TOKEN is not configured.");
    }

    private sealed record TelegramApiEnvelope<T>(bool Ok, string? Description, T? Result);
    private sealed record TelegramApiUpdate(
        [property: JsonPropertyName("update_id")] long UpdateId,
        [property: JsonPropertyName("message")] TelegramApiMessage? Message);
    private sealed record TelegramApiMessage(
        [property: JsonPropertyName("message_id")] long MessageId,
        [property: JsonPropertyName("chat")] TelegramApiChat Chat,
        [property: JsonPropertyName("text")] string? Text);
    private sealed record TelegramApiChat([property: JsonPropertyName("id")] long Id);
}

public sealed class TelegramApiException(string message, Exception? innerException = null)
    : Exception(message, innerException);
