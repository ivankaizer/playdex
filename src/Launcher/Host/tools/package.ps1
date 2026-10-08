<#
  Builds the signed MSIX for local FSE testing.

  1. Builds the UI (bun run build in src/Launcher/ui).
  2. Creates a self-signed code-signing certificate as files in %LOCALAPPDATA%\PlaydexLauncher\dev-cert (first run
     only). Nothing is put in a certificate store here; install-dev.ps1 does that, elevated.
  3. Publishes the host (Native AOT) and packages + signs the MSIX into src/Launcher/Host/AppPackages.

  Never commit the .pfx or its password. The subject must match Publisher in Package.appxmanifest.
#>
$ErrorActionPreference = 'Stop'
$hostDir = Resolve-Path "$PSScriptRoot\.."
$certDir = Join-Path $env:LOCALAPPDATA 'PlaydexLauncher\dev-cert'
$pfx = Join-Path $certDir 'playdex-launcher-dev.pfx'
$passwordFile = Join-Path $certDir 'password.txt'
$subject = 'CN=Playdex Launcher Dev'

Push-Location "$hostDir\..\ui"
try { bun install --frozen-lockfile; bun run build; if ($LASTEXITCODE) { throw 'UI build failed' } } finally { Pop-Location }

if (-not (Test-Path $pfx)) {
    New-Item -ItemType Directory -Force $certDir | Out-Null
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        $subject, $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $req.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $req.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
    $eku = [System.Security.Cryptography.OidCollection]::new()
    [void]$eku.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3')) # code signing
    $req.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($eku, $false))
    $cert = $req.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1), [DateTimeOffset]::Now.AddYears(2))
    $password = [Guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllBytes($pfx, $cert.Export('Pfx', $password))
    [IO.File]::WriteAllBytes((Join-Path $certDir 'playdex-launcher-dev.cer'), $cert.Export('Cert'))
    Set-Content $passwordFile $password -NoNewline
    Write-Host "Created dev certificate $($cert.Thumbprint) in $certDir"
}

# Native AOT needs vswhere on PATH to find the MSVC linker.
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
Push-Location $hostDir
try {
    if (Test-Path AppPackages) { Remove-Item -Recurse -Force AppPackages }
    dotnet publish -c Release -r win-x64 -p:PackageMsix=true `
        -p:PackageCertificateKeyFile="$pfx" -p:PackageCertificatePassword="$(Get-Content $passwordFile)"
    if ($LASTEXITCODE) { throw 'Packaging failed' }
} finally { Pop-Location }
Get-ChildItem "$hostDir\AppPackages" -Recurse -Filter *.msix | ForEach-Object { Write-Host "Package: $($_.FullName)" }
