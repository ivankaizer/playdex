using Microsoft.Win32;

namespace Wrapper.UI;

internal sealed record TrayPalette(Color Background, Color Surface, Color Hover, Color Border, Color Text, Color MutedText, Color Accent, Color AccentHover, Color Danger);

internal static class TrayTheme
{
    public static readonly TrayPalette Light = new(
        Color.FromArgb(250, 250, 250), Color.White, Color.FromArgb(242, 242, 242), Color.FromArgb(224, 224, 224),
        Color.FromArgb(30, 30, 30), Color.FromArgb(105, 105, 105), Color.FromArgb(0, 95, 184), Color.FromArgb(0, 80, 158), Color.FromArgb(196, 43, 28));
    public static readonly TrayPalette Dark = new(
        Color.FromArgb(32, 32, 32), Color.FromArgb(43, 43, 43), Color.FromArgb(54, 54, 54), Color.FromArgb(65, 65, 65),
        Color.FromArgb(245, 245, 245), Color.FromArgb(170, 170, 170), Color.FromArgb(96, 205, 255), Color.FromArgb(75, 181, 232), Color.FromArgb(255, 153, 164));

    public static bool ShouldUseDarkTheme(int? appsUseLightTheme) => appsUseLightTheme == 0;

    public static TrayPalette Current()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return ShouldUseDarkTheme(key?.GetValue("AppsUseLightTheme") as int?) ? Dark : Light;
        }
        catch
        {
            return Light;
        }
    }
}

internal sealed record TrayGamePresentation(string Title, string Subtitle)
{
    public static TrayGamePresentation From(GameConfig game)
    {
        var title = string.IsNullOrWhiteSpace(game.DisplayName) ? game.GameId : game.DisplayName;
        var provider = string.IsNullOrWhiteSpace(game.GameProvider) ? "Game" : Capitalize(game.GameProvider);
        var identity = string.IsNullOrWhiteSpace(game.ApiGameId) ? game.GameId : game.ApiGameId;
        return new TrayGamePresentation(title, $"{provider} - {identity}");
    }

    private static string Capitalize(string value) => value.Length == 1
        ? value.ToUpperInvariant()
        : char.ToUpperInvariant(value[0]) + value[1..];
}

internal enum BackupIndicatorKind
{
    Disabled,
    Due,
    Running,
    Success,
    Unsupported,
    Failed
}

internal sealed record BackupStatusPresentation(BackupIndicatorKind Kind, string Glyph, string Tooltip)
{
    public static BackupStatusPresentation From(GameConfig game, GameBackupState? state, DateTimeOffset now)
    {
        if (state?.Result == BackupResultKind.Running)
            return new(BackupIndicatorKind.Running, "●", "Backup is running.");

        if (state?.Result == BackupResultKind.Failed)
            return new(BackupIndicatorKind.Failed, "!", Format("Last backup failed", state.LastAttemptAt, state.LastError));

        if (state?.Result == BackupResultKind.Unsupported)
            return new(BackupIndicatorKind.Unsupported, "?", Format("Game is not supported by Ludusavi", state.LastAttemptAt, state.LastError));

        if (game.Backup.DailyEnabled && BackupCoordinator.IsDue(state, now))
            return new(BackupIndicatorKind.Due, "●", "Daily backup is due.");

        if (state?.LastSuccessAt is not null)
            return new(BackupIndicatorKind.Success, "✓", $"Last backup succeeded {state.LastSuccessAt.Value.ToLocalTime():g}.");

        return new(BackupIndicatorKind.Disabled, "○", "No backup has been recorded. Daily backup is disabled.");
    }

    private static string Format(string text, DateTimeOffset? timestamp, string? error)
    {
        var when = timestamp is null ? "" : $" {timestamp.Value.ToLocalTime():g}.";
        var detail = string.IsNullOrWhiteSpace(error) ? "" : $" {error}";
        return $"{text}{when}{detail}".Trim();
    }
}

internal static class TrayWindowLifecycle
{
    public static bool ShouldHideOnClose(CloseReason reason) => reason == CloseReason.UserClosing;

    public static FormWindowState RestoredState(FormWindowState state) =>
        state == FormWindowState.Minimized ? FormWindowState.Normal : state;
}

internal sealed class TrayLayoutMetrics
{
    public int Dpi { get; }
    public int PopupWidth => Scale(340);
    public int RowStride => Scale(60);
    public int RowHeight => Scale(56);
    public int GamePanelMinHeight => Scale(72);
    public const int MaxVisibleGameRows = 5;
    public int GamePanelMaxHeight => RowStride * MaxVisibleGameRows;
    public int ActionAreaHeight => Scale(122);
    public int PrimaryButtonHeight => Scale(32);
    public int ManagementButtonHeight => Scale(36);
    public int ExitButtonHeight => Scale(26);
    public int OverflowButtonSize => Scale(30);
    public int ScrollbarWidth => Scale(6);
    public int ScrollbarMargin => Scale(2);
    public int ScrollbarMinimumThumbHeight => Scale(24);

    private TrayLayoutMetrics(int dpi) => Dpi = Math.Max(96, dpi);

    public static TrayLayoutMetrics ForDpi(int dpi) => new(dpi);

    public int Scale(int logicalPixels) =>
        (int)Math.Round(logicalPixels * Dpi / 96d, MidpointRounding.AwayFromZero);
}

internal sealed class ApiSettingsDialogMetrics
{
    public int Dpi { get; }
    public Size ClientSize => new(Scale(420), Scale(428));
    public Size InputSize => new(Scale(388), Scale(27));
    public Size ButtonSize => new(Scale(68), Scale(28));

    private ApiSettingsDialogMetrics(int dpi) => Dpi = Math.Max(96, dpi);

    public static ApiSettingsDialogMetrics ForDpi(int dpi) => new(dpi);

    public int Scale(int logicalPixels) =>
        (int)Math.Round(logicalPixels * Dpi / 96d, MidpointRounding.AwayFromZero);
}

internal sealed class TrayMenuMetrics
{
    public int Dpi { get; }
    public int OuterPadding => Scale(4);
    public int HorizontalPadding => Scale(10);
    public int ItemHeight => Scale(24);
    public int SeparatorHeight => Scale(6);
    public int CornerRadius => Scale(7);
    public int ItemCornerRadius => Scale(5);

    private TrayMenuMetrics(int dpi) => Dpi = Math.Max(96, dpi);

    public static TrayMenuMetrics ForDpi(int dpi) => new(dpi);

    public int Scale(int logicalPixels) =>
        (int)Math.Round(logicalPixels * Dpi / 96d, MidpointRounding.AwayFromZero);
}

internal static class TrayDpi
{
    public static int GetAtPoint(Point point)
    {
        try
        {
            var monitor = MonitorFromPoint(point, 2);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return checked((int)dpiX);
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }

        return 96;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}

internal sealed record TrayWindowActions(
    Func<string, Task> LaunchGame,
    Func<string, Task> BackupGame,
    Func<string, Task> EditGameSettings,
    Func<string, Task> AddGameToSteam,
    Func<string, Task> RemoveGame,
    Func<Task> AddGame,
    Func<Task> OpenSettings,
    Func<Task> OpenLauncherFolder,
    Func<Task> SynchronizeLaunchers,
    Func<Task> Update,
    Func<Task> Exit);
