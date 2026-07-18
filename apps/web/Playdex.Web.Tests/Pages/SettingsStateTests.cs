using Playdex.Web.Models;
using Playdex.Web.Pages;
using Xunit;

namespace Playdex.Web.Tests.Pages;

public sealed class SettingsStateTests
{
    [Fact]
    public void InitialLoads_BeginInReactOrderAndRemainIndependent()
    {
        var state = new SettingsState();

        Assert.Equal("/providers", state.BeginProvidersLoad());
        Assert.True(state.IsProvidersLoading);
        Assert.Equal("/settings", state.BeginSettingsLoad());
        Assert.True(state.IsSettingsLoading);

        state.FailProvidersLoad("Providers unavailable.");

        Assert.False(state.IsProvidersLoading);
        Assert.Equal("Providers unavailable.", state.ProvidersError);
        Assert.True(state.IsSettingsLoading);
        Assert.Empty(state.SettingsError);
    }

    [Fact]
    public async Task InitialFlow_LaunchesProvidersThenSettingsWithoutWaitingForEither()
    {
        var calls = new List<string>();
        var providers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var flow = SettingsInitialLoadFlow.RunAsync(
            () =>
            {
                calls.Add("providers");
                return providers.Task;
            },
            () =>
            {
                calls.Add("settings");
                return settings.Task;
            });

        Assert.Equal(["providers", "settings"], calls);
        Assert.False(flow.IsCompleted);
        settings.SetResult();
        Assert.False(flow.IsCompleted);
        providers.SetResult();
        await flow;
    }

    [Fact]
    public void Loads_RetainOldValuesWhileLoadingAndFailIndependently()
    {
        var state = new SettingsState();
        var provider = Provider(1, "steam", "active");
        state.CompleteProvidersLoad(new ProvidersResponse([provider]));
        state.CompleteSettingsLoad(new UserSettingsResponse(30));

        state.BeginProvidersLoad();
        state.BeginSettingsLoad();

        Assert.Same(provider, Assert.Single(state.Providers));
        Assert.Equal("30", state.IgnoreShortSessionsMinutes);

        state.FailProvidersLoad("Provider error.");

        Assert.Empty(state.Providers);
        Assert.Equal("Provider error.", state.ProvidersError);
        Assert.True(state.IsSettingsLoading);
        Assert.Equal("30", state.IgnoreShortSessionsMinutes);

        state.FailSettingsLoad("Settings error.");
        Assert.Equal("Settings error.", state.SettingsError);
        Assert.False(state.IsSettingsLoading);
        Assert.Equal("30", state.IgnoreShortSessionsMinutes);
    }

    [Fact]
    public void VisibleProviders_HideOnlyExactLowercaseManualAndLocalAndKeepOrder()
    {
        var state = new SettingsState();
        state.CompleteProvidersLoad(new ProvidersResponse([
            Provider(1, "manual", "active"),
            Provider(2, "steam", "active"),
            Provider(3, "local", "active"),
            Provider(4, "Manual", "paused"),
            Provider(5, "psn", "paused"),
        ]));

        Assert.Equal([2, 4, 5], state.VisibleProviders.Select(item => item.Id));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("0", 0)]
    [InlineData("1.0", 1)]
    [InlineData("1e2", 100)]
    public void SaveValidation_MatchesJavaScriptNumberIntegerCases(string draft, int expected)
    {
        var state = new SettingsState();
        state.SetIgnoreShortSessionsMinutes(draft);

        Assert.True(state.TryBuildSettingsSubmission(out var submission));
        Assert.NotNull(submission);
        Assert.Equal("/settings", submission.Path);
        Assert.Equal(HttpMethod.Put, submission.Method);
        Assert.Equal(["ignoreShortSessionsMinutes"], submission.Body.Keys);
        Assert.Equal(expected, submission.Body["ignoreShortSessionsMinutes"]);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("nope")]
    [InlineData("Infinity")]
    public void SaveValidation_RejectsNonIntegerOrNegativeNumbers(string draft)
    {
        var state = new SettingsState();
        state.SetIgnoreShortSessionsMinutes(draft);

        Assert.False(state.TryBuildSettingsSubmission(out _));
        Assert.Equal("Enter a non-negative number of minutes.", state.SettingsError);
    }

    [Fact]
    public void SettingsSave_DisablesThenUsesTheResponseWithoutSuccessState()
    {
        var state = new SettingsState();
        state.SetIgnoreShortSessionsMinutes("15");
        state.BeginSettingsSave();

        Assert.True(state.IsSettingsSaving);
        Assert.Empty(state.SettingsError);

        state.CompleteSettingsSave(new UserSettingsResponse(20));
        state.EndSettingsSave();

        Assert.False(state.IsSettingsSaving);
        Assert.Equal("20", state.IgnoreShortSessionsMinutes);
        Assert.Empty(state.SettingsError);

        state.BeginSettingsSave();
        state.FailSettingsSave("Save failed.");
        state.EndSettingsSave();
        Assert.Equal("Save failed.", state.SettingsError);
        Assert.Equal("20", state.IgnoreShortSessionsMinutes);
    }

