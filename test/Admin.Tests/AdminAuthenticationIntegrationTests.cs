using System.Net;
using System.Reflection;
using System.Security.Claims;
using Admin.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playdex.Persistence;
using Xunit;

namespace Admin.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AdminAuthenticationFixtureGroup : ICollectionFixture<AdminAuthenticationFixture>
{
    public const string Name = "Admin authentication integration";
}

public sealed class AdminAuthenticationFixture : IAsyncLifetime
{
    private const string DatabaseVariable = "PLAYDEX_INTEGRATION_DATABASE_URL";
    private readonly string? databaseUrl = Environment.GetEnvironmentVariable(DatabaseVariable);

    public WebApplicationFactory<Program>? Factory { get; private set; }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("DATABASE_URL", databaseUrl));

        await using var scope = Factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlaydexDbContext>>();
        await using var db = await contextFactory.CreateDbContextAsync();
        await RemoveTestUsersAsync(db);
        var now = DateTimeOffset.UtcNow;
        db.Users.AddRange(
            new User
            {
                Id = Random.Shared.NextInt64(6_000_000_000_000, 6_100_000_000_000),
                Email = AdminEmail,
                Password = BCrypt.Net.BCrypt.HashPassword(Password),
                IsAdmin = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new User
            {
                Id = Random.Shared.NextInt64(6_100_000_000_000, 6_200_000_000_000),
                Email = UserEmail,
                Password = BCrypt.Net.BCrypt.HashPassword(Password),
                IsAdmin = false,
                CreatedAt = now,
                UpdatedAt = now,
            });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is null)
        {
            return;
        }

        await using var scope = Factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlaydexDbContext>>();
        await using var db = await contextFactory.CreateDbContextAsync();
        await RemoveTestUsersAsync(db);
        await Factory.DisposeAsync();
    }

    public async Task<long> GetAdminIdAsync()
    {
        await using var scope = Factory!.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlaydexDbContext>>();
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.Users.Where(user => user.Email == AdminEmail).Select(user => user.Id).SingleAsync();
    }

    public async Task SetAdminAsync(bool isAdmin)
    {
        await using var scope = Factory!.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlaydexDbContext>>();
        await using var db = await contextFactory.CreateDbContextAsync();
        var updated = await db.Users.Where(row => row.Email == AdminEmail).ExecuteUpdateAsync(setters => setters
            .SetProperty(user => user.IsAdmin, isAdmin)
            .SetProperty(user => user.UpdatedAt, DateTimeOffset.UtcNow));
        Assert.Equal(1, updated);
    }

    private static Task<int> RemoveTestUsersAsync(PlaydexDbContext db) =>
        db.Users.Where(user => user.Email == AdminEmail || user.Email == UserEmail).ExecuteDeleteAsync();

    public const string AdminEmail = "admin-auth-test@playdex.local";
    public const string UserEmail = "user-auth-test@playdex.local";
    public const string Password = "correct horse battery staple";
}

[Collection(AdminAuthenticationFixtureGroup.Name)]
public sealed class AdminAuthenticationIntegrationTests(AdminAuthenticationFixture fixture)
{
    [Fact]
    public async Task AdminLoginIssuesBoundedSecureSessionAndLogoutInvalidatesIt()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var client = CreateClient();
        using var login = await client.PostAsync("/auth/login", Form(AdminAuthenticationFixture.AdminEmail, AdminAuthenticationFixture.Password));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location?.OriginalString);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("playdex.admin=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=Fri, 31 Dec 9999", cookie, StringComparison.OrdinalIgnoreCase);

        client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';', 2)[0]);
        using var logout = await client.GetAsync("/auth/logout");
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/login", logout.Headers.Location?.OriginalString);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("playdex.admin=", StringComparison.Ordinal) && value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("missing-auth-test@playdex.local", AdminAuthenticationFixture.Password)]
    [InlineData(AdminAuthenticationFixture.AdminEmail, "wrong-password")]
    [InlineData(AdminAuthenticationFixture.UserEmail, AdminAuthenticationFixture.Password)]
    public async Task InvalidMissingAndNonAdminCredentialsHaveOneFailure(string email, string password)
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var client = CreateClient();
        using var response = await client.PostAsync("/auth/login", Form(email, password));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?failed=1", response.Headers.Location?.OriginalString);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task AuthenticationTrimsEmailAndPrivilegeGuardRejectsRevocation()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var authentication = scope.ServiceProvider.GetRequiredService<AdminAuthenticationService>();
        var guard = scope.ServiceProvider.GetRequiredService<AdminOperationGuard>();
        var admin = await authentication.ValidateAsync($"  {AdminAuthenticationFixture.AdminEmail}  ", AdminAuthenticationFixture.Password);
        Assert.NotNull(admin);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            CookieAuthenticationDefaults.AuthenticationScheme));
        Assert.True(await guard.IsAdministratorAsync(principal));

        await fixture.SetAdminAsync(false);
        try
        {
            Assert.False(await guard.IsAdministratorAsync(principal));
        }
        finally
        {
            await fixture.SetAdminAsync(true);
        }
    }

    [Fact]
    public void WorkflowPagesRequireAdministratorAuthorization()
    {
        var pages = new[]
        {
            typeof(global::Admin.Components.Pages.Home),
            typeof(global::Admin.Components.Pages.Integrations),
            typeof(global::Admin.Components.Pages.Sessions),
            typeof(global::Admin.Components.Pages.Games),
            typeof(global::Admin.Components.Pages.Logout),
        };

        foreach (var page in pages)
        {
            var attribute = Assert.Single(page.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
            Assert.Equal("Administrator", attribute.Policy);
        }
    }

    private HttpClient CreateClient() => fixture.Factory!.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://admin.playdex.test"),
    });

    private static FormUrlEncodedContent Form(string email, string password) => new(
        new Dictionary<string, string> { ["email"] = email, ["password"] = password });
}
