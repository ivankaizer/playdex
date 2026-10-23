using System.Diagnostics;
using System.Security.Cryptography;
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
/// <remarks>
/// The .cer and the .msix come from the same place (the release feed, or the setup's payload), so neither proves
/// anything about the other. Only the certificate pinned in <see cref="SigningCertificateSha256"/> is ever trusted, and
/// only a package Windows reports as signed with it is installed.
/// </remarks>
public static class FsePackage
{
    public const string PackageName = "Playdex.Launcher";

    /// <summary>
    /// SHA-256 thumbprint of the certificate the release workflow signs the package with (the LAUNCHER_SIGNING_PFX
    /// secret: CN=Playdex Launcher Dev, valid until 2028-10-08), as published in playdex-launcher.cer from v2.1.0 to
    /// v2.1.4. Rotating the signing certificate needs a new app release with the new thumbprint here: apps and setups
    /// refuse a package signed with any other certificate. Each installs the package of its own release, so change this
    /// and the secret together; the release workflow fails while they differ (docs/windows-releases.md).
    /// </summary>
    public const string SigningCertificateSha256 = "6B2A3A13143345EE25196CEDFBAEC0F796D8942177B2124F4BCD626E99979E07";

    internal const long MaximumCertificateBytes = 64 * 1024;

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

    /// <summary>
    /// Check that <paramref name="certificatePath"/> is the pinned signing certificate and that the package is signed with
    /// it, trust it if needed (one admin prompt), then install or update the package for this user.
    /// </summary>
    public static async Task InstallAsync(string msixPath, string certificatePath)
    {
        if (!DeveloperModeOn())
        {
            throw new InvalidOperationException("Turn on Developer Mode first (Settings → System → For developers).");
        }
        using var certificate = LoadSigningCertificate(certificatePath);
        CheckPackageSignature(await RunPowerShellAsync(
            $"$s = Get-AuthenticodeSignature -LiteralPath {Quote(msixPath)}; [string]$s.Status; "
            + "if ($s.SignerCertificate) { [Convert]::ToBase64String($s.SignerCertificate.RawData) }",
            elevated: false));
        var trusted = await RunPowerShellAsync($"Test-Path Cert:\\LocalMachine\\TrustedPeople\\{certificate.Thumbprint}", elevated: false);
        if (!trusted.Contains("True", StringComparison.Ordinal))
        {
            Log.Info("fse: trusting the package certificate (admin prompt)");
            await RunPowerShellAsync(TrustCommand(certificate), elevated: true);
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

    /// <summary>Load the package's certificate, refusing anything but the pinned signing certificate.</summary>
    internal static X509Certificate2 LoadSigningCertificate(string certificatePath)
    {
        var info = new FileInfo(certificatePath);
        if (!info.Exists || info.Length > MaximumCertificateBytes)
        {
            throw new InvalidDataException("The Full Screen Experience certificate is missing or too large.");
        }
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("The Full Screen Experience certificate could not be read.", ex);
        }
        if (!IsSigningCertificate(certificate))
        {
            certificate.Dispose();
            throw new InvalidDataException(
                "The Full Screen Experience certificate is not Playdex's launcher signing certificate, so it was not trusted.");
        }
        return certificate;
    }

    internal static bool IsSigningCertificate(X509Certificate2 certificate) =>
        string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), SigningCertificateSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Check what Get-AuthenticodeSignature says about the package: its status on the first line, then its signer
    /// certificate as base64. The signer must be the pinned certificate and the signature must match the package. The
    /// status need not be Valid: until the certificate is trusted, Windows reports its self-signed chain as untrusted.
    /// </summary>
    internal static void CheckPackageSignature(string authenticode)
    {
        var lines = authenticode.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var status = lines.Length > 0 ? lines[0] : "";
        if (lines.Length < 2 || status is "NotSigned" or "HashMismatch" or "NotSupportedFileFormat" or "Incompatible")
        {
            throw new InvalidDataException($"The Full Screen Experience package is not properly signed ({status}).");
        }
        X509Certificate2 signer;
        try
        {
            // Base64 has no line breaks of its own; join any the console wrapped in.
            signer = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(string.Concat(lines[1..])));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidDataException("The Full Screen Experience package's signer could not be read.", ex);
        }
        using (signer)
        {
            if (!IsSigningCertificate(signer))
            {
                throw new InvalidDataException("The Full Screen Experience package is not signed with Playdex's launcher signing certificate.");
            }
        }
    }

    /// <summary>
    /// Elevated: add exactly the checked certificate's bytes to Trusted People, not the file it came from, which could
    /// be replaced between the check and the admin prompt.
    /// </summary>
    internal static string TrustCommand(X509Certificate2 certificate) =>
        "$ErrorActionPreference = 'Stop'; "
        + "$store = [System.Security.Cryptography.X509Certificates.X509Store]::new('TrustedPeople', 'LocalMachine'); "
        + "$store.Open('ReadWrite'); "
        + "$store.Add([System.Security.Cryptography.X509Certificates.X509Certificate2]::new("
        + $"[Convert]::FromBase64String('{Convert.ToBase64String(certificate.RawData)}'))); "
        + "$store.Close()";

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
