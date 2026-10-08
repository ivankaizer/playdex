<#
  Installs the dev MSIX built by package.ps1. Run from an elevated PowerShell.

  - Trusts the dev certificate in LocalMachine\TrustedPeople (needed for sideloading a self-signed package).
  - Installs (or updates) the package for the current user.
  - Developer Mode must be on: the gamingHome custom capability is only honoured with the developer-mode SCCD.

  To remove: first set another Home app in Settings > Gaming > Full screen experience (uninstalling the current home
  app can break Task View), then Get-AppxPackage Playdex.Launcher | Remove-AppxPackage, and optionally delete the
  certificate from Cert:\LocalMachine\TrustedPeople.
#>
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$cer = Join-Path $env:LOCALAPPDATA 'PlaydexLauncher\dev-cert\playdex-launcher-dev.cer'
if (-not (Test-Path $cer)) { throw "Run package.ps1 first ($cer not found)." }

$devMode = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
if ($devMode -ne 1) { Write-Warning 'Developer Mode is off: the FSE home-app capability will not be granted.' }

Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
$msix = Get-ChildItem "$PSScriptRoot\..\AppPackages" -Recurse -Filter *.msix | Select-Object -First 1
if (-not $msix) { throw 'No .msix found; run package.ps1 first.' }
Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
Get-AppxPackage Playdex.Launcher | Select-Object Name, Version, InstallLocation, SignatureKind
Write-Host 'Next: Settings > Gaming > Full screen experience > Home app > Playdex.'
