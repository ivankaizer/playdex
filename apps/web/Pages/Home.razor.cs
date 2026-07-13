using Microsoft.AspNetCore.Components;
using Playdex.Web.Api;
using Playdex.Web.Auth;
using Playdex.Web.Browser;
using Playdex.Web.Models;

namespace Playdex.Web.Pages;

public enum HomeLoginStatus
{
    Idle,
    Loading,
    Success,
}

public sealed class HomeState
{
    public HomeLoginStatus LoginStatus { get; private set; }
    public string Error { get; private set; } = string.Empty;
    public string SessionEmail { get; private set; } = string.Empty;
    public bool IsProfileLoading { get; private set; }
    public bool IsLoginDisabled => LoginStatus == HomeLoginStatus.Loading;
    public string ProfileLabel => SessionEmail.Length > 0
        ? SessionEmail
        : IsProfileLoading ? "Loading..." : "unknown";

    public void BeginLogin()
    {
        Error = string.Empty;
        LoginStatus = HomeLoginStatus.Loading;
    }

    public void CompleteLogin(LoginResponse response)
    {
        SessionEmail = response.Email;
        LoginStatus = HomeLoginStatus.Success;
    }

    public void FailLogin(string error)
    {
        Error = error;
        LoginStatus = HomeLoginStatus.Idle;
    }

    public bool BeginProfile(bool isLoggedIn, string? cachedEmail)
    {
        SessionEmail = isLoggedIn ? cachedEmail ?? string.Empty : string.Empty;
        IsProfileLoading = isLoggedIn && SessionEmail.Length == 0;
        return IsProfileLoading;
    }

    public void CompleteProfile(MeResponse response)
    {
        SessionEmail = response.Email;
        IsProfileLoading = false;
    }

    public void FailProfile() => IsProfileLoading = false;
}

public partial class Home : ComponentBase, IDisposable
{
    private const string ThemeKey = "playdex.theme";
    private static readonly IReadOnlyList<string> PreviewPlatformColors =
        ["#34d399", "#38bdf8", "#f59e0b", "#a78bfa"];
    private static readonly IReadOnlyList<(string Label, string Value)> PreviewRecapSummary =
    [
        ("Hours played", "124.8h"),
        ("Sessions", "93"),
        ("Top title", "Hades II"),
    ];
    private static readonly IReadOnlyList<(string Label, double Value, string Hours)> PreviewRecapPlatforms =
    [
        ("Steam", 52, "64.9h"),
        ("PlayStation", 30, "37.4h"),
        ("Xbox", 12, "14.9h"),
        ("Switch", 6, "7.6h"),
    ];
    private static readonly IReadOnlyList<(string GameId, string Title, string Hours)> PreviewRecapTopGames =
    [
        ("hades-2", "Hades II", "26.4h"),
        ("bg3", "Baldur's Gate 3", "18.2h"),
        ("re4", "Resident Evil 4", "14.7h"),
        ("helldivers-2", "Helldivers 2", "12.6h"),
        ("fh5", "Forza Horizon 5", "10.8h"),
    ];
    private static readonly IReadOnlyList<PreviewCard> PreviewRecapCards =
    [
        new("hades-2", "Hades II", "26.4h", "bg-gradient-to-br from-rose-400/60 via-fuchsia-500/40 to-slate-950/70"),
        new("bg3", "Baldur's Gate 3", "18.2h", "bg-gradient-to-br from-amber-300/60 via-orange-500/40 to-slate-950/70"),
        new("re4", "Resident Evil 4", "14.7h", "bg-gradient-to-br from-emerald-300/60 via-teal-500/40 to-slate-950/70"),
        new("helldivers-2", "Helldivers 2", "12.6h", "bg-gradient-to-br from-sky-300/60 via-cyan-500/40 to-slate-950/70"),
        new("fh5", "Forza Horizon 5", "10.8h", "bg-gradient-to-br from-violet-300/60 via-indigo-500/40 to-slate-950/70"),
    ];

    private readonly CancellationTokenSource _lifetime = new();
    private readonly HomeState _state = new();
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _theme = "light";
    private bool _isLoggedIn;

    [Inject]
    private PlaydexApiClient Api { get; set; } = null!;

    [Inject]
    private AuthSession AuthSession { get; set; } = null!;

    [Inject]
    private BrowserInterop Browser { get; set; } = null!;

    [Inject]
    private IBrowserStorage Storage { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var savedTheme = await Storage.GetAsync(ThemeKey);
        _theme = savedTheme is "light" or "dark"
            ? savedTheme
            : await Browser.PrefersDarkAsync() ? "dark" : "light";
        await ApplyThemeAsync();

        _isLoggedIn = await AuthSession.IsLoggedInAsync();
        var cachedEmail = await AuthSession.GetEmailAsync();
        if (!_state.BeginProfile(_isLoggedIn, cachedEmail)) return;

        try
        {
            var profile = await Api.SendAsync<MeResponse>("/me", cancellationToken: _lifetime.Token);
            await AuthSession.StoreProfileAsync(profile);
            _state.CompleteProfile(profile);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            _state.FailProfile();
        }
    }

    private async Task HandleSubmitAsync()
    {
        _state.BeginLogin();
        try
        {
            var response = await Api.SendAsync<LoginResponse>("/login",
                HttpMethod.Post,
                new LoginRequest(_email, _password),
                authenticated: false,
                cancellationToken: _lifetime.Token);
            await AuthSession.StoreLoginAsync(response);
            _state.CompleteLogin(response);
            Navigation.NavigateTo("/dashboard", forceLoad: true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _state.FailLogin(exception.Message);
        }
    }

    private async Task ToggleThemeAsync()
    {
        _theme = _theme == "dark" ? "light" : "dark";
        await ApplyThemeAsync();
    }

    private async Task ApplyThemeAsync()
    {
        await Browser.ApplyThemeAsync(_theme);
        await Storage.SetAsync(ThemeKey, _theme);
    }

    private async Task LogoutAsync()
    {
        await AuthSession.LogoutAsync();
        Navigation.NavigateTo("/", forceLoad: true);
    }

    private void OpenDashboard() => Navigation.NavigateTo("/dashboard", forceLoad: true);

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed record PreviewCard(string GameId, string Title, string Hours, string ArtClass);
}
