using System.Globalization;
using Microsoft.AspNetCore.Components;
using Web.Infrastructure.Browser;
using Web.Infrastructure.Http;
using Web.Modules.Settings;
using Web.Infrastructure.Common;

namespace Web.Modules.Settings;

internal sealed record SettingsSubmission(
    string Path,
    HttpMethod Method,
    IReadOnlyDictionary<string, object?> Body);

internal sealed record SettingsProviderPresentation(
    string Label,
    string Color,
    string Accent,
    string Name,
    string Subtitle);

internal static class SettingsInitialLoadFlow
{
    public static Task RunAsync(Func<Task> loadProviders, Func<Task> loadSettings)
    {
        var providersTask = loadProviders();
        var settingsTask = loadSettings();
        return Task.WhenAll(providersTask, settingsTask);
    }
}

internal sealed class SettingsState
{
    private const int MaxIntegrationsPerPlatform = 3;
    private static readonly string[] SupportedCreationPlatforms = ["steam", "psn", "xbox"];
    private readonly HashSet<long> _updatingProviderIds = [];
    private readonly HashSet<long> _savingCredentialIds = [];
    private readonly HashSet<long> _savingSessionPlatformIds = [];
    private string _savedTelegramChatId = string.Empty;

    public IReadOnlyList<ProviderStatus> Providers { get; private set; } = [];
    public bool IsProvidersLoading { get; private set; }
    public string ProvidersError { get; private set; } = string.Empty;
    public string IgnoreShortSessionsMinutes { get; private set; } = "0";
    public string TelegramChatId { get; private set; } = string.Empty;
    public bool IsSettingsLoading { get; private set; }
    public bool IsSettingsSaving { get; private set; }
    public string SettingsError { get; private set; } = string.Empty;
    public bool IsTestNotificationSending { get; private set; }
    public string TestNotificationMessage { get; private set; } = string.Empty;
    public string TestNotificationError { get; private set; } = string.Empty;
    public bool CanSendTestNotification =>
        !IsSettingsLoading &&
        !IsSettingsSaving &&
        !IsTestNotificationSending &&
        _savedTelegramChatId.Length > 0 &&
        TelegramChatId.Trim() == _savedTelegramChatId;
    public long? EditingCredentialId { get; private set; }
    public string EditingAccountId { get; private set; } = string.Empty;
    public string EditingApiKey { get; private set; } = string.Empty;
    public string CredentialError { get; private set; } = string.Empty;
    public long? PendingSessionPlatformProviderId { get; private set; }
    public string SelectedSessionPlatformKey { get; private set; } = string.Empty;
    public string SessionPlatformError { get; private set; } = string.Empty;
    public bool IsAnySessionPlatformSaving => _savingSessionPlatformIds.Count > 0;
    public bool IsIntegrationCreatorOpen { get; private set; }
    public string? SelectedCreationPlatform { get; private set; }
    public string CreationApiKey { get; private set; } = string.Empty;
    public string CreationAccountId { get; private set; } = string.Empty;
    public string CreationError { get; private set; } = string.Empty;
    public bool IsCreatingIntegration { get; private set; }
    public IReadOnlyList<string> AvailableIntegrationPlatforms => SupportedCreationPlatforms
        .Where(platform => Providers.Count(provider => provider.Platform == platform) < MaxIntegrationsPerPlatform)
        .ToArray();
    public IReadOnlyList<ProviderStatus> VisibleProviders => Providers
        .Where(provider => provider.Platform != "manual" && provider.Platform != "local")
        .ToArray();

    public string BeginProvidersLoad()
    {
        IsProvidersLoading = true;
        ProvidersError = string.Empty;
        return "/providers";
    }

    public void CompleteProvidersLoad(ProvidersResponse response)
    {
        Providers = response.Providers;
        IsProvidersLoading = false;
    }

    public void FailProvidersLoad(string error)
    {
        Providers = [];
        IsProvidersLoading = false;
        ProvidersError = error;
    }

