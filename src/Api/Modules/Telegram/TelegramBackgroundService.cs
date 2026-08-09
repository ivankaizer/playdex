using System.Globalization;
using Api.Infrastructure.Http;
using Api.Infrastructure.Common;

namespace Api.Modules.Telegram;

public sealed partial class TelegramBackgroundService(
    IServiceScopeFactory scopeFactory,
    TelegramClient telegram,
    ILogger<TelegramBackgroundService> logger) : BackgroundService
{
    internal const int DefaultPollIntervalMs = 2_000;
    internal const int BackoffIntervalMs = 10_000;

    [LoggerMessage(Level = LogLevel.Warning, Message = "TELEGRAM_BOT_TOKEN is not configured. Telegram integration is disabled. Set the environment variable to enable it.")]
    private partial void LogTelegramNotConfigured();

    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram integration started — polling for commands.")]
    private partial void LogTelegramStarted();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing update {UpdateId} from chat {ChatId}: {Text}")]
    private partial void LogProcessingUpdate(long updateId, long chatId, string? text);

    [LoggerMessage(Level = LogLevel.Error, Message = "Telegram API request failed (network / auth error).")]
    private partial void LogTelegramApiRequestFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error in Telegram polling loop.")]
    private partial void LogTelegramPollingLoopError(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram integration stopped.")]
    private partial void LogTelegramStopped();

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing /start for chat {ChatId}")]
    private partial void LogStartError(Exception exception, long chatId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing /stop for chat {ChatId}")]
    private partial void LogStopError(Exception exception, long chatId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!telegram.IsEnabled)
        {
            LogTelegramNotConfigured();
            return;
        }

        LogTelegramStarted();

        long? offset = null;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var updates = await telegram.GetUpdatesAsync(offset, stoppingToken);

                    foreach (var update in updates)
                    {
                        offset = update.UpdateId + 1;
                        if (update.Message?.Text is null)
                            continue;

                        LogProcessingUpdate(update.UpdateId, update.Message.Chat.Id, update.Message.Text);

                        await ProcessCommandAsync(telegram, update, stoppingToken);
                    }

                    if (updates.Count == 0)
                    {
                        await Task.Delay(DefaultPollIntervalMs, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpRequestException ex)
                {
                    LogTelegramApiRequestFailed(ex);
                    await Task.Delay(BackoffIntervalMs, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    LogTelegramPollingLoopError(ex);
                    await Task.Delay(BackoffIntervalMs, stoppingToken);
                }
            }
        }
        finally
        {
            LogTelegramStopped();
        }
    }

    internal async Task ProcessCommandAsync(
        TelegramClient telegram, TelegramUpdate update, CancellationToken ct)
    {
        var text = update.Message!.Text!.Trim();
        var chatId = update.Message.Chat.Id;

        var (command, args) = ParseCommand(text);

        switch (command.StartsWith("/stop", StringComparison.Ordinal) ? "/stop" : command)
        {
            case "/start":
                await HandleStartAsync(telegram, chatId, args, ct);
                break;
            case "/stop":
                await HandleStopAsync(telegram, chatId, ct);
                break;
            case "/help":
                await HandleHelpAsync(telegram, chatId, ct);
                break;
            default:
                await telegram.SendMessageAsync(chatId,
                    $"Unknown command \"{command}\". Type /help for available commands.", ct);
                break;
        }
    }

    private async Task HandleStartAsync(
        TelegramClient telegram, long chatId, string? args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            await telegram.SendMessageAsync(chatId,
                """
                🎮 Please provide a game title:
                /start <title>

                Example: /start The Legend of Zelda
                """, ct);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<TelegramRepository>();
            var manual = scope.ServiceProvider.GetRequiredService<Api.Modules.Manual.ManualService>();

            var user = await userRepo.GetUserByChatId(chatId.ToString(CultureInfo.InvariantCulture), ct);

            if (user is null)
            {
                await telegram.SendMessageAsync(chatId,
                    "This chat is not linked to a Playdex user. " +
                    "Ask an admin to set chat_id on your user record.", ct);
                return;
            }

            var result = await manual.StartSession(
                user.Id,
                new Api.Modules.Manual.ManualSessionStartRequest(args),
                ct);
            var summary = await userRepo.GetSessionSummary(result.SessionId, ct);

            await telegram.SendMessageAsync(chatId,
                summary is null
                    ? "Session started."
                    : $"Session started: {summary.Title} ({summary.Platform}).", ct);
        }
        catch (AppProblemException ex)
        {
            await telegram.SendMessageAsync(chatId, $"❌ {ex.Message}", ct);
        }
        catch (Exception ex)
        {
            LogStartError(ex, chatId);
            await telegram.SendMessageAsync(chatId,
                "❌ An unexpected error occurred while starting the session.", ct);
        }
    }

    private async Task HandleStopAsync(
        TelegramClient telegram, long chatId, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<TelegramRepository>();

            var user = await userRepo.GetUserByChatId(chatId.ToString(CultureInfo.InvariantCulture), ct);

            if (user is null)
            {
                await telegram.SendMessageAsync(chatId,
                    "This chat is not linked to a Playdex user. " +
                    "Ask an admin to set chat_id on your user record.", ct);
                return;
            }

            var sessions = await userRepo.EndActiveSessionsForUser(
                user.Id, DateTimeOffset.UtcNow, ct);
            var message = sessions.Count == 0
                ? "No active sessions to stop."
                : $"Stopped {sessions.Count} session(s): " +
                  string.Join(", ", sessions.Select(row => $"{row.Title} ({row.Platform})")) + ".";
            await telegram.SendMessageAsync(chatId, message, ct);
        }
        catch (AppProblemException ex)
        {
            await telegram.SendMessageAsync(chatId, $"❌ {ex.Message}", ct);
        }
        catch (Exception ex)
        {
            LogStopError(ex, chatId);
            await telegram.SendMessageAsync(chatId,
                "❌ An unexpected error occurred while stopping sessions.", ct);
        }
    }

    private static async Task HandleHelpAsync(
        TelegramClient telegram, long chatId, CancellationToken ct)
    {
        var help = """
            🤖 *Playdex Bot Commands*

            /start <title> — Start tracking a play session for a game
            /stop          — End all of your active play sessions
            /help          — Show this help message

            *Examples:*
            /start Skyrim
            /start Hollow Knight
            /stop
            """;

        await telegram.SendMessageAsync(chatId, help, ct);
    }

    internal static (string Command, string? Args) ParseCommand(string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.TrimEntries);
        var command = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1] : null;
        return (command, args);
    }
}
