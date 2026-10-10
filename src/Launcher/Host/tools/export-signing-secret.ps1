<#
  Prepares the GitHub secrets the release workflow signs the launcher MSIX with (.github/workflows/release-windows.yaml):
    LAUNCHER_SIGNING_PFX       the code-signing certificate as base64
    LAUNCHER_SIGNING_PASSWORD  its password

  It uses the dev certificate package.ps1 creates (%LOCALAPPDATA%\PlaydexLauncher\dev-cert; it is created here if
  missing). Its subject, CN=Playdex Launcher Dev, is the Publisher in Package.appxmanifest, so released packages keep
  the package family (Playdex.Launcher_bmt4bex73v8a8) of the ones installed from local builds, and this PC already
  trusts it.

  Nothing is uploaded: it writes the base64 to a file and prints the two `gh secret set` commands to run yourself.
  Never commit the .pfx, the password or the base64 file.
#>
$ErrorActionPreference = 'Stop'
$certDir = Join-Path $env:LOCALAPPDATA 'PlaydexLauncher\dev-cert'
$pfx = Join-Path $certDir 'playdex-launcher-dev.pfx'
$passwordFile = Join-Path $certDir 'password.txt'
if (-not (Test-Path $pfx)) {
    throw "No dev certificate in $certDir. Run tools/package.ps1 once to create it."
}
$password = Get-Content $passwordFile -Raw
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $password)
if ($cert.Subject -ne 'CN=Playdex Launcher Dev') {
    throw "The certificate's subject is '$($cert.Subject)', not the manifest's Publisher 'CN=Playdex Launcher Dev'."
}
$out = Join-Path $certDir 'launcher-signing-pfx.base64.txt'
[IO.File]::WriteAllText($out, [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx)))
Write-Host "Certificate: $($cert.Subject), thumbprint $($cert.Thumbprint), valid until $($cert.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host ""
Write-Host "Set the secrets (from the repository folder, signed in with gh):"
Write-Host "  Get-Content '$out' -Raw | gh secret set LAUNCHER_SIGNING_PFX"
Write-Host "  Get-Content '$passwordFile' -Raw | gh secret set LAUNCHER_SIGNING_PASSWORD"
Write-Host ""
Write-Host "Then delete $out."
