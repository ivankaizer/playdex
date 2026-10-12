using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace Playdex.Core;

/// <summary>
/// The Full Screen Experience entry's MSIX (src/Launcher/Host): installed by the setup and by the app's Settings, removed
/// by the uninstaller. It is signed with the project's self-signed certificate, so Windows must trust that certificate
/// first (Trusted People, machine-wide: one admin prompt, skipped when it is trusted already). Developer Mode must be
/// on, because the gamingHome capability is only granted with the developer-mode SCCD. Windows PowerShell does the
/// package work (Add-AppxPackage), as no Appx API is reachable from a trimmed Native AOT app.
/// </summary>
public static class FsePackage
{
    public const string PackageName = "Playdex.Launcher";

    public static bool DeveloperModeOn()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
        return key?.GetValue("AllowDevelopmentWithoutDevLicense") is int value && value == 1;
    }

    public static async Task<bool> IsInstalledAsync()
    {
        var output = await RunPowerShellAsync($"(Get-AppxPackage -Name {PackageName}).Name", elevated: false);
        return output.Contains(PackageName, StringComparison.Ordinal);
    }

    /// <summary>Trust <paramref name="certificatePath"/> if needed (one admin prompt), then install or update the package for this user.</summary>
    public static async Task InstallAsync(string msixPath, string certificatePath)
    {
        if (!DeveloperModeOn())
        {
            throw new InvalidOperationException("Turn on Developer Mode first (Settings → System → For developers).");
        }
        var thumbprint = X509CertificateLoader.LoadCertificateFromFile(certificatePath).Thumbprint;
        var trusted = await RunPowerShellAsync($"Test-Path Cert:\\LocalMachine\\TrustedPeople\\{thumbprint}", elevated: false);
        if (!trusted.Contains("True", StringComparison.Ordinal))
        {
            Log.Info("fse: trusting the package certificate (admin prompt)");
            await RunPowerShellAsync($"Import-Certificate -FilePath {Quote(certificatePath)} -CertStoreLocation Cert:\\LocalMachine\\TrustedPeople | Out-Null", elevated: true);
        }
        await RunPowerShellAsync($"Add-AppxPackage -Path {Quote(msixPath)} -ForceApplicationShutdown", elevated: false);
        if (!await IsInstalledAsync())
        {
            throw new InvalidOperationException("Windows did not install the Full Screen Experience package.");
        }
        Log.Info($"fse: installed {Path.GetFileName(msixPath)}");
    }

    public static async Task RemoveAsync()
    {
        await RunPowerShellAsync($"Get-AppxPackage -Name {PackageName} | Remove-AppxPackage", elevated: false);
        Log.Info("fse: package removed");
    }

    private static string Quote(string path) => $"'{path.Replace("'", "''", StringComparison.Ordinal)}'";

    /// <summary>Windows PowerShell; elevated runs through the shell's "runas" (a UAC prompt) and can't capture output.</summary>
    private static async Task<string> RunPowerShellAsync(string command, bool elevated)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = elevated,
            Verb = elevated ? "runas" : "",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !elevated,
            RedirectStandardError = !elevated,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start.");
        var output = elevated ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync();
        var error = elevated ? Task.FromResult("") : process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var detail = (await error).Trim();
            throw new InvalidOperationException(detail.Length > 0 ? detail : $"PowerShell exited with {process.ExitCode}.");
        }
        return await output;
    }
}
