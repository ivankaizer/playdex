using Web.Shared.Models;
using Web.Modules.Games;
using Web.Shared.Common;
using Xunit;

namespace Web.Tests.Pages;

public sealed class GameStateTests
{
    [Fact]
    public void RequestsMatchReactDetailAndPagedSessionContract()
    {
        var requests = GameRequests.For("3H4h3", new DateTimeOffset(2026, 7, 18, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal("42", requests.GameId);
        Assert.Equal("/games/3H4h3", requests.Details);
        Assert.Equal(
            "/sessions?gameId=3H4h3&from=2025-01-01&to=2026-07-18&sort=date&direction=desc&limit=5&offset=0",
            requests.FirstSessions);
        Assert.Equal(
            "/sessions?gameId=3H4h3&from=2025-01-01&to=2026-07-18&sort=date&direction=desc&limit=100&offset=5",
            requests.SessionsPage(5));
        Assert.Equal("/games/ignored?gameId=3H4h3", requests.IgnoreStatus);
        Assert.Equal("/games/rating?gameId=3H4h3", requests.Rating);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    public void InvalidHashUsesReactErrors(string? hash)
    {
        var state = new GameState();

        Assert.False(state.TryBegin(hash, DateTimeOffset.UtcNow, out _));
        Assert.Equal("Invalid game link.", state.GameDetailsError);
        Assert.Equal("Invalid game link.", state.GameError);
        Assert.Equal("Invalid game link.", state.IgnoreError);
        Assert.Equal("Invalid game link.", state.RatingError);
        Assert.True(state.SessionsLoaded);
    }

    [Fact]
    public void IgnoredSessionsStayRecentButAreExcludedFromMetricsAndCharts()
    {
        var state = new GameState();
        Assert.True(state.TryBegin("3H4h3", new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero), out _));
        state.CompleteSessions(new AccountSessionsResponse(null,
        [
            Session(1, "2026-07-18T08:00:00Z", "2026-07-18T10:00:00Z", ignored: true, capsule: "ignored.jpg"),
            Session(2, "2026-06-10T08:00:00Z", "2026-06-10T09:30:00Z", ignored: false),
            Session(3, "2025-01-01T08:00:00Z", "2025-01-01T08:30:00Z", ignored: false),
        ], 3, 5, 0));

        Assert.Equal(3, state.RecentSessions.Count);
        Assert.Equal("ignored.jpg", state.GameImage);
        Assert.Equal(2, state.StatsSessions.Count);
        Assert.Equal(7200, state.TotalSeconds);
        Assert.Equal("Jun 10, 2026", state.LastPlayedLabel);
        Assert.Equal(0.5, state.YearlyStats.Single(item => item.Year == 2025).Hours);
        Assert.Equal(1.5, state.YearlyStats.Single(item => item.Year == 2026).Hours);
        Assert.Equal(1.5, state.MonthlyStats.Single(item => item.Month == 5).Hours);
    }

    [Fact]
    public void RecentCardsUseFiveItemLocalPagination()
    {
        var state = new GameState();
        Assert.True(state.TryBegin("3H4h3", new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero), out _));
        state.CompleteSessions(new AccountSessionsResponse(null,
            Enumerable.Range(1, 11)
                .Select(index => Session(index, $"2026-07-{index:00}T08:00:00Z", $"2026-07-{index:00}T09:00:00Z"))
                .ToArray(), 11, 5, 0));

        Assert.Equal(3, state.RecentTotalPages);
        Assert.Equal([1L, 2, 3, 4, 5], state.RecentPageItems.Select(item => item.SessionId));
        state.SetRecentPage(3);
        Assert.Equal([11L], state.RecentPageItems.Select(item => item.SessionId));
        state.SetRecentPage(99);
        Assert.Equal(3, state.RecentPage);
    }

    [Fact]
    public void TrackingAndRatingSubmissionsMatchReactBodies()
    {
        var state = new GameState();
        Assert.True(state.TryBegin("3H4h3", DateTimeOffset.UtcNow, out _));

        var ignore = state.BuildIgnoreSubmission(isIgnored: false);
        Assert.Equal("/games/ignored", ignore.Path);
        Assert.Equal(HttpMethod.Post, ignore.Method);
        Assert.Equal("3H4h3", ignore.Body["gameId"]);

        var resume = state.BuildIgnoreSubmission(isIgnored: true);
        Assert.Equal(HttpMethod.Delete, resume.Method);

        var rating = state.BuildRatingSubmission(5);
        Assert.Equal(HttpMethod.Post, rating.Method);
        Assert.Equal(5, rating.Body["rating"]);

        var clear = state.BuildRatingSubmission(null);
        Assert.Equal(HttpMethod.Delete, clear.Method);
        Assert.False(clear.Body.ContainsKey("rating"));
    }

    [Fact]
    public void GamePageWiresCatchAllRouteReactMarkupAndAuthenticatedRequests()
    {
        var root = FindWebRoot();
        var markupPath = Path.Combine(root, "Modules", "Games", "Game.razor");
        var codePath = Path.Combine(root, "Modules", "Games", "Game.razor.cs");

        Assert.True(File.Exists(markupPath), "Missing Game Razor page.");
        var markup = File.ReadAllText(markupPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains("@page \"/games/{*Hash}\"", markup);
        Assert.Contains("<AppLayout Title=\"Game\">", markup);
        Assert.Contains("lg:grid-cols-[1fr_2fr]", markup);
        Assert.Contains("Latest play snapshots", markup);
        Assert.Contains("Your score", markup);
        Assert.Contains("Scheduler will skip new sessions for this game.", markup);
        Assert.Contains("grid grid-cols-1 gap-3 sm:grid-cols-3 sm:gap-6", markup);
        Assert.Contains("Yearly play time", markup);
        Assert.Contains("Monthly play time", markup);
        Assert.DoesNotContain("<table", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hero", markup, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("[CascadingParameter(Name = \"NormalizedGameHash\")]", code);
        Assert.Contains("GameInitialLoadFlow.RunAsync", code);
        Assert.Contains("SendAsync<GameResponse>(requests.Details", code);
        Assert.Contains("SendAsync<AccountSessionsResponse>(path", code);
        Assert.Contains("SendAsync<GameIgnoreResponse>(requests.IgnoreStatus", code);
        Assert.Contains("SendAsync<GameRatingResponse>(requests.Rating", code);
        Assert.Contains("onAuthFailure: LogoutAsync", code);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource", code);
        Assert.Contains("_lifetime.Cancel()", code);
    }

    private static AccountSession Session(
        long id,
        string started,
        string ended,
        bool ignored = false,
        string? capsule = null) =>
        new(
            id,
            DateTimeOffset.Parse(started),
            DateTimeOffset.Parse(ended),
            "42",
            "steam",
            "Game",
            capsule,
            ignored,
            null,
            null);

    private static string FindWebRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Web");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not find src/Web.");
    }
}
