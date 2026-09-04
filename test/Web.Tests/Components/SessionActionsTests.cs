using Bunit;
using Web.Infrastructure.Components;
using Xunit;

namespace Web.Tests.Components;

public sealed class SessionActionsTests : BunitContext
{
    [Fact]
    public void FinishedSessionOffersEditAndRemoveActions()
    {
        var cut = Render<SessionActions>(parameters => parameters
            .Add(component => component.CanEdit, true));

        Assert.Equal("Edit", cut.Find("button[data-action=edit]").TextContent.Trim());
        Assert.Equal("Remove", cut.Find("button[data-action=remove]").TextContent.Trim());
    }

    [Fact]
    public void ActiveSessionDoesNotOfferEditAction()
    {
        var cut = Render<SessionActions>(parameters => parameters
            .Add(component => component.CanEdit, false));

        Assert.Empty(cut.FindAll("button[data-action=edit]"));
        Assert.Single(cut.FindAll("button[data-action=remove]"));
    }
}
