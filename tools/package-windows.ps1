<#
  Publishes the Windows apps (Native AOT, win-x64) and writes the release files into -Out. The release workflow runs it;
  run it locally (with -Msix from src/Launcher/Host/tools/package.ps1) to get the same files for a test.

    app/                                          the app folder: Playdex.exe, Playdex.Run.exe, Playdex-Setup.exe (its
                                                  uninstaller), ui/
    playdex-windows-v<version>.zip (+ .sha256)    the app folder; what Playdex updates itself from
    Playdex-Setup.exe                             the offline installer: the setup with the app (and the Full Screen
                                                  Experience package, when given) appended
    playdex-wrapper-windows-v<version>.zip (+ .sha256)
                                                  the online setup as Wrapper.exe: how WinForms Wrapper installs move over

  Needs the UI built first (src/Launcher/ui: bun run build). Native AOT needs vswhere on PATH (the Visual Studio
  Installer folder).

  Usage: powershell -File tools/package-windows.ps1 -Version 2.0.0 [-Out artifacts] [-Run <published Playdex.Run.exe>]
         [-Msix <playdex-launcher.msix> -Cer <playdex-launcher.cer>]
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Out = "artifacts",
    [string]$Run,
    [string]$Msix,
    [string]$Cer
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = Resolve-Path $Out
$tag = "v$Version"
$common = @('-c', 'Release', '-r', 'win-x64', "-p:Version=$Version", '-p:IncludeSourceRevisionInInformationalVersion=false')

function Publish($project, $destination) {
    dotnet publish "$root\$project" @common -o $destination
    if ($LASTEXITCODE) { throw "Publishing $project failed." }
}

function Write-Checksum($path) {
    $name = Split-Path $path -Leaf
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$path.sha256" -Value "$hash  $name" -Encoding ascii -NoNewline
}

if (-not $Run) {
    Publish 'src/Playdex.Run/Playdex.Run.csproj' "$Out\run"
    $Run = "$Out\run\Playdex.Run.exe"
}
Publish 'src/Playdex.Setup/Playdex.Setup.csproj' "$Out\setup"
Publish 'src/Desktop/Playdex.Desktop.csproj' "$Out\app"
Copy-Item $Run "$Out\app\"
# The uninstaller lives with the app, so the self-updater (which swaps the whole folder) keeps it.
Copy-Item "$Out\setup\Playdex.Setup.exe" "$Out\app\Playdex-Setup.exe"
Get-ChildItem "$Out\app" -Recurse -Filter *.pdb | Remove-Item

$appZip = "$Out\playdex-windows-$tag.zip"
if (Test-Path $appZip) { Remove-Item $appZip }
Compress-Archive -Path "$Out\app\*" -DestinationPath $appZip
Write-Checksum $appZip

# The offline installer: the setup appends the app (and the FSE package) to a copy of itself.
# Start-Process doesn't quote arguments with spaces.
$bundle = @('--bundle', "`"--app=$Out\app`"", "`"--out=$Out\Playdex-Setup.exe`"")
if ($Msix) { $bundle += "`"--fse=$(Resolve-Path $Msix)`""; $bundle += "`"--fse=$(Resolve-Path $Cer)`"" }
$process = Start-Process "$Out\setup\Playdex.Setup.exe" -ArgumentList $bundle -Wait -PassThru
if ($process.ExitCode -ne 0) {
    Get-Content (Join-Path $env:LOCALAPPDATA 'Playdex\setup.log') -Tail 20 -ErrorAction SilentlyContinue
    throw "Bundling the setup failed."
}

# The WinForms Wrapper's updater installs the single Wrapper.exe in this archive: the online setup.
$bridge = "$Out\bridge"
New-Item -ItemType Directory -Force $bridge | Out-Null
Copy-Item "$Out\setup\Playdex.Setup.exe" "$bridge\Wrapper.exe"
$bridgeZip = "$Out\playdex-wrapper-windows-$tag.zip"
if (Test-Path $bridgeZip) { Remove-Item $bridgeZip }
Compress-Archive -Path "$bridge\Wrapper.exe" -DestinationPath $bridgeZip
Write-Checksum $bridgeZip

Get-ChildItem $Out -File | ForEach-Object { "{0,-50} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
