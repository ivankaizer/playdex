using Playdex.Api.Http;
using Playdex.Api.Manual;

namespace Playdex.Api.Telegram;

/// <summary>
/// Background service that polls the Telegram Bot API for incoming commands
/// and delegates session lifecycle operations to <see cref="ManualService"/>.
///
/// <para>
/// Registration (<c>Program.cs</c>):
/// <code>
/// builder.Services.AddHostedService&lt;TelegramBackgroundService&gt;();
/// </code>
/// </para>
///
/// <para>
/// The service is fully disabled when the <c>TELEGRAM_BOT_TOKEN</c>
/// environment variable is not set. A warning is logged at startup.
/// </para>
/// </summary>
public sealed partial class TelegramBackgroundService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramBackgroundService> _logger;

    internal const int DefaultPollIntervalMs = 2_000;
    internal const int BackoffIntervalMs = 10_000;

    public TelegramBackgroundService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramBackgroundService> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botToken = _configuration["TELEGRAM_BOT_TOKEN"];

        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogWarning(
                "TELEGRAM_BOT_TOKEN is not configured. " +
                "Telegram integration is disabled. " +
                "Set the environment variable to enable it.");
            return;
        }

        _logger.LogInformation("Telegram integration started — polling for commands.");

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

                        _logger.LogDebug(
                            "Processing update {UpdateId} from chat {ChatId}: {Text}",
                            update.UpdateId,
                            update.Message.Chat.Id,
                            update.Message.Text);

                        await ProcessCommandAsync(telegram, update, stoppingToken);

                        // Mark update as processed so we never see it again.
                        offset = update.UpdateId + 1;
                    }

                    if (updates.Count == 0)
                    {
                        await Task.Delay(DefaultPollIntervalMs, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Graceful shutdown requested — exit the loop cleanly.
                    break;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Telegram API request failed (network / auth error).");
                    await Task.Delay(BackoffIntervalMs, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Timeout from the HTTP request itself (not shutdown).
                    // This is normal for long-polling — just retry.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in Telegram polling loop.");
                    await Task.Delay(BackoffIntervalMs, stoppingToken);
                }
            }
        }
        finally
        {
            _logger.LogInformation("Telegram integration stopped.");
        }
    }

    // ── Command dispatch ───────────────────────────────────────

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

    // ── /start ─────────────────────────────────────────────────

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
            using var scope = _scopeFactory.CreateScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();
            var manualService = scope.ServiceProvider.GetRequiredService<ManualService>();

            var user = await userRepo.GetUserByChatId(chatId.ToString(), ct);

            if (user is null)
            {
                await telegram.SendMessageAsync(chatId,
                    "Your Telegram account is not linked to any Playdex account. " +
                    "Please link it via the Playdex web interface first.", ct);
                return;
            }

            var request = new ManualSessionStartRequest(args);
            var result = await manualService.StartSession(user.Id, request, ct);

            await telegram.SendMessageAsync(chatId,
                $"✅ Started tracking \"{args}\" (session #{result.SessionId})." +
                $"\nUse /stop when you are done playing.", ct);
        }
        catch (AppProblem ex)
        {
            await telegram.SendMessageAsync(chatId, $"❌ {ex.Message}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing /start for chat {ChatId}", chatId);
            await telegram.SendMessageAsync(chatId,
                "❌ An unexpected error occurred while starting the session.", ct);
        }
    }

    // ── /stop ──────────────────────────────────────────────────

    private async Task HandleStopAsync(
        TelegramClient telegram, long chatId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramRepository>();
            var manualService = scope.ServiceProvider.GetRequiredService<ManualService>();

            var user = await userRepo.GetUserByChatId(chatId.ToString(), ct);

            if (user is null)
            {
                await telegram.SendMessageAsync(chatId,
                    "Your Telegram account is not linked to any Playdex account. " +
                    "Please link it via the Playdex web interface first.", ct);
                return;
            }

            var result = await manualService.StopSession(user.Id, ct);

            await telegram.SendMessageAsync(chatId,
                result.EndedSessionCount > 0
                    ? $"✅ Stopped {result.EndedSessionCount} active session(s)."
                    : "No active sessions to stop.", ct);
        }
        catch (AppProblem ex)
        {
            await telegram.SendMessageAsync(chatId, $"❌ {ex.Message}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing /stop for chat {ChatId}", chatId);
            await telegram.SendMessageAsync(chatId,
                "❌ An unexpected error occurred while stopping sessions.", ct);
        }
    }

    // ── /help ──────────────────────────────────────────────────

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

    // ── Helpers ────────────────────────────────────────────────

    /// <summary>
    /// Split <c>"/command arg1 arg2"</c> into <c>("/command", "arg1 arg2")</c>.
    /// </summary>
    private static (string Command, string? Args) ParseCommand(string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.TrimEntries);
        var command = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1] : null;
        return (command, args);
    }
}
