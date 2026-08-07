using Api.Shared.Common;

namespace Api.Modules.Telegram;

public sealed class TelegramEventHandlers(
    TelegramRepository repository,
    TelegramClient client,
    TelegramNotificationRenderer renderer,
    ILogger<TelegramEventHandlers> logger) :
    IEventHandler<SessionStarted>,
    IEventHandler<SessionEnded>,
    IEventHandler<PollFailed>
{
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
            logger.LogError(ex,
                "Telegram poll-failure notification failed for credential {CredentialId}",
                @event.CredentialId);
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
                logger.LogWarning(ex,
                    "Telegram card rendering or photo delivery failed; using text fallback");
            }
        }

        try
        {
            await client.SendMessageAsync(chatId, $"{fallbackText}.", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telegram text fallback failed");
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
