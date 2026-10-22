using Api.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace Api.Modules.Notifications;

public sealed partial class NotificationEventHandlers(
    NotificationsRepository repository,
    AppriseClient client,
    NotificationRenderer renderer,
    ILogger<NotificationEventHandlers> logger) :
    IEventSubscriber<SessionStarted>,
    IEventSubscriber<SessionEnded>,
    IEventSubscriber<PollFailed>
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Poll-failure notification failed for credential {CredentialId}")]
    private partial void LogPollFailureNotificationFailed(Exception exception, long credentialId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification card rendering or attachment delivery failed; using text fallback")]
    private partial void LogCardRenderingFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification text fallback failed")]
    private partial void LogTextFallbackFailed(Exception exception);

    public async Task Handle(SessionStarted @event, CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        var target = await repository.GetSessionStartedTarget(@event.SessionId, ct);
        if (target is null || string.IsNullOrWhiteSpace(target.TelegramChatId)) return;
        var (title, platform) = CleanNames(target);
        await SendSessionCard(
            target.TelegramChatId,
            target,
            new NotificationCard(title, platform, false, null),
            $"Session started: {title} on {platform}",
            ct);
    }

    public async Task Handle(SessionEnded @event, CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        var target = await repository.GetSessionEndedTarget(@event.SessionId, ct);
        if (target is null || string.IsNullOrWhiteSpace(target.TelegramChatId)) return;
        var duration = FormatDuration(@event.EndedAt - @event.StartedAt);
        var (title, platform) = CleanNames(target);
        await SendSessionCard(
            target.TelegramChatId,
            target,
            new NotificationCard(title, platform, true, duration),
            $"Session ended: {title} on {platform}\nDuration: {duration}",
            ct);
    }

    public async Task Handle(PollFailed @event, CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        var target = await repository.GetCredentialTarget(@event.CredentialId, ct);
        if (target is null || string.IsNullOrWhiteSpace(target.TelegramChatId)) return;
        // The provider's error text stays in the server log, where the poller already records it with the
        // credential: it can be long, carries provider internals, and is nothing the user can act on.
        var platform = NotificationText.Clean(target.Platform, NotificationText.MaxPlatformLength);
        try
        {
            await client.SendMessageAsync(
                target.TelegramChatId,
                $"Playdex could not check your {platform} activity. " +
                $"Retrying in {FormatDuration(@event.RetryDelay)}. " +
                "If this keeps happening, check the connection in Settings.",
                ct);
        }
        catch (Exception ex)
        {
            LogPollFailureNotificationFailed(ex, @event.CredentialId);
        }
    }

    private async Task SendSessionCard(
        string telegramChatId,
        NotificationTarget target,
        NotificationCard card,
        string fallbackText,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(target.CapsuleUrl))
        {
            try
            {
                var image = await renderer.RenderAsync(card, target.CapsuleUrl, ct);
                await client.SendAttachmentAsync(telegramChatId, image, fallbackText, ct);
                return;
            }
            catch (Exception ex)
            {
                LogCardRenderingFailed(ex);
            }
        }

        try
        {
            await client.SendMessageAsync(telegramChatId, fallbackText, ct);
        }
        catch (Exception ex)
        {
            LogTextFallbackFailed(ex);
        }
    }

    private static (string Title, string Platform) CleanNames(NotificationTarget target) => (
        NotificationText.Clean(target.GameTitle, NotificationText.MaxTitleLength),
        NotificationText.Clean(target.Platform, NotificationText.MaxPlatformLength));

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
