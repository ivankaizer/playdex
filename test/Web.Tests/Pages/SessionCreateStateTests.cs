using Web.Infrastructure.Models;
using Web.Modules.Sessions;
using Xunit;

namespace Web.Tests.Pages;

public sealed class SessionCreateStateTests
{
    private static readonly ManualMappingSearchResult SavedGame = new(
        "local", "3H4h3", null, "Hades II", null, null,
        "https://images.test/hades.jpg");

    private static readonly ManualMappingSearchResult IgdbGame = new(
        "igdb", null, "114283", "Hades II", "PC", 6,
        "https://images.test/hades.jpg");

    [Fact]
    public void LiveSavedGameBuildsStartRequestWithSelectedPlatform()
    {
        var state = OpenState(SessionCreateMode.Live);
        state.SelectGame(SavedGame);
        state.SetPlatform("steam");

        Assert.True(state.TryBuildSubmission(out var submission));

        Assert.Equal("/manual/sessions/start", submission!.Path);
        Assert.Equal(HttpMethod.Post, submission.Method);
        var body = Assert.IsType<ManualSessionStartRequest>(submission.Body);
        Assert.Equal("3H4h3", body.GameId);
        Assert.Equal("steam", body.PlatformKey);
        Assert.Null(body.IgdbId);
    }

    [Fact]
    public void LiveIgdbGameCarriesIdentityMetadataForCreation()
    {
        var state = OpenState(SessionCreateMode.Live);
        state.SelectGame(IgdbGame);
        state.SetPlatform("pc");

        Assert.True(state.TryBuildSubmission(out var submission));

        var body = Assert.IsType<ManualSessionStartRequest>(submission!.Body);
        Assert.Null(body.GameId);
        Assert.Equal("114283", body.IgdbId);
        Assert.Equal("Hades II", body.Title);
        Assert.Equal("https://images.test/hades.jpg", body.CapsuleUrl);
        Assert.Equal("pc", body.PlatformKey);
    }

    [Fact]
    public void HistoricalEntryBuildsDateAndWholeMinuteDuration()
    {
        var state = OpenState(SessionCreateMode.Historical);
        state.SelectGame(SavedGame);
        state.SetPlatform("ps5");
        state.SetDate("2026-09-07");
        state.SetHours("1");
        state.SetMinutes("45");

        Assert.True(state.TryBuildSubmission(out var submission));

        Assert.Equal("/manual/entries", submission!.Path);
        var body = Assert.IsType<ManualEntryCreateRequest>(submission.Body);
        Assert.Equal("2026-09-07", body.Date);
        Assert.Equal(105, body.DurationMinutes);
        Assert.Equal("3H4h3", body.GameId);
        Assert.Equal("ps5", body.PlatformKey);
    }

    [Fact]
    public void SubmissionRequiresGamePlatformAndPositiveHistoricalDuration()
    {
        var state = OpenState(SessionCreateMode.Live);

        Assert.False(state.TryBuildSubmission(out _));
        Assert.Equal("Select a game.", state.Error);

        state.SelectGame(SavedGame);
        Assert.False(state.TryBuildSubmission(out _));
        Assert.Equal("Select a platform.", state.Error);

        state.SetPlatform("pc");
        state.SetMode(SessionCreateMode.Historical);
        state.SetHours("0");
        state.SetMinutes("0");
        Assert.False(state.TryBuildSubmission(out _));
        Assert.Equal("Duration must be at least 1 minute.", state.Error);
    }

    [Fact]
    public void SearchUsesManualCatalogAndChangingQueryClearsTheSelection()
    {
        var state = OpenState(SessionCreateMode.Live);
        state.SetQuery("Hades & friends");

        Assert.True(state.TryBeginSearch(out var path));
        Assert.Equal("/manual/search?query=Hades%20%26%20friends", path);

        state.CompleteSearch(new ManualMappingSearchResponse(
            "Hades & friends", true, [SavedGame, IgdbGame]));
        state.SelectGame(SavedGame);
        state.SetQuery("Celeste");

        Assert.Null(state.SelectedGame);
        Assert.Empty(state.Results);
    }

    [Fact]
    public void PlatformCatalogAndActiveManualSessionHaveDedicatedRequests()
    {
        var state = new SessionsState(DateTimeOffset.UtcNow);

        state.CompletePlatforms(new ManualPlatformsResponse(
            [new("pc", "PC"), new("ps5", "PS5")]));
        var active = new ManualActiveSession(
            9, "3H4h3", "Hades II", "pc", "PC", DateTimeOffset.UtcNow, null);
        state.CompleteActiveManualSession(new ManualActiveSessionResponse(active));

        Assert.Equal("/manual/platforms", SessionsState.PlatformsRequest);
        Assert.Equal("/manual/sessions/current", SessionsState.ActiveManualSessionRequest);
        Assert.Equal(["pc", "ps5"], state.Platforms.Select(item => item.Key));
        Assert.Same(active, state.ActiveManualSession);
    }

    [Fact]
    public void SaveCannotBeginTwiceWhileTheFirstRequestIsPending()
    {
        var state = OpenState(SessionCreateMode.Live);

        Assert.True(state.TryBeginSave());
        Assert.False(state.TryBeginSave());

        state.FailSave("Failed.");
        Assert.True(state.TryBeginSave());
    }

    [Fact]
    public void DelayedActiveSessionResponseCannotOverwriteANewerRefresh()
    {
        var versions = new SessionsRequestVersions();
        var stale = versions.BeginActiveManualSessionRequest();
        var current = versions.BeginActiveManualSessionRequest();

        Assert.False(versions.IsCurrentActiveManualSessionRequest(stale));
        Assert.True(versions.IsCurrentActiveManualSessionRequest(current));
    }

    private static SessionCreateState OpenState(SessionCreateMode mode)
    {
        var state = new SessionCreateState();
        state.Open(mode, new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
        return state;
    }
}
