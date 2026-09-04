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
            .Add(component => component.NavItems, [])
            .Add(component => component.Path, "/dashboard")
            .Add(component => component.Theme, "light")
            .Add(component => component.SearchAsync, (_, _) =>
                Task.FromResult(new GameSearchResponse("", []))));

        Assert.Equal("Search games", cut.Find("input").GetAttribute("placeholder"));
    }
}