    public string BeginSettingsLoad()
    {
        IsSettingsLoading = true;
        SettingsError = string.Empty;
        return "/settings";
    }

    public void CompleteSettingsLoad(UserSettingsResponse response)
    {
        IgnoreShortSessionsMinutes = response.IgnoreShortSessionsMinutes
            .ToString(CultureInfo.InvariantCulture);
        TelegramChatId = response.TelegramChatId ?? string.Empty;
        _savedTelegramChatId = TelegramChatId;
        IsSettingsLoading = false;
    }

    public void FailSettingsLoad(string error)
    {
        IsSettingsLoading = false;
        SettingsError = error;
    }

    public void SetIgnoreShortSessionsMinutes(string value) =>
        IgnoreShortSessionsMinutes = value;

    public void SetTelegramChatId(string value)
    {
        TelegramChatId = value;
        TestNotificationMessage = string.Empty;
        TestNotificationError = string.Empty;
    }

    public bool TryBuildSettingsSubmission(out SettingsSubmission? submission)
    {
        submission = null;
        var draft = IgnoreShortSessionsMinutes;
        double parsed;
        if (string.IsNullOrWhiteSpace(draft))
        {
            parsed = 0;
        }
        else if (!double.TryParse(
                     draft,
                     NumberStyles.Float,
                     CultureInfo.InvariantCulture,
                     out parsed))
        {
            return FailSettingsValidation();
        }

        if (!double.IsFinite(parsed) || parsed < 0 || Math.Truncate(parsed) != parsed || parsed > int.MaxValue)
        {
            return FailSettingsValidation();
        }

        var telegramChatId = TelegramChatId.Trim();
        if (telegramChatId.Length > 0 &&
            (!long.TryParse(
                telegramChatId,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsedChatId) || parsedChatId == 0))
        {
            SettingsError = "Enter a valid Telegram chat ID.";
            return false;
        }

        submission = new SettingsSubmission(
            "/settings",
            HttpMethod.Put,
            new Dictionary<string, object?>
            {
                ["ignoreShortSessionsMinutes"] = (int)parsed,
                ["telegramChatId"] = telegramChatId.Length == 0 ? null : telegramChatId,
            });
        return true;
    }

    public void BeginSettingsSave()
    {
        IsSettingsSaving = true;
        SettingsError = string.Empty;
        TestNotificationMessage = string.Empty;
        TestNotificationError = string.Empty;
    }

    public void CompleteSettingsSave(UserSettingsResponse response)
    {
        IgnoreShortSessionsMinutes = response.IgnoreShortSessionsMinutes
            .ToString(CultureInfo.InvariantCulture);
        TelegramChatId = response.TelegramChatId ?? string.Empty;
        _savedTelegramChatId = TelegramChatId;
    }

    public void FailSettingsSave(string error) => SettingsError = error;

    public void EndSettingsSave() => IsSettingsSaving = false;

    public string BeginTestNotification()
    {
        IsTestNotificationSending = true;
        TestNotificationMessage = string.Empty;
        TestNotificationError = string.Empty;
        return "/settings/notifications/test";
    }

    public void CompleteTestNotification(TestNotificationResponse response)
    {
        if (response.Sent)
        {
            TestNotificationMessage = "Test message sent.";
        }
        else
        {
            TestNotificationError = "The test message was not sent.";
        }
    }

    public void FailTestNotification(string error) => TestNotificationError = error;

    public void EndTestNotification() => IsTestNotificationSending = false;

    public SettingsSubmission BeginProviderUpdate(ProviderStatus provider)
    {
        if (IsProviderActionBlocked(provider.Id))
            throw new InvalidOperationException("Another provider change is already pending.");
        _updatingProviderIds.Add(provider.Id);
        ProvidersError = string.Empty;
        return new SettingsSubmission(
            "/providers/status",
            HttpMethod.Put,
            new Dictionary<string, object?>
            {
                ["providerId"] = provider.Id,
                ["status"] = provider.Status == "active" ? "paused" : "active",
            });
    }

