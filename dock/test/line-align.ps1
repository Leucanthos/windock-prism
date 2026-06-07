# line-align.ps1 — verify line endpoints == first & last icon centers
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName

Write-Host "=== Line-Icon Alignment Test ===" -ForegroundColor Cyan

# Launch WinDock
$exe = "$base\WinDock-d.exe"
if (-not (Test-Path $exe)) { Write-Host "FAIL: $exe not found" -ForegroundColor Red; exit 1 }

$proc = Start-Process $exe -ArgumentList '--debug' -PassThru
Start-Sleep 5

$log = "$env:TEMP\_dock_line.txt"
if (-not (Test-Path $log)) { Write-Host "FAIL: No debug dump at $log" -ForegroundColor Red; $proc.Kill(); exit 1 }

$content = Get-Content $log
$lineX0 = $lineX1 = 0
$iconCenters = @()

foreach ($line in $content) {
    if ($line -match 'LINE: x0=([\d.]+) x1=([\d.]+)') { $lineX0 = [double]$Matches[1]; $lineX1 = [double]$Matches[2] }
    if ($line -match 'ICON\[(\d+)\] BaseX=(\d+) W=(\d+) Center=([\d.]+)') {
        $i = [int]$Matches[1]; $iconCenters += [double]$Matches[4]
    }
}

Write-Host "Line:  x0=$lineX0  x1=$lineX1"
for ($i=0; $i -lt $iconCenters.Count; $i++) { Write-Host "Icon[$i] center=$($iconCenters[$i])" }

$firstOk = [Math]::Abs($lineX0 - $iconCenters[0]) -lt 1.0
$lastOk = [Math]::Abs($lineX1 - $iconCenters[-1]) -lt 1.0

if ($firstOk -and $lastOk) {
    Write-Host "PASS: line endpoints match first & last icon centers" -ForegroundColor Green
    $proc.Kill(); exit 0
} else {
    if (-not $firstOk) { Write-Host "FAIL: x0=$lineX0 != icon[0] center=$($iconCenters[0])" -ForegroundColor Red }
    if (-not $lastOk) { Write-Host "FAIL: x1=$lineX1 != icon[$($iconCenters.Count-1)] center=$($iconCenters[-1])" -ForegroundColor Red }
    $proc.Kill(); exit 1
}
