using Playdex.Web.Shared.Models;
using Playdex.Web.Modules.Mapping;
using Xunit;

namespace Playdex.Web.Tests.Pages;

public sealed class MappingStateTests
{
    [Fact]
    public void InitialLoads_BeginInReactOrderAndRemainIndependent()
    {
        var state = new MappingState();

        Assert.Equal("/manual/inputs", state.BeginManualInputsLoad());
        Assert.True(state.IsManualInputsLoading);
        Assert.Equal("/games/connections", state.BeginGameConnectionsLoad());
        Assert.True(state.IsGameConnectionsLoading);

        state.FailManualInputsLoad("Manual inputs unavailable.");

        Assert.False(state.IsManualInputsLoading);
        Assert.Equal("Manual inputs unavailable.", state.ManualInputsError);
        Assert.True(state.IsGameConnectionsLoading);
        Assert.Empty(state.GameConnectionsError);
    }

    [Fact]
    public async Task InitialFlow_LaunchesManualInputsThenConnectionsWithoutWaitingForEither()
    {
        var calls = new List<string>();
        var manualCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionsCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var flow = MappingInitialLoadFlow.RunAsync(
            () =>
            {
                calls.Add("manual");
                return manualCompletion.Task;
            },
            () =>
            {
                calls.Add("connections");
                return connectionsCompletion.Task;
            });

        Assert.Equal(["manual", "connections"], calls);
        Assert.False(flow.IsCompleted);

        connectionsCompletion.SetResult();
        Assert.False(flow.IsCompleted);
        manualCompletion.SetResult();
        await flow;
    }

    [Fact]
    public void ListLoads_RetainOldRowsWhileLoadingAndClearOnlyTheirOwnFailure()
    {
        var state = new MappingState();
        var input = ManualInput();
        var connection = Connection();
        state.CompleteManualInputsLoad(new ManualInputsResponse([input]));
        state.CompleteGameConnectionsLoad(new GameConnectionsResponse([connection]));

        state.BeginManualInputsLoad();

        Assert.True(state.IsManualInputsLoading);
        Assert.Same(input, Assert.Single(state.ManualInputs));
        Assert.Same(connection, Assert.Single(state.GameConnections));

        state.FailManualInputsLoad("Manual load failed.");

        Assert.Empty(state.ManualInputs);
        Assert.Equal("Manual load failed.", state.ManualInputsError);
        Assert.Same(connection, Assert.Single(state.GameConnections));
        Assert.Empty(state.GameConnectionsError);

        state.BeginGameConnectionsLoad();
        state.FailGameConnectionsLoad("Connections load failed.");
        Assert.Empty(state.GameConnections);
        Assert.Equal("Connections load failed.", state.GameConnectionsError);
        Assert.Equal("Manual load failed.", state.ManualInputsError);
    }

    [Fact]
    public void MappingEditor_AllowsOneRowAndCloseOnlyResetsIdAndOverride()
    {
        var state = new MappingState();
        var mapped = ManualInput(
            id: 7,
            input: "Hades raw",
            mapped: new ManualGame("42", "Hades II", "steam", null));

        state.OpenMappingRow(mapped);

        Assert.Equal(7, state.ActiveMappingInputId);
        Assert.Equal("Hades II", state.MappingSearchQuery);
        Assert.Equal("steam", state.MappingPlatformOverride);
        Assert.Empty(state.MappingSearchResults);
        Assert.Empty(state.MappingSearchError);

        state.CompleteMappingSearch(new ManualMappingSearchResponse("Hades", false, [LocalResult()]));
        state.OpenMappingRow(ManualInput(id: 8, input: "Celeste"));

        Assert.Equal(8, state.ActiveMappingInputId);
        Assert.Equal("Celeste", state.MappingSearchQuery);
        Assert.Equal("auto", state.MappingPlatformOverride);
        Assert.Empty(state.MappingSearchResults);
        Assert.Empty(state.MappingSearchError);
        Assert.False(state.MappingIgdbAvailable);

        state.SetMappingSearchQuery("draft");
        state.CompleteMappingSearch(new ManualMappingSearchResponse("draft", false, [LocalResult()]));
        state.SetMappingPlatformOverride("pc");
        state.CloseMappingRow();

        Assert.Null(state.ActiveMappingInputId);
        Assert.Equal("auto", state.MappingPlatformOverride);
        Assert.Equal("draft", state.MappingSearchQuery);
        Assert.Single(state.MappingSearchResults);
        Assert.False(state.MappingIgdbAvailable);
    }

    [Fact]
    public void MappingSearch_UsesEncodeURIComponentAndPreservesResultsUntilFailure()
    {
        var state = new MappingState();
        state.CompleteMappingSearch(new ManualMappingSearchResponse("old", false, [LocalResult()]));
        state.SetMappingSearchQuery("   ");

        Assert.False(state.TryBeginMappingSearch(out _));
        Assert.Equal("Enter a search query.", state.MappingSearchError);
        Assert.Single(state.MappingSearchResults);

        state.SetMappingSearchQuery("  A! (Test)*~'  ");
        Assert.True(state.TryBeginMappingSearch(out var path));
        Assert.Equal("/manual/search?query=A!%20(Test)*~'", path);
        Assert.True(state.IsMappingSearchLoading);
        Assert.Empty(state.MappingSearchError);
        Assert.Single(state.MappingSearchResults);

        state.FailMappingSearch("Search failed.");

        Assert.False(state.IsMappingSearchLoading);
        Assert.Empty(state.MappingSearchResults);
        Assert.Equal("Search failed.", state.MappingSearchError);
        Assert.False(state.MappingIgdbAvailable);
    }