    public void CompleteProviderUpdate(ProviderUpdateResponse response) =>
        Providers = Providers
            .Select(provider => provider.Id == response.Provider.Id ? response.Provider : provider)
            .ToArray();

    public void FailProviderUpdate(string error) => ProvidersError = error;

    public void EndProviderUpdate(long providerId) => _updatingProviderIds.Remove(providerId);

    public bool IsProviderUpdating(long providerId) => _updatingProviderIds.Contains(providerId);

    public bool IsProviderMutationInProgress(long providerId) =>
        _updatingProviderIds.Contains(providerId) ||
        _savingCredentialIds.Contains(providerId) ||
        _savingSessionPlatformIds.Contains(providerId);

    public bool IsProviderActionBlocked(long providerId) =>
        IsProviderMutationInProgress(providerId) ||
        PendingSessionPlatformProviderId == providerId;

    public void BeginCredentialEdit(ProviderStatus provider)
    {
        if (IsAnySessionPlatformSaving) return;
        if (PendingSessionPlatformProviderId != provider.Id)
            CancelSessionPlatformChange();
        EditingCredentialId = provider.Id;
        EditingAccountId = provider.AccountId ?? string.Empty;
        EditingApiKey = provider.ApiKey ?? string.Empty;
        SelectedSessionPlatformKey = provider.SessionPlatform?.Key ?? string.Empty;
        CredentialError = string.Empty;
    }

    public void CancelCredentialEdit()
    {
        CancelSessionPlatformChange();
        EditingCredentialId = null;
        EditingAccountId = string.Empty;
        EditingApiKey = string.Empty;
        CredentialError = string.Empty;
    }

    public bool IsEditingCredential(long providerId) => EditingCredentialId == providerId;

    public bool IsCredentialSaving(long providerId) => _savingCredentialIds.Contains(providerId);

    public void SetEditingAccountId(string value) => EditingAccountId = value;

    public void SetEditingApiKey(string value) => EditingApiKey = value;

    public bool TryBuildCredentialSubmission(out SettingsSubmission? submission)
    {
        submission = null;
        CredentialError = string.Empty;
        if (EditingCredentialId is not { } providerId) return false;

        var provider = Providers.FirstOrDefault(item => item.Id == providerId);
        if (provider is null) return false;

        var accountIdChanged = EditingAccountId != (provider.AccountId ?? string.Empty);
        var apiKeyChanged = !provider.ApiKeyLocked && EditingApiKey != (provider.ApiKey ?? string.Empty);

        if (!accountIdChanged && !apiKeyChanged)
        {
            CredentialError = "No changes to save.";
            return false;
        }

        var trimmedAccountId = EditingAccountId.Trim();
        var trimmedApiKey = EditingApiKey.Trim();
        accountIdChanged = trimmedAccountId != (provider.AccountId ?? string.Empty);
        apiKeyChanged = !provider.ApiKeyLocked && trimmedApiKey != (provider.ApiKey ?? string.Empty);
        if (!accountIdChanged && !apiKeyChanged)
        {
            CredentialError = "No changes to save.";
            return false;
        }

        var body = new Dictionary<string, object?>();
        if (accountIdChanged) body["accountId"] = string.IsNullOrEmpty(trimmedAccountId) ? null : trimmedAccountId;
        if (apiKeyChanged) body["apiKey"] = string.IsNullOrEmpty(trimmedApiKey) ? null : trimmedApiKey;

        submission = new SettingsSubmission(
            $"/providers/{providerId}",
            HttpMethod.Put,
            body);
        return true;
    }

    public void BeginCredentialSave(long providerId)
    {
        if (IsProviderActionBlocked(providerId))
            throw new InvalidOperationException("Another provider change is already pending.");
        _savingCredentialIds.Add(providerId);
        CredentialError = string.Empty;
    }

    public void CompleteCredentialSave(ProviderStatus updated)
    {
        Providers = Providers
            .Select(provider => provider.Id == updated.Id ? updated : provider)
            .ToArray();
        EditingCredentialId = null;
        EditingAccountId = string.Empty;
        EditingApiKey = string.Empty;
        _savingCredentialIds.Remove(updated.Id);
        CancelSessionPlatformChange();
    }

