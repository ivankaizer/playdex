using Api.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace Api.Modules.Notifications;

/// <summary>
/// Delivers notifications for domain events. It renders cards and calls Apprise (seconds to minutes), so it runs on
/// <see cref="NotificationDispatcher"/>, never inline in the request or poll that raised the event. A delivery that
/// fails throws, and the dispatcher retries it; a card that cannot be rendered falls back to text first.
/// </summary>
public sealed partial class NotificationSender(
    NotificationsRepository repository,
    AppriseClient client,
    NotificationRenderer renderer,
    ILogger<NotificationSender> logger)
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification card rendering or attachment delivery failed; using text fallback")]
    private partial void LogCardRenderingFailed(Exception exception);

    public Task Send(object notification, CancellationToken ct) => notification switch
    {
        SessionStarted started => Send(started, ct),
        SessionEnded ended => Send(ended, ct),
        PollFailed failed => Send(failed, ct),
        IntegrationPaused paused => Send(paused, ct),
        _ => Task.CompletedTask,
    };

    public async Task Send(SessionStarted @event, CancellationToken ct)
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

    public async Task Send(SessionEnded @event, CancellationToken ct)
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

    public async Task Send(PollFailed @event, CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        var target = await repository.GetCredentialTarget(@event.CredentialId, ct);
        if (target is null || string.IsNullOrWhiteSpace(target.TelegramChatId)) return;
        // The provider's error text stays in the server log, where the poller already records it with the
        // credential: it can be long, carries provider internals, and is nothing the user can act on.
        var platform = NotificationText.Clean(target.Platform, NotificationText.MaxPlatformLength);
        await client.SendMessageAsync(
            target.TelegramChatId,
            $"Playdex could not check your {platform} activity. " +
            $"Retrying in {FormatDuration(@event.RetryDelay)}; no further failure messages until it works again. " +
            "If it keeps failing, check the connection in Settings.",
            ct);
    }

    public async Task Send(IntegrationPaused @event, CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        var target = await repository.GetCredentialTarget(@event.CredentialId, ct);
        if (target is null || string.IsNullOrWhiteSpace(target.TelegramChatId)) return;
        var platform = NotificationText.Clean(target.Platform, NotificationText.MaxPlatformLength);
        await client.SendMessageAsync(
            target.TelegramChatId,
            $"Polling for {platform} is paused: {@event.Reason} " +
            $"It was rejected {@event.AuthFailures} times in a row. " +
            "Update the credential in Playdex settings, then resume the integration.",
            ct);
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

        await client.SendMessageAsync(telegramChatId, fallbackText, ct);
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
