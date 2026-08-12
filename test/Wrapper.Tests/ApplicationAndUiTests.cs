using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Wrapper;
using Wrapper.UI;

namespace Wrapper.Tests;

public sealed class ApplicationAndUiTests
{
    [Fact]
    public Task TestProgramEntryPoint()
    {
        var main = typeof(Wrapper.Program).GetMethod(
            nameof(Wrapper.Program.Main),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Program.Main was not found.");

        Assert.Equal(typeof(int), main.ReturnType);
        Assert.Empty(main.GetParameters());
        Assert.True(main.GetCustomAttribute<STAThreadAttribute>() is not null);
        var runConfiguredGame = typeof(Wrapper.Program).GetMethod(
            "RunConfiguredGame",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Program.RunConfiguredGame was not found.");
        Assert.Equal(typeof(void), runConfiguredGame.ReturnType);
        Assert.DoesNotContain(
            runConfiguredGame.GetParameters(),
            parameter => parameter.ParameterType == typeof(ConfigStore));
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestImplementationTypesAreInternal()
    {
        Assert.Empty(typeof(Wrapper.Program).Assembly.ExportedTypes);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestApplicationBranding()
    {
        var assembly = typeof(AppBrand).Assembly;
        Assert.Equal(AppBrand.Name + " Wrapper", AppBrand.ProductName);
        Assert.Equal(AppBrand.ProductName + " game launcher window", AppBrand.WindowAccessibleName);
        Assert.True(!string.IsNullOrWhiteSpace(AppBrand.Name));
        Assert.True(assembly.GetCustomAttribute<AssemblyTitleAttribute>() is not null);
        Assert.True(assembly.GetCustomAttribute<AssemblyProductAttribute>() is not null);

        using var icon = AppBrand.LoadApplicationIcon();
        Assert.True(icon.Width > 0 && icon.Height > 0);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestBrandNameHasSingleSource()
    {
        var root = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(root, "src", "Wrapper"),
            Path.Combine(root, "test", "Wrapper.Tests")
        };
        var violations = sourceRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".cs" or ".csproj")
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            .Where(path => !string.Equals(Path.GetFileName(path), nameof(AppBrand) + ".cs", StringComparison.Ordinal))
            .Where(path => !string.Equals(Path.GetFileName(path), "Wrapper.csproj", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(AppBrand.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(violations);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestTrayThemeSelection()
    {
        Assert.True(TrayTheme.ShouldUseDarkTheme(0));
        Assert.True(!TrayTheme.ShouldUseDarkTheme(1));
        Assert.True(!TrayTheme.ShouldUseDarkTheme(null));
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestTrayGamePresentation()
    {
        var presentation = TrayGamePresentation.From(new GameConfig
        {
            GameId = "cronos",
            DisplayName = "Cronos: The New Dawn",
            ApiGameId = "1850570",
            GameProvider = "steam"
        });

        Assert.Equal("Cronos: The New Dawn", presentation.Title);
        Assert.Equal("Steam - 1850570", presentation.Subtitle);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestTrayLayoutMetrics()
    {
        var expected = new[]
        {
            (Dpi: 96, Width: 340, RowHeight: 56, RowStride: 60),
            (Dpi: 120, Width: 425, RowHeight: 70, RowStride: 75),
            (Dpi: 144, Width: 510, RowHeight: 84, RowStride: 90),
            (Dpi: 192, Width: 680, RowHeight: 112, RowStride: 120)
        };

        foreach (var item in expected)
        {
            var metrics = TrayLayoutMetrics.ForDpi(item.Dpi);
            Assert.Equal(item.Dpi, metrics.Dpi);
            Assert.Equal(item.Width, metrics.PopupWidth);
            Assert.Equal(item.RowHeight, metrics.RowHeight);
            Assert.Equal(item.RowStride, metrics.RowStride);
            Assert.Equal(metrics.Scale(122), metrics.ActionAreaHeight);
            Assert.Equal(metrics.Scale(32), metrics.PrimaryButtonHeight);
            Assert.Equal(metrics.Scale(36), metrics.ManagementButtonHeight);
            Assert.Equal(metrics.Scale(26), metrics.ExitButtonHeight);
            Assert.Equal(metrics.Scale(30), metrics.OverflowButtonSize);
            Assert.Equal(metrics.Scale(6), metrics.ScrollbarWidth);
            Assert.Equal(metrics.Scale(2), metrics.ScrollbarMargin);
            Assert.Equal(metrics.Scale(24), metrics.ScrollbarMinimumThumbHeight);
            Assert.Equal(5, TrayLayoutMetrics.MaxVisibleGameRows);
            Assert.Equal(metrics.RowStride * 5, metrics.GamePanelMaxHeight);
            Assert.True(metrics.GamePanelMinHeight >= metrics.RowHeight);
            Assert.True(metrics.GamePanelMaxHeight >= metrics.GamePanelMinHeight);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task TestApiSettingsDialogMetrics()
    {
        Assert.Equal("https://steamdb.info/", GameSettingsDialog.SteamDbUrl);
        var expected = new[]
        {
            (Dpi: 96, Client: new Size(420, 364), Input: new Size(388, 27), Button: new Size(68, 28)),
            (Dpi: 120, Client: new Size(525, 455), Input: new Size(485, 34), Button: new Size(85, 35)),
            (Dpi: 144, Client: new Size(630, 546), Input: new Size(582, 41), Button: new Size(102, 42)),
            (Dpi: 192, Client: new Size(840, 728), Input: new Size(776, 54), Button: new Size(136, 56))
        };

        foreach (var item in expected)
        {
            var metrics = ApiSettingsDialogMetrics.ForDpi(item.Dpi);
            Assert.Equal(item.Dpi, metrics.Dpi);
            Assert.Equal(item.Client, metrics.ClientSize);
            Assert.Equal(item.Input, metrics.InputSize);
            Assert.Equal(item.Button, metrics.ButtonSize);
        }

        Assert.Equal(96, ApiSettingsDialogMetrics.ForDpi(72).Dpi);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestTrayMenuMetrics()
    {
        var expected = new[]
        {
            (Dpi: 96, OuterPadding: 4, ItemHeight: 24, SeparatorHeight: 6, CornerRadius: 7),
            (Dpi: 144, OuterPadding: 6, ItemHeight: 36, SeparatorHeight: 9, CornerRadius: 11),
            (Dpi: 192, OuterPadding: 8, ItemHeight: 48, SeparatorHeight: 12, CornerRadius: 14)
        };

        foreach (var item in expected)
        {
            var metrics = TrayMenuMetrics.ForDpi(item.Dpi);
            Assert.Equal(item.Dpi, metrics.Dpi);
            Assert.Equal(item.OuterPadding, metrics.OuterPadding);
            Assert.Equal(item.ItemHeight, metrics.ItemHeight);
            Assert.Equal(item.SeparatorHeight, metrics.SeparatorHeight);
            Assert.Equal(item.CornerRadius, metrics.CornerRadius);
            Assert.True(metrics.HorizontalPadding > metrics.OuterPadding);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task TestTrayWindowLifecycle()
    {
        Assert.True(TrayWindowLifecycle.ShouldHideOnClose(CloseReason.UserClosing));
        Assert.True(!TrayWindowLifecycle.ShouldHideOnClose(CloseReason.ApplicationExitCall));
        Assert.True(!TrayWindowLifecycle.ShouldHideOnClose(CloseReason.TaskManagerClosing));
        Assert.Equal(FormWindowState.Normal, TrayWindowLifecycle.RestoredState(FormWindowState.Minimized));
        Assert.Equal(FormWindowState.Normal, TrayWindowLifecycle.RestoredState(FormWindowState.Normal));
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestBackupStatusPresentation()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(2));
        var game = new GameConfig { Backup = new BackupConfig { DailyEnabled = true } };
        Assert.Equal(BackupIndicatorKind.Due, BackupStatusPresentation.From(game, null, now).Kind);

        var success = new GameBackupState
        {
            Result = BackupResultKind.Success,
            LastAttemptAt = now,
            LastSuccessAt = now
        };
        Assert.Equal(BackupIndicatorKind.Success, BackupStatusPresentation.From(game, success, now).Kind);

        success.LastSuccessAt = now.AddDays(-1);
        success.LastAttemptAt = now.AddDays(-1);
        Assert.Equal(BackupIndicatorKind.Due, BackupStatusPresentation.From(game, success, now).Kind);

        var failed = new GameBackupState { Result = BackupResultKind.Failed, LastAttemptAt = now, LastError = "cloud conflict" };
        var presentation = BackupStatusPresentation.From(game, failed, now);
        Assert.Equal(BackupIndicatorKind.Failed, presentation.Kind);
        Assert.Contains("cloud conflict", presentation.Tooltip);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestStartupRouting()
    {
        Assert.Null(LauncherRouting.Resolve(null));
        Assert.Null(LauncherRouting.Resolve("C:\\Tools\\Wrapper.exe"));
        return Task.CompletedTask;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Wrapper")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