    public void FailCredentialSave(string error) => CredentialError = error;

    public void EndCredentialSave(long providerId) => _savingCredentialIds.Remove(providerId);

    public bool SelectSessionPlatform(ProviderStatus provider, string platformKey)
    {
        if (IsAnySessionPlatformSaving || IsProviderMutationInProgress(provider.Id)) return false;
        if (provider.SessionPlatformOptions?.Any(option => option.Key == platformKey) != true)
            return false;

        SelectedSessionPlatformKey = platformKey;
        SessionPlatformError = string.Empty;
        PendingSessionPlatformProviderId = platformKey == provider.SessionPlatform?.Key
            ? null
            : provider.Id;
        return true;
    }

    public string GetSelectedSessionPlatformKey(ProviderStatus provider) =>
        PendingSessionPlatformProviderId == provider.Id
            ? SelectedSessionPlatformKey
            : provider.SessionPlatform?.Key ?? string.Empty;

    public SettingsSubmission BuildSessionPlatformSubmission(bool rewriteExistingSessions)
    {
        if (PendingSessionPlatformProviderId is not { } providerId ||
            string.IsNullOrEmpty(SelectedSessionPlatformKey))
        {
            throw new InvalidOperationException("No session platform change is pending.");
        }

        return new SettingsSubmission(
            $"/providers/{providerId}/session-platform",
            HttpMethod.Put,
            new Dictionary<string, object?>
            {
                ["platform"] = SelectedSessionPlatformKey,
                ["rewriteExistingSessions"] = rewriteExistingSessions,
            });
    }

    public void CancelSessionPlatformChange()
    {
        var provider = PendingSessionPlatformProviderId is { } providerId
            ? Providers.FirstOrDefault(item => item.Id == providerId)
            : null;
        SelectedSessionPlatformKey = provider?.SessionPlatform?.Key ?? string.Empty;
        PendingSessionPlatformProviderId = null;
        SessionPlatformError = string.Empty;
    }

    public void BeginSessionPlatformSave(long providerId)
    {
        if (IsProviderMutationInProgress(providerId))
            throw new InvalidOperationException("Another provider change is already in progress.");
        _savingSessionPlatformIds.Add(providerId);
        SessionPlatformError = string.Empty;
    }

    public bool IsSessionPlatformSaving(long providerId) =>
        _savingSessionPlatformIds.Contains(providerId);

    public void CompleteSessionPlatformSave(ProviderStatus updated)
    {
        Providers = Providers
            .Select(provider => provider.Id == updated.Id ? updated : provider)
            .ToArray();
        SelectedSessionPlatformKey = updated.SessionPlatform?.Key ?? string.Empty;
        PendingSessionPlatformProviderId = null;
        SessionPlatformError = string.Empty;
    }

    public void FailSessionPlatformSave(string error) => SessionPlatformError = error;

    public void EndSessionPlatformSave(long providerId) =>
        _savingSessionPlatformIds.Remove(providerId);

    public void OpenIntegrationCreator()
    {
        if (AvailableIntegrationPlatforms.Count == 0) return;
        IsIntegrationCreatorOpen = true;
        CreationError = string.Empty;
    }

    public bool SelectCreationPlatform(string platform)
    {
        if (!IsIntegrationCreatorOpen || !AvailableIntegrationPlatforms.Contains(platform)) return false;
        SelectedCreationPlatform = platform;
        CreationApiKey = string.Empty;
        CreationAccountId = string.Empty;
        CreationError = string.Empty;
        return true;
    }

    public void CancelIntegrationCreation() => ResetIntegrationCreator();

    public void SetCreationApiKey(string value) => CreationApiKey = value;

    public void SetCreationAccountId(string value) => CreationAccountId = value;

