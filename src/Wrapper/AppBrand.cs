namespace Wrapper;

internal static class AppBrand
{
    public const string Name = "Playdex";
    public const string ProductName = Name + " Wrapper";
    public const string StorageDirectoryName = Name + "Wrapper";
    public const string WindowAccessibleName = ProductName + " game launcher window";
    public const string ApiSettingsAccessibleName = ProductName + " API settings";
    public const string LauncherPrefix = Name + "-";
    public const string LauncherManifestExtension = ".playdex.json";
    public const string PublishedWrapperEnvironmentVariable = "PLAYDEX_PUBLISHED_WRAPPER";
    public const string GitHubLatestReleaseUrl = "https://api.github.com/repos/ivankaizer/playdex/releases/latest";
    public const string ReleaseAssetPrefix = "playdex-wrapper-windows-";

    public static string DefaultStorageDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        StorageDirectoryName);

    // Defaults for a new install; an existing install keeps the folders its config names (see ConfigStore).
    public static string DefaultStateDirectory => Paths.DefaultStateDirectory;
    public static string DefaultLogDirectory => Paths.DefaultLogDirectory;
    public static string DefaultLauncherDirectory => Paths.DefaultLauncherDirectory;
    public static string DefaultUpdateDirectory { get; } = Path.Combine(DefaultStorageDirectory, "updates");

    public static Icon LoadApplicationIcon()
    {
        try
        {
            using var extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (extracted is not null)
            {
                return (Icon)extracted.Clone();
            }
        }
        catch
        {
            // A generic icon is preferable to preventing the tray application from opening.
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
