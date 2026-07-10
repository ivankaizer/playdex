using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Api.Telegram;

// ── Public DTOs ───────────────────────────────────────────────

/// <summary>
/// Represents a single update from the Telegram Bot API (typically a new message).
/// </summary>
public sealed record TelegramUpdate(long UpdateId, TelegramMessage? Message);

/// <summary>
/// A message sent to a chat, with optional text content.
/// </summary>
public sealed record TelegramMessage(long MessageId, TelegramChat Chat, string? Text);

/// <summary>
/// A Telegram chat (direct conversation or group).
/// </summary>
public sealed record TelegramChat(long Id);

// ── HTTP Client ───────────────────────────────────────────────

/// <summary>
/// Thin HTTP client for the Telegram Bot API.
///
/// The <see cref="HttpClient"/> passed to the constructor MUST have its
/// <c>BaseAddress</c> set to <c>https://api.telegram.org/bot{token}/</c>.
/// </summary>
public sealed class TelegramClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Poll <c>getUpdates</c> for incoming messages.
    /// Uses long polling with a 30-second timeout.
    /// </summary>
    /// <param name="offset">
    /// The <c>update_id</c> of the last processed update, or <c>null</c>
    /// to receive all pending updates.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        long? offset, CancellationToken ct)
    {
        var url = "getUpdates?timeout=30";
        if (offset.HasValue)
            url += $"&offset={offset.Value}";

        var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content
            .ReadFromJsonAsync<TelegramApiEnvelope>(JsonOptions, ct);

        if (body?.Ok != true || body.Result is null)
            return [];

        return body.Result
            .Where(api => api.Message is not null)
            .Select(api => new TelegramUpdate(
                api.UpdateId,
                new TelegramMessage(
                    api.Message!.MessageId,
                    new TelegramChat(api.Message.Chat.Id),
                    api.Message.Text)))
            .ToList();
    }

    /// <summary>
    /// Send a plain-text message to a chat.
    /// </summary>
    public async Task SendMessageAsync(long chatId, string text, CancellationToken ct)
    {
        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["disable_web_page_preview"] = true
        };

        var response = await httpClient.PostAsJsonAsync("sendMessage", payload, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    // ── Internal Telegram API envelope ─────────────────────────

    private sealed record TelegramApiEnvelope(
        bool Ok,
        string? Description,
        IReadOnlyList<TelegramApiUpdate>? Result);

    private sealed record TelegramApiUpdate(
        [property: JsonPropertyName("update_id")] long UpdateId,
        [property: JsonPropertyName("message")] TelegramApiMessage? Message);

    private sealed record TelegramApiMessage(
        [property: JsonPropertyName("message_id")] long MessageId,
        [property: JsonPropertyName("chat")] TelegramApiChat Chat,
        [property: JsonPropertyName("text")] string? Text);

    private sealed record TelegramApiChat(
        [property: JsonPropertyName("id")] long Id);
}
