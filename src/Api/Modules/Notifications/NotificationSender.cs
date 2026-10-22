using Api.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace Api.Modules.Notifications;

/// <summary>
/// Delivers notifications for domain events. It renders cards and calls Apprise (seconds to minutes), so it runs on
/// <see cref="NotificationDispatcher"/>, never inline in the request or poll that raised the event.
/// </summary>
public sealed partial class NotificationSender(
    NotificationsRepository repository,
    AppriseClient client,
    NotificationRenderer renderer,
    ILogger<NotificationSender> logger)
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Poll-failure notification failed for credential {CredentialId}")]
    private partial void LogPollFailureNotificationFailed(Exception exception, long credentialId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification card rendering or attachment delivery failed; using text fallback")]
    private partial void LogCardRenderingFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification text fallback failed")]
    private partial void LogTextFallbackFailed(Exception exception);

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
        await SendSessionCard(
            target.TelegramChatId,
            target,
            new NotificationCard(target.GameTitle, target.Platform, false, null),
            $"Session started: {target.GameTitle} on {target.Platform}",
            ct);
    }

    public async Task Send(SessionEnded @event, CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        var target = await repository.GetSessionEndedTarget(@event.SessionId, ct);
        if (target is null || string.IsNullOrWhiteSpace(target.TelegramChatId)) return;
        var duration = FormatDuration(@event.EndedAt - @event.StartedAt);
        await SendSessionCard(
            target.TelegramChatId,
            target,
            new NotificationCard(target.GameTitle, target.Platform, true, duration),
            $"Session ended: {target.GameTitle} on {target.Platform}\nDuration: {duration}",
            ct);
    }

    public async Task Send(PollFailed @event, CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        var target = await repository.GetCredentialTarget(@event.CredentialId, ct);
        if (target is null || string.IsNullOrWhiteSpace(target.TelegramChatId)) return;
        try
        {
            await client.SendMessageAsync(
                target.TelegramChatId,
                $"Polling failed for {@event.Platform}. Error: {@event.Message}. " +
                $"Retrying in {Math.Round(@event.RetryDelay.TotalSeconds)}s; " +
                "no further failure messages until polling works again.",
                ct);
        }
        catch (Exception ex)
        {
            LogPollFailureNotificationFailed(ex, @event.CredentialId);
        }
    }

    public async Task Send(IntegrationPaused @event, CancellationToken ct)
    {
        if (!client.IsEnabled) return;
        var target = await repository.GetCredentialTarget(@event.CredentialId, ct);
        if (target is null || string.IsNullOrWhiteSpace(target.TelegramChatId)) return;
        try
        {
            await client.SendMessageAsync(
                target.TelegramChatId,
                $"Polling for {target.Platform} is paused: {@event.Reason} " +
                $"It was rejected {@event.AuthFailures} times in a row. " +
                "Update the credential in Playdex settings, then resume the integration.",
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
