# test-startup-dump.ps1 — verify dock starts cleanly and produces expected debug output
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.Parent.FullName

Write-Host "=== Test 10: Startup Dump ===" -ForegroundColor Cyan

$exe = "$base\WinDock-d.exe"
if (-not (Test-Path $exe)) {
    Write-Host "FAIL: $exe not found — did you compile WinDock-d.exe?" -ForegroundColor Red
    exit 1
}

# Clean old dump files
$startupLog = "$env:TEMP\_dock_startup.txt"
$dumpLog    = "$env:TEMP\_dock_dump.txt"
@($startupLog, $dumpLog) | ForEach-Object { if (Test-Path $_) { Remove-Item $_ -Force } }

# Launch dock in debug mode
Write-Host "Launching WinDock-d.exe --debug..."
$proc = Start-Process $exe -ArgumentList '--debug' -PassThru
Start-Sleep 3

$errors = @()

# Check startup log
if (Test-Path $startupLog) {
    $startupContent = Get-Content $startupLog -Raw
    if ($startupContent -match 'DebugMode=True') {
        Write-Host "  PASS: _dock_startup.txt contains DebugMode=True" -ForegroundColor Green
    } else {
        $errors += "_dock_startup.txt missing DebugMode=True"
        Write-Host "  FAIL: _dock_startup.txt: $startupContent" -ForegroundColor Red
    }
} else {
    $errors += "_dock_startup.txt not found"
    Write-Host "  FAIL: _dock_startup.txt not found" -ForegroundColor Red
}

# Check icon dump
if (Test-Path $dumpLog) {
    $dumpContent = Get-Content $dumpLog -Raw
    if ($dumpContent -match '=== Icons @') {
        Write-Host "  PASS: _dock_dump.txt contains icon dump header" -ForegroundColor Green
    } else {
        $errors += "_dock_dump.txt missing header"
        Write-Host "  FAIL: _dock_dump.txt: $dumpContent" -ForegroundColor Red
    }

    # Count icons
    $iconLines = ($dumpContent -split "`n" | Where-Object { $_ -match '^\s*\[\d+\]' })
    $iconCount = $iconLines.Count
    if ($iconCount -ge 2) {
        Write-Host "  PASS: at least 2 icons found ($iconCount icons)" -ForegroundColor Green
    } else {
        $errors += "Only $iconCount icons (need >= 2)"
        Write-Host "  FAIL: only $iconCount icons found (need >= 2)" -ForegroundColor Red
    }
} else {
    $errors += "_dock_dump.txt not found"
    Write-Host "  FAIL: _dock_dump.txt not found" -ForegroundColor Red
}

# Cleanup
if ($proc -and !$proc.HasExited) { $proc.Kill(); $proc.WaitForExit(2000) }

if ($errors.Count -eq 0) {
    Write-Host "RESULT: PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host "RESULT: FAIL ($($errors.Count) errors)" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
