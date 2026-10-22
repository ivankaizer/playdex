using System.Globalization;
using Playdex.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Seeder;

public class DatabaseSeeder(PlaydexDbContext db)
{
    private List<Integration> _integrations = [];
    private List<Game> _games = [];
    private readonly Dictionary<Game, (Platform Platform, string ExternalId)> _gameSources = [];

    public async Task Seed()
    {
        await Truncate();

        // users
        var user = await db.AddAsync(MakeUser("user@playdex.dev"));
        var secondUser = await db.AddAsync(MakeUser("second_user@playdex.dev"));
        var admin = await db.AddAsync(MakeUser("admin@playdex.dev", isAdmin: true));
        await db.SaveChangesAsync();

        await db.NotificationChannels.AddRangeAsync(
            MakeTelegramChannel(user.Entity, "12345"),
            MakeTelegramChannel(admin.Entity, "67890"));

        // settings
        foreach (var entity in new[] { user, admin, secondUser })
        {
            await db.AddAsync(MakeUserSettings(entity.Entity));
        }

        // platforms: the migrations seed the reference platforms (unknown, ps5, gog, xbox-series-xs, ...) and the
        // draft and manual flows rely on them, so they are reused; only the demo's "custom" platform is added.
        var platformPc = await EnsurePlatform("pc", "PC", 6);
        var platformSteam = await EnsurePlatform("steam", "Steam", parent: platformPc);
        var platformPlaystation = await EnsurePlatform("psn", "PSN");
        var platformXbox = await EnsurePlatform("xbox", "Xbox");
        var platformCustom = await EnsurePlatform("custom", "Custom");

        // games
        _games =
        [
            MakeGame("Marathon",
                "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/3065800/c3093fec19fed6eb0750a87261c53743a5757827/header.jpg",
                platformSteam),

            MakeGame("Marathon",
                "https://image.api.playstation.com/vulcan/ap/rnd/202604/1317/114eab462f59c24556b6a20a657353ec5aa040bbd05cf48d.jpg?w=1920&thumb=false",
                platformPlaystation),

            MakeGame("Baldur's Gate 3",
                "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1086940/48a2fcbda8565bb45025e98fd8ebde8a7203f6a0/header.jpg",
                platformPc),

            MakeGame("Assassin's Creed Black Flag Resynced",
                "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/3751950/9b046115b1663a4be2b252712328e4f6c162da68/header.jpg",
                platformCustom),

            MakeGame("Clair Obscur: Expedition 33",
                "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1903340/be3305b02d4db0dffa3458537118423bf2792d7e/header.jpg",
                platformSteam),

            MakeGame("ELDEN RING",
                "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1245620/header.jpg",
                platformSteam),

            MakeGame("ELDEN RING",
                "https://image.api.playstation.com/vulcan/ap/rnd/202402/2214/fe6de6126062e4d38dd6c4cef9ba1966ee2440a43429dc98.jpg?w=1920&thumb=false",
                platformPlaystation),

            MakeGame("DSX", "malformed_url", platformSteam)
        ];
        await db.AddRangeAsync(_games);
        await db.SaveChangesAsync();
        await db.AddRangeAsync(_gameSources
            .Where(entry => TryGetExternalIdentityType(entry.Value.Platform, out _))
            .Select(entry => new GameExternalIdentity
            {
                GameId = entry.Key.Id,
                Type = GetExternalIdentityType(entry.Value.Platform),
                ExternalId = entry.Value.ExternalId,
                Title = entry.Key.Title,
            }));
        await db.SaveChangesAsync();


        // game connections
        if (_games.Where(g => g.Title == "Marathon").ToList() is [var game1, var game2])
        {
            await db.AddAsync(
                new GameConnection
                {
                    GameId = game1.Id,
                    ConnectedGameId = game2.Id,
                    UserId = user.Entity.Id
                });

            await db.AddAsync(new GameRating { GameId = game1.Id, Rating = 4, UserId = user.Entity.Id });
            await db.AddAsync(new GameRating { GameId = game2.Id, Rating = 1, UserId = user.Entity.Id });
        }

        await db.AddAsync(new GameRating
        {
            GameId = _games.FirstOrDefault(g => g.Title == "Baldur's Gate 3")!.Id,
            Rating = 5,
            UserId = user.Entity.Id
        });
        await db.AddAsync(new GameRating
        {
            GameId = _games.FirstOrDefault(g => g.Title == "Baldur's Gate 3")!.Id,
            Rating = 3,
            UserId = secondUser.Entity.Id
        });

        // ignored games
        await db.AddAsync(new IgnoredGame
        { GameId = _games.FirstOrDefault(g => g.Title == "DSX")!.Id, UserId = user.Entity.Id });

        // integrations
        _integrations =
        [
            new Integration
            {
                Type = IntegrationType.Steam,
                AccountId = "steam-account-id", UserId = user.Entity.Id, ApiKey = "steam-api-key", Active = true,
                PlatformId = platformSteam.Id
            },

            new Integration
            {
                Type = IntegrationType.Psn,
                AccountId = "psn-account-id", UserId = user.Entity.Id, ApiKey = "psn-npsso", Active = true,
                PlatformId = platformPlaystation.Id
            },
            new Integration
            {
                Type = IntegrationType.Xbox,
                ApiKey = "xbox-api-key", UserId = user.Entity.Id, Active = true,
                PlatformId = platformXbox.Id
            },
            new Integration
            {
                Type = IntegrationType.Api,
                AccountId = "api-account-id",
                UserId = user.Entity.Id,
                Active = true,
            }
        ];
        await db.AddRangeAsync(_integrations);
        await db.SaveChangesAsync();

        // play sessions
        await SeedPlaySessions(FirstMinuteOfTheYear(DateTime.UtcNow.Year), 2);
        await SeedPlaySessions(FirstMinuteOfTheYear(DateTime.UtcNow.Year - 1), 2);
        var recentGame = _games[0];
        var recentPlatform = _gameSources[recentGame].Platform;
        var recentIntegration = FindIntegration(recentPlatform);
        await db.AddAsync(new PlaySession
        {
            UserId = recentIntegration.UserId,
            GameId = recentGame.Id,
            PlatformId = recentPlatform.Id,
            CaptureMethod = GetCaptureMethod(recentIntegration),
            IntegrationId = recentIntegration.Id,
            StartedAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)),
        });

        await db.SaveChangesAsync();
    }

    private async Task Truncate()
    {
        // Platforms are reference data owned by the migrations, not demo data.
        var tables = db.Model.GetEntityTypes()
            .Where(e => e.ClrType != typeof(Platform))
            .Select(e => e.GetTableName())
            .Where(e => e is not null)
            .ToList();

#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
#pragma warning restore EF1002
            $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;"
        );
    }

    private static User MakeUser(string email, bool isAdmin = false)
    {
        return new User
        {
            Email = email,
            Password = BCrypt.Net.BCrypt.HashPassword("password"),
            IsAdmin = isAdmin,
        };
    }

    private static NotificationChannel MakeTelegramChannel(User user, string chatId)
    {
        return new NotificationChannel
        {
            UserId = user.Id,
            Transport = NotificationChannel.Telegram,
            Destination = chatId,
        };
    }

    private static UserSettings MakeUserSettings(User user)
    {
        return new UserSettings
        {
            UserId = user.Id,
            IgnoreShortSessionsMinutes = 3,
        };
    }

    /// <summary>The platform with this key as the migrations seeded it, or a new one when it is missing.</summary>
    private async Task<Platform> EnsurePlatform(string key, string name, int? igdbPlatformId = null, Platform? parent = null)
    {
        var existing = await db.Platforms.SingleOrDefaultAsync(platform => platform.Key == key);
        if (existing is not null)
        {
            return existing;
        }

        var platform = new Platform { Key = key, Name = name, IgdbPlatformId = igdbPlatformId, ParentId = parent?.Id };
        await db.AddAsync(platform);
        await db.SaveChangesAsync();
        return platform;
    }

    private Game MakeGame(string title, string imageUrl, Platform platform)
    {
        var externalId = Random.Shared.Next().ToString(CultureInfo.InvariantCulture);

        var game = new Game
        {
            Title = title,
            CapsuleUrl = imageUrl
        };
        _gameSources[game] = (platform, externalId);
        return game;
    }

    private async Task SeedPlaySessions(DateTime date, int averageDaysBetweenPlay)
    {
        foreach (var day in Enumerable.Range(0, 364))
        {
            var playedThatDay = Random.Shared.Next(averageDaysBetweenPlay) == 0;

            if (!playedThatDay)
            {
                continue;
            }

            var alteredDate = date.AddDays(day);
            if (alteredDate > DateTime.UtcNow)
            {
                continue;
            }

            foreach (var _ in Enumerable.Range(0, 10))
            {
                foreach (var game in _games)
                {
                    var platform = _gameSources[game].Platform;
                    var integration = FindIntegration(platform);

                    var startedAt = alteredDate.AddHours(Random.Shared.Next(1, 23));
                    await db.AddAsync(new PlaySession
                    {
                        UserId = integration.UserId,
                        GameId = game.Id,
                        PlatformId = platform.Id,
                        CaptureMethod = GetCaptureMethod(integration),
                        IntegrationId = integration.Id,
                        StartedAt = startedAt,
                        EndedAt = startedAt.AddMinutes(Random.Shared.Next(1, 120)),
                    });
                }
            }
        }
    }

    private Integration FindIntegration(Platform platform)
    {
        var type = platform.Key switch
        {
            "steam" => IntegrationType.Steam,
            "psn" => IntegrationType.Psn,
            "xbox" => IntegrationType.Xbox,
            _ => IntegrationType.Api,
        };
        return _integrations.Single(integration => integration.Type == type);
    }

    private static CaptureMethod GetCaptureMethod(Integration integration) =>
        integration.Type == IntegrationType.Api ? CaptureMethod.Api : CaptureMethod.Polling;

    private static bool TryGetExternalIdentityType(
        Platform platform,
        out GameExternalIdentityType type)
    {
        type = platform.Key switch
        {
            "steam" => GameExternalIdentityType.Steam,
            "psn" => GameExternalIdentityType.Psn,
            "xbox" => GameExternalIdentityType.Xbox,
            _ => default,
        };
        return platform.Key is "steam" or "psn" or "xbox";
    }

    private static GameExternalIdentityType GetExternalIdentityType(Platform platform)
    {
        if (TryGetExternalIdentityType(platform, out var type)) return type;
        throw new InvalidOperationException($"Platform {platform.Key} has no external identity type.");
    }

    private static DateTime FirstMinuteOfTheYear(int year) => new(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
