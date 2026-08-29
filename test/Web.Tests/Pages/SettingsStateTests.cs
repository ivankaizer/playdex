using Web.Modules.Settings;
using Xunit;

namespace Web.Tests.Pages;

public sealed class SettingsStateTests
{
    [Fact]
    public void InitialLoadsBeginInReactOrderAndRemainIndependent()
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
    public async Task InitialFlowLaunchesProvidersThenSettingsWithoutWaitingForEither()
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
    public void LoadsRetainOldValuesWhileLoadingAndFailIndependently()
    {
        var state = new SettingsState();
        var provider = Provider(1, "steam", "active");
        state.CompleteProvidersLoad(new ProvidersResponse([provider]));
        state.CompleteSettingsLoad(new UserSettingsResponse(30, "123456789"));

        state.BeginProvidersLoad();
        state.BeginSettingsLoad();

        Assert.Same(provider, Assert.Single(state.Providers));
        Assert.Equal("30", state.IgnoreShortSessionsMinutes);
        Assert.Equal("123456789", state.TelegramChatId);

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
    public void VisibleProvidersHideOnlyExactLowercaseManualAndLocalAndKeepOrder()
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
    public void SaveValidationMatchesJavaScriptNumberIntegerCases(string draft, int expected)
    {
        var state = new SettingsState();
        state.SetIgnoreShortSessionsMinutes(draft);

        Assert.True(state.TryBuildSettingsSubmission(out var submission));
        Assert.NotNull(submission);
        Assert.Equal("/settings", submission.Path);
        Assert.Equal(HttpMethod.Put, submission.Method);
        Assert.Equal(["ignoreShortSessionsMinutes", "telegramChatId"], submission.Body.Keys);
        Assert.Equal(expected, submission.Body["ignoreShortSessionsMinutes"]);
        Assert.Null(submission.Body["telegramChatId"]);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("nope")]
    [InlineData("Infinity")]
    public void SaveValidationRejectsNonIntegerOrNegativeNumbers(string draft)
    {
        var state = new SettingsState();
        state.SetIgnoreShortSessionsMinutes(draft);

        Assert.False(state.TryBuildSettingsSubmission(out _));
        Assert.Equal("Enter a non-negative number of minutes.", state.SettingsError);
    }

    [Theory]
    [InlineData("123456789", "123456789")]
    [InlineData("  -100123456789  ", "-100123456789")]
    public void SaveAcceptsTelegramChatIds(string draft, string expected)
    {
        var state = new SettingsState();
        state.SetTelegramChatId(draft);

        Assert.True(state.TryBuildSettingsSubmission(out var submission));
        Assert.Equal(expected, submission!.Body["telegramChatId"]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("@username")]
    [InlineData("12.5")]
    public void SaveRejectsInvalidTelegramChatIds(string draft)
    {
        var state = new SettingsState();
        state.SetTelegramChatId(draft);

        Assert.False(state.TryBuildSettingsSubmission(out _));
        Assert.Equal("Enter a valid Telegram chat ID.", state.SettingsError);
    }

    [Fact]
    public void SettingsSaveDisablesThenUsesTheResponseWithoutSuccessState()
    {
        var state = new SettingsState();
        state.SetIgnoreShortSessionsMinutes("15");
        state.BeginSettingsSave();

        Assert.True(state.IsSettingsSaving);
        Assert.Empty(state.SettingsError);

        state.CompleteSettingsSave(new UserSettingsResponse(20, "987654321"));
        state.EndSettingsSave();

        Assert.False(state.IsSettingsSaving);
        Assert.Equal("20", state.IgnoreShortSessionsMinutes);
        Assert.Equal("987654321", state.TelegramChatId);
        Assert.Empty(state.SettingsError);

        state.BeginSettingsSave();
        state.FailSettingsSave("Save failed.");
        state.EndSettingsSave();
        Assert.Equal("Save failed.", state.SettingsError);
        Assert.Equal("20", state.IgnoreShortSessionsMinutes);
    }

    [Fact]
    public void TestNotificationRequiresAnUnchangedSavedChatIdAndReportsItsOutcome()
    {
        var state = new SettingsState();

        Assert.False(state.CanSendTestNotification);
        state.CompleteSettingsLoad(new UserSettingsResponse(0, "123456789"));
        Assert.True(state.CanSendTestNotification);

        state.SetTelegramChatId("987654321");
        Assert.False(state.CanSendTestNotification);

        state.CompleteSettingsSave(new UserSettingsResponse(0, "987654321"));
        Assert.True(state.CanSendTestNotification);
        Assert.Equal("/settings/notifications/test", state.BeginTestNotification());
        Assert.True(state.IsTestNotificationSending);
        Assert.False(state.CanSendTestNotification);

        state.CompleteTestNotification(new TestNotificationResponse(true));
        state.EndTestNotification();
        Assert.False(state.IsTestNotificationSending);
        Assert.True(state.CanSendTestNotification);
        Assert.Equal("Test message sent.", state.TestNotificationMessage);
        Assert.Empty(state.TestNotificationError);

        state.BeginTestNotification();
        state.FailTestNotification("Apprise unavailable.");
        state.EndTestNotification();
        Assert.Equal("Apprise unavailable.", state.TestNotificationError);
        Assert.Empty(state.TestNotificationMessage);
    }

    [Fact]
    public void ProviderUpdatesArePerIdNonOptimisticAndReplaceFromTheResponse()
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

    [Fact]
    public void CredentialEditBuildsExactBodyAndIgnoresLockedApiKeyChanges()
    {
        var state = new SettingsState();
        var steam = Provider(1, "steam", "active");
        var locked = new ProviderStatus(2, "psn", "account-2", "active", "secret", true);
        state.CompleteProvidersLoad(new ProvidersResponse([steam, locked]));

        state.BeginCredentialEdit(steam);
        Assert.True(state.IsEditingCredential(1));
        Assert.Equal("account-1", state.EditingAccountId);
        Assert.Equal("", state.EditingApiKey);

        state.SetEditingAccountId("  new-account  ");
        state.SetEditingApiKey("new-secret");
        Assert.True(state.TryBuildCredentialSubmission(out var submission));
        Assert.NotNull(submission);
        Assert.Equal("/providers/1", submission.Path);
        Assert.Equal(HttpMethod.Put, submission.Method);
        Assert.Equal(["accountId", "apiKey"], submission.Body.Keys);
        Assert.Equal("new-account", submission.Body["accountId"]);
        Assert.Equal("new-secret", submission.Body["apiKey"]);
        state.CancelCredentialEdit();

        state.BeginCredentialEdit(locked);
        state.SetEditingAccountId("new-account");
        state.SetEditingApiKey("should-be-ignored");
        Assert.True(state.TryBuildCredentialSubmission(out var lockedSubmission));
        Assert.NotNull(lockedSubmission);
        Assert.Equal(["accountId"], lockedSubmission.Body.Keys);
    }

    [Fact]
    public void ApiCredentialEditPresentsKeyButNeverSubmitsAKeyChange()
    {
        var state = new SettingsState();
        var api = new ProviderStatus(2, "api", "local", "active", "plx_secret", true);
        state.CompleteProvidersLoad(new ProvidersResponse([api]));

        state.BeginCredentialEdit(api);

        Assert.Equal("plx_secret", state.EditingApiKey);
        state.SetEditingApiKey("replacement");
        state.SetEditingAccountId("renamed");
        Assert.True(state.TryBuildCredentialSubmission(out var submission));
        Assert.NotNull(submission);
        Assert.Equal(["accountId"], submission.Body.Keys);
    }

    [Fact]
    public void CredentialEditRejectsNoChangesAndCompletesByReplacingTheProvider()
    {
        var state = new SettingsState();
        var steam = Provider(1, "steam", "active");
        state.CompleteProvidersLoad(new ProvidersResponse([steam]));

        state.BeginCredentialEdit(steam);
        Assert.False(state.TryBuildCredentialSubmission(out _));
        Assert.Equal("No changes to save.", state.CredentialError);

        state.SetEditingAccountId("changed");
        Assert.True(state.TryBuildCredentialSubmission(out _));
        state.BeginCredentialSave(1);
        var updated = steam with { AccountId = "changed" };
        state.CompleteCredentialSave(updated);

        Assert.Same(updated, state.Providers[0]);
        Assert.False(state.IsEditingCredential(1));
        Assert.False(state.IsCredentialSaving(1));
        Assert.Empty(state.EditingAccountId);
    }

    [Theory]
    [InlineData("steam", "S", "bg-slate-200 dark:bg-slate-800", "text-slate-900 dark:text-slate-100")]
    [InlineData("psn", "P", "bg-indigo-500/30", "text-indigo-700 dark:text-indigo-200")]
    [InlineData("playnite", "PL", "bg-amber-500/30", "text-amber-700 dark:text-amber-200")]
    [InlineData("custom", "PX", "bg-slate-200 dark:bg-slate-800", "text-slate-700 dark:text-slate-200")]
    public void ProviderPresentationUsesTheExactReactMetadata(
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
    public void IntegrationCreatorIsClosedByDefaultAndOffersTypesBelowTheirLimitInOrder()
    {
        var state = new SettingsState();
        state.CompleteProvidersLoad(new ProvidersResponse([
            Provider(1, "steam", "active"),
            Provider(2, "api", "active"),
        ]));

        Assert.False(state.IsIntegrationCreatorOpen);
        Assert.Null(state.SelectedCreationPlatform);
        Assert.Equal(["steam", "psn", "xbox"], state.AvailableIntegrationPlatforms);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void IntegrationCreatorOffersAServiceUntilItsThirdAccount(int steamCount, bool expectedAvailable)
    {
        var state = new SettingsState();
        state.CompleteProvidersLoad(new ProvidersResponse(
            Enumerable.Range(1, steamCount)
                .Select(id => Provider(id, "steam", "active"))
                .ToArray()));

        Assert.Equal(expectedAvailable, state.AvailableIntegrationPlatforms.Contains("steam"));
        Assert.Contains("psn", state.AvailableIntegrationPlatforms);
        Assert.Contains("xbox", state.AvailableIntegrationPlatforms);
    }

    [Fact]
    public void IntegrationCreatorSelectionAndCancellationClearLocalDraftState()
    {
        var state = new SettingsState();
        state.CompleteProvidersLoad(new ProvidersResponse([]));

        state.OpenIntegrationCreator();
        Assert.True(state.IsIntegrationCreatorOpen);
        Assert.True(state.SelectCreationPlatform("xbox"));
        state.SetCreationApiKey("openxbl-key");
        state.SetCreationAccountId("invalid-xuid");
        Assert.False(state.TryBuildIntegrationCreationSubmission(out _));
        Assert.Equal("Xbox XUID must contain digits only.", state.CreationError);

        state.CancelIntegrationCreation();

        Assert.False(state.IsIntegrationCreatorOpen);
        Assert.Null(state.SelectedCreationPlatform);
        Assert.Empty(state.CreationApiKey);
        Assert.Empty(state.CreationAccountId);
        Assert.Empty(state.CreationError);
    }

    [Theory]
    [InlineData("steam", " steam-key ", " steam-id ", "steam-key", "steam-id")]
    [InlineData("psn", " npsso-token ", " ", "npsso-token", null)]
    [InlineData("xbox", " openxbl-key ", " 2533274817903789 ", "openxbl-key", "2533274817903789")]
    public void IntegrationCreatorBuildsPlatformSpecificGenericSubmissions(
        string platform,
        string apiKey,
        string accountId,
        string expectedApiKey,
        string? expectedAccountId)
    {
        var state = new SettingsState();
        state.CompleteProvidersLoad(new ProvidersResponse([]));
        state.OpenIntegrationCreator();
        Assert.True(state.SelectCreationPlatform(platform));
        state.SetCreationApiKey(apiKey);
        state.SetCreationAccountId(accountId);

        Assert.True(state.TryBuildIntegrationCreationSubmission(out var submission));
        Assert.NotNull(submission);
        Assert.Equal("/providers", submission.Path);
        Assert.Equal(HttpMethod.Post, submission.Method);
        Assert.Equal(["platform", "apiKey", "accountId"], submission.Body.Keys);
        Assert.Equal(platform, submission.Body["platform"]);
        Assert.Equal(expectedApiKey, submission.Body["apiKey"]);
        Assert.Equal(expectedAccountId, submission.Body["accountId"]);
    }

    [Fact]
    public void SuccessfulIntegrationCreationAddsProviderAndClosesCreator()
    {
        var state = new SettingsState();
        state.CompleteProvidersLoad(new ProvidersResponse([
            Provider(1, "steam", "active"),
            Provider(2, "xbox", "active"),
            Provider(3, "xbox", "active"),
        ]));
        state.OpenIntegrationCreator();
        Assert.True(state.SelectCreationPlatform("xbox"));
        state.SetCreationApiKey("openxbl-key");
        state.SetCreationAccountId("2533274817903789");

        state.BeginIntegrationCreation();
        Assert.True(state.IsCreatingIntegration);
        var xbox = new ProviderStatus(4, "xbox", "2533274817903789", "active", "openxbl-key", false);
        state.CompleteIntegrationCreation(xbox);
        state.EndIntegrationCreation();

        Assert.Equal([1, 2, 3, 4], state.Providers.Select(provider => provider.Id));
        Assert.Equal(["steam", "psn"], state.AvailableIntegrationPlatforms);
        Assert.False(state.IsIntegrationCreatorOpen);
        Assert.False(state.IsCreatingIntegration);
        Assert.Null(state.SelectedCreationPlatform);
        Assert.Empty(state.CreationApiKey);
        Assert.Empty(state.CreationAccountId);
    }

    [Fact]
    public void ProviderPresentationPreservesEmptySubtitlesAndUsesNullFallbacks()
    {
        Assert.Equal("Local", SettingsState.Present(
            new ProviderStatus(1, "steam", null, "active", "secret", false)).Subtitle);
        Assert.Equal("", SettingsState.Present(
            new ProviderStatus(1, "steam", "", "active", "secret", false)).Subtitle);
        Assert.Equal("Local", SettingsState.Present(
            new ProviderStatus(1, "playnite", "account", "active", null, false)).Subtitle);
        Assert.Equal("", SettingsState.Present(
            new ProviderStatus(1, "playnite", "account", "active", "", false)).Subtitle);
    }

    [Fact]
    public void SettingsPageWiresLiteralReactMarkupRequestsAndCancellation()
    {
        var root = FindWebRoot();
        var markupPath = Path.Combine(root, "Modules", "Settings", "Settings.razor");
        var codePath = Path.Combine(root, "Modules", "Settings", "Settings.razor.cs");
        Assert.True(File.Exists(markupPath), "Missing Settings Razor page.");

        var markup = File.ReadAllText(markupPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains("@page \"/settings\"", markup);
        Assert.Contains("<AppLayout Title=\"Settings\">", markup);
        Assert.Contains("data-settings-section=\"preferences\"", markup);
        Assert.Contains("data-settings-section=\"integrations\"", markup);
        Assert.Contains("grid gap-5 lg:grid-cols-2", markup);
        Assert.Contains("Sessions shorter than this duration are marked ignored and excluded from stats. Set to 0 to disable.", markup);
        Assert.Contains("type=\"number\"", markup);
        Assert.Contains("min=\"0\"", markup);
        Assert.Contains("step=\"1\"", markup);
        Assert.Contains("inputmode=\"numeric\"", markup);
        Assert.Contains("Telegram notifications", markup);
        Assert.Contains("Chat ID", markup);
        Assert.Contains("ChangeTelegramChatId", markup);
        Assert.Contains("Send test", markup);
        Assert.Contains("SendTestNotificationAsync", markup);
        Assert.Contains("aria-live=\"polite\"", markup);
        Assert.Contains("No integrations connected yet. Add one to start syncing playtime.", markup);
        Assert.Contains("Updating...", markup);
        Assert.Contains("Managed by administrator", markup);
        Assert.Contains("provider.Platform == \"api\"", markup);
        Assert.Contains("aria-readonly=\"true\"", markup);
        Assert.Contains("Generated locally and read-only.", markup);
        Assert.Contains("ToggleCredentialEditorAsync", markup);
        Assert.Contains("SaveCredentialAsync", markup);
        Assert.DoesNotContain("EditForm", markup);
        Assert.DoesNotContain("success", markup, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("SettingsInitialLoadFlow.RunAsync", code);
        Assert.Contains("SendAsync<ProvidersResponse>(path", code);
        Assert.Contains("SendAsync<UserSettingsResponse>(path", code);
        Assert.Contains("SendAsync<UserSettingsResponse>(submission!.Path", code);
        Assert.Contains("SendAsync<TestNotificationResponse>", code);
        Assert.Contains("HttpMethod.Post", code);
        Assert.Contains("SendAsync<ProviderUpdateResponse>(submission.Path", code);
        Assert.Contains("SendAsync<ProviderCredentialUpdateResponse>(submission!.Path", code);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource", code);
        Assert.Contains("_lifetime.Cancel()", code);
        Assert.DoesNotContain("onAuthFailure", code);
        Assert.DoesNotContain("LogoutAsync", code);
    }

    [Fact]
    public void SettingsPagePlacesResponsivePreferencesBeforeIntegrations()
    {
        var markup = File.ReadAllText(Path.Combine(
            FindWebRoot(), "Modules", "Settings", "Settings.razor"));

        var preferences = markup.IndexOf("data-settings-section=\"preferences\"", StringComparison.Ordinal);
        var sessions = markup.IndexOf("data-settings-group=\"sessions\"", StringComparison.Ordinal);
        var telegram = markup.IndexOf("data-settings-group=\"telegram\"", StringComparison.Ordinal);
        var integrations = markup.IndexOf("data-settings-section=\"integrations\"", StringComparison.Ordinal);

        Assert.True(preferences >= 0);
        Assert.True(preferences < sessions);
        Assert.True(sessions < telegram);
        Assert.True(telegram < integrations);
    }

    [Fact]
    public void IntegrationStatusIsInformationalAndManagementOwnsExplicitStatusAction()
    {
        var markup = File.ReadAllText(Path.Combine(
            FindWebRoot(), "Modules", "Settings", "Settings.razor"));

        Assert.Contains("data-integration-status=\"@provider.Status\"", markup);
        Assert.Contains("IntegrationStatusClass(provider)", markup);
        Assert.Contains("@(_state.IsEditingCredential(provider.Id) ? \"Close\" : \"Manage\")", markup);
        Assert.Contains("@onclick=\"() => ToggleProviderAsync(provider)\"", markup);
        Assert.Contains("provider.Status == \"active\" ? \"Pause\" : \"Resume\"", markup);

        var status = markup.IndexOf("data-integration-status=\"@provider.Status\"", StringComparison.Ordinal);
        var statusAction = markup.IndexOf("@onclick=\"() => ToggleProviderAsync(provider)\"", StringComparison.Ordinal);
        var management = markup.IndexOf("@if (_state.IsEditingCredential(provider.Id))", StringComparison.Ordinal);
        Assert.True(status >= 0);
        Assert.True(management < statusAction);
        Assert.True(status < statusAction);
    }

    [Fact]
    public void ExistingIntegrationsPrecedeCollapsedGenericCreator()
    {
        var markup = File.ReadAllText(Path.Combine(
            FindWebRoot(), "Modules", "Settings", "Settings.razor"));

        var existing = markup.IndexOf("@foreach (var provider in _state.VisibleProviders)", StringComparison.Ordinal);
        var creator = markup.IndexOf("data-integration-creator", StringComparison.Ordinal);

        Assert.True(existing >= 0);
        Assert.True(existing < creator);
        Assert.Contains("Add integration", markup);
        Assert.Contains("Connect up to 3 accounts per service.", markup);
        Assert.Contains("You can connect up to 3 accounts per service.", markup);
        Assert.Contains("SelectCreationPlatform(\"steam\")", markup);
        Assert.Contains("SelectCreationPlatform(\"psn\")", markup);
        Assert.Contains("SelectCreationPlatform(\"xbox\")", markup);
        Assert.Contains("Steam Web API key", markup);
        Assert.Contains("Steam ID", markup);
        Assert.Contains("NPSSO token", markup);
        Assert.Contains("Account ID", markup);
        Assert.Contains("OpenXBL API key", markup);
        Assert.Contains("Xbox XUID", markup);
        Assert.Contains("OpenXBL", markup);
        Assert.DoesNotContain("data-available-integration=\"xbox\"", markup);
        Assert.DoesNotContain("experimental", markup, StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderStatus Provider(long id, string platform, string status) =>
        new(id, platform, $"account-{id}", status, platform == "playnite" ? $"key-{id}" : null, false);

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
