using Web.Shared.Models;
using Web.Modules.Mapping;
using Xunit;

namespace Web.Tests.Pages;

public sealed class MappingStateTests
{
    [Fact]
    public void GameSearches_AreIndependentAndSelectingDoesNotBindLaterQueryEdits()
    {
        var state = new MappingState();
        var primary = Game("42", "Hades II");
        var secondary = Game("99", "Celeste");
        state.CompletePrimarySearch(new GameSearchResponse("old", [primary]));
        state.CompleteSecondarySearch(new GameSearchResponse("old", [secondary]));

        state.SetPrimaryGameQuery(" A! (Test)*~' ");
        Assert.True(state.TryBeginPrimarySearch(out var primaryPath));
        Assert.Equal("/games/search?query=A!%20(Test)*~'", primaryPath);
        Assert.True(state.IsPrimaryGameLoading);
        Assert.Single(state.PrimaryGameResults);
        Assert.False(state.IsSecondaryGameLoading);
        Assert.Single(state.SecondaryGameResults);

        state.FailPrimarySearch("Primary failed.");
        Assert.Empty(state.PrimaryGameResults);
        Assert.Equal("Primary failed.", state.PrimaryGameError);
        Assert.Single(state.SecondaryGameResults);

        state.SelectSecondaryGame(secondary);
        Assert.Same(secondary, state.SelectedSecondaryGame);
        Assert.Equal("Celeste", state.SecondaryGameQuery);
        Assert.Empty(state.SecondaryGameResults);
        Assert.Empty(state.SecondaryGameError);

        state.SetSecondaryGameQuery("edited title");
        Assert.Same(secondary, state.SelectedSecondaryGame);
        Assert.Equal("edited title", state.SecondaryGameQuery);
    }

    [Fact]
    public void ConnectionValidationBodiesSuccessAndClearMatchReact()
    {
        var state = new MappingState();

        Assert.False(state.TryBuildConnectionSubmission(out _));
        Assert.Equal("Select two games to connect.", state.ConnectionSaveError);

        var primary = Game("42", "Hades II");
        state.SelectPrimaryGame(primary);
        state.SelectSecondaryGame(primary);
        Assert.False(state.TryBuildConnectionSubmission(out _));
        Assert.Equal("Pick two different games to connect.", state.ConnectionSaveError);

        state.SelectSecondaryGame(Game("99", "Celeste"));
        Assert.True(state.TryBuildConnectionSubmission(out var connect));
        Assert.NotNull(connect);
        Assert.Equal("/games/connections", connect.Path);
        Assert.Equal(HttpMethod.Post, connect.Method);
        Assert.Equal(["gameId", "connectedGameId"], connect.Body.Keys);
        Assert.Equal("3H4h3", connect.Body["gameId"]);
        Assert.Equal("8HWvi", connect.Body["connectedGameId"]);

        var disconnect = MappingState.BuildDisconnectSubmission("42", "99");
        Assert.Equal(HttpMethod.Delete, disconnect.Method);
        Assert.Equal(connect.Body.Keys, disconnect.Body.Keys);
        Assert.Equal(connect.Body["gameId"], disconnect.Body["gameId"]);
        Assert.Equal(connect.Body["connectedGameId"], disconnect.Body["connectedGameId"]);

        state.SetPrimaryGameQuery("primary draft");
        state.SetSecondaryGameQuery("secondary draft");
        state.FailPrimarySearch("primary error");
        state.FailSecondarySearch("secondary error");
        state.BeginConnectionSave();
        Assert.True(state.IsConnectionSaving);
        Assert.Empty(state.ConnectionSaveError);

        state.CompleteConnect();
        Assert.True(state.IsConnectionSaving);
        state.EndConnectionSave();
        Assert.False(state.IsConnectionSaving);
        Assert.Null(state.SelectedPrimaryGame);
        Assert.Null(state.SelectedSecondaryGame);
        Assert.Empty(state.PrimaryGameQuery);
        Assert.Empty(state.SecondaryGameQuery);
        Assert.Empty(state.PrimaryGameResults);
        Assert.Empty(state.SecondaryGameResults);
        Assert.Equal("primary error", state.PrimaryGameError);
        Assert.Equal("secondary error", state.SecondaryGameError);

        state.SelectPrimaryGame(primary);
        state.SelectSecondaryGame(Game("99", "Celeste"));
        state.FailPrimarySearch("again");
        state.FailSecondarySearch("again");
        state.FailConnectionSave("save error");
        state.ClearConnectionDraft();

        Assert.Null(state.SelectedPrimaryGame);
        Assert.Null(state.SelectedSecondaryGame);
        Assert.Empty(state.PrimaryGameError);
        Assert.Empty(state.SecondaryGameError);
        Assert.Empty(state.ConnectionSaveError);
    }

    [Fact]
    public void MappingPage_WiresLiteralResponsiveEditorsRequestsAndCancellation()
    {
        var root = FindWebRoot();
        var markupPath = Path.Combine(root, "Modules", "Mapping", "Mapping.razor");
        var codePath = Path.Combine(root, "Modules", "Mapping", "Mapping.razor.cs");
        Assert.True(File.Exists(markupPath), "Missing Mapping Razor page.");

        var markup = File.ReadAllText(markupPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains("@page \"/mapping\"", markup);
        Assert.Contains("<AppLayout Title=\"Mapping\"", markup);
        Assert.Contains("Game connections", markup);
        Assert.Contains("lg:grid-cols-[1.1fr_0.9fr]", markup);
        Assert.Contains("No game connections yet.", markup);
        Assert.Contains("+ +", markup);
        Assert.DoesNotContain("EditForm", markup);
        Assert.DoesNotContain("fixed inset-0", markup);
        Assert.DoesNotContain("manual", markup, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("MappingInitialLoadFlow.RunAsync", code);
        Assert.Contains("SendAsync<GameConnectionsResponse>(path", code);
        Assert.Contains("SendAsync<GameSearchResponse>(path", code);
        Assert.Contains("SendAsync<GameConnectionChangeResponse>(submission.Path", code);
        Assert.Contains("GameHash.EncodeGameId", code);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource", code);
        Assert.Contains("_lifetime.Cancel()", code);
        Assert.DoesNotContain("ManualInputs", code);
        Assert.DoesNotContain("onAuthFailure", code);
        Assert.DoesNotContain("LogoutAsync", code);
        Assert.DoesNotContain("innerWidth", code);
        Assert.DoesNotContain("Prefers", code);
    }

    private static GameSearchResult Game(string id, string title) =>
        new(id, title, "steam", null);

    private static string FindWebRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Web");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate src/Web from the test output directory.");
    }
}
