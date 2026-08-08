using System.Text.RegularExpressions;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Seeder;

public class DatabaseSeeder(PlaydexDbContext db)
{
    private List<PlatformCredential> _credentials = [];
    private List<Game> _games = [];

    public async Task Seed()
    {
        await Truncate();

        // users
        var user = await db.AddAsync(MakeUser("user@playdex.dev", "12345"));
        var secondUser = await db.AddAsync(MakeUser("second_user@playdex.dev"));
        var admin = await db.AddAsync(MakeUser("admin@playdex.dev", "67890", true));
        await db.SaveChangesAsync();

        // settings
        foreach (var entity in new[] { user, admin, secondUser })
        {
            await db.AddAsync(MakeUserSettings(entity.Entity));
        }

        // platforms
        var platformCustom = MakePlatform("Custom");
        var platformPlaystation = MakePlatform("PS5", 167);
        var platformPc = MakePlatform("PC", 6);
        await db.AddAsync(platformPc);
        await db.SaveChangesAsync();
        var platformSteam = MakePlatform("Steam", parentId: platformPc.Id);

        await db.AddAsync(platformCustom);
        await db.AddAsync(platformPlaystation);
        await db.AddAsync(platformSteam);
        await db.SaveChangesAsync();

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
            GameId = _games.FirstOrDefault(g => g.Title == "Baldur's Gate 3")!.Id, Rating = 5, UserId = user.Entity.Id
        });
        await db.AddAsync(new GameRating
        {
            GameId = _games.FirstOrDefault(g => g.Title == "Baldur's Gate 3")!.Id, Rating = 3,
            UserId = secondUser.Entity.Id
        });

        // ignored games
        await db.AddAsync(new IgnoredGame
            { GameId = _games.FirstOrDefault(g => g.Title == "DSX")!.Id, UserId = user.Entity.Id });

        // platform credentials
        _credentials =
        [
            new PlatformCredential
            {
                AccountId = "steam-account-id", UserId = user.Entity.Id, ApiKey = "steam-api-key", Active = true,
                PlatformId = platformSteam.Id
            },

            new PlatformCredential
            {
                AccountId = "psn-account-id", UserId = user.Entity.Id, Active = true,
                PlatformId = platformPlaystation.Id
            },

            new PlatformCredential
                { AccountId = "pc-account-id", UserId = user.Entity.Id, Active = true, PlatformId = platformPc.Id },
            new PlatformCredential
                { AccountId = "custom-account-id", UserId = user.Entity.Id, Active = false, PlatformId = platformCustom.Id }
        ];
        await db.AddRangeAsync(_credentials);
        await db.SaveChangesAsync();

        // play sessions
        await SeedPlaySessions(FirstMinuteOfTheYear(DateTime.UtcNow.Year), 2);
        await SeedPlaySessions(FirstMinuteOfTheYear(DateTime.UtcNow.Year - 1), 2);
        await db.AddAsync(new PlaySession
        {
            PlatformCredentialId = _credentials[0].Id,
            GameId = _games[0].Id,
            StartedAt = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10)),
        });

        await db.SaveChangesAsync();
    }

    private async Task Truncate()
    {
        var tables = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(e => e is not null)
            .ToList();

#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
#pragma warning restore EF1002
            $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;"
        );
    }

    private static User MakeUser(string email, string? chatId = null, bool isAdmin = false)
    {
        return new User
        {
            Email = email,
            Password = BCrypt.Net.BCrypt.HashPassword("password"),
            ChatId = chatId,
            IsAdmin = isAdmin,
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

    private static Platform MakePlatform(string name, int? igdbPlatformId = null, long? parentId = null)
    {
        return new Platform
        {
            Name = name,
            Key = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant(),
            IgdbPlatformId = igdbPlatformId,
            ParentId = parentId,
        };
    }

    private static Game MakeGame(string title, string imageUrl, Platform platform)
    {
        var externalId = Random.Shared.Next();

        return new Game
        {
            Title = title,
            CanonicalKey = $"{platform.Key}:{externalId}",
            ExternalId = externalId.ToString(),
            PlatformId = platform.Id,
            CapsuleUrl = imageUrl
        };
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

            foreach (var _ in Enumerable.Range(0, 10))
            {
                foreach (var game in _games)
                {
                    var credential = _credentials.FirstOrDefault(c => c.PlatformId == game.PlatformId);
                    if (credential is null)
                    {
                        throw new InvalidOperationException("Platform does not have required credentials");
                    }

                    var startedAt = alteredDate.AddHours(Random.Shared.Next(1, 23));
                    await db.AddAsync(new PlaySession
                    {
                        PlatformCredentialId = credential.Id,
                        GameId = game.Id,
                        StartedAt = startedAt,
                        EndedAt = startedAt.AddMinutes(Random.Shared.Next(1, 120)),
                    });
                }
            }
        }
    }

    private static DateTime FirstMinuteOfTheYear(int year) => new(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
