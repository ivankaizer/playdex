using Bunit;
using Web.Infrastructure.Components;
using Web.Infrastructure.Models;
using Xunit;

namespace Web.Tests.Components;

public sealed class GameSearchBoxComponentTests : BunitContext
{
    [Fact]
    public void RendersAnAccessibleGlobalGameSearch()
    {
        var cut = Render<GameSearchBox>(parameters => parameters
            .Add(component => component.SearchAsync, (_, _) =>
                Task.FromResult(new GameSearchResponse("", []))));

        var input = cut.Find("input");
        Assert.Equal("Search games", input.GetAttribute("placeholder"));
        Assert.Equal("combobox", input.GetAttribute("role"));
        Assert.Equal("game-search-results", input.GetAttribute("aria-controls"));
        Assert.Equal("false", input.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void SharedHeaderRendersTheGameSearchControl()
    {
        var cut = Render<AppHeader>(parameters => parameters
            .Add(component => component.Title, "Dashboard")
            .Add(component => component.NavItems, [("Sessions", "/sessions")])
            .Add(component => component.Path, "/dashboard")
            .Add(component => component.Theme, "light")
            .Add(component => component.SearchAsync, (_, _) =>
                Task.FromResult(new GameSearchResponse("", []))));

        Assert.Equal("Search games", cut.Find("input").GetAttribute("placeholder"));
        var navigation = cut.Find("[data-header-nav]");
        Assert.Contains("lg:ml-auto", navigation.ClassList);
        Assert.NotNull(cut.Find("[data-header-search]"));
        var utilities = cut.Find("[data-header-utilities]");
        Assert.Contains("lg:ml-0", utilities.ClassList);
        Assert.True(
            cut.Markup.IndexOf("data-header-nav", StringComparison.Ordinal) <
            cut.Markup.IndexOf("data-header-search", StringComparison.Ordinal));

        var logout = cut.Find("button[aria-label='Log out']");
        Assert.Equal("Log out", logout.GetAttribute("title"));
        Assert.Equal(string.Empty, logout.TextContent.Trim());
        Assert.NotNull(logout.QuerySelector("svg"));
    }
}
