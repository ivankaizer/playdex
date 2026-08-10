using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Api.Modules.Telegram;
using Api.Infrastructure.Common;
using Api.Infrastructure.Persistence;
using SkiaSharp;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class TelegramNotificationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RendererProducesDeterministicSizeWebpWithCardContent()
    {
        var cover = CreateCover();
        var renderer = new TelegramNotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cover),
            })));

        var image = await renderer.RenderAsync(
            new TelegramNotificationCard("Hades", "Steam", true, "1h 5m"),
            "https://covers.test/hades.png",
            CancellationToken.None);

        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Webp, codec!.EncodedFormat);
        Assert.Equal(TelegramNotificationRenderer.Width, codec.Info.Width);
        Assert.Equal(TelegramNotificationRenderer.Height, codec.Info.Height);
    }

    [Fact]
    public async Task GoldenSessionStartedCardMatchesExpectedPixelContent()
    {
        var cover = CreateCover();
        var renderer = new TelegramNotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cover),
            })));

        var started = await renderer.RenderAsync(
            new TelegramNotificationCard("Hades", "Steam", false, null),
            "https://covers.test/hades.png",
            CancellationToken.None);

        using var bitmap = SKBitmap.Decode(started);
        Assert.NotNull(bitmap);
        Assert.Equal(1280, bitmap!.Width);
        Assert.Equal(1024, bitmap.Height);

        var cornerPixel = bitmap.GetPixel(0, 0);
        Assert.InRange(cornerPixel.Red, 0, 30);
        Assert.InRange(cornerPixel.Green, 0, 30);
        Assert.InRange(cornerPixel.Blue, 0, 45);
    }

    [Fact]
    public async Task GoldenSessionEndedCardShowsDuration()
    {
        var cover = CreateCover();
        var ended = await RenderWithCover("Hades", "Steam", true, "1h 5m", cover);
        var started = await RenderWithCover("Hades", "Steam", false, null, cover);

        Assert.NotEqual(started, ended);

        using var bitmap = SKBitmap.Decode(ended);
        Assert.NotNull(bitmap);
        Assert.Equal(1280, bitmap!.Width);
        Assert.Equal(1024, bitmap.Height);
    }

    [Fact]
    public async Task GoldenShortTitleRendersAtLargerSize()
    {
        var cover = CreateCover();
        var renderer = new TelegramNotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cover),
            })));

        var image = await renderer.RenderAsync(
            new TelegramNotificationCard("Doom", "Steam", false, null),
            "https://covers.test/doom.png",
            CancellationToken.None);

        using var bitmap = SKBitmap.Decode(image);
        Assert.NotNull(bitmap);
        Assert.Equal(1280, bitmap!.Width);
        Assert.Equal(1024, bitmap.Height);
    }

    [Fact]
    public async Task FallbackLongTitleWrapsToTwoLines()
    {
        var cover = CreateCover();
        var renderer = new TelegramNotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cover),
            })));

        var image = await renderer.RenderAsync(
            new TelegramNotificationCard(
                "The Legend of Zelda Tears of the Kingdom Extended Edition",
                "Nintendo Switch", false, null),
            "https://covers.test/zelda.png",
            CancellationToken.None);

        using var bitmap = SKBitmap.Decode(image);
        Assert.NotNull(bitmap);
        Assert.Equal(1280, bitmap!.Width);
        Assert.Equal(1024, bitmap.Height);
    }

    [Fact]
    public async Task FallbackCoverRetrievalFailureIsThrown()
    {
        var renderer = new TelegramNotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.NotFound))));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            renderer.RenderAsync(
                new TelegramNotificationCard("Hades", "Steam", false, null),
                "https://covers.test/missing.png",
                CancellationToken.None));
    }

    [Fact]
    public async Task FallbackInvalidImageDataIsThrown()
    {
        var renderer = new TelegramNotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not an image"),
            })));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            renderer.RenderAsync(
                new TelegramNotificationCard("Hades", "Steam", false, null),
                "https://covers.test/corrupt.png",
                CancellationToken.None));
    }

    [Fact]
    public void FontsAreBundledAndCanBeLoaded()
    {
        var regular = TelegramNotificationRenderer.GetRegularTypeface();
        var bold = TelegramNotificationRenderer.GetBoldTypeface();
        Assert.NotNull(regular);
        Assert.NotNull(bold);
        Assert.Equal("Space Grotesk", regular.FamilyName);
        Assert.Equal("Space Grotesk", bold.FamilyName);
    }

    [Fact]
    public async Task GoldenSessionStartedPixelsMatchReference()
    {
        var cover = CreateCover();
        var image1 = await RenderWithCover("Hades", "Steam", false, null, cover);
        var image2 = await RenderWithCover("Hades", "Steam", true, "1h 5m", cover);

        Assert.NotEqual(image1, image2);

        var image3 = await RenderWithCover("Hades", "Steam", false, null, cover);
        Assert.Equal(image1, image3);
    }

    private static async Task<byte[]> RenderWithCover(
        string title, string platform, bool ended, string? duration, byte[] cover)
    {
        var renderer = new TelegramNotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cover),
            })));
        return await renderer.RenderAsync(
            new TelegramNotificationCard(title, platform, ended, duration),
            "https://covers.test/game.png",
            CancellationToken.None);
    }

    [Fact]
    public async Task SessionEventsSendPhotoFallbackToTextAndSkipUnlinkedChat()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var user = new User
        {
            Email = $"telegram-{suffix}@example.test",
            ChatId = "4242",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var credential = new PlatformCredential
        {
            UserId = user.Id,
            PlatformId = steam.Id,
            Active = true,
        };
        var game = new Game
        {
            PlatformId = steam.Id,
            Title = "Hades",
            CanonicalKey = $"steam:{suffix}",
            CapsuleUrl = "https://covers.test/hades.png",
        };
        db.AddRange(credential, game);
        await db.SaveChangesAsync();

        try
        {
            var telegramHandler = new RecordingHandler(
                Json("{\"ok\":true,\"result\":{}}"),
                Json("{\"ok\":true,\"result\":{}}"));
            var client = new TelegramClient(
                new HttpClient(telegramHandler) { BaseAddress = new Uri("https://api.telegram.test/") },
                "test-token");
            var renderer = new TelegramNotificationRenderer(new HttpClient(
                new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateCover()),
                })));
            var handler = new TelegramEventHandlers(
                new TelegramRepository(db),
                client,
                renderer,
                NullLogger<TelegramEventHandlers>.Instance);
            var now = DateTimeOffset.UtcNow;

            await handler.Handle(
                new SessionStarted(1, credential.Id, game.Id, now),
                CancellationToken.None);
            Assert.Contains("sendPhoto", telegramHandler.Requests[0]);

            game.CapsuleUrl = null;
            await db.SaveChangesAsync();
            await handler.Handle(
                new SessionEnded(1, credential.Id, game.Id, now, now.AddMinutes(65)),
                CancellationToken.None);
            Assert.Contains("sendMessage", telegramHandler.Requests[1]);

            user.ChatId = null;
            await db.SaveChangesAsync();
            await handler.Handle(
                new SessionStarted(2, credential.Id, game.Id, now),
                CancellationToken.None);
            Assert.Equal(2, telegramHandler.Requests.Count);
        }
        finally
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            db.Games.Remove(game);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public void AdminChatNormalizationSkipsInvalidAndDuplicateCandidates()
    {
        var result = TelegramEventHandlers.NormalizeChatIds(
            ["6101", "not-a-chat", "6101", "6102", ""]);

        Assert.Equal([6101L, 6102L], result);
    }

    [Fact]
    public async Task PollFailuresNotifyDistinctAdminChatsButNotRegularOwner()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Users.Where(user => user.IsAdmin).ExecuteUpdateAsync(
            setters => setters.SetProperty(user => user.IsAdmin, false));

        var suffix = Guid.NewGuid().ToString("n");
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var owner = new User
        {
            Email = $"telegram-owner-{suffix}@example.test",
            ChatId = "5100",
        };
        var adminOne = new User
        {
            Email = $"telegram-admin-one-{suffix}@example.test",
            ChatId = "6101",
            IsAdmin = true,
        };
        var adminTwo = new User
        {
            Email = $"telegram-admin-two-{suffix}@example.test",
            ChatId = "6102",
            IsAdmin = true,
        };
        var invalidAdmin = new User
        {
            Email = $"telegram-admin-invalid-{suffix}@example.test",
            ChatId = "not-a-chat",
            IsAdmin = true,
        };
        var unlinkedAdmin = new User
        {
            Email = $"telegram-admin-unlinked-{suffix}@example.test",
            IsAdmin = true,
        };
        db.AddRange(
            owner,
            adminOne,
            adminTwo,
            invalidAdmin,
            unlinkedAdmin);
        await db.SaveChangesAsync();
        var credential = new PlatformCredential
        {
            UserId = owner.Id,
            PlatformId = steam.Id,
            Active = true,
        };
        db.PlatformCredentials.Add(credential);
        await db.SaveChangesAsync();

        var telegramHandler = new RecordingHandler(
            Json("{\"ok\":true,\"result\":{}}"),
            Json("{\"ok\":true,\"result\":{}}"));
        var handler = CreateEventHandler(db, telegramHandler);

        await handler.Handle(
            new PollFailed(credential.Id, "steam", "rate limited", TimeSpan.FromMinutes(1)),
            CancellationToken.None);

        Assert.Equal(2, telegramHandler.Requests.Count);
        Assert.Contains(telegramHandler.Bodies, body => body.Contains("\"chat_id\":6101"));
        Assert.Contains(telegramHandler.Bodies, body => body.Contains("\"chat_id\":6102"));
        Assert.DoesNotContain(telegramHandler.Bodies, body => body.Contains("\"chat_id\":5100"));
        Assert.All(telegramHandler.Bodies, body =>
        {
            Assert.Contains("rate limited", body);
            Assert.Contains("Retrying in 60s", body);
        });

        await db.Users.Where(user => user.IsAdmin).ExecuteUpdateAsync(
            setters => setters.SetProperty(user => user.IsAdmin, false));
        await handler.Handle(
            new PollFailed(credential.Id, "steam", "still limited", TimeSpan.FromMinutes(2)),
            CancellationToken.None);
        Assert.Equal(2, telegramHandler.Requests.Count);
    }

    [Fact]
    public async Task PollFailureDeliveryContinuesAfterOneAdminChatFails()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Users.Where(user => user.IsAdmin).ExecuteUpdateAsync(
            setters => setters.SetProperty(user => user.IsAdmin, false));

        var suffix = Guid.NewGuid().ToString("n");
        db.Users.AddRange(
            new User
            {
                Email = $"telegram-failing-admin-{suffix}@example.test",
                ChatId = "7101",
                IsAdmin = true,
            },
            new User
            {
                Email = $"telegram-success-admin-{suffix}@example.test",
                ChatId = "7102",
                IsAdmin = true,
            });
        await db.SaveChangesAsync();

        var telegramHandler = new RecordingHandler(
            Json("{\"ok\":false,\"description\":\"blocked\"}"),
            Json("{\"ok\":true,\"result\":{}}"));
        var logger = new RecordingLogger<TelegramEventHandlers>();
        var handler = CreateEventHandler(db, telegramHandler, logger);

        await handler.Handle(
            new PollFailed(999, "steam", "provider unavailable", TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        Assert.Equal(2, telegramHandler.Requests.Count);
        Assert.Contains("\"chat_id\":7101", telegramHandler.Bodies[0]);
        Assert.Contains("\"chat_id\":7102", telegramHandler.Bodies[1]);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Error &&
            entry.Exception is TelegramApiException &&
            entry.Message.Contains("admin chat 7101"));
    }

    private static byte[] CreateCover()
    {
        using var surface = SKSurface.Create(new SKImageInfo(320, 480));
        surface.Canvas.Clear(new SKColor(48, 32, 80));
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!)
            .Options);

    private static TelegramEventHandlers CreateEventHandler(
        PlaydexDbContext db,
        HttpMessageHandler telegramHandler,
        ILogger<TelegramEventHandlers>? logger = null)
    {
        var client = new TelegramClient(
            new HttpClient(telegramHandler) { BaseAddress = new Uri("https://api.telegram.test/") },
            "test-token");
        var renderer = new TelegramNotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(CreateCover()),
            })));
        return new TelegramEventHandlers(
            new TelegramRepository(db),
            client,
            renderer,
            logger ?? NullLogger<TelegramEventHandlers>.Instance);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private sealed class StaticHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class RecordingHandler(params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<string> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            Bodies.Add(request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Dequeue();
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);
}
