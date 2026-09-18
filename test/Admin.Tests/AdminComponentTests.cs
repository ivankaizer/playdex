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
    public void MobileNavigationCanBeOpenedDismissedAndClosesAfterNavigation()
    {
        using var context = CreateContext(new FakeOperations(), out _);
        var component = context.Render<global::Admin.Components.Layout.MainLayout>();
        var toggle = component.Find("button[aria-controls=admin-navigation]");
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));

        toggle.Click();
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        component.Find("button[aria-label='Close navigation']").Click();
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));

        toggle.Click();
        component.Find("header").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));

        toggle.Click();
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/sessions");
        component.WaitForAssertion(() => Assert.Equal("false", toggle.GetAttribute("aria-expanded")));
    }

    [Fact]
    public void SessionsNavigationDuringLoadingEventuallyAppliesLatestScope()
    {
        var pending = new TaskCompletionSource<SessionSearchResult>();
        var fake = new FakeOperations { SessionPage = (_, _, _) => pending.Task };
        using var context = CreateContext(fake, out _);
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/sessions?status=live");
        var component = context.Render<global::Admin.Components.Pages.Sessions>();
        Assert.Equal("live", fake.SessionScope.Status);
        navigation.NavigateTo("/sessions?status=ended");
        fake.SessionPage = (_, _, _) => Task.FromResult(new SessionSearchResult([], 0, 0));
        pending.SetResult(new SessionSearchResult([], 0, 0));
        component.WaitForAssertion(() => Assert.Equal("ended", fake.SessionScope.Status));
    }

    [Fact]
    public void SessionsApplyDashboardQueryScopeWithoutResettingPagingOnRerender()
    {
        var fake = new FakeOperations();
        using var context = CreateContext(fake, out _);
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/sessions?status=live&from=2026-09-15T22%3A00%3A00Z&to=2026-09-16T22%3A00%3A00Z");
        var component = context.Render<global::Admin.Components.Pages.Sessions>();
        component.WaitForAssertion(() => Assert.Equal("live", fake.SessionScope.Status));
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 22, 0, 0, TimeSpan.Zero), fake.SessionScope.From);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 22, 0, 0, TimeSpan.Zero), fake.SessionScope.To);
        navigation.NavigateTo("/sessions?status=ended");
        component.WaitForAssertion(() => Assert.Equal("ended", fake.SessionScope.Status));
        Assert.Null(fake.SessionScope.From);
    }

    [Fact]
    public async Task IntegrationsShowApiKeysFilterAndPreserveIntentionalEditSemantics()
    {
        var fake = new FakeOperations
        {
            Integrations = _ => Task.FromResult<IReadOnlyList<IntegrationSummary>>([
                new(1, "owner@example.test", "steam", "steam-account", true, "steam-test-key"),
                new(2, "owner@example.test", "api", "api-account", true, "api-test-key"),
            ]),
            Platforms = _ => Task.FromResult<IReadOnlyList<PlatformSummary>>([new("steam", "Steam", 1, 1)]),
        };
        using var context = CreateContext(fake, out _);
        var component = context.Render<global::Admin.Components.Pages.Integrations>();
        component.WaitForAssertion(() => Assert.Contains("steam-account", component.Markup, StringComparison.Ordinal));
        Assert.Contains("steam-test-key", component.Markup, StringComparison.Ordinal);
        Assert.Contains("api-test-key", component.Markup, StringComparison.Ordinal);

        SetField(component.Instance, "_filter", "steam");
        component.Render();
        Assert.Contains("steam-account", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("api-account", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("api-test-key", component.Markup, StringComparison.Ordinal);

        component.FindAll("button").Single(button => button.TextContent.Contains("Edit", StringComparison.Ordinal)).Click();
        component.FindAll("input[type=checkbox]")[0].Change(true);
        var keyInput = component.FindAll("input").Single(input => input.GetAttribute("placeholder") == "API key (empty clears it)");
        Assert.Equal("steam-test-key", keyInput.GetAttribute("value"));
        Assert.NotEqual("password", keyInput.GetAttribute("type"));
        component.FindAll("input[type=checkbox]")[0].Change(false);
        component.FindAll("input[type=checkbox]")[1].Change(true);
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SavingGameFromDefaultListRefreshesWithoutRequiringSearch(string query)
    {
        var game = new GameSummary(1, "Original title", null, false, false);
        var fake = new FakeOperations();
        fake.Games = (_, _) => Task.FromResult<IReadOnlyList<GameSummary>>([
            fake.UpdateGameCalls == 0 ? game : game with { Title = "Updated title" },
        ]);
        using var context = CreateContext(fake, out _);
        var component = context.Render<global::Admin.Components.Pages.Games>();
        SetField(component.Instance, "_query", query);
        var grid = component.FindComponent<RadzenDataGrid<GameSummary>>();
        await component.InvokeAsync(() => grid.Instance.RowSelect.InvokeAsync(game));
        component.FindAll("input").Single(input => input.GetAttribute("placeholder") == "Title").Change("Updated title");
        component.FindAll("button").Single(button => button.TextContent == "Save").Click();
        component.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("A game search query is required", component.Markup, StringComparison.Ordinal);
            Assert.Contains("Updated title", component.Markup, StringComparison.Ordinal);
        });
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
        public (string? Status, DateTimeOffset? From, DateTimeOffset? To) SessionScope { get; private set; }
        public (bool ApiKeyChanged, bool AccountChanged, string? Account) LastCredentialIntent { get; private set; }

        public Task<IReadOnlyList<UserRecentSessions>> GetRecentSessionsAsync(CancellationToken ct = default) => Recent(ct);
        public Task<IReadOnlyList<PlatformSummary>> GetPlatformSummariesAsync(CancellationToken ct = default) => Platforms(ct);
        public Task<IReadOnlyList<IntegrationSummary>> GetIntegrationsAsync(ClaimsPrincipal principal, CancellationToken ct = default) => Integrations(ct);
        public Task<IReadOnlyList<GameSummary>> SearchGamesAsync(string query, CancellationToken ct = default) =>
            string.IsNullOrWhiteSpace(query)
                ? Task.FromException<IReadOnlyList<GameSummary>>(new ArgumentException("A game search query is required.", nameof(query)))
                : Games(query, ct);
        public Task<IReadOnlyList<GameSummary>> ListGamesAsync(CancellationToken ct = default) => Games("", ct);
        public Task<IReadOnlyList<SessionSummary>> SearchSessionsAsync(string query, int page, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SessionSummary>>([]);
        public Task<SessionSearchResult> SearchSessionsPageAsync(string query, int page, string? status = null, DateTimeOffset? startedFrom = null, DateTimeOffset? startedBefore = null, CancellationToken ct = default) { LastSessionPage = page; SessionScope = (status, startedFrom, startedBefore); return SessionPage(query, page, ct); }
        public Task<int> SetPlatformEnabledAsync(ClaimsPrincipal principal, string platform, bool enabled, CancellationToken ct = default) { PlatformMutationCalls++; return SetPlatform(principal, platform, enabled, ct); }
        public Task<int> TransferSessionsAsync(ClaimsPrincipal principal, IReadOnlyCollection<long> sessionIds, long targetIntegrationId, CancellationToken ct = default) { TransferCalls++; return Transfer(principal, sessionIds, targetIntegrationId, ct); }
        public Task<bool> UpdateIntegrationAsync(ClaimsPrincipal principal, long id, bool apiKeyChanged, string? apiKey, bool accountIdChanged, string? accountId, CancellationToken ct = default) { LastCredentialIntent = (apiKeyChanged, accountIdChanged, accountId); return Task.FromResult(true); }
        public Task<bool> UpdateGameAsync(ClaimsPrincipal principal, long id, string? title, bool titleChanged, string? capsuleUrl, bool capsuleUrlChanged, bool? titleLocked, bool? capsuleUrlLocked, CancellationToken ct = default) { UpdateGameCalls++; return Task.FromResult(true); }
        public Task MergeGamesAsync(ClaimsPrincipal principal, long sourceId, long targetId, CancellationToken ct = default) { MergeCalls++; return Merge(principal, sourceId, targetId, ct); }
    }
}
