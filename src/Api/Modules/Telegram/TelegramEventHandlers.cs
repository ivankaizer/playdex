using Microsoft.Extensions.Logging;
using Api.Infrastructure.Common;

namespace Api.Modules.Telegram;

public sealed partial class TelegramEventHandlers(
    TelegramRepository repository,
    TelegramClient client,
    TelegramNotificationRenderer renderer,
    ILogger<TelegramEventHandlers> logger) :
    IEventSubscriber<SessionStarted>,
    IEventSubscriber<SessionEnded>,
    IEventSubscriber<PollFailed>
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Telegram poll-failure notification failed for credential {CredentialId}")]
    private partial void LogPollFailureNotificationFailed(Exception exception, long credentialId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram card rendering or photo delivery failed; using text fallback")]
    private partial void LogCardRenderingFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Telegram text fallback failed")]
    private partial void LogTextFallbackFailed(Exception exception);

    public async Task Handle(SessionStarted @event, CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        var target = await repository.GetNotificationTarget(
            @event.CredentialId, @event.GameId, ct);
        if (target is null || !TryChat(target.ChatId, out var chatId)) return;
        var text = "Session started";
        await SendSessionCard(
            chatId,
            target,
            new TelegramNotificationCard(target.GameTitle, target.Platform, false, null),
            text,
            ct);
    }

    public async Task Handle(SessionEnded @event, CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        var target = await repository.GetNotificationTarget(
            @event.CredentialId, @event.GameId, ct);
        if (target is null || !TryChat(target.ChatId, out var chatId)) return;
        var duration = FormatDuration(@event.EndedAt - @event.StartedAt);
        var text = $"Session ended\nDuration: {duration}";
        await SendSessionCard(
            chatId,
            target,
            new TelegramNotificationCard(target.GameTitle, target.Platform, true, duration),
            text,
            ct);
    }

    public async Task Handle(PollFailed @event, CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        var target = await repository.GetCredentialTarget(@event.CredentialId, ct);
        if (target is null || !TryChat(target.ChatId, out var chatId)) return;
        try
        {
            await client.SendMessageAsync(chatId,
                $"Polling failed for {@event.Platform}. " +
                $"Error: {@event.Message}. " +
                $"Retrying in {Math.Round(@event.RetryDelay.TotalSeconds)}s.",
                ct);
        }
        catch (Exception ex)
        {
            LogPollFailureNotificationFailed(ex, @event.CredentialId);
        }
    }

    private async Task SendSessionCard(
        long chatId,
        TelegramNotificationTarget target,
        TelegramNotificationCard card,
        string fallbackText,
        CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        if (!string.IsNullOrWhiteSpace(target.CapsuleUrl))
        {
            try
            {
                var image = await renderer.RenderAsync(card, target.CapsuleUrl, ct);
                await client.SendPhotoAsync(chatId, image, fallbackText, ct);
                return;
            }
            catch (Exception ex)
            {
                LogCardRenderingFailed(ex);
            }
        }

        try
        {
            await client.SendMessageAsync(chatId, $"{fallbackText}.", ct);
        }
        catch (Exception ex)
        {
            LogTextFallbackFailed(ex);
        }
    }

    private static bool TryChat(string? value, out long chatId) =>
        long.TryParse(value, out chatId);

    private static string FormatDuration(TimeSpan duration)
    {
        var totalSeconds = Math.Max(0, (int)Math.Floor(duration.TotalSeconds));
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        if (hours > 0) return $"{hours}h {minutes}m";
        if (minutes > 0) return $"{minutes}m {seconds}s";
        return $"{seconds}s";
    }
}
