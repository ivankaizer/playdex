using Microsoft.Win32;

namespace Playdex.Core.Tests;

public sealed class SetupPayloadTests : IDisposable
{
    private readonly string _dir = TestPaths.NewTempDir();

    [Fact]
    public void BundlesTheAppAndThePackageAfterTheExeAndExtractsThem()
    {
        var setup = Exe("setup.exe", 70_000);
        var app = Directory.CreateDirectory(Path.Combine(_dir, "app")).FullName;
        File.WriteAllText(Path.Combine(app, AppUpdate.AppExecutable), "app");
        Directory.CreateDirectory(Path.Combine(app, "ui", "assets"));
        File.WriteAllText(Path.Combine(app, "ui", "assets", "index.js"), "js");
        var msix = Path.Combine(_dir, "playdex-launcher-v2.0.0.msix");
        File.WriteAllText(msix, "package");
        var bundled = Path.Combine(_dir, "Playdex-Setup.exe");

        SetupPayload.Bundle(setup, app, [msix], bundled);

        var (exeLength, payloadLength) = SetupPayload.Measure(bundled);
        Assert.Equal(70_000, exeLength);
        Assert.True(payloadLength > 0);
        var extracted = Path.Combine(_dir, "extracted");
        Assert.True(SetupPayload.TryExtract(bundled, extracted));
        Assert.Equal("app", File.ReadAllText(Path.Combine(extracted, SetupPayload.AppFolder, AppUpdate.AppExecutable)));
        Assert.Equal("js", File.ReadAllText(Path.Combine(extracted, SetupPayload.AppFolder, "ui", "assets", "index.js")));
        Assert.Equal("package", File.ReadAllText(Path.Combine(extracted, SetupPayload.FseFolder, "playdex-launcher-v2.0.0.msix")));
        Assert.False(File.Exists(Path.Combine(extracted, "payload.zip")));

        // The uninstaller kept with the app is the exe alone, and bundling again replaces the payload.
        var copy = Path.Combine(_dir, "copy.exe");
        SetupPayload.CopyExecutable(bundled, copy);
        Assert.Equal(File.ReadAllBytes(setup), File.ReadAllBytes(copy));
        var again = Path.Combine(_dir, "again.exe");
        SetupPayload.Bundle(bundled, app, [], again);
        Assert.Equal(70_000, SetupPayload.Measure(again).ExecutableLength);
    }

    [Fact]
    public void AnExeWithoutAPayloadIsTheOnlineSetup()
    {
        var setup = Exe("online.exe", 1_000);

        Assert.Equal((1_000L, 0L), SetupPayload.Measure(setup));
        Assert.False(SetupPayload.TryExtract(setup, Path.Combine(_dir, "nothing")));
        Assert.Equal((5L, 0L), SetupPayload.Measure(WriteBytes("tiny.exe", [1, 2, 3, 4, 5])));
    }

    [Fact]
    public void TheInstalledAppsEntryPointsAtTheInstallAndFollowsItsVersion()
    {
        var key = $@"Software\Playdex.Tests\Uninstall-{Guid.NewGuid():N}";
        var install = Directory.CreateDirectory(Path.Combine(_dir, "Programs", "Playdex")).FullName;
        File.WriteAllBytes(Path.Combine(install, AppUpdate.AppExecutable), new byte[4096]);
        try
        {
            InstallRegistration.Register(install, "2.0.0", key);

            using (var entry = Registry.CurrentUser.OpenSubKey(key)!)
            {
                Assert.Equal("Playdex", entry.GetValue("DisplayName"));
                Assert.Equal("2.0.0", entry.GetValue("DisplayVersion"));
                Assert.Equal($"\"{Path.Combine(install, InstallRegistration.SetupExecutable)}\" --uninstall", entry.GetValue("UninstallString"));
                Assert.Equal(4, entry.GetValue("EstimatedSize"));
            }
            Assert.Equal(install, InstallRegistration.InstalledLocation(key));

            InstallRegistration.Refresh(Path.Combine(_dir, "elsewhere"), "9.9.9", key); // another install's entry stays
            InstallRegistration.Refresh(install + "\\", "2.1.0", key);
            using (var entry = Registry.CurrentUser.OpenSubKey(key)!)
            {
                Assert.Equal("2.1.0", entry.GetValue("DisplayVersion"));
                Assert.Equal(install, entry.GetValue("InstallLocation"));
            }
            Assert.True(InstallRegistration.IsRegistered(install + "\\", key));

            InstallRegistration.Unregister(key);
            Assert.Null(InstallRegistration.InstalledLocation(key));
            InstallRegistration.Refresh(install, "2.2.0", key); // no uninstaller in the folder: nothing to register
            Assert.Null(Registry.CurrentUser.OpenSubKey(key));
            File.WriteAllText(Path.Combine(install, InstallRegistration.SetupExecutable), "setup");
            InstallRegistration.Refresh(install, "2.2.0", key); // an install from before the entry gets one
            Assert.Equal(install, InstallRegistration.InstalledLocation(key));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Playdex.Tests", throwOnMissingSubKey: false);
        }
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Exe(string name, int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        return WriteBytes(name, bytes);
    }

    private string WriteBytes(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