    [Fact]
    public void MappingBodies_AreExactForLocalAndIgdbSelections()
    {
        var state = new MappingState();

        Assert.True(state.TryBuildMappingSubmission(7, LocalResult(), out var local));
        Assert.NotNull(local);
        Assert.Equal("/manual/inputs/7/map", local.Path);
        Assert.Equal(["gameId"], local.Body.Keys);
        Assert.Equal("3H4h3", local.Body["gameId"]);

        state.SetMappingPlatformOverride("pc");
        var igdbResult = new ManualMappingSearchResult(
            "igdb", null, "000123", "Hades II", "PlayStation 5", 167,
            "https://images.test/hades.jpg");

        Assert.True(state.TryBuildMappingSubmission(8, igdbResult, out var igdb));
        Assert.NotNull(igdb);
        Assert.Equal("/manual/inputs/8/map", igdb.Path);
        Assert.Equal(
            ["igdbId", "title", "platformId", "platformKey", "capsuleUrl"],
            igdb.Body.Keys);
        Assert.Equal("000123", igdb.Body["igdbId"]);
        Assert.Equal("Hades II", igdb.Body["title"]);
        Assert.Equal(167, igdb.Body["platformId"]);
        Assert.Equal("pc", igdb.Body["platformKey"]);
        Assert.Equal("https://images.test/hades.jpg", igdb.Body["capsuleUrl"]);

        state.SetMappingPlatformOverride("auto");
        Assert.True(state.TryBuildMappingSubmission(8, igdbResult, out var automatic));
        Assert.Null(automatic!.Body["platformKey"]);

        Assert.False(state.TryBuildMappingSubmission(
            9,
            new ManualMappingSearchResult("local", null, null, "Broken", null, null, null),
            out _));
        Assert.Equal("Invalid mapping selection.", state.MappingSearchError);
    }

    [Fact]
    public void MappingMutation_SuccessClearsEditorBeforeRefreshAndFailureRetainsDraft()
    {
        var state = new MappingState();
        state.OpenMappingRow(ManualInput());
        state.SetMappingSearchQuery("Hades");
        state.CompleteMappingSearch(new ManualMappingSearchResponse("Hades", true, [LocalResult()]));
        state.SetMappingPlatformOverride("pc");

        state.BeginMappingUpdate();
        state.CompleteMappingUpdate();

        Assert.False(state.IsMappingUpdateLoading);
        Assert.Null(state.ActiveMappingInputId);
        Assert.Empty(state.MappingSearchQuery);
        Assert.Empty(state.MappingSearchResults);
        Assert.Equal("auto", state.MappingPlatformOverride);

        state.OpenMappingRow(ManualInput());
        state.SetMappingSearchQuery("Celeste");
        state.SetMappingPlatformOverride("steam");
        state.BeginMappingUpdate();
        state.FailMappingUpdate("Map failed.");

        Assert.False(state.IsMappingUpdateLoading);
        Assert.NotNull(state.ActiveMappingInputId);
        Assert.Equal("Celeste", state.MappingSearchQuery);
        Assert.Equal("steam", state.MappingPlatformOverride);
        Assert.Equal("Map failed.", state.MappingSearchError);
    }

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
        Assert.Contains("mt-6 grid gap-4 md:hidden", markup);
        Assert.Contains("mt-6 hidden overflow-hidden", markup);
        Assert.Contains("Search IGDB", markup);
        Assert.Contains("Platform override", markup);
        Assert.True(markup.Split("@mappingEditor(input)").Length - 1 >= 2);
        Assert.Contains("Use IGDB platform", code);
        Assert.Contains("IGDB is not configured on the server.", markup);
        Assert.Contains("Search for a game to map this input.", markup);
        Assert.Contains("Game connections", markup);
        Assert.Contains("lg:grid-cols-[1.1fr_0.9fr]", markup);
        Assert.Contains("No game connections yet.", markup);
        Assert.Contains("+ +", markup);
        Assert.DoesNotContain("EditForm", markup);
        Assert.DoesNotContain("fixed inset-0", markup);

        Assert.Contains("MappingInitialLoadFlow.RunAsync", code);
        Assert.Contains("SendAsync<ManualInputsResponse>(path", code);
        Assert.Contains("SendAsync<GameConnectionsResponse>(path", code);
        Assert.Contains("SendAsync<ManualMappingSearchResponse>(path", code);
        Assert.Contains("SendAsync<GameSearchResponse>(path", code);
        Assert.Contains("SendAsync<ManualMappingResponse>(submission!.Path", code);
        Assert.Contains("SendAsync<GameConnectionChangeResponse>(submission.Path", code);
        Assert.Contains("GameHash.EncodeGameId", code);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource", code);
        Assert.Contains("_lifetime.Cancel()", code);
        Assert.DoesNotContain("onAuthFailure", code);
        Assert.DoesNotContain("LogoutAsync", code);
        Assert.DoesNotContain("innerWidth", code);
        Assert.DoesNotContain("Prefers", code);
    }

    private static ManualInput ManualInput(
        long id = 7,
        string input = "Hades raw",
        ManualGame? mapped = null) =>
        new(id, input, input, mapped?.Id, mapped is null ? null : "local", mapped);

    private static ManualMappingSearchResult LocalResult() =>
        new("local", "42", null, "Hades II", "steam", null, null);

    private static GameSearchResult Game(string id, string title) =>
        new(id, title, "steam", null);

    private static GameConnection Connection() =>
        new("42", "Hades II", "steam", null, "99", "Celeste", "steam", null);

    private static string FindWebRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "apps", "web");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate apps/web from the test output directory.");
    }
}
