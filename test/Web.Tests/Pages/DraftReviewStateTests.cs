using Web.Infrastructure.Models;
using Web.Modules.Sessions;
using Xunit;
namespace Web.Tests.Pages;
public class DraftReviewStateTests
{
    private static DraftRowDto Row() => new(Guid.NewGuid(), 1, "pending", "Hades", "2026-09-08", 60, "unknown", new("local", "game", null, "Hades", null, null, null), ["Confirm Unknown platform."], true, null);
    [Fact] public void MissingFieldsPreventAcceptanceAndUnknownIsAllowed()
    {
        var row = new DraftRowEdit(Row());
        Assert.True(row.CanAccept);
        row.Date = "";
        Assert.False(row.CanAccept);
        Assert.Contains("date", row.Corrections, StringComparison.OrdinalIgnoreCase);
        row.Date = "2026-09-08"; row.Minutes = "0"; row.Hours = "0";
        Assert.False(row.CanAccept);
    }
    [Fact] public void TitleChangeRequiresReselectionAndFailedSaveKeepsEdits()
    {
        var row = new DraftRowEdit(Row());
        row.SetTitle("Hades II"); row.Error = "Revision conflict. Reload to review server changes.";
        Assert.Null(row.Selection); Assert.False(row.CanAccept);
        Assert.Equal("Hades II", row.Title); Assert.Equal(1, row.Revision);
    }
    [Fact] public void SavedRevisionUsedForAcceptanceAndAcceptedRowsAreReadOnly()
    {
        var dto = Row(); var row = new DraftRowEdit(dto);
        row.Apply(dto with { Revision = 2 });
        Assert.Equal(2, row.Acceptance.Revision);
        row.MarkAccepted(42);
        Assert.True(row.IsAccepted); Assert.False(row.CanAccept); Assert.Equal(42, row.AcceptedSessionId);
    }
    [Fact] public void InvalidFractionAndOverflowDurationsRemainUnready()
    {
        var row = new DraftRowEdit(Row()); row.Hours = "2147483647";
        Assert.False(row.CanAccept);
        row.Hours = "0"; row.Minutes = "1.5";
        Assert.False(row.CanAccept);
    }
}
