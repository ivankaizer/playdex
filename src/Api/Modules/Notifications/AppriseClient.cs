using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Modules.Notifications;

public sealed partial class AppriseClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly AppriseOptions _options;
    private readonly ILogger<AppriseClient> _logger;

    [LoggerMessage(Level = LogLevel.Information, Message = "APPRISE_MOCK notify {ConfigurationKey} {Payload}")]
    private static partial void LogMockNotify(
        ILogger logger,
        string configurationKey,
        string payload);

    public AppriseClient(
        HttpClient httpClient,
        IOptions<AppriseOptions> options,
        ILogger<AppriseClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    internal AppriseClient(
        HttpClient httpClient,
        AppriseOptions? options = null,
        ILogger<AppriseClient>? logger = null)
    {
        _httpClient = httpClient;
        _options = options ?? new AppriseOptions { BaseUrl = httpClient.BaseAddress?.ToString() };
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AppriseClient>.Instance;
    }

    public bool IsEnabled => _options.UseMock || _httpClient.BaseAddress is not null;

    public async Task SendMessageAsync(
        string telegramChatId,
        string body,
        CancellationToken ct)
    {
        EnsureEnabled();
        var normalizedChatId = NormalizeTelegramChatId(telegramChatId);
        var configurationKey = TelegramConfigurationKey(normalizedChatId);
        var payload = new ApprisePayload(body, "Playdex", "info", "text");
        if (_options.UseMock)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                LogMockNotify(_logger, normalizedChatId, payloadJson);
            }
            return;
        }

        using var firstResponse = await _httpClient.PostAsJsonAsync(
            NotifyUrl(configurationKey), payload, JsonOptions, ct);
        if (firstResponse.StatusCode != System.Net.HttpStatusCode.NoContent)
        {
            EnsureDelivered(firstResponse, configurationKey);
            return;
        }

        await ConfigureTelegramAsync(normalizedChatId, ct);
        using var retryResponse = await _httpClient.PostAsJsonAsync(
            NotifyUrl(configurationKey), payload, JsonOptions, ct);
        EnsureDelivered(retryResponse, configurationKey);
    }

    public async Task SendAttachmentAsync(
        string telegramChatId,
        byte[] attachment,
        string body,
        CancellationToken ct)
    {
        EnsureEnabled();
        var normalizedChatId = NormalizeTelegramChatId(telegramChatId);
        var configurationKey = TelegramConfigurationKey(normalizedChatId);
        if (_options.UseMock)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var payload = JsonSerializer.Serialize(new
                {
                    body,
                    title = "Playdex",
                    type = "info",
                    format = "text",
                    attachmentFileName = "notification.webp",
                    attachmentContentType = "image/webp",
                    attachmentLength = attachment.Length,
                }, JsonOptions);
                LogMockNotify(_logger, normalizedChatId, payload);
            }
            return;
        }

        using var firstResponse = await PostAttachmentAsync(
            configurationKey, attachment, body, ct);
        if (firstResponse.StatusCode != System.Net.HttpStatusCode.NoContent)
        {
            EnsureDelivered(firstResponse, configurationKey);
            return;
        }

        await ConfigureTelegramAsync(normalizedChatId, ct);
        using var retryResponse = await PostAttachmentAsync(
            configurationKey, attachment, body, ct);
        EnsureDelivered(retryResponse, configurationKey);
    }

    public async Task ConfigureTelegramAsync(
        string telegramChatId,
        CancellationToken ct)
    {
        EnsureEnabled();
        var normalizedChatId = NormalizeTelegramChatId(telegramChatId);
        if (_options.UseMock) return;

        var botToken = NormalizeTelegramBotToken(_options.TelegramBotToken);
        var configurationKey = TelegramConfigurationKey(normalizedChatId);
        var serviceUrl = $"tgram://{botToken}/{normalizedChatId}/";
        using var response = await _httpClient.PostAsJsonAsync(
            $"add/{configurationKey}",
            new AppriseConfigurationPayload(serviceUrl),
            JsonOptions,
            ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new AppriseApiException(
                $"Apprise configuration failed with HTTP {(int)response.StatusCode}.");
        }
    }

    private async Task<HttpResponseMessage> PostAttachmentAsync(
        string configurationKey,
        byte[] attachment,
        string body,
        CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(body), "body");
        content.Add(new StringContent("Playdex"), "title");
        content.Add(new StringContent("info"), "type");
        content.Add(new StringContent("text"), "format");
        var file = new ByteArrayContent(attachment);
        file.Headers.ContentType = new("image/webp");
        content.Add(file, "attach", "notification.webp");

        return await _httpClient.PostAsync(NotifyUrl(configurationKey), content, ct);
    }

    private static string NotifyUrl(string configurationKey) =>
        $"notify/{Uri.EscapeDataString(configurationKey)}";

    private void EnsureEnabled()
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Apprise:BaseUrl is not configured.");
    }

    private static string NormalizeTelegramChatId(string telegramChatId)
    {
        if (!long.TryParse(
                telegramChatId.Trim(),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed) || parsed == 0)
            throw new ArgumentException("Telegram chat ID must be a non-zero integer.", nameof(telegramChatId));
        return parsed.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeTelegramBotToken(string? botToken)
    {
        var value = botToken?.Trim();
        var separator = value?.IndexOf(':') ?? -1;
        if (value is null || separator <= 0 || separator == value.Length - 1 ||
            value[..separator].Any(character => !char.IsAsciiDigit(character)) ||
            value[(separator + 1)..].Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
            throw new InvalidOperationException(
                "Apprise:TelegramBotToken is missing or invalid.");
        return value;
    }

    private static string TelegramConfigurationKey(string telegramChatId) =>
        $"telegram_{telegramChatId.Replace("-", "n", StringComparison.Ordinal)}";

    private static void EnsureDelivered(
        HttpResponseMessage response,
        string configurationKey)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            throw new AppriseApiException(
                $"Apprise has no notification configuration for key '{configurationKey}'.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new AppriseApiException(
                $"Apprise notification failed with HTTP {(int)response.StatusCode}.");
        }
    }

    private sealed record ApprisePayload(
        string Body,
        string Title,
        string Type,
        string Format);

    private sealed record AppriseConfigurationPayload(string Urls);
}

public sealed class AppriseApiException(string message) : Exception(message);
