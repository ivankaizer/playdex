using Bunit;
using Web.Infrastructure.Components;
using Web.Infrastructure.Models;
using Web.Modules.Sessions;
using Xunit;

namespace Web.Tests.Components;

public sealed class SessionCreateModalTests : BunitContext
{
    [Fact]
    public void AiEntryRequiresHistoricalModeAndAvailableCapability()
    {
        var state = new SessionCreateState();
        state.Open(SessionCreateMode.Historical, DateTimeOffset.UtcNow);
        var clicked = false;
        var cut = Render<SessionCreateModal>(p => p.Add(c => c.State, state).Add(c => c.AiAvailable, true).Add(c => c.OnUseAi, () => clicked = true));
        cut.FindAll("button").Single(b => b.TextContent == "Use AI").Click();
        Assert.True(clicked);
        cut.Find("[data-mode=live]").Click();
        Assert.DoesNotContain("Use AI", cut.Markup);
        cut.Find("[data-mode=historical]").Click();
        cut.Render(p => p.Add(c => c.AiAvailable, false));
        Assert.DoesNotContain("Use AI", cut.Markup);
    }

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

    [Fact]
    public void EmbeddedHistoricalCreatorUsesPageSectionsWithoutModalControls()
    {
        var state = new SessionCreateState();
        state.Open(SessionCreateMode.Historical, DateTimeOffset.UtcNow);

        var cut = Render<SessionCreateModal>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.Embedded, true)
            .Add(component => component.AiAvailable, true)
            .Add(component => component.Platforms, [new ManualPlatform("pc", "PC")]));

        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Empty(cut.FindAll("button[data-mode]"));
        Assert.DoesNotContain("Close session creator", cut.Markup);
        Assert.DoesNotContain(">Cancel<", cut.Markup);
        Assert.Contains("Choose a game", cut.Markup);
        Assert.Contains("Session details", cut.Markup);
        Assert.NotNull(cut.Find("[data-layout=extended-manual]"));
    }
}
