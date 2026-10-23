using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AdminApiCollectionDefinition : ICollectionFixture<AdminApiFixture>
{
    public const string Name = "Admin API integration";
}

/// <summary>
/// Hosts the API against PLAYDEX_INTEGRATION_DATABASE_URL with one administrator and one ordinary user.
/// </summary>
public sealed class AdminApiFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin-api-test@playdex.local";
    public const string UserEmail = "user-api-test@playdex.local";
    public const string Password = "correct horse battery staple";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public WebApplicationFactory<Program>? Factory { get; private set; }

    public CapturingLoggerProvider Logs { get; } = new();

    public long AdminId { get; private set; }

    public long UserId { get; private set; }

    public async Task InitializeAsync()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        Environment.SetEnvironmentVariable("Auth__JwtSecret", "admin-api-integration-secret-at-least-thirty-two-bytes");
        Factory = new PlaydexApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(Logs)));

        await using var db = CreateDb();
        await RemoveTestUsersAsync(db);
        var now = DateTimeOffset.UtcNow;
        var admin = new User
        {
            Id = Random.Shared.NextInt64(6_200_000_000_000, 6_300_000_000_000),
            Email = AdminEmail,
            Password = BCrypt.Net.BCrypt.HashPassword(Password),
            IsAdmin = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var user = new User
        {
            Id = Random.Shared.NextInt64(6_300_000_000_000, 6_400_000_000_000),
            Email = UserEmail,
            Password = BCrypt.Net.BCrypt.HashPassword(Password),
            IsAdmin = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.AddRange(admin, user);
        await db.SaveChangesAsync();
        AdminId = admin.Id;
        UserId = user.Id;
    }

    public async Task DisposeAsync()
    {
        if (Factory is null)
        {
            return;
        }

        await using (var db = CreateDb())
        {
            await db.AiGenerations.Where(row => row.UserId == AdminId || row.UserId == UserId).ExecuteDeleteAsync();
            await RemoveTestUsersAsync(db);
        }

        await Factory.DisposeAsync();
    }

    public PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(Factory!.Services.GetRequiredService<NpgsqlDataSource>(), npgsql =>
            {
                npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                npgsql.MapEnum<IntegrationType>("integration_type");
                npgsql.MapEnum<CaptureMethod>("capture_method");
            })
            .Options);

    public HttpClient CreateClient() => Factory!.CreateClient();

    public async Task<HttpClient> LoginAsync(string email)
    {
        var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/login", new { email, password = Password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("data").GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task SetAdminAsync(bool isAdmin)
    {
        await using var db = CreateDb();
        var updated = await db.Users.Where(row => row.Email == AdminEmail).ExecuteUpdateAsync(setters => setters
            .SetProperty(user => user.IsAdmin, isAdmin)
            .SetProperty(user => user.UpdatedAt, DateTimeOffset.UtcNow));
        Assert.Equal(1, updated);
    }

    public static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("data").Deserialize<T>(Json)!;
    }

    public static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("error").GetProperty("error").GetString()!;
    }

    private static Task<int> RemoveTestUsersAsync(PlaydexDbContext db) =>
        db.Users.Where(user => user.Email == AdminEmail || user.Email == UserEmail).ExecuteDeleteAsync();
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

    public void Dispose() { }

    private sealed class CapturingLogger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => entries.Enqueue(formatter(state, exception));
    }
}
