using System.Globalization;
using Microsoft.AspNetCore.Components;
using Playdex.Web.Shared.Http;
using Playdex.Web.Modules.Settings;
using Playdex.Web.Shared.Common;

namespace Playdex.Web.Modules.Settings;

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

    public IReadOnlyList<ProviderStatus> Providers { get; private set; } = [];
    public bool IsProvidersLoading { get; private set; }
    public string ProvidersError { get; private set; } = string.Empty;
    public string IgnoreShortSessionsMinutes { get; private set; } = "0";
    public bool IsSettingsLoading { get; private set; }
    public bool IsSettingsSaving { get; private set; }
    public string SettingsError { get; private set; } = string.Empty;
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

    public static SettingsProviderPresentation Present(ProviderStatus provider)
    {
        var (label, color, accent) = provider.Platform switch
        {
            "steam" => ("S", "bg-slate-200 dark:bg-slate-800", "text-slate-900 dark:text-slate-100"),
            "psn" => ("P", "bg-indigo-500/30", "text-indigo-700 dark:text-indigo-200"),
            "playnite" => ("PL", "bg-amber-500/30", "text-amber-700 dark:text-amber-200"),
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
    }
}
