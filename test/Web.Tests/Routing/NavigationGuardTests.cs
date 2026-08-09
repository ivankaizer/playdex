using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Web;
using Web.Routing;
using Xunit;

namespace Web.Tests.Routing;

public sealed class NavigationGuardTests
{
    [Fact]
    public async Task CancelledAdminNavigationCannotOverrideNewerRoute()
    {
        var app = new App();
        var navigation = new RecordingNavigationManager();
        var profile = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetProperty(app, "Navigation", navigation);
        SetField(app, "_initializationTask", Task.CompletedTask);
        SetField(app, "_profileTask", profile.Task);
        SetField(app, "_isLoggedIn", true);
        SetField(app, "_isProfileResolved", false);
        SetField(app, "_isAdmin", false);

        using var staleNavigationCancellation = new CancellationTokenSource();
        var staleNavigation = NavigateAsync(app, "/admin", staleNavigationCancellation.Token);
        Assert.False(staleNavigation.IsCompleted);

        staleNavigationCancellation.Cancel();
        await NavigateAsync(app, "/sessions", CancellationToken.None);
        SetField(app, "_isProfileResolved", true);
        profile.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => staleNavigation);
        Assert.Equal(
            NavigationOutcome.Render,
            GetField<NavigationDecision>(app, "_currentDecision").Outcome);
        Assert.Empty(navigation.Navigations);
        Assert.True(profile.Task.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void RootAlwaysRenders(bool isLoggedIn, bool isProfileResolved, bool isAdmin)
    {
        var decision = NavigationGuard.Resolve("/", isLoggedIn, isProfileResolved, isAdmin);

        Assert.Equal(NavigationOutcome.Render, decision.Outcome);
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/sessions")]
    [InlineData("/recap")]
    [InlineData("/mapping")]
    [InlineData("/settings")]
    [InlineData("/admin")]
    [InlineData("/games/3H4h3")]
    public void AnonymousProtectedRoutesReplaceWithHome(string path)
    {
        var decision = NavigationGuard.Resolve(path, false, true, false);

        Assert.Equal(NavigationOutcome.Redirect, decision.Outcome);
        Assert.Equal("/", decision.RedirectPath);
    }

    [Fact]
    public void LoggedInStandardProtectedRoutesRenderWhileProfileIsPending()
    {
        string[] paths = ["/dashboard", "/sessions", "/recap", "/mapping", "/settings", "/games/3H4h3"];

        foreach (var path in paths)
        {
            var decision = NavigationGuard.Resolve(path, true, false, false);
            Assert.Equal(NavigationOutcome.Render, decision.Outcome);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdminWaitsForProfileEvenWhenCachedAdminExists(bool isAdmin)
    {
        var decision = NavigationGuard.Resolve("/admin", true, false, isAdmin);

        Assert.Equal(NavigationOutcome.Wait, decision.Outcome);
    }

    [Theory]
    [InlineData(false, NavigationOutcome.Redirect, "/dashboard")]
    [InlineData(true, NavigationOutcome.Render, null)]
    public void ResolvedAdminRouteUsesCurrentRole(
        bool isAdmin,
        NavigationOutcome outcome,
        string? redirectPath)
    {
        var decision = NavigationGuard.Resolve("/admin", true, true, isAdmin);

        Assert.Equal(outcome, decision.Outcome);
        Assert.Equal(redirectPath, decision.RedirectPath);
    }

    [Theory]
    [InlineData("/games")]
    [InlineData("/dashboard/")]
    [InlineData("/Dashboard")]
    [InlineData("/ADMIN")]
    [InlineData("/games%2F42")]
    [InlineData("/unknown")]
    public void NonExactAndUnknownPathsUseHomeFallback(string path)
    {
        var decision = NavigationGuard.Resolve(path, true, true, true);

        Assert.Equal(NavigationOutcome.HomeFallback, decision.Outcome);
    }

    [Theory]
    [InlineData("/games/42", "3H4h3")]
    [InlineData("/games/00042/", "3H4h3")]
    [InlineData("/games/%33H4h3", "3H4h3")]
    [InlineData("/games/%20%33H4h3%20/", "3H4h3")]
    [InlineData("/games/3h4H3", null)]
    [InlineData("/games/not-a-hash", null)]
    [InlineData("/games/", null)]
    [InlineData("/games/3H4h3/extra", null)]
    [InlineData("/games/%253H4h3", null)]
    [InlineData("/games/%E0%A4%A", null)]
    public void GameRoutesMatchReactNormalization(string path, string? expectedHash)
    {
        var decision = NavigationGuard.Resolve(path, true, true, false);

        Assert.Equal(NavigationOutcome.Render, decision.Outcome);
        Assert.Equal(expectedHash, decision.GameHash);
    }

    [Fact]
    public void OrdinaryNavigationToExactCurrentPathAndQueryIsANoOp()
    {
        var instruction = NavigationGuard.PlanNavigation(
            "/recap?from=2026-01-01",
            "/recap?from=2026-01-01",
            replaceHistory: false);

        Assert.Equal(NavigationInstructionKind.NoOp, instruction.Kind);
        Assert.False(instruction.ScrollToTop);
    }

    [Fact]
    public void OrdinaryNavigationWithDifferentQueryPushesAndSmoothScrolls()
    {
        var instruction = NavigationGuard.PlanNavigation(
            "/recap?from=2026-01-01",
            "/recap?from=2026-02-01",
            replaceHistory: false);

        Assert.Equal(NavigationInstructionKind.Navigate, instruction.Kind);
        Assert.Equal("/recap?from=2026-02-01", instruction.Target);
        Assert.False(instruction.ReplaceHistory);
        Assert.True(instruction.ScrollToTop);
    }

    [Fact]
    public void RedirectNavigationReplacesWithoutScrolling()
    {
        var instruction = NavigationGuard.PlanNavigation("/admin", "/dashboard", replaceHistory: true);

        Assert.Equal(NavigationInstructionKind.Navigate, instruction.Kind);
        Assert.Equal("/dashboard", instruction.Target);
        Assert.True(instruction.ReplaceHistory);
        Assert.False(instruction.ScrollToTop);
    }

    private static Task NavigateAsync(App app, string path, CancellationToken cancellationToken)
    {
        var constructor = typeof(NavigationContext).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(CancellationToken)],
            modifiers: null);
        Assert.NotNull(constructor);

        var context = (NavigationContext)constructor.Invoke([path, cancellationToken]);
        var handler = typeof(App).GetMethod(
            "HandleNavigationAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(handler);

        return (Task)handler.Invoke(app, [context])!;
    }

    private static void SetField<T>(App app, string name, T value)
    {
        var field = typeof(App).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(app, value);
    }

    private static T GetField<T>(App app, string name)
    {
        var field = typeof(App).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field.GetValue(app)!;
    }

    private static void SetProperty<T>(App app, string name, T value)
    {
        var property = typeof(App).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(app, value);
    }

    private sealed class RecordingNavigationManager : NavigationManager
    {
        public RecordingNavigationManager()
        {
            Initialize("https://playdex.test/", "https://playdex.test/admin");
        }

        public List<string> Navigations { get; } = [];

        protected override void NavigateToCore(string uri, bool forceLoad) => Navigations.Add(uri);

        protected override void NavigateToCore(string uri, NavigationOptions options) =>
            Navigations.Add(uri);
    }
}
