using Bunit;
using Web.Infrastructure.Components;
using Xunit;

namespace Web.Tests.Components;

public sealed class BreadcrumbsTests : BunitContext
{
    [Fact]
    public void RendersAnAccessibleClickableParentAndCurrentLocation()
    {
        var parentClicked = false;
        var cut = Render<Breadcrumbs>(parameters => parameters
            .Add(component => component.ParentLabel, "Saved batches")
            .Add(component => component.CurrentLabel, "Review batch")
            .Add(component => component.OnParent, () => parentClicked = true));

        Assert.NotNull(cut.Find("nav[aria-label='Breadcrumb']"));
        Assert.Equal("page", cut.Find("[aria-current='page']").GetAttribute("aria-current"));
        Assert.Equal("Review batch", cut.Find("[aria-current='page']").TextContent.Trim());

        cut.Find("button").Click();

        Assert.True(parentClicked);
    }
}
