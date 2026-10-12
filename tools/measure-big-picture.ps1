<#
  Measures the Big Picture budgets from docs/launcher/performance.md on this machine and prints a summary.
  Numbers come from the app's own metrics (metrics.jsonl) plus process sampling for memory and CPU.

  Usage (Windows PowerShell 5.1 or 7, from the repo root, with the release build of src/Desktop published):
    powershell -File tools/measure-big-picture.ps1 [-Exe <path>] [-ColdRuns 12] [-Presses 600] [-IdleSeconds 60]

  Quit Playdex first (tray → Quit): it is single instance.
#>
param(
    [string]$Exe = "$PSScriptRoot\..\src\Desktop\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish\Playdex.exe",
    [int]$ColdRuns = 12,
    [int]$Presses = 600,
    [int]$IdleSeconds = 60,
    [string]$ExtraArgs = ""
)
$ErrorActionPreference = 'Stop'
$data = Join-Path $env:LOCALAPPDATA 'Playdex'
$metrics = Join-Path $data 'metrics.jsonl'
if (Test-Path $metrics) { Remove-Item $metrics }

function Summary {
    Get-Content $metrics | ConvertFrom-Json | Group-Object metric | ForEach-Object {
        $v = @($_.Group.ms | Sort-Object)
        "{0,-36} n={1,3} min={2,8:N2} median={3,8:N2} max={4,8:N2}" -f $_.Name, $v.Count, $v[0], $v[[int][Math]::Floor(($v.Count - 1) / 2)], $v[-1]
    }
}

function Tree([int]$rootId) {
    $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name
    $ids = [System.Collections.Generic.List[int]]::new(); $ids.Add($rootId)
    for ($i = 0; $i -lt $ids.Count; $i++) { $all | Where-Object ParentProcessId -eq $ids[$i] | ForEach-Object { $ids.Add([int]$_.ProcessId) } }
    $all | Where-Object { $ids.Contains([int]$_.ProcessId) }
}

function PrivateWorkingSetMB([int[]]$ids) {
    $perf = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | Where-Object { $ids -contains [int]$_.IDProcess }
    [Math]::Round((($perf | Measure-Object WorkingSetPrivate -Sum).Sum) / 1MB, 1)
}

# 1. Cold start: process start -> first interactive frame.
Write-Host "Cold start x$ColdRuns..."
for ($i = 0; $i -lt $ColdRuns; $i++) {
    $p = Start-Process $Exe -ArgumentList "--big-picture --exit-on-ready $ExtraArgs" -PassThru
    if (-not $p.WaitForExit(20000)) { $p.Kill(); Write-Warning "run $i did not reach ready" }
    Start-Sleep -Milliseconds 800
}

# 2. Input latency with synthetic presses, then idle memory and CPU on the home screen.
Write-Host "Input bench ($Presses presses), then $IdleSeconds s idle..."
$p = Start-Process $Exe -ArgumentList "--big-picture --bench-input=$Presses $ExtraArgs" -PassThru
Start-Sleep -Seconds ([Math]::Ceiling($Presses * 0.08) + 8)
Start-Sleep -Seconds 10 # settle
$tree = Tree $p.Id
$ids = @($tree.ProcessId | ForEach-Object { [int]$_ })
$hostMB = PrivateWorkingSetMB @($p.Id)
$webMB = PrivateWorkingSetMB @($ids | Where-Object { $_ -ne $p.Id })
$hostCpu0 = (Get-Process -Id $p.Id).TotalProcessorTime.TotalMilliseconds
$cpuStart = Get-Process -Id $ids -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Id = $_.Id; Ms = $_.TotalProcessorTime.TotalMilliseconds } }
Start-Sleep -Seconds $IdleSeconds
$cpuEnd = Get-Process -Id $ids -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ Id = $_.Id; Ms = $_.TotalProcessorTime.TotalMilliseconds } }
$usedMs = ($cpuEnd | ForEach-Object { $id = $_.Id; $_.Ms - (($cpuStart | Where-Object Id -eq $id).Ms) } | Measure-Object -Sum).Sum
$hostUsedMs = (Get-Process -Id $p.Id).TotalProcessorTime.TotalMilliseconds - $hostCpu0
$cores = [Environment]::ProcessorCount
$p | Stop-Process
Start-Sleep -Seconds 2

Summary
""
"Processes: host + $($ids.Count - 1) WebView2 ($(($tree | Where-Object ProcessId -ne $p.Id | Select-Object -ExpandProperty Name | Sort-Object -Unique) -join ', '))"
"Idle private working set: host {0} MB, WebView2 {1} MB" -f $hostMB, $webMB
"Idle CPU over {0}s: all processes {1:N3}% of one core ({2:N4}% of {3} logical CPUs); host alone {4:N3}% of one core" -f $IdleSeconds, (100 * $usedMs / ($IdleSeconds * 1000)), (100 * $usedMs / ($IdleSeconds * 1000 * $cores)), $cores, (100 * $hostUsedMs / ($IdleSeconds * 1000))
