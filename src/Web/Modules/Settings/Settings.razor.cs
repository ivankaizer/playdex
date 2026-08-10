using System.Globalization;
using Microsoft.AspNetCore.Components;
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
    private readonly HashSet<long> _updatingProviderIds = [];
    private readonly HashSet<long> _savingCredentialIds = [];

    public IReadOnlyList<ProviderStatus> Providers { get; private set; } = [];
    public bool IsProvidersLoading { get; private set; }
    public string ProvidersError { get; private set; } = string.Empty;
    public string IgnoreShortSessionsMinutes { get; private set; } = "0";
    public bool IsSettingsLoading { get; private set; }
    public bool IsSettingsSaving { get; private set; }
    public string SettingsError { get; private set; } = string.Empty;
    public long? EditingCredentialId { get; private set; }
    public string EditingAccountId { get; private set; } = string.Empty;
    public string EditingApiKey { get; private set; } = string.Empty;
    public string CredentialError { get; private set; } = string.Empty;
    public string OpenXblApiKey { get; private set; } = string.Empty;
    public string OpenXblXuid { get; private set; } = string.Empty;
    public string OpenXblError { get; private set; } = string.Empty;
    public bool IsOpenXblConnecting { get; private set; }
    public bool HasXboxProvider => Providers.Any(provider => provider.Platform == "xbox");
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
        IsSettingsLoading = false;
    }

    public void FailSettingsLoad(string error)
    {
        IsSettingsLoading = false;
        SettingsError = error;
    }

    public void SetIgnoreShortSessionsMinutes(string value) =>
        IgnoreShortSessionsMinutes = value;

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

        submission = new SettingsSubmission(
            "/settings",
            HttpMethod.Put,
            new Dictionary<string, object?>
            {
                ["ignoreShortSessionsMinutes"] = (int)parsed,
            });
        return true;
    }

    public void BeginSettingsSave()
    {
        IsSettingsSaving = true;
        SettingsError = string.Empty;
    }

    public void CompleteSettingsSave(UserSettingsResponse response) =>
        IgnoreShortSessionsMinutes = response.IgnoreShortSessionsMinutes
            .ToString(CultureInfo.InvariantCulture);

    public void FailSettingsSave(string error) => SettingsError = error;

    public void EndSettingsSave() => IsSettingsSaving = false;

    public SettingsSubmission BeginProviderUpdate(ProviderStatus provider)
    {
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

    public void BeginCredentialEdit(ProviderStatus provider)
    {
        EditingCredentialId = provider.Id;
        EditingAccountId = provider.AccountId ?? string.Empty;
        EditingApiKey = provider.ApiKey ?? string.Empty;
        CredentialError = string.Empty;
    }

    public void CancelCredentialEdit()
    {
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
    }

    public void FailCredentialSave(string error) => CredentialError = error;

    public void EndCredentialSave(long providerId) => _savingCredentialIds.Remove(providerId);

    public void SetOpenXblApiKey(string value) => OpenXblApiKey = value;

    public void SetOpenXblXuid(string value) => OpenXblXuid = value;

    public bool TryBuildOpenXblSubmission(out SettingsSubmission? submission)
    {
        submission = null;
        OpenXblError = string.Empty;
        var apiKey = OpenXblApiKey.Trim();
        if (apiKey.Length == 0)
        {
            OpenXblError = "Enter an OpenXBL API key.";
            return false;
        }

        var xuid = OpenXblXuid.Trim();
        if (xuid.Any(character => !char.IsAsciiDigit(character)))
        {
            OpenXblError = "Xbox XUID must contain digits only.";
            return false;
        }

        submission = new SettingsSubmission(
            "/providers/openxbl",
            HttpMethod.Post,
            new Dictionary<string, object?>
            {
                ["apiKey"] = apiKey,
                ["xuid"] = xuid.Length == 0 ? null : xuid,
            });
        return true;
    }

    public void BeginOpenXblConnect()
    {
        IsOpenXblConnecting = true;
        OpenXblError = string.Empty;
    }

    public void CompleteOpenXblConnect(ProviderStatus provider)
    {
        Providers = Providers
            .Where(item => item.Platform != "xbox")
            .Append(provider)
            .ToArray();
        OpenXblApiKey = string.Empty;
        OpenXblXuid = string.Empty;
    }

    public void FailOpenXblConnect(string error) => OpenXblError = error;

    public void EndOpenXblConnect() => IsOpenXblConnecting = false;

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

    private void ChangeOpenXblApiKey(ChangeEventArgs eventArgs) =>
        _state.SetOpenXblApiKey(eventArgs.Value?.ToString() ?? string.Empty);

    private void ChangeOpenXblXuid(ChangeEventArgs eventArgs) =>
        _state.SetOpenXblXuid(eventArgs.Value?.ToString() ?? string.Empty);

    private async Task ConnectOpenXblAsync()
    {
        if (!_state.TryBuildOpenXblSubmission(out var submission)) return;

        _state.BeginOpenXblConnect();
        try
        {
            var response = await Api.SendAsync<OpenXblConnectResponse>(submission!.Path,
                submission.Method,
                submission.Body,
                cancellationToken: _lifetime.Token);
            _state.CompleteOpenXblConnect(response.Provider);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailOpenXblConnect(exception.Message);
        }
        finally
        {
            _state.EndOpenXblConnect();
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

    private static string ProviderStatusButtonClass(ProviderStatus provider) =>
        "rounded-full px-3 py-2 font-semibold uppercase tracking-[0.3em] disabled:cursor-not-allowed disabled:opacity-60 " +
        (provider.Status == "active"
            ? "bg-emerald-400 text-slate-900"
            : "border border-slate-300 dark:border-slate-700 text-slate-700 dark:text-slate-200");

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
