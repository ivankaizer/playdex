using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Api.Modules.Notifications;
using Api.Infrastructure.Common;
using Playdex.Persistence;
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
        var renderer = Renderer(new HttpClient(
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
        var renderer = Renderer(new HttpClient(
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
        var renderer = Renderer(new HttpClient(
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
        var renderer = Renderer(new HttpClient(
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
        var renderer = Renderer(new HttpClient(
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
        var renderer = Renderer(new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not an image"),
            })));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
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
        var renderer = Renderer(new HttpClient(
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
    public async Task SessionEventsSendPhotoFallbackToTextSkipUnlinkedAndReportDeliveryFailure()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var user = new User
        {
            Email = $"notifications-{suffix}@example.test",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var notificationChannel = new NotificationChannel
        {
            UserId = user.Id,
            Transport = NotificationChannel.Telegram,
            Destination = "4242",
        };
        db.NotificationChannels.Add(notificationChannel);
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
            var renderer = Renderer(new HttpClient(
                new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateCover()),
                })));
            var handler = new NotificationSender(
                new NotificationsRepository(db),
                client,
                renderer,
                NullLogger<NotificationSender>.Instance);
            var now = DateTimeOffset.UtcNow;

            await handler.Send(
                new SessionStarted(session.Id, integration.Id, game.Id, now),
                CancellationToken.None);
            Assert.EndsWith("/notify/telegram_4242", appriseHandler.Requests[0]);

            game.CapsuleUrl = null;
            session.EndedAt = now.AddMinutes(65);
            await db.SaveChangesAsync();
            await handler.Send(
                new SessionEnded(session.Id, integration.Id, game.Id, now, session.EndedAt.Value),
                CancellationToken.None);
            Assert.EndsWith("/notify/telegram_4242", appriseHandler.Requests[1]);

            // A failed delivery throws, so that the dispatcher retries it. The provider's error text is not sent.
            await Assert.ThrowsAsync<AppriseApiException>(() => handler.Send(
                new PollFailed(integration.Id, "steam", "rate limited <provider detail>", TimeSpan.FromMinutes(1), now),
                CancellationToken.None));
            Assert.DoesNotContain("provider detail", appriseHandler.Bodies[2], StringComparison.Ordinal);
            Assert.Contains("could not check your Steam activity", appriseHandler.Bodies[2], StringComparison.Ordinal);
            Assert.Contains("Retrying in 1m 0s", appriseHandler.Bodies[2], StringComparison.Ordinal);

            db.NotificationChannels.Remove(notificationChannel);
            await db.SaveChangesAsync();
            await handler.Send(
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

    [Fact]
    public async Task OverlappingSessionsForSameUserGameAndPlatformNotifyOnce()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var idBase = DateTimeOffset.UtcNow.UtcTicks;
        var psn = await db.Platforms.SingleAsync(row => row.Key == "psn");
        var user = new User
        {
            Id = idBase,
            Email = $"notification-dedup-{suffix}@example.test",
        };
        var game = new Game { Id = idBase + 1, Title = "Skate Story" };
        var notificationChannel = new NotificationChannel
        {
            UserId = user.Id,
            Transport = NotificationChannel.Telegram,
            Destination = "4343",
        };
        db.AddRange(user, game, notificationChannel);
        await db.SaveChangesAsync();

        var firstIntegration = new Integration
        {
            Id = idBase + 2,
            UserId = user.Id,
            PlatformId = psn.Id,
            Type = IntegrationType.Psn,
            Active = true,
        };
        var secondIntegration = new Integration
        {
            Id = idBase + 3,
            UserId = user.Id,
            PlatformId = psn.Id,
            Type = IntegrationType.Psn,
            Active = true,
        };
        db.AddRange(firstIntegration, secondIntegration);
        await db.SaveChangesAsync();

        var startedAt = DateTimeOffset.UtcNow;
        var firstSession = new PlaySession
        {
            Id = idBase + 4,
            UserId = user.Id,
            GameId = game.Id,
            PlatformId = psn.Id,
            CaptureMethod = CaptureMethod.Polling,
            IntegrationId = firstIntegration.Id,
            StartedAt = startedAt,
        };
        db.PlaySessions.Add(firstSession);
        await db.SaveChangesAsync();
        PlaySession? secondSession = null;

        try
        {
            var appriseHandler = new RecordingHandler(Json("{}"), Json("{}"), Json("{}"), Json("{}"));
            var handler = new NotificationSender(
                new NotificationsRepository(db),
                new AppriseClient(
                    new HttpClient(appriseHandler) { BaseAddress = new Uri("https://apprise.test/") }),
                Renderer(new HttpClient()),
                NullLogger<NotificationSender>.Instance);

            await handler.Send(
                new SessionStarted(
                    firstSession.Id,
                    firstIntegration.Id,
                    game.Id,
                    firstSession.StartedAt),
                CancellationToken.None);

            secondSession = new PlaySession
            {
                Id = idBase + 5,
                UserId = user.Id,
                GameId = game.Id,
                PlatformId = psn.Id,
                CaptureMethod = CaptureMethod.Polling,
                IntegrationId = secondIntegration.Id,
                StartedAt = startedAt.AddMilliseconds(300),
            };
            db.PlaySessions.Add(secondSession);
            await db.SaveChangesAsync();

            await handler.Send(
                new SessionStarted(
                    secondSession.Id,
                    secondIntegration.Id,
                    game.Id,
                    secondSession.StartedAt),
                CancellationToken.None);
            Assert.Single(appriseHandler.Requests);

            firstSession.EndedAt = startedAt.AddMinutes(10);
            await db.SaveChangesAsync();
            await handler.Send(
                new SessionEnded(
                    firstSession.Id,
                    firstIntegration.Id,
                    game.Id,
                    firstSession.StartedAt,
                    firstSession.EndedAt.Value),
                CancellationToken.None);
            Assert.Single(appriseHandler.Requests);

            secondSession.EndedAt = startedAt.AddMinutes(11);
            await db.SaveChangesAsync();
            await handler.Send(
                new SessionEnded(
                    secondSession.Id,
                    secondIntegration.Id,
                    game.Id,
                    secondSession.StartedAt,
                    secondSession.EndedAt.Value),
                CancellationToken.None);
            Assert.Equal(2, appriseHandler.Requests.Count);

        }
        finally
        {
            if (secondSession is not null)
            {
                db.PlaySessions.Remove(secondSession);
            }

            db.PlaySessions.Remove(firstSession);
            db.Integrations.RemoveRange(firstIntegration, secondIntegration);
            db.Users.Remove(user);
            db.Games.Remove(game);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task RendererDoesNotFetchCoversFromHostsOutsideTheAllowlist()
    {
        var handler = new RecordingHandler();
        var renderer = Renderer(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.RenderAsync(
            new NotificationCard("Hades", "Steam", false, null),
            "http://169.254.169.254/latest/meta-data",
            CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RendererChecksEveryRedirectAgainstTheAllowlist()
    {
        var redirectOut = new RecordingHandler(Redirect("https://internal.test/cover.png"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Renderer(new HttpClient(redirectOut)).RenderAsync(
            new NotificationCard("Hades", "Steam", false, null), "https://covers.test/hades.png", CancellationToken.None));
        Assert.Single(redirectOut.Requests);

        var redirectWithin = new RecordingHandler(
            Redirect("/moved/hades.png"),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CreateCover()) });
        var image = await Renderer(new HttpClient(redirectWithin)).RenderAsync(
            new NotificationCard("Hades", "Steam", false, null), "https://covers.test/hades.png", CancellationToken.None);
        Assert.NotEmpty(image);
        Assert.Equal("https://covers.test/moved/hades.png", redirectWithin.Requests[1]);
    }

    [Fact]
    public async Task RendererRefusesCoversOverTheSizeCap()
    {
        var renderer = Renderer(new HttpClient(new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[NotificationRenderer.MaxCoverBytes + 1]),
        })));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.RenderAsync(
            new NotificationCard("Hades", "Steam", false, null), "https://covers.test/huge.png", CancellationToken.None));
        Assert.Contains("too large to download", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RendererRefusesCoversThatDeclareTooManyPixels()
    {
        // A few dozen bytes that claim a 20000 x 20000 bitmap: rejected from the header, before any decoding.
        var renderer = Renderer(new HttpClient(new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(PngHeader(20_000, 20_000)),
        })));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.RenderAsync(
            new NotificationCard("Hades", "Steam", false, null), "https://covers.test/bomb.png", CancellationToken.None));
        Assert.Contains("too large to render", error.Message, StringComparison.Ordinal);
    }

    private static NotificationRenderer Renderer(HttpClient client) =>
        new(client, new CoverUrlPolicy(["https://covers.test"]));

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    /// <summary>A PNG whose header declares the given size but which carries no pixel data.</summary>
    private static byte[] PngHeader(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        void Chunk(string type, byte[] payload)
        {
            var body = Encoding.ASCII.GetBytes(type).Concat(payload).ToArray();
            bytes.AddRange(BigEndian(payload.Length));
            bytes.AddRange(body);
            bytes.AddRange(BigEndian(unchecked((int)Crc32(body))));
        }

        Chunk("IHDR", [.. BigEndian(width), .. BigEndian(height), 8, 6, 0, 0, 0]);
        // An empty zlib stream: Skia reads up to the first IDAT before it reports the image size.
        Chunk("IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        Chunk("IEND", []);
        return bytes.ToArray();
    }

    private static byte[] BigEndian(int value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
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
            .UseNpgsql(
                postgres.DataSource!,
                npgsql =>
                {
                    npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                    npgsql.MapEnum<IntegrationType>("integration_type");
                    npgsql.MapEnum<CaptureMethod>("capture_method");
                })
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