    public bool TryBuildIntegrationCreationSubmission(out SettingsSubmission? submission)
    {
        submission = null;
        CreationError = string.Empty;
        if (SelectedCreationPlatform is not { } platform) return false;

        var apiKey = CreationApiKey.Trim();
        if (apiKey.Length == 0)
        {
            CreationError = platform switch
            {
                "steam" => "Enter a Steam Web API key.",
                "psn" => "Enter a PlayStation NPSSO token.",
                _ => "Enter an OpenXBL API key.",
            };
            return false;
        }

        var accountId = CreationAccountId.Trim();
        if (platform == "steam" && accountId.Length == 0)
        {
            CreationError = "Enter a Steam ID.";
            return false;
        }
        if (platform == "xbox" && accountId.Any(character => !char.IsAsciiDigit(character)))
        {
            CreationError = "Xbox XUID must contain digits only.";
            return false;
        }

        submission = new SettingsSubmission(
            "/providers",
            HttpMethod.Post,
            new Dictionary<string, object?>
            {
                ["platform"] = platform,
                ["apiKey"] = apiKey,
                ["accountId"] = accountId.Length == 0 ? null : accountId,
            });
        return true;
    }

    public void BeginIntegrationCreation()
    {
        IsCreatingIntegration = true;
        CreationError = string.Empty;
    }

    public void CompleteIntegrationCreation(ProviderStatus provider)
    {
        Providers = Providers.Append(provider).ToArray();
        ResetIntegrationCreator();
    }

    public void FailIntegrationCreation(string error) => CreationError = error;

    public void EndIntegrationCreation() => IsCreatingIntegration = false;

    public static string CreationPlatformName(string platform) => platform switch
    {
        "steam" => "Steam",
        "psn" => "PlayStation",
        _ => "Xbox",
    };

    public static SettingsProviderPresentation Present(ProviderStatus provider)
    {
        var (label, color, accent) = provider.Platform switch
        {
            "steam" => ("S", "bg-slate-200 dark:bg-slate-800", "text-slate-900 dark:text-slate-100"),
            "psn" => ("P", "bg-indigo-500/30", "text-indigo-700 dark:text-indigo-200"),
            "playnite" => ("PL", "bg-amber-500/30", "text-amber-700 dark:text-amber-200"),
            "xbox" => ("X", "bg-lime-500/30", "text-lime-700 dark:text-lime-200"),
            _ => ("PX", "bg-slate-200 dark:bg-slate-800", "text-slate-700 dark:text-slate-200"),
        };
        var subtitle = provider.Platform == "playnite"
            ? provider.ApiKey ?? "Local"
            : provider.AccountId ?? "Local";
        return new SettingsProviderPresentation(
            label,
            color,
            accent,
            Formatters.FormatProviderName(provider.Platform),
            subtitle);
    }

    private bool FailSettingsValidation()
    {
        SettingsError = "Enter a non-negative number of minutes.";
        return false;
    }

    private void ResetIntegrationCreator()
    {
        IsIntegrationCreatorOpen = false;
        SelectedCreationPlatform = null;
        CreationApiKey = string.Empty;
        CreationAccountId = string.Empty;
        CreationError = string.Empty;
    }
}

