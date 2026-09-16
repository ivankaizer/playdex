using Web.Infrastructure.Components;
using Web.Infrastructure.Models;
using Xunit;

namespace Web.Tests.Components;

public sealed class GameSearchBoxStateTests
{
    [Fact]
    public void NonEmptyQueryBuildsFiveResultSearchRequest()
    {
        var state = new GameSearchBoxState();

        state.SetQuery("  Hades & friends  ");

        Assert.True(state.TryBeginSearch(out var path));
        Assert.Equal("/games/search?query=Hades%20%26%20friends&limit=5", path);
        Assert.True(state.IsLoading);
        Assert.True(state.IsOpen);
    }

    [Fact]
    public void CompletedSearchShowsAtMostFiveChoicesWithoutSelectingOne()
    {
        var state = new GameSearchBoxState();
        state.SetQuery("game");
        state.TryBeginSearch(out _);
        var results = Enumerable.Range(1, 7)
            .Select(index => new GameSearchResult($"hash-{index}", $"Game {index}", "PC", null))
            .ToArray();

        state.CompleteSearch(new GameSearchResponse("game", results));

        Assert.Equal(5, state.Results.Count);
        Assert.Equal(-1, state.ActiveIndex);
        Assert.True(state.IsOpen);
        Assert.False(state.IsLoading);
    }

    [Fact]
    public void KeyboardMovementStaysWithinAvailableChoices()
    {
        var state = StateWithResults("Alpha", "Beta");

        state.MoveActive(1);
        Assert.Equal(0, state.ActiveIndex);
        state.MoveActive(1);
        state.MoveActive(1);
        Assert.Equal(1, state.ActiveIndex);
        state.MoveActive(-1);
        state.MoveActive(-1);
        Assert.Equal(0, state.ActiveIndex);
    }

    [Fact]
    public void TakingActiveChoiceReturnsItAndResetsSearch()
    {
        var state = StateWithResults("Alpha", "Beta");
        state.MoveActive(1);
        state.MoveActive(1);

        var selected = state.TakeActiveChoice();

        Assert.Equal("Beta", selected?.Title);
        Assert.Empty(state.Query);
        Assert.Empty(state.Results);
        Assert.False(state.IsOpen);
        Assert.Equal(-1, state.ActiveIndex);
    }

    [Fact]
    public void ClearingQueryClosesExistingResultsAndPreventsARequest()
    {
        var state = StateWithResults("Alpha");

        state.SetQuery("   ");

        Assert.False(state.TryBeginSearch(out var path));
        Assert.Null(path);
        Assert.Empty(state.Results);
        Assert.False(state.IsOpen);
        Assert.False(state.IsLoading);
    }

    [Fact]
    public void FailedSearchStopsLoadingAndExposesTheError()
    {
        var state = new GameSearchBoxState();
        state.SetQuery("Hades");
        state.TryBeginSearch(out _);

        state.FailSearch("Search unavailable.");

        Assert.Equal("Search unavailable.", state.Error);
        Assert.True(state.IsOpen);
        Assert.False(state.IsLoading);
        Assert.Empty(state.Results);
    }

    [Fact]
    public void DismissClosesChoicesWhileKeepingTheTypedQuery()
    {
        var state = StateWithResults("Alpha");

        state.Dismiss();

        Assert.Equal("game", state.Query);
        Assert.False(state.IsOpen);
        Assert.Equal(-1, state.ActiveIndex);
    }

    [Fact]
    public void TakingClickedChoiceResetsSearchAndReturnsItsGamePath()
    {
        var state = StateWithResults("Alpha");

        var path = state.TakeChoice(state.Results[0]);

        Assert.Equal("/games/hash-0", path);
        Assert.Empty(state.Query);
        Assert.False(state.IsOpen);
    }

    private static GameSearchBoxState StateWithResults(params string[] titles)
    {
        var state = new GameSearchBoxState();
        state.SetQuery("game");
        state.TryBeginSearch(out _);
        state.CompleteSearch(new GameSearchResponse(
            "game",
            titles.Select((title, index) =>
                new GameSearchResult($"hash-{index}", title, "PC", null)).ToArray()));
        return state;
    }
}
