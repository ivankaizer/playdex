using System.Security.Cryptography;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Credentials;
using Api.Modules.Games;
using Api.Modules.Polling;
using Api.Modules.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class CredentialProtectorTests(PostgresFixture postgres)
{
    private static readonly CredentialProtector _protector = TestCredentials.Protector;

    [Fact]
    public void ProtectRoundTripsWithAFreshNonce()
    {
        var first = _protector.Protect("steam-web-api-key");
        var second = _protector.Protect("steam-web-api-key");

        Assert.StartsWith(CredentialProtector.Prefix, first);
        Assert.DoesNotContain("steam-web-api-key", first);
        Assert.NotEqual(first, second);
        Assert.Equal("steam-web-api-key", _protector.Unprotect(first));
        Assert.Equal("steam-web-api-key", _protector.Unprotect(second));
    }

    [Fact]
    public void ProtectIsIdempotentAndKeepsNull()
    {
        var protectedValue = _protector.Protect("npsso");

        Assert.Equal(protectedValue, _protector.Protect(protectedValue));
        Assert.Null(_protector.Protect(null));
        Assert.Null(_protector.Unprotect(null));
    }

    [Fact]
    public void UnprotectPassesLegacyPlaintextThrough() =>
        Assert.Equal("legacy-plaintext", _protector.Unprotect("legacy-plaintext"));

    [Fact]
    public void PolledCredentialsAreEncryptedAndPlaydexAppKeysHashed()
    {
        Assert.Equal(PlaydexAppKeys.Hash("plx_abc"), _protector.Protect(IntegrationType.Api, "plx_abc"));
        Assert.Equal(
            "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            PlaydexAppKeys.Hash("abc"));
        Assert.True(CredentialProtector.IsProtected(_protector.Protect(IntegrationType.Xbox, "openxbl")));
        Assert.Null(_protector.Protect(IntegrationType.Api, null));
    }

    [Fact]
    public void AnotherKeyCannotDecrypt()
    {
        var protectedValue = _protector.Protect("openxbl-key");
        var other = TestCredentials.Create("another-credentials-key-that-is-also-long-enough");

        Assert.ThrowsAny<CryptographicException>(() => other.Unprotect(protectedValue));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangedOrTruncatedCiphertextIsRejected(bool truncate)
    {
        var protectedValue = _protector.Protect("openxbl-key")!;
        var payload = Convert.FromBase64String(protectedValue[CredentialProtector.Prefix.Length..]);
        if (truncate)
        {
            payload = payload[..20];
        }
        else
        {
            payload[^1] ^= 0x01;
        }

        Assert.ThrowsAny<CryptographicException>(
            () => _protector.Unprotect(CredentialProtector.Prefix + Convert.ToBase64String(payload)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void AShortKeyStopsStartup(string key)
    {
        var error = Assert.Throws<OptionsValidationException>(() => OptionsFor(key, Environments.Production).Value);

        Assert.Contains("CREDENTIALS_KEY", error.Message);
    }

    [Fact]
    public void ThePublishedDevelopmentKeyIsOnlyAcceptedInDevelopment()
    {
        const string developmentKey = "dev-only-credentials-key-do-not-use-in-production";

        var error = Assert.Throws<OptionsValidationException>(() => OptionsFor(developmentKey, Environments.Production).Value);
        Assert.Contains("development value", error.Message);
        Assert.Equal(developmentKey, OptionsFor(developmentKey, Environments.Development).Value.EncryptionKey);
    }

    [Fact]
    public async Task BackfillEncryptsPlaintextCredentialsButNotPlaydexAppKeys()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var (user, steam) = await CreateUser(db, "credential-backfill");
        var alreadyProtected = _protector.Protect("already-encrypted");
        db.Integrations.AddRange(
            SteamIntegration(user, steam, "steam-1", "legacy-steam-key"),
            SteamIntegration(user, steam, "steam-2", alreadyProtected),
            new Integration
            {
                UserId = user.Id,
                Type = IntegrationType.Api,
                ApiKey = $"plx_{user.Id}",
                AccountId = "api",
                Active = true,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.CreatedAt,
            });
        await db.SaveChangesAsync();

        try
        {
            await CredentialEncryptionBackfill.Run(db, _protector, CancellationToken.None);

            var rows = await db.Integrations.AsNoTracking().Where(integration => integration.UserId == user.Id).ToListAsync();
            var legacy = rows.Single(row => row.AccountId == "steam-1").ApiKey;
            Assert.True(CredentialProtector.IsProtected(legacy));
            Assert.Equal("legacy-steam-key", _protector.Unprotect(legacy));
            Assert.Equal(alreadyProtected, rows.Single(row => row.AccountId == "steam-2").ApiKey);
            Assert.Equal($"plx_{user.Id}", rows.Single(row => row.Type == IntegrationType.Api).ApiKey);
        }
        finally
        {
            await Remove(db, user);
        }
    }

    [Fact]
    public async Task SavedKeysAreEncryptedAndPollingGetsThemBack()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var (user, _) = await CreateUser(db, "credential-roundtrip");
        try
        {
            var created = await new ProvidersRepository(db, TestSessions.Lifecycle(db), _protector)
                .CreateProvider(user.Id, "steam", IntegrationType.Steam, "steam-web-api-key", "76561190000000101", CancellationToken.None);

            var stored = await db.Integrations.AsNoTracking().SingleAsync(row => row.Id == created.Provider!.Id);
            Assert.True(CredentialProtector.IsProtected(stored.ApiKey));
            var polled = (await new PollingRepository(db, new GameResolverNotUsed(), TestSessions.Lifecycle(db), _protector)
                    .LoadActiveCredentials(CancellationToken.None))
                .Single(credential => credential.Credential.Id == stored.Id);
            Assert.Equal("steam-web-api-key", polled.Credential.ApiKey);
        }
        finally
        {
            await Remove(db, user);
        }
    }

    private static IOptions<CredentialOptions> OptionsFor(string key, string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Credentials:EncryptionKey"] = key })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
        services.AddCredentialEncryption(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<CredentialOptions>>();
    }

    private static async Task<(User User, Platform Steam)> CreateUser(PlaydexDbContext db, string prefix)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User { Email = $"{prefix}-{Guid.NewGuid():n}@example.test", CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user, await db.Platforms.SingleAsync(platform => platform.Key == "steam"));
    }

    private static Integration SteamIntegration(User user, Platform steam, string accountId, string? apiKey) => new()
    {
        UserId = user.Id,
        Type = IntegrationType.Steam,
        PlatformId = steam.Id,
        ApiKey = apiKey,
        AccountId = accountId,
        Active = true,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.CreatedAt,
    };

    private static async Task Remove(PlaydexDbContext db, User user)
    {
        await db.Integrations.Where(item => item.UserId == user.Id).ExecuteDeleteAsync();
        await db.Users.Where(item => item.Id == user.Id).ExecuteDeleteAsync();
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

    private sealed class GameResolverNotUsed : IGameResolver
    {
        public Task<GameResolution> Resolve(GameResolutionRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
