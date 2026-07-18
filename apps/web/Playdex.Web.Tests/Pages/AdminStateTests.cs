using Playdex.Web.Models;
using Playdex.Web.Pages;
using Xunit;

namespace Playdex.Web.Tests.Pages;

public sealed class AdminStateTests
{
    [Fact]
    public void InitialRequestsMatchReactOrderAndLimits()
    {
        Assert.Equal(
        [
            "/admin/sessions/recent",
            "/admin/providers/platforms",
            "/admin/credentials",
            "/admin/sessions?limit=25&offset=0",
        ], AdminRequests.Initial);
    }

    [Fact]
    public void InitialSnapshotAppliesAtomicallyAndBuildsCredentialDrafts()
    {
        var state = new AdminState();
        state.BeginInitialLoad();
        var snapshot = new AdminSnapshot(
            new AdminRecentSessionsResponse([new AdminUserRecentSessions(1, "a@example.com", [])]),
            new AdminPlatformStatusesResponse([new AdminPlatformStatus("steam", 1, 1, true)]),
            new AdminCredentialsResponse(
            [
                Credential(4, "steam", " key ", "player"),
                Credential(5, "psn", null, null),
            ]),
            new AdminSessionsListResponse("", [], 8, 25, 0));

        state.CompleteInitialLoad(snapshot);

        Assert.Single(state.RecentUsers);
        Assert.Single(state.Platforms);
        Assert.Equal(2, state.Credentials.Count);
        Assert.Equal("steam", state.SelectedCredentialPlatform);
        Assert.Equal(" key ", state.CredentialDrafts[4].ApiKey);
        Assert.Equal("", state.CredentialDrafts[5].AccountUser);
        Assert.Equal(8, state.SessionTotal);
        Assert.False(state.IsAnyInitialLoading);
    }

    [Fact]
    public void InitialFailureUsesOneMessageForAllFourPanels()
    {
        var state = new AdminState();
        state.BeginInitialLoad();

        state.FailInitialLoad("boom");

        Assert.Equal("boom", state.SessionsError);
        Assert.Equal("boom", state.PlatformsError);
        Assert.Equal("boom", state.CredentialsError);
        Assert.Equal("boom", state.MoveSessionsError);
        Assert.False(state.IsAnyInitialLoading);
    }

    [Fact]
    public void PlatformAndCredentialSubmissionsMatchReactBodies()
    {
        var state = LoadedState();
        var platform = state.BeginPlatformUpdate(new AdminPlatformStatus("steam", 3, 2, true));
        Assert.Equal("/admin/providers/platforms/status", platform.Path);
        Assert.Equal(HttpMethod.Put, platform.Method);
        Assert.Equal("steam", platform.Body["platform"]);
        Assert.Equal(false, platform.Body["enabled"]);

        state.SetCredentialDraft(4, CredentialDraftField.ApiKey, "  secret  ");
        state.SetCredentialDraft(4, CredentialDraftField.AccountUser, "   ");
        var credential = state.BeginCredentialSave(4);
        Assert.NotNull(credential);
        Assert.Equal("/admin/credentials/4", credential!.Path);
        Assert.Equal("secret", credential.Body["apiKey"]);
        Assert.Null(credential.Body["user"]);
    }

    [Fact]
    public void SessionSearchSelectionMoveAndPaginationMatchReact()
    {
        var state = LoadedState();
        state.SetSessionQuery(" player & game ");
        Assert.Equal(
            "/admin/sessions?query=player%20%26%20game&limit=25&offset=25",
            state.BeginMovableSessionsLoad("player & game", 2));
        state.CompleteMovableSessionsLoad(new AdminSessionsListResponse("player", [Movable(10), Movable(11)], 60, 25, 25), 2);
        Assert.Equal(3, state.SessionPageCount);
        Assert.Equal(26, state.SessionRangeStart);
        Assert.Equal(27, state.SessionRangeEnd);

        state.ToggleSessionSelection(10);
        Assert.False(state.TryBuildMoveSubmission(out _));
        Assert.Equal("Select a target account.", state.MoveSessionsError);
        state.SetTargetCredentialId("4");
        Assert.True(state.TryBuildMoveSubmission(out var move));
        Assert.Equal([10L], Assert.IsType<long[]>(move!.Body["sessionIds"]));
        Assert.Equal(4L, move.Body["targetCredentialId"]);
    }

