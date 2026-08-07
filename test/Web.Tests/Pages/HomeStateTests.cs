using Web.Shared.Auth;
using Web.Modules.Home;
using Xunit;

namespace Web.Tests.Pages;

public sealed class HomeStateTests
{
    [Fact]
    public void HomeStateTypes_AreInternalImplementationDetails()
    {
        Assert.False(typeof(HomeLoginStatus).IsPublic);
        Assert.False(typeof(HomeState).IsPublic);
    }

    [Fact]
    public void LoginLifecycle_DisablesOnlyWhileLoadingAndPreservesTheReactMessages()
    {
        var state = new HomeState();

        state.BeginLogin();

        Assert.Equal(HomeLoginStatus.Loading, state.LoginStatus);
        Assert.True(state.IsLoginDisabled);
        Assert.Empty(state.Error);

        state.FailLogin("Invalid credentials.");

        Assert.Equal(HomeLoginStatus.Idle, state.LoginStatus);
        Assert.False(state.IsLoginDisabled);
        Assert.Equal("Invalid credentials.", state.Error);
    }

    [Fact]
    public void SuccessfulLogin_StoresTheSessionEmailAndSuccessState()
    {
        var state = new HomeState();
        state.BeginLogin();

        state.CompleteLogin(new LoginResponse(42, "player@example.com", false, "access", "refresh"));

        Assert.Equal(HomeLoginStatus.Success, state.LoginStatus);
        Assert.Equal("player@example.com", state.SessionEmail);
        Assert.Equal("player@example.com", state.ProfileLabel);
    }

    [Fact]
    public void ProfileFallback_LoadsOnlyWhenATokenExistsWithoutACachedEmail()
    {
        var state = new HomeState();

        Assert.False(state.BeginProfile(isLoggedIn: false, "stale@example.com"));
        Assert.Empty(state.SessionEmail);
        Assert.Equal("unknown", state.ProfileLabel);

        Assert.False(state.BeginProfile(isLoggedIn: true, "cached@example.com"));
        Assert.Equal("cached@example.com", state.ProfileLabel);

        Assert.True(state.BeginProfile(isLoggedIn: true, cachedEmail: null));
        Assert.True(state.IsProfileLoading);
        Assert.Equal("Loading...", state.ProfileLabel);

        state.CompleteProfile(new MeResponse(42, "loaded@example.com", true));

        Assert.False(state.IsProfileLoading);
        Assert.Equal("loaded@example.com", state.ProfileLabel);
    }

    [Fact]
    public void AuthenticatedHome_FirstRenderUsesTheResolvedRouterSession()
    {
        var root = FindWebRoot();
        var app = File.ReadAllText(Path.Combine(root, "App.razor"));
        var home = File.ReadAllText(Path.Combine(root, "Modules", "Home", "Home.razor.cs"));

        Assert.Contains("new(NavigationOutcome.Wait)", app);
        Assert.Contains("<CascadingValue Value=\"@_homeSession\">", app);
        Assert.Contains("AuthSession.GetEmailAsync()", app);
        Assert.Contains("[CascadingParameter]", home);
        Assert.Contains("HomeSessionState InitialSession", home);

        var sessionAssignment = home.IndexOf(
            "_isLoggedIn = InitialSession.IsLoggedIn;",
            StringComparison.Ordinal);
        var profileInitialization = home.IndexOf(
            "_state.BeginProfile(_isLoggedIn, InitialSession.CachedEmail)",
            StringComparison.Ordinal);
        var firstAwait = home.IndexOf("await ", StringComparison.Ordinal);

        Assert.True(sessionAssignment >= 0 && sessionAssignment < firstAwait);
        Assert.True(profileInitialization >= 0 && profileInitialization < firstAwait);
        Assert.DoesNotContain("AuthSession.IsLoggedInAsync()", home);
        Assert.DoesNotContain("AuthSession.GetEmailAsync()", home);
    }

    [Fact]
    public void HomePage_WiresTheReactLoginProfileSessionAndVisibleRegions()
    {
        var root = FindWebRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Modules", "Home", "Home.razor"));
        var code = File.ReadAllText(Path.Combine(root, "Modules", "Home", "Home.razor.cs"));

        Assert.Contains("<ThemeToggle", markup);
        Assert.Contains("Your cross-platform playtime, perfectly organized.", markup);
        Assert.Contains("Everything in one place", markup);
        Assert.Contains("<StatsRecapSection", markup);
        Assert.Contains("Top 5 games from recap", markup);
        Assert.Contains("<AppFooter", markup);
        Assert.Contains("SendAsync<LoginResponse>(\"/login\"", code);
        Assert.Contains("authenticated: false", code);
        Assert.Contains("StoreLoginAsync", code);
        Assert.Contains("NavigateTo(\"/dashboard\", forceLoad: true)", code);
        Assert.Contains("SendAsync<MeResponse>(\"/me\"", code);
        Assert.Contains("StoreProfileAsync", code);
        Assert.Contains("LogoutAsync", code);
    }

    private static string FindWebRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Web");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate src/Web from the test output directory.");
    }
}
