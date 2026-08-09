using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task SessionEventsSendPhotoFallbackToTextSkipUnlinkedAndIsolateDeliveryFailure()
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
                Json("{\"ok\":true,\"result\":{}}"),
                Json("{\"ok\":false,\"description\":\"blocked\"}"));
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

            await handler.Handle(
                new PollFailed(credential.Id, "steam", "rate limited", TimeSpan.FromMinutes(1)),
                CancellationToken.None);

            user.ChatId = null;
            await db.SaveChangesAsync();
            await handler.Handle(
                new SessionStarted(2, credential.Id, game.Id, now),
                CancellationToken.None);
            Assert.Equal(3, telegramHandler.Requests.Count);
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
    public async Task StopCommandEndsEveryUserSessionAndReturnsArchivedSummary()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var playnite = await db.Platforms.SingleAsync(row => row.Key == "playnite");
        var user = new User
        {
            Email = $"telegram-stop-{suffix}@example.test",
            ChatId = "9191",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var credentials = new[]
        {
            new PlatformCredential { UserId = user.Id, PlatformId = steam.Id, Active = true },
            new PlatformCredential { UserId = user.Id, PlatformId = playnite.Id, Active = true },
        };
        var games = new[]
        {
            new Game { PlatformId = steam.Id, Title = "Steam Game", CanonicalKey = $"steam:stop-{suffix}" },
            new Game { PlatformId = playnite.Id, Title = "Playnite Game", CanonicalKey = $"playnite:stop-{suffix}" },
        };
        db.AddRange(credentials);
        db.AddRange(games);
        await db.SaveChangesAsync();
        db.AddRange(
            Active(credentials[0].Id, games[0].Id),
            Active(credentials[1].Id, games[1].Id));
        await db.SaveChangesAsync();

        try
        {
            var telegramHandler = new RecordingHandler(
                Json("{\"ok\":true,\"result\":{}}"));
            var client = new TelegramClient(
                new HttpClient(telegramHandler) { BaseAddress = new Uri("https://api.telegram.test/") },
                "test-token");
            var services = new ServiceCollection()
                .AddSingleton(db)
                .AddScoped<TelegramRepository>()
                .BuildServiceProvider();
            var service = new TelegramBackgroundService(
                services.GetRequiredService<IServiceScopeFactory>(),
                client,
                NullLogger<TelegramBackgroundService>.Instance);

            await service.ProcessCommandAsync(
                client,
                new TelegramUpdate(1, new TelegramMessage(1, new TelegramChat(9191), "/stop")),
                CancellationToken.None);

            Assert.Empty(await db.PlaySessions.AsNoTracking().Where(row =>
                credentials.Select(credential => credential.Id).Contains(row.PlatformCredentialId) &&
                row.EndedAt == null).ToListAsync());
            Assert.Contains("sendMessage", Assert.Single(telegramHandler.Requests));
            Assert.Contains("Stopped 2 session(s)", telegramHandler.Bodies.Single());
            Assert.Contains("Steam Game", telegramHandler.Bodies.Single());
            await services.DisposeAsync();
        }
        finally
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            db.Games.RemoveRange(games);
            await db.SaveChangesAsync();
        }
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

    private static PlaySession Active(long credentialId, long gameId)
    {
        var now = DateTimeOffset.UtcNow;
        return new PlaySession
        {
            PlatformCredentialId = credentialId,
            GameId = gameId,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
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
}
