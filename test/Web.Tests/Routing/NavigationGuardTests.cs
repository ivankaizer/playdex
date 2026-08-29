using Web.Routing;
using Xunit;

namespace Web.Tests.Routing;

public sealed class NavigationGuardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootAlwaysRenders(bool isLoggedIn)
    {
        var decision = NavigationGuard.Resolve("/", isLoggedIn);

        Assert.Equal(NavigationOutcome.Render, decision.Outcome);
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/sessions")]
    [InlineData("/recap")]
    [InlineData("/mapping")]
    [InlineData("/settings")]
    [InlineData("/games/3H4h3")]
    public void AnonymousProtectedRoutesReplaceWithHome(string path)
    {
        var decision = NavigationGuard.Resolve(path, false);

        Assert.Equal(NavigationOutcome.Redirect, decision.Outcome);
        Assert.Equal("/", decision.RedirectPath);
    }

    [Fact]
    public void LoggedInProtectedRoutesRender()
    {
        string[] paths = ["/dashboard", "/sessions", "/recap", "/mapping", "/settings", "/games/3H4h3"];

        foreach (var path in paths)
        {
            var decision = NavigationGuard.Resolve(path, true);
            Assert.Equal(NavigationOutcome.Render, decision.Outcome);
        }
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
        var decision = NavigationGuard.Resolve(path, true);

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
        var decision = NavigationGuard.Resolve(path, true);

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
        var instruction = NavigationGuard.PlanNavigation("/sessions", "/dashboard", replaceHistory: true);

        Assert.Equal(NavigationInstructionKind.Navigate, instruction.Kind);
        Assert.Equal("/dashboard", instruction.Target);
        Assert.True(instruction.ReplaceHistory);
        Assert.False(instruction.ScrollToTop);
    }
}
