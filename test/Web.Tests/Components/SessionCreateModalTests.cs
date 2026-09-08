using Bunit;
using Web.Infrastructure.Components;
using Web.Infrastructure.Models;
using Web.Modules.Sessions;
using Xunit;

namespace Web.Tests.Components;

public sealed class SessionCreateModalTests : BunitContext
{
    [Fact]
    public void RendersLiveAndHistoricalModesWithSearchAndPlatformControls()
    {
        var state = new SessionCreateState();
        state.Open(SessionCreateMode.Live, DateTimeOffset.UtcNow);
        state.CompleteSearch(new ManualMappingSearchResponse(
            "Hades",
            true,
            [new("igdb", null, "114283", "Hades II", "PC", 6, null)]));

        var cut = Render<SessionCreateModal>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.Platforms,
                [new ManualPlatform("pc", "PC"), new ManualPlatform("ps5", "PS5")]));

        Assert.Equal("true", cut.Find("[role=dialog]").GetAttribute("aria-modal"));
        Assert.Equal(2, cut.FindAll("button[data-mode]").Count);
        Assert.NotNull(cut.Find("input[type=search]"));
        Assert.Equal(3, cut.FindAll("select[data-field=platform] option").Count);
        Assert.Contains("IGDB", cut.Markup);
        Assert.DoesNotContain("free-form", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