public partial class Settings : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SettingsState _state = new();
    private CancellationTokenSource? _providersRequest;
    private CancellationTokenSource? _settingsRequest;
    private long _providersRequestVersion;
    private long _settingsRequestVersion;

    [Inject]
    private PlaydexApiClient Api { get; set; } = null!;

    [Inject]
    private BrowserInterop Browser { get; set; } = null!;

    /// <summary>Which value was just copied ("server" or "key"), for the button's "Copied" for a moment.</summary>
    private string? _copied;

    private async Task CopyAsync(string what, string text)
    {
        try
        {
            await Browser.CopyAsync(text);
            _copied = what;
            StateHasChanged();
            await Task.Delay(2000, _lifetime.Token);
            _copied = null;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Microsoft.JSInterop.JSException)
        {
            _copied = null; // The browser refused the clipboard; the value stays selectable.
        }
    }

    protected override Task OnInitializedAsync() =>
        SettingsInitialLoadFlow.RunAsync(LoadProvidersAsync, LoadSettingsAsync);

    private async Task LoadProvidersAsync()
    {
        var path = _state.BeginProvidersLoad();
        var version = ++_providersRequestVersion;
        _providersRequest?.Cancel();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _providersRequest = requestCancellation;
        try
        {
            var response = await Api.SendAsync<ProvidersResponse>(path,
                cancellationToken: requestCancellation.Token);
            if (version == _providersRequestVersion && !requestCancellation.IsCancellationRequested)
            {
                _state.CompleteProvidersLoad(response);
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _providersRequestVersion) _state.FailProvidersLoad(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_providersRequest, requestCancellation)) _providersRequest = null;
            requestCancellation.Dispose();
        }
    }

    private async Task LoadSettingsAsync()
    {
        var path = _state.BeginSettingsLoad();
        var version = ++_settingsRequestVersion;
        _settingsRequest?.Cancel();
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _settingsRequest = requestCancellation;
        try
        {
            var response = await Api.SendAsync<UserSettingsResponse>(path,
                cancellationToken: requestCancellation.Token);
            if (version == _settingsRequestVersion && !requestCancellation.IsCancellationRequested)
            {
                _state.CompleteSettingsLoad(response);
            }
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (version == _settingsRequestVersion) _state.FailSettingsLoad(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_settingsRequest, requestCancellation)) _settingsRequest = null;
            requestCancellation.Dispose();
        }
    }

    private void ChangeIgnoreShortSessionsMinutes(ChangeEventArgs eventArgs) =>
        _state.SetIgnoreShortSessionsMinutes(eventArgs.Value?.ToString() ?? string.Empty);

    private void ChangeTelegramChatId(ChangeEventArgs eventArgs) =>
        _state.SetTelegramChatId(eventArgs.Value?.ToString() ?? string.Empty);

    private async Task SaveSettingsAsync()
    {
        if (!_state.TryBuildSettingsSubmission(out var submission)) return;

        _state.BeginSettingsSave();
        try
        {
            var response = await Api.SendAsync<UserSettingsResponse>(submission!.Path,
                submission.Method,
                submission.Body,
                cancellationToken: _lifetime.Token);
            _state.CompleteSettingsSave(response);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailSettingsSave(exception.Message);
        }
        finally
        {
            _state.EndSettingsSave();
        }
    }

    private async Task SendTestNotificationAsync()
    {
        if (!_state.CanSendTestNotification) return;

        var path = _state.BeginTestNotification();
        try
        {
            var response = await Api.SendAsync<TestNotificationResponse>(
                path,
                HttpMethod.Post,
                cancellationToken: _lifetime.Token);
            _state.CompleteTestNotification(response);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailTestNotification(exception.Message);
        }
        finally
        {
            _state.EndTestNotification();
        }
    }

    private async Task ToggleProviderAsync(ProviderStatus provider)
    {
        var submission = _state.BeginProviderUpdate(provider);
        try
        {
            var response = await Api.SendAsync<ProviderUpdateResponse>(submission.Path,
                submission.Method,
                submission.Body,
                cancellationToken: _lifetime.Token);
            _state.CompleteProviderUpdate(response);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailProviderUpdate(exception.Message);
        }
        finally
        {
            _state.EndProviderUpdate(provider.Id);
        }
    }

    private Task ToggleCredentialEditorAsync(ProviderStatus provider)
    {
        if (_state.IsEditingCredential(provider.Id))
        {
            CancelCredentialEdit();
        }
        else
        {
            BeginCredentialEdit(provider);
        }
        return Task.CompletedTask;
    }

    private void BeginCredentialEdit(ProviderStatus provider) => _state.BeginCredentialEdit(provider);

    private void CancelCredentialEdit() => _state.CancelCredentialEdit();

    private void ChangeEditingAccountId(ChangeEventArgs eventArgs) =>
        _state.SetEditingAccountId(eventArgs.Value?.ToString() ?? string.Empty);

    private void ChangeEditingApiKey(ChangeEventArgs eventArgs) =>
        _state.SetEditingApiKey(eventArgs.Value?.ToString() ?? string.Empty);

    private void ChangeSessionPlatform(ProviderStatus provider, ChangeEventArgs eventArgs) =>
        _state.SelectSessionPlatform(provider, eventArgs.Value?.ToString() ?? string.Empty);

    private void CancelSessionPlatformChange() => _state.CancelSessionPlatformChange();

    private async Task SaveSessionPlatformAsync(
        ProviderStatus provider,
        bool rewriteExistingSessions)
    {
        var submission = _state.BuildSessionPlatformSubmission(rewriteExistingSessions);
        _state.BeginSessionPlatformSave(provider.Id);
        try
        {
            var response = await Api.SendAsync<ProviderSessionPlatformUpdateResponse>(
                submission.Path,
                submission.Method,
                submission.Body,
                cancellationToken: _lifetime.Token);
            _state.CompleteSessionPlatformSave(response.Provider);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailSessionPlatformSave(exception.Message);
        }
        finally
        {
            _state.EndSessionPlatformSave(provider.Id);
        }
    }

    private void OpenIntegrationCreator() => _state.OpenIntegrationCreator();

    private void SelectCreationPlatform(string platform) => _state.SelectCreationPlatform(platform);

    private void CancelIntegrationCreation() => _state.CancelIntegrationCreation();

    private void ChangeCreationApiKey(ChangeEventArgs eventArgs) =>
        _state.SetCreationApiKey(eventArgs.Value?.ToString() ?? string.Empty);

    private void ChangeCreationAccountId(ChangeEventArgs eventArgs) =>
        _state.SetCreationAccountId(eventArgs.Value?.ToString() ?? string.Empty);

    private async Task CreateIntegrationAsync()
    {
        if (!_state.TryBuildIntegrationCreationSubmission(out var submission)) return;

        _state.BeginIntegrationCreation();
        try
        {
            var response = await Api.SendAsync<ProviderCreateResponse>(submission!.Path,
                submission.Method,
                submission.Body,
                cancellationToken: _lifetime.Token);
            _state.CompleteIntegrationCreation(response.Provider);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailIntegrationCreation(exception.Message);
        }
        finally
        {
            _state.EndIntegrationCreation();
        }
    }

    private async Task SaveCredentialAsync(ProviderStatus provider)
    {
        if (!_state.TryBuildCredentialSubmission(out var submission)) return;

        _state.BeginCredentialSave(provider.Id);
        try
        {
            var response = await Api.SendAsync<ProviderCredentialUpdateResponse>(submission!.Path,
                submission.Method,
                submission.Body,
                cancellationToken: _lifetime.Token);
            _state.CompleteCredentialSave(response.Provider);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailCredentialSave(exception.Message);
        }
        finally
        {
            _state.EndCredentialSave(provider.Id);
        }
    }

    private static string IntegrationStatusClass(ProviderStatus provider) =>
        "rounded-full px-3 py-1.5 font-semibold uppercase tracking-[0.2em] " +
        (provider.Status == "active"
            ? "bg-emerald-400 text-slate-900"
            : "border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200");

    private static string IntegrationActionButtonClass(ProviderStatus provider) =>
        "rounded-full border px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] transition " +
        "disabled:cursor-not-allowed disabled:opacity-60 " +
        (provider.Status == "active"
            ? "border-rose-300 text-rose-700 hover:border-rose-400 dark:border-rose-900/70 dark:text-rose-300"
            : "border-emerald-300 text-emerald-700 hover:border-emerald-400 dark:border-emerald-900/70 dark:text-emerald-300");

    public void Dispose()
    {
        _providersRequestVersion++;
        _settingsRequestVersion++;
        _lifetime.Cancel();
        _providersRequest?.Cancel();
        _settingsRequest?.Cancel();
        _providersRequest?.Dispose();
        _settingsRequest?.Dispose();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
