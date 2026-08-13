using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Api.Modules.Notifications;
using Api.Infrastructure.Common;
using Api.Infrastructure.Persistence;
using SkiaSharp;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class AppriseNotificationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RendererProducesDeterministicSizeWebpWithCardContent()
    {
        var cover = CreateCover();
        var renderer = new NotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cover),
            })));

        var image = await renderer.RenderAsync(
            new NotificationCard("Hades", "Steam", true, "1h 5m"),
            "https://covers.test/hades.png",
            CancellationToken.None);

        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Webp, codec!.EncodedFormat);
        Assert.Equal(NotificationRenderer.Width, codec.Info.Width);
        Assert.Equal(NotificationRenderer.Height, codec.Info.Height);
    }

    [Fact]
    public async Task GoldenSessionStartedCardMatchesExpectedPixelContent()
    {
        var cover = CreateCover();
        var renderer = new NotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cover),
            })));

        var started = await renderer.RenderAsync(
            new NotificationCard("Hades", "Steam", false, null),
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
        var renderer = new NotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cover),
            })));

        var image = await renderer.RenderAsync(
            new NotificationCard("Doom", "Steam", false, null),
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
        var renderer = new NotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cover),
            })));

        var image = await renderer.RenderAsync(
            new NotificationCard(
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
        var renderer = new NotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.NotFound))));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            renderer.RenderAsync(
                new NotificationCard("Hades", "Steam", false, null),
                "https://covers.test/missing.png",
                CancellationToken.None));
    }

    [Fact]
    public async Task FallbackInvalidImageDataIsThrown()
    {
        var renderer = new NotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not an image"),
            })));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            renderer.RenderAsync(
                new NotificationCard("Hades", "Steam", false, null),
                "https://covers.test/corrupt.png",
                CancellationToken.None));
    }

    [Fact]
    public void FontsAreBundledAndCanBeLoaded()
    {
        var regular = NotificationRenderer.GetRegularTypeface();
        var bold = NotificationRenderer.GetBoldTypeface();
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
        var renderer = new NotificationRenderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cover),
            })));
        return await renderer.RenderAsync(
            new NotificationCard(title, platform, ended, duration),
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
            Email = $"notifications-{suffix}@example.test",
            TelegramChatId = "4242",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var integration = new Integration
        {
            UserId = user.Id,
            PlatformId = steam.Id,
            Type = IntegrationType.Steam,
            Active = true,
        };
        var game = new Game
        {
            Title = "Hades",
            CapsuleUrl = "https://covers.test/hades.png",
        };
        db.AddRange(integration, game);
        await db.SaveChangesAsync();
        var session = new PlaySession
        {
            UserId = user.Id,
            GameId = game.Id,
            PlatformId = steam.Id,
            CaptureMethod = CaptureMethod.Polling,
            IntegrationId = integration.Id,
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.PlaySessions.Add(session);
        await db.SaveChangesAsync();

        try
        {
            var appriseHandler = new RecordingHandler(
                Json("{}"),
                Json("{}"),
                new HttpResponseMessage(HttpStatusCode.FailedDependency));
            var client = new AppriseClient(
                new HttpClient(appriseHandler) { BaseAddress = new Uri("https://apprise.test/") });
            var renderer = new NotificationRenderer(new HttpClient(
                new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateCover()),
                })));
            var handler = new NotificationEventHandlers(
                new NotificationsRepository(db),
                client,
                renderer,
                NullLogger<NotificationEventHandlers>.Instance);
            var now = DateTimeOffset.UtcNow;

            await handler.Handle(
                new SessionStarted(session.Id, integration.Id, game.Id, now),
                CancellationToken.None);
            Assert.EndsWith("/notify/telegram_4242", appriseHandler.Requests[0]);

            game.CapsuleUrl = null;
            await db.SaveChangesAsync();
            await handler.Handle(
                new SessionEnded(session.Id, integration.Id, game.Id, now, now.AddMinutes(65)),
                CancellationToken.None);
            Assert.EndsWith("/notify/telegram_4242", appriseHandler.Requests[1]);

            await handler.Handle(
                new PollFailed(integration.Id, "steam", "rate limited", TimeSpan.FromMinutes(1)),
                CancellationToken.None);

            user.TelegramChatId = null;
            await db.SaveChangesAsync();
            await handler.Handle(
                new SessionStarted(session.Id, integration.Id, game.Id, now),
                CancellationToken.None);
            Assert.Equal(3, appriseHandler.Requests.Count);
        }
        finally
        {
            db.PlaySessions.Remove(session);
            db.Integrations.Remove(integration);
            db.Users.Remove(user);
            db.Games.Remove(game);
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
