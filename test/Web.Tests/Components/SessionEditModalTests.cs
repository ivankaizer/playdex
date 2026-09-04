using Bunit;
using Web.Infrastructure.Components;
using Xunit;

namespace Web.Tests.Components;

public sealed class SessionEditModalTests : BunitContext
{
    [Fact]
    public void RendersAccessibleSecondPrecisionDateTimeEditor()
    {
        var cut = Render<SessionEditModal>(parameters => parameters
            .Add(component => component.GameTitle, "Hades II")
            .Add(component => component.StartedAt, "2026-08-04T18:30:15")
            .Add(component => component.EndedAt, "2026-08-04T20:45:30"));

        var dialog = cut.Find("[role=dialog]");
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Contains("Hades II", dialog.TextContent);

        var inputs = cut.FindAll("input[type=datetime-local]");
        Assert.Equal(2, inputs.Count);
        Assert.All(inputs, input => Assert.Equal("1", input.GetAttribute("step")));
        Assert.Equal("2026-08-04T18:30:15", inputs[0].GetAttribute("value"));
        Assert.Equal("2026-08-04T20:45:30", inputs[1].GetAttribute("value"));
        Assert.Equal("Cancel", cut.Find("button[data-action=cancel]").TextContent.Trim());
        Assert.Equal("Save changes", cut.Find("button[data-action=save]").TextContent.Trim());
    }
}
