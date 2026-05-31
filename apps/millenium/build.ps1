function Build-Plugin {
  Write-Host "Building plugin..."
  pnpm millennium-ttc --build prod
}

function Copy-PluginFiles {
  param ([string]$Destination)

  Write-Host "Copying plugin files to $Destination..."
  New-Item -Path $Destination -ItemType Directory -Force | Out-Null
  Copy-Item -Path "plugin.json" -Destination $Destination -Force
  Copy-Item -Path ".millennium" -Destination $Destination -Recurse -Force
  Copy-Item -Path "backend" -Destination $Destination -Recurse -Force
}

function Get-PluginVersion {
  param ([string]$Path)

  $json = Get-Content $Path | ConvertFrom-Json
  return $json.version
}

#####
$version = Get-PluginVersion -Path ".\plugin.json"
$zipName = "playdex-$version.zip"

Build-Plugin
Copy-PluginFiles -Destination build
Compress-Archive -Path build -DestinationPath $zipName -Force
Remove-Item -Path build -Recurse -Force