    [Fact]
    public void ProviderUpdates_ArePerIdNonOptimisticAndReplaceFromTheResponse()
    {
        var state = new SettingsState();
        var steam = Provider(1, "steam", "active");
        var psn = Provider(2, "psn", "paused");
        state.CompleteProvidersLoad(new ProvidersResponse([steam, psn]));

        var steamUpdate = state.BeginProviderUpdate(steam);
        var psnUpdate = state.BeginProviderUpdate(psn);

        Assert.Equal("/providers/status", steamUpdate.Path);
        Assert.Equal(HttpMethod.Put, steamUpdate.Method);
        Assert.Equal(["providerId", "status"], steamUpdate.Body.Keys);
        Assert.Equal(1L, steamUpdate.Body["providerId"]);
        Assert.Equal("paused", steamUpdate.Body["status"]);
        Assert.Equal("active", psnUpdate.Body["status"]);
        Assert.Equal("active", state.Providers[0].Status);
        Assert.Equal("paused", state.Providers[1].Status);
        Assert.True(state.IsProviderUpdating(1));
        Assert.True(state.IsProviderUpdating(2));

        var updatedSteam = steam with { Status = "paused", AccountId = "updated" };
        state.CompleteProviderUpdate(new ProviderUpdateResponse(updatedSteam));
        state.EndProviderUpdate(1);

        Assert.Same(updatedSteam, state.Providers[0]);
        Assert.Same(psn, state.Providers[1]);
        Assert.False(state.IsProviderUpdating(1));
        Assert.True(state.IsProviderUpdating(2));

        state.FailProviderUpdate("Provider failed.");
        state.EndProviderUpdate(2);
        Assert.Equal("Provider failed.", state.ProvidersError);
        Assert.Same(psn, state.Providers[1]);
    }

    [Theory]
    [InlineData("steam", "S", "bg-slate-200 dark:bg-slate-800", "text-slate-900 dark:text-slate-100")]
    [InlineData("psn", "P", "bg-indigo-500/30", "text-indigo-700 dark:text-indigo-200")]
    [InlineData("playnite", "PL", "bg-amber-500/30", "text-amber-700 dark:text-amber-200")]
    [InlineData("custom", "PX", "bg-slate-200 dark:bg-slate-800", "text-slate-700 dark:text-slate-200")]
    public void ProviderPresentation_UsesTheExactReactMetadata(
        string platform,
        string label,
        string color,
        string accent)
    {
        var view = SettingsState.Present(Provider(1, platform, "active"));

        Assert.Equal(label, view.Label);
        Assert.Equal(color, view.Color);
        Assert.Equal(accent, view.Accent);
    }

    [Fact]
    public void ProviderPresentation_PreservesEmptySubtitlesAndUsesNullFallbacks()
    {
        Assert.Equal("Local", SettingsState.Present(
            new ProviderStatus(1, "steam", null, "active", "secret")).Subtitle);
        Assert.Equal("", SettingsState.Present(
            new ProviderStatus(1, "steam", "", "active", "secret")).Subtitle);
        Assert.Equal("Local", SettingsState.Present(
            new ProviderStatus(1, "playnite", "account", "active", null)).Subtitle);
        Assert.Equal("", SettingsState.Present(
            new ProviderStatus(1, "playnite", "account", "active", "")).Subtitle);
    }

    [Fact]
    public void SettingsPage_WiresLiteralReactMarkupRequestsAndCancellation()
    {
        var root = FindWebRoot();
        var markupPath = Path.Combine(root, "Pages", "Settings.razor");
        var codePath = Path.Combine(root, "Pages", "Settings.razor.cs");
        Assert.True(File.Exists(markupPath), "Missing Settings Razor page.");

        var markup = File.ReadAllText(markupPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains("@page \"/settings\"", markup);
        Assert.Contains("<AppLayout Title=\"Settings\">", markup);
        Assert.Contains("mt-10 grid gap-6 lg:grid-cols-[1.05fr_0.95fr]", markup);
        Assert.Contains("Sessions shorter than this duration are marked ignored and excluded from stats. Set to 0 to disable.", markup);
        Assert.Contains("type=\"number\"", markup);
        Assert.Contains("min=\"0\"", markup);
        Assert.Contains("step=\"1\"", markup);
        Assert.Contains("inputmode=\"numeric\"", markup);
        Assert.Contains("No providers connected yet. Add one to start syncing playtime.", markup);
        Assert.Contains("Updating...", markup);
        Assert.DoesNotContain("EditForm", markup);
        Assert.DoesNotContain("success", markup, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("SettingsInitialLoadFlow.RunAsync", code);
        Assert.Contains("SendAsync<ProvidersResponse>(path", code);
        Assert.Contains("SendAsync<UserSettingsResponse>(path", code);
        Assert.Contains("SendAsync<UserSettingsResponse>(submission!.Path", code);
        Assert.Contains("SendAsync<ProviderUpdateResponse>(submission.Path", code);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource", code);
        Assert.Contains("_lifetime.Cancel()", code);
        Assert.DoesNotContain("onAuthFailure", code);
        Assert.DoesNotContain("LogoutAsync", code);
    }

    private static ProviderStatus Provider(long id, string platform, string status) =>
        new(id, platform, $"account-{id}", status, platform == "playnite" ? $"key-{id}" : null);

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
