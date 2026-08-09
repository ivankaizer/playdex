using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Modules.Telegram;

public sealed partial class TelegramClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
    private readonly HttpClient _httpClient;
    private readonly string? _botToken;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramClient> _logger;

    [LoggerMessage(Level = LogLevel.Information, Message = "TELEGRAM_MOCK sendMessage {Payload}")]
    private static partial void LogMockSendMessage(ILogger logger, string payload);

    [LoggerMessage(Level = LogLevel.Information, Message = "TELEGRAM_MOCK sendPhoto {Payload}")]
    private static partial void LogMockSendPhoto(ILogger logger, string payload);

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
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                LogMockSendMessage(_logger, payloadJson);
            }
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
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                LogMockSendPhoto(_logger, payloadJson);
            }
            return;
        }
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(chatId.ToString(CultureInfo.InvariantCulture)), "chat_id");
        content.Add(new StringContent(caption), "caption");
        var image = new ByteArrayContent(photo);
        image.Headers.ContentType = new("image/webp");
        content.Add(image, "photo", "notification.webp");
        using var response = await _httpClient.PostAsync(MethodUrl("sendPhoto"), content, ct);
        await ReadEnvelope<JsonElement>(response, ct);
    }

    private static async Task<T?> ReadEnvelope<T>(
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
}

public sealed class TelegramApiException(string message, Exception? innerException = null)
    : Exception(message, innerException);
