using Bunit;
using Web.Infrastructure.Components;
using Web.Infrastructure.Models;
using Xunit;

namespace Web.Tests.Components;

public sealed class LiveSessionControlTests : BunitContext
{
    [Fact]
    public void SearchShowsPosterAndStartRequiresSelectionAndPlatform()
    {
        ManualSessionStartRequest? started = null;
        var result = new ManualMappingSearchResult("igdb", null, "114283", "Hades", "PC", 6, "https://covers.test/hades.png");
        var cut = Render<LiveSessionControl>(parameters => parameters
            .Add(component => component.Platforms, [new ManualPlatform("pc", "PC")])
            .Add(component => component.SearchGamesAsync, (_, _) => Task.FromResult(new ManualMappingSearchResponse("Hades", true, [result])))
            .Add(component => component.OnStart, request => started = request));

        var platform = cut.Find("[data-live-platform]");
        Assert.Contains("sm:w-36", platform.ClassList);
        Assert.DoesNotContain("min-w-[10rem]", platform.ClassList);
        Assert.True(cut.FindAll("button").Single(button => button.TextContent.Contains("Start live")).HasAttribute("disabled"));
        cut.Find("input[type=search]").Input("Hades");
        cut.FindAll("button").Single(button => button.TextContent == "Search").Click();
        cut.WaitForAssertion(() => Assert.Contains("Hades", cut.Markup));
        Assert.Contains("hades.png", cut.Markup);

        cut.FindAll("button").Single(button => button.TextContent.Contains("Hades")).Click();
        cut.Find("select").Change("pc");
        var start = cut.FindAll("button").Single(button => button.TextContent.Contains("Start live"));
        Assert.False(start.HasAttribute("disabled"));
        start.Click();

        Assert.NotNull(started);
        Assert.Equal("114283", started!.IgdbId);
    }
}
