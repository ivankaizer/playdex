using Api.Infrastructure.Http;
using Api.Modules.Manual;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

public sealed class ManualServiceTests
{
    [Fact]
    public void ExistingGameSelectionUsesTheOwnedGameId()
    {
        var selection = ManualService.ValidateGameSelection(
            gameId: "3H4h3",
            igdbId: null);

        Assert.Equal(42, selection.GameId);
        Assert.Null(selection.IgdbId);
    }

    [Fact]
    public void IgdbSelectionBuildsAnIdentityBasedResolution()
    {
        var selection = ManualService.ValidateGameSelection(
            gameId: null,
            igdbId: "114283");

        Assert.Null(selection.GameId);
        Assert.Equal("114283", selection.IgdbId);
    }

    [Fact]
    public void TitleOnlySelectionIsRejectedToPreventDuplicateGames()
    {
        var exception = Assert.Throws<AppProblemException>(() =>
            ManualService.ValidateGameSelection(
                gameId: null,
                igdbId: null));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("Select a saved game or an IGDB result.", exception.Message);
    }

    [Fact]
    public void PlatformCatalogIsNamedOrderedAndExcludesClientPseudoPlatforms()
    {
        var platforms = ManualService.BuildPlatformCatalog(
        [
            new Platform { Key = "ps5", Name = "PS5" },
            new Platform { Key = "playnite", Name = "Playnite" },
            new Platform { Key = "pc", Name = "PC" },
            new Platform { Key = "unknown", Name = "Unknown" },
        ]);

        Assert.Equal(["pc", "ps5", "unknown"], platforms.Platforms.Select(item => item.Key));
        Assert.Equal(["PC", "PS5", "Unknown"], platforms.Platforms.Select(item => item.Name));
    }

    [Fact]
    public void ActiveSessionRowIsMappedToThePublicManualShape()
    {
        var startedAt = new DateTimeOffset(2026, 9, 8, 10, 30, 0, TimeSpan.Zero);

        var response = ManualService.ToActiveSessionResponse(new ManualActiveSessionRow(
            9, 42, "Hades II", "pc", "PC", startedAt, "https://images.test/hades.jpg"));

        Assert.NotNull(response.Session);
        Assert.Equal(9, response.Session.SessionId);
        Assert.Equal("3H4h3", response.Session.GameId);
        Assert.Equal("Hades II", response.Session.Title);
        Assert.Equal("pc", response.Session.PlatformKey);
        Assert.Equal(startedAt, response.Session.StartedAt);
    }

    [Fact]
    public void MissingSavedGameProducesAControlledValidationError()
    {
        var exception = Assert.Throws<AppProblemException>(() =>
            ManualService.RequireExistingGame(null));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("Game not found for the specified gameId.", exception.Message);
    }

    [Fact]
    public async Task IgdbSearchFailureBecomesUnavailableInsteadOfHidingLocalResults()
    {
        var result = await ManualService.SearchIgdbSafely(
            () => Task.FromException<List<Api.Modules.Polling.IgdbSearchResult>>(
                new HttpRequestException("IGDB unavailable.")),
            CancellationToken.None);

        Assert.False(result.Available);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task IgdbSearchCancellationStillPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ManualService.SearchIgdbSafely(
                () => Task.FromCanceled<List<Api.Modules.Polling.IgdbSearchResult>>(
                    cancellation.Token),
                cancellation.Token));
    }

    [Fact]
    public async Task IgdbHttpTimeoutBecomesUnavailableWhileCallerRemainsActive()
    {
        var result = await ManualService.SearchIgdbSafely(
            () => Task.FromException<List<Api.Modules.Polling.IgdbSearchResult>>(
                new TaskCanceledException("HTTP timeout.")),
            CancellationToken.None);

        Assert.False(result.Available);
        Assert.Empty(result.Results);
    }
}
