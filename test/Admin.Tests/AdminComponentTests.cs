using System.Reflection;
using System.Security.Claims;
using Admin.Features;
using Admin.Infrastructure;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Radzen.Blazor;
using Xunit;

namespace Admin.Tests;

public sealed class AdminComponentTests
{
    [Fact]
    public async Task DashboardShowsLoadingDataEmptyAndRecoverableErrorStates()
    {
        var recent = new TaskCompletionSource<IReadOnlyList<UserRecentSessions>>();
        using var context = CreateContext(new FakeOperations { Recent = _ => recent.Task }, out _);

        var component = context.Render<global::Admin.Components.Pages.Home>();
        Assert.NotEmpty(component.FindComponents<RadzenProgressBar>());

        using var dataContext = CreateContext(new FakeOperations
        {
            Recent = _ => Task.FromResult<IReadOnlyList<UserRecentSessions>>([
                new UserRecentSessions(1, "admin@example.test", [new RecentSession(7, "Live game", DateTimeOffset.UtcNow.AddMinutes(-5), null)]),
            ]),
        }, out _);
        var loaded = dataContext.Render<global::Admin.Components.Pages.Home>();
        Assert.True(loaded.Markup.Contains("admin@example.test", StringComparison.Ordinal), loaded.Markup);
        Assert.Contains("Live", loaded.Markup, StringComparison.Ordinal);

        var recoveryFake = new FakeOperations
        {
            Recent = _ => Task.FromException<IReadOnlyList<UserRecentSessions>>(new InvalidOperationException("dashboard unavailable")),
        };
        using var recoveryContext = CreateContext(recoveryFake, out _);
        var recovery = recoveryContext.Render<global::Admin.Components.Pages.Home>();
        Assert.Contains("dashboard unavailable", recovery.Markup, StringComparison.Ordinal);
        recoveryFake.Recent = _ => Task.FromResult<IReadOnlyList<UserRecentSessions>>([]);
        await recovery.FindAll("button").Single(button => button.TextContent.Contains("Retry", StringComparison.Ordinal)).ClickAsync(new());
        recovery.WaitForAssertion(() => Assert.Contains("No sessions found", recovery.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task IntegrationsFilterConcealsSecretsAndPreservesIntentionalEditSemantics()
    {
        var fake = new FakeOperations
        {
            Integrations = _ => Task.FromResult<IReadOnlyList<IntegrationSummary>>([
                new(1, "owner@example.test", "steam", "steam-account", true),
                new(2, "owner@example.test", "api", "api-account", true),
            ]),
            Platforms = _ => Task.FromResult<IReadOnlyList<PlatformSummary>>([new("steam", "Steam", 1, 1)]),
        };
        using var context = CreateContext(fake, out _);
        var component = context.Render<global::Admin.Components.Pages.Integrations>();
        component.WaitForAssertion(() => Assert.Contains("steam-account", component.Markup, StringComparison.Ordinal));
        Assert.Contains("••••••••", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", component.Markup, StringComparison.OrdinalIgnoreCase);

        SetField(component.Instance, "_filter", "steam");
        component.Render();
        Assert.Contains("steam-account", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("api-account", component.Markup, StringComparison.Ordinal);

        component.FindAll("button").Single(button => button.TextContent.Contains("Edit", StringComparison.Ordinal)).Click();
        var checkboxes = component.FindAll("input[type=checkbox]");
        checkboxes[1].Change(true);
        component.FindAll("input").Single(input => input.GetAttribute("placeholder") == "Account identifier").Change("  changed-account  ");
        component.FindAll("button").Single(button => button.TextContent.Contains("Save credentials", StringComparison.Ordinal)).Click();
        component.WaitForAssertion(() => Assert.Equal((false, true, "  changed-account  "), fake.LastCredentialIntent));
    }

    [Fact]
    public async Task IntegrationsRequireConfirmationPreventDuplicatesAndRecoverAfterErrors()
    {
        var update = new TaskCompletionSource<int>();
        var fake = new FakeOperations
        {
            Integrations = _ => Task.FromResult<IReadOnlyList<IntegrationSummary>>([]),
            Platforms = _ => Task.FromResult<IReadOnlyList<PlatformSummary>>([new("steam", "Steam", 3, 3)]),
            SetPlatform = (_, _, _, _) => update.Task,
        };
        using var context = CreateContext(fake, out var interactions);
        interactions.Confirmed = false;
        var component = context.Render<global::Admin.Components.Pages.Integrations>();
        var toggle = component.FindAll("button").Single(button => button.TextContent.Contains("Disable steam", StringComparison.Ordinal));
        toggle.Click();
        Assert.Equal(0, fake.PlatformMutationCalls);
        Assert.Contains("3 integrations", interactions.LastConfirmation, StringComparison.Ordinal);

        interactions.Confirmed = true;
        toggle.Click();
        component.WaitForAssertion(() => Assert.Equal(1, fake.PlatformMutationCalls));
        Assert.NotEmpty(component.FindComponents<RadzenProgressBar>());
        await InvokePrivateAsync(component.Instance, "TogglePlatformAsync", new PlatformSummary("steam", "Steam", 3, 3));
        Assert.Equal(1, fake.PlatformMutationCalls);
        await component.InvokeAsync(() => update.SetResult(3));
        component.WaitForAssertion(() => Assert.Contains(interactions.Successes, message => message.Contains("Updated 3", StringComparison.Ordinal)));

        var reads = 0;
        fake.Integrations = _ => ++reads == 1
            ? Task.FromException<IReadOnlyList<IntegrationSummary>>(new InvalidOperationException("temporary failure"))
            : Task.FromResult<IReadOnlyList<IntegrationSummary>>([]);
        component.FindAll("button").Single(button => button.TextContent.Contains("Refresh", StringComparison.Ordinal)).Click();
        component.WaitForAssertion(() => Assert.Contains("temporary failure", component.Markup, StringComparison.Ordinal));
        component.FindAll("button").Single(button => button.TextContent.Contains("Retry", StringComparison.Ordinal)).Click();
        component.WaitForAssertion(() => Assert.DoesNotContain("temporary failure", component.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SessionsSearchPageSelectTransferAndFailureStatesAreInteractive()
    {
        var transfer = new TaskCompletionSource<int>();
        var fake = new FakeOperations
        {
            Integrations = _ => Task.FromResult<IReadOnlyList<IntegrationSummary>>([new(90, "target@example.test", "api", "target", true)]),
            SessionPage = (_, page, _) => Task.FromResult(new SessionSearchResult([
                new(11, "owner@example.test", "steam", "source", "Game", DateTimeOffset.UtcNow, null),
                new(10, "owner@example.test", "steam", "source", "Game", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow),
            ], 27, page)),
            Transfer = (_, _, _, _) => transfer.Task,
        };
        using var context = CreateContext(fake, out var interactions);
        var component = context.Render<global::Admin.Components.Pages.Sessions>();
        component.WaitForAssertion(() => Assert.Contains("Total: 27", component.Markup, StringComparison.Ordinal));
        component.Find("input[type=checkbox]").Change(true);
        SetField(component.Instance, "_target", (long?)90);
        component.Render();
        var button = component.FindAll("button").Single(item => item.TextContent.Contains("Transfer selected", StringComparison.Ordinal));
        button.Click();
        component.WaitForAssertion(() => Assert.Equal(1, fake.TransferCalls));
        Assert.Contains("1 selected sessions", interactions.LastConfirmation, StringComparison.Ordinal);
        Assert.True(component.FindAll("button").Single(item => item.TextContent.Contains("Transfer selected", StringComparison.Ordinal)).HasAttribute("disabled"));
        transfer.SetResult(1);
        component.WaitForAssertion(() => Assert.Contains(interactions.Successes, message => message.Contains("Transferred 1", StringComparison.Ordinal)));

        component.FindAll("button").Single(item => item.TextContent.Contains("Next", StringComparison.Ordinal)).Click();
        component.WaitForAssertion(() => Assert.Equal(1, fake.LastSessionPage));
        fake.SessionPage = (_, _, _) => Task.FromException<SessionSearchResult>(new InvalidOperationException("session search failed"));
        component.FindAll("button").Single(item => item.TextContent.Contains("Search", StringComparison.Ordinal)).Click();
        component.WaitForAssertion(() => Assert.Contains("session search failed", component.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GamesSearchEditMergeConfirmationAndFailuresAreInteractive()
    {
        var games = new[]
        {
            new GameSummary(1, "Source", "https://source", false, false),
            new GameSummary(2, "Target", null, true, true),
        };
        var fake = new FakeOperations { Games = (_, _) => Task.FromResult<IReadOnlyList<GameSummary>>(games) };
        using var context = CreateContext(fake, out var interactions);
        var component = context.Render<global::Admin.Components.Pages.Games>();
        component.Find("input").Change("Source");
        component.FindAll("button").Single(button => button.TextContent.Contains("Search", StringComparison.Ordinal)).Click();
        component.WaitForAssertion(() => Assert.Contains("Source", component.Markup, StringComparison.Ordinal));

        var grid = component.FindComponent<RadzenDataGrid<GameSummary>>();
        await component.InvokeAsync(() => grid.Instance.RowSelect.InvokeAsync(games[0]));
        SetField(component.Instance, "_title", "Updated source");
        component.Render();
        component.FindAll("button").Single(button => button.TextContent == "Save").Click();
        component.WaitForAssertion(() => Assert.Equal(1, fake.UpdateGameCalls));

        SetField(component.Instance, "_sourceId", (long?)1);
        SetField(component.Instance, "_targetId", (long?)2);
        component.Render();
        interactions.Confirmed = false;
        component.FindAll("button").Single(button => button.TextContent == "Merge").Click();
        Assert.Equal(0, fake.MergeCalls);
        interactions.Confirmed = true;
        fake.Merge = (_, _, _, _) => Task.FromException(new InvalidOperationException("merge failed"));
        component.FindAll("button").Single(button => button.TextContent == "Merge").Click();
        component.WaitForAssertion(() => Assert.Contains("merge failed", component.Markup, StringComparison.Ordinal));
        Assert.Equal(1, fake.MergeCalls);
        Assert.Contains(interactions.Errors, message => message.Contains("merge failed", StringComparison.Ordinal));
    }

    private static BunitContext CreateContext(FakeOperations operations, out FakeInteractions interactions)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddRadzenComponents();
        context.Services.AddSingleton<IAdminOperations>(operations);
        context.Services.AddSingleton<BrowserTimeZone>();
        interactions = new FakeInteractions();
        context.Services.AddSingleton<IAdminInteractions>(interactions);
        var authorization = context.AddAuthorization();
        authorization.SetAuthorized("admin");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, "1"));
        return context;
    }

    private static void SetField<TComponent>(TComponent component, string name, object? value) where TComponent : class =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private static async Task InvokePrivateAsync<TComponent>(TComponent component, string name, params object[] arguments) where TComponent : class
    {
        var task = (Task)component.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments)!;
        await task;
    }

    private sealed class FakeInteractions : IAdminInteractions
    {
        public bool Confirmed { get; set; } = true;
        public string LastConfirmation { get; private set; } = "";
        public List<string> Successes { get; } = [];
        public List<string> Errors { get; } = [];
        public Task<bool> ConfirmAsync(string message, string title) { LastConfirmation = message; return Task.FromResult(Confirmed); }
        public void Success(string summary, string detail) => Successes.Add($"{summary}: {detail}");
        public void ShowError(string summary, string detail) => Errors.Add($"{summary}: {detail}");
    }

    private sealed class FakeOperations : IAdminOperations
    {
        public Func<CancellationToken, Task<IReadOnlyList<UserRecentSessions>>> Recent { get; set; } = _ => Task.FromResult<IReadOnlyList<UserRecentSessions>>([]);
        public Func<CancellationToken, Task<IReadOnlyList<PlatformSummary>>> Platforms { get; set; } = _ => Task.FromResult<IReadOnlyList<PlatformSummary>>([]);
        public Func<ClaimsPrincipal, string, bool, CancellationToken, Task<int>> SetPlatform { get; set; } = (_, _, _, _) => Task.FromResult(0);
        public Func<CancellationToken, Task<IReadOnlyList<IntegrationSummary>>> Integrations { get; set; } = _ => Task.FromResult<IReadOnlyList<IntegrationSummary>>([]);
        public Func<string, int, CancellationToken, Task<SessionSearchResult>> SessionPage { get; set; } = (_, page, _) => Task.FromResult(new SessionSearchResult([], 0, page));
        public Func<string, CancellationToken, Task<IReadOnlyList<GameSummary>>> Games { get; set; } = (_, _) => Task.FromResult<IReadOnlyList<GameSummary>>([]);
        public Func<ClaimsPrincipal, IReadOnlyCollection<long>, long, CancellationToken, Task<int>> Transfer { get; set; } = (_, _, _, _) => Task.FromResult(0);
        public Func<ClaimsPrincipal, long, long, CancellationToken, Task> Merge { get; set; } = (_, _, _, _) => Task.CompletedTask;
        public int PlatformMutationCalls { get; private set; }
        public int TransferCalls { get; private set; }
        public int UpdateGameCalls { get; private set; }
        public int MergeCalls { get; private set; }
        public int LastSessionPage { get; private set; }
        public (bool ApiKeyChanged, bool AccountChanged, string? Account) LastCredentialIntent { get; private set; }

        public Task<IReadOnlyList<UserRecentSessions>> GetRecentSessionsAsync(CancellationToken ct = default) => Recent(ct);
        public Task<IReadOnlyList<PlatformSummary>> GetPlatformSummariesAsync(CancellationToken ct = default) => Platforms(ct);
        public Task<IReadOnlyList<IntegrationSummary>> GetIntegrationsAsync(CancellationToken ct = default) => Integrations(ct);
        public Task<IReadOnlyList<GameSummary>> SearchGamesAsync(string query, CancellationToken ct = default) => Games(query, ct);
        public Task<IReadOnlyList<GameSummary>> ListGamesAsync(CancellationToken ct = default) => Games("", ct);
        public Task<IReadOnlyList<SessionSummary>> SearchSessionsAsync(string query, int page, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SessionSummary>>([]);
        public Task<SessionSearchResult> SearchSessionsPageAsync(string query, int page, string? status = null, CancellationToken ct = default) { LastSessionPage = page; return SessionPage(query, page, ct); }
        public Task<int> SetPlatformEnabledAsync(ClaimsPrincipal principal, string platform, bool enabled, CancellationToken ct = default) { PlatformMutationCalls++; return SetPlatform(principal, platform, enabled, ct); }
        public Task<int> TransferSessionsAsync(ClaimsPrincipal principal, IReadOnlyCollection<long> sessionIds, long targetIntegrationId, CancellationToken ct = default) { TransferCalls++; return Transfer(principal, sessionIds, targetIntegrationId, ct); }
        public Task<bool> UpdateIntegrationAsync(ClaimsPrincipal principal, long id, bool apiKeyChanged, string? apiKey, bool accountIdChanged, string? accountId, CancellationToken ct = default) { LastCredentialIntent = (apiKeyChanged, accountIdChanged, accountId); return Task.FromResult(true); }
        public Task<bool> UpdateGameAsync(ClaimsPrincipal principal, long id, string? title, bool titleChanged, string? capsuleUrl, bool capsuleUrlChanged, bool? titleLocked, bool? capsuleUrlLocked, CancellationToken ct = default) { UpdateGameCalls++; return Task.FromResult(true); }
        public Task MergeGamesAsync(ClaimsPrincipal principal, long sourceId, long targetId, CancellationToken ct = default) { MergeCalls++; return Merge(principal, sourceId, targetId, ct); }
    }
}
