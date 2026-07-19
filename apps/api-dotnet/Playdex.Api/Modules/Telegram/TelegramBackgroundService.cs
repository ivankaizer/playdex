using Playdex.Api.Shared.Http;
using Playdex.Api.Shared.Common;

namespace Playdex.Api.Modules.Telegram;

public sealed partial class TelegramBackgroundService(
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    EventBus bus,
    ILogger<TelegramBackgroundService> logger) : BackgroundService
{
    internal const int DefaultPollIntervalMs = 2_000;
    internal const int BackoffIntervalMs = 10_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botToken = configuration["TELEGRAM_BOT_TOKEN"];

        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning(
                "TELEGRAM_BOT_TOKEN is not configured. " +
                "Telegram integration is disabled. " +
                "Set the environment variable to enable it.");
            return;
        }

        logger.LogInformation("Telegram integration started — polling for commands.");

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"https://api.telegram.org/bot{botToken}/"),
            Timeout = TimeSpan.FromSeconds(60)
        };

        var telegram = new TelegramClient(httpClient);
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
                        if (update.Message?.Text is null)
                            continue;

                        logger.LogDebug(
                            "Processing update {UpdateId} from chat {ChatId}: {Text}",
                            update.UpdateId,
                            update.Message.Chat.Id,
                            update.Message.Text);

                        await ProcessCommandAsync(telegram, update, stoppingToken);

                        offset = update.UpdateId + 1;
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
                    logger.LogError(ex, "Telegram API request failed (network / auth error).");
                    await Task.Delay(BackoffIntervalMs, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled error in Telegram polling loop.");
                    await Task.Delay(BackoffIntervalMs, stoppingToken);
                }
            }
        }
        finally
        {
            logger.LogInformation("Telegram integration stopped.");
        }
    }

    private async Task ProcessCommandAsync(
        TelegramClient telegram, TelegramUpdate update, CancellationToken ct)
    {
        var text = update.Message!.Text!.Trim();
        var chatId = update.Message.Chat.Id;

        var (command, args) = ParseCommand(text);

        switch (command)
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

            var user = await userRepo.GetUserByChatId(chatId.ToString(), ct);

            if (user is null)
            {
                await telegram.SendMessageAsync(chatId,
                    "Your Telegram account is not linked to any Playdex account. " +
                    "Please link it via the Playdex web interface first.", ct);
                return;
            }

            await bus.Publish(new StartSessionRequested(user.Id, 0, args), ct);

            await telegram.SendMessageAsync(chatId,
                $"✅ Started tracking \"{args}\".\nUse /stop when you are done playing.", ct);
        }
        catch (AppProblem ex)
        {
            await telegram.SendMessageAsync(chatId, $"❌ {ex.Message}", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing /start for chat {ChatId}", chatId);
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

            var user = await userRepo.GetUserByChatId(chatId.ToString(), ct);

            if (user is null)
            {
                await telegram.SendMessageAsync(chatId,
                    "Your Telegram account is not linked to any Playdex account. " +
                    "Please link it via the Playdex web interface first.", ct);
                return;
            }

            await bus.Publish(new StopSessionRequested(user.Id), ct);

            await telegram.SendMessageAsync(chatId, "✅ Stopped active session(s).", ct);
        }
        catch (AppProblem ex)
        {
            await telegram.SendMessageAsync(chatId, $"❌ {ex.Message}", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing /stop for chat {ChatId}", chatId);
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

    private static (string Command, string? Args) ParseCommand(string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.TrimEntries);
        var command = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1] : null;
        return (command, args);
    }
}