    [Fact]
    public void GameSearchAndSaveMatchReactValidationPathAndBody()
    {
        var state = LoadedState();
        state.SetGameQuery("   ");
        Assert.False(state.TryBeginGameSearch(out _));
        Assert.Equal("Enter a game title to search.", state.GamesError);

        state.SetGameQuery(" Halo / 2 ");
        Assert.True(state.TryBeginGameSearch(out var searchPath));
        Assert.Equal("/admin/games/search?query=Halo%20%2F%202&limit=20", searchPath);
        state.CompleteGameSearch(new AdminGameSearchResponse("Halo", [Game()]));
        state.SetGameDraft("42", GameDraftField.Title, " Halo Infinite ");
        state.SetGameDraft("42", GameDraftField.ImageUrl, "  ");
        var save = state.BeginGameSave("42");
        Assert.NotNull(save);
        Assert.Equal("/admin/games?gameId=42", save!.Path);
        Assert.Equal("42", save.Body["gameId"]);
        Assert.Equal("42", save.Body["id"]);
        Assert.Equal("Halo Infinite", save.Body["title"]);
        Assert.Null(save.Body["imageUrl"]);
        Assert.Equal(true, save.Body["lockTitle"]);
        Assert.Equal(true, save.Body["lockImage"]);
    }

    [Fact]
    public void AdminPageUsesCentralGuardAndLiteralSingleDomMarkup()
    {
        var root = FindWebRoot();
        var markupPath = Path.Combine(root, "Pages", "Admin.razor");
        var codePath = Path.Combine(root, "Pages", "Admin.razor.cs");
        Assert.True(File.Exists(markupPath), "Missing Admin Razor page.");
        var markup = File.ReadAllText(markupPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains("@page \"/admin\"", markup);
        Assert.Contains("<AppLayout Title=\"Admin\">", markup);
        Assert.Contains("mt-10 grid gap-6 lg:grid-cols-2", markup);
        Assert.Contains("Last 3 sessions per user", markup);
        Assert.Contains("Platform-level enable toggle", markup);
        Assert.Contains("Update platform credential records", markup);
        Assert.Contains("Move game sessions between accounts", markup);
        Assert.Contains("Update title and image without next poll override", markup);
        Assert.DoesNotContain("<table", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fixed inset-0", markup);

        Assert.Contains("AdminInitialLoadFlow.RunAsync", code);
        Assert.Contains("SendAsync<AdminRecentSessionsResponse>(AdminRequests.Initial[0]", code);
        Assert.Contains("SendAsync<AdminPlatformStatusesResponse>(AdminRequests.Initial[1]", code);
        Assert.Contains("SendAsync<AdminCredentialsResponse>(AdminRequests.Initial[2]", code);
        Assert.Contains("SendAsync<AdminSessionsListResponse>(AdminRequests.Initial[3]", code);
        Assert.Contains("SendAsync<AdminSessionMoveResponse>", code);
        Assert.Contains("SendAsync<AdminGameUpdateResponse>", code);
        Assert.DoesNotContain("onAuthFailure", code);
        Assert.DoesNotContain("IsAdminAsync", code);
        Assert.Contains("_lifetime.Cancel()", code);
    }

    private static AdminState LoadedState()
    {
        var state = new AdminState();
        state.BeginInitialLoad();
        state.CompleteInitialLoad(new AdminSnapshot(
            new AdminRecentSessionsResponse([]),
            new AdminPlatformStatusesResponse([]),
            new AdminCredentialsResponse([Credential(4, "steam", null, null)]),
            new AdminSessionsListResponse("", [], 0, 25, 0)));
        return state;
    }

    private static AdminCredential Credential(long id, string platform, string? apiKey, string? user) =>
        new(id, "owner@example.com", platform, apiKey, user, true);

    private static AdminMovableSession Movable(long id) =>
        new(id, 4, "owner@example.com", "steam", "player", "Game", DateTimeOffset.UtcNow, null, 60, true);

    private static AdminGame Game() => new("42", "Halo", true, "steam", "cover.jpg", true);

    private static string FindWebRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "apps", "web");
            if (File.Exists(Path.Combine(candidate, "Playdex.Web.csproj"))) return candidate;
        }

        throw new DirectoryNotFoundException("Could not find apps/web.");
    }
}
