# build-and-test.ps1 — compile and run Shared/ unit tests
$ErrorActionPreference = 'Stop'

$sharedDir = Split-Path $MyInvocation.MyCommand.Path
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Shared/ Unit Tests                 ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Cyan

# ── Compile ──────────────────────────────────────────────────
Write-Host "`nCompiling..." -ForegroundColor Yellow

$src = @(
    "$sharedDir\DebugMode.cs",
    "$sharedDir\Version.cs",
    "$sharedDir\EventLog.cs",
    "$sharedDir\W.cs",
    "$sharedDir\tests\test-shared.cs"
)
$srcStr = ($src | ForEach-Object { """$_""" }) -join " "

$exe = "$sharedDir\test-shared.exe"
$cmd = "& ""$csc"" /target:exe /out:""$exe"" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $srcStr 2>&1"

$output = Invoke-Expression $cmd
if ($LASTEXITCODE -ne 0) {
    Write-Host "  FAIL: compilation error" -ForegroundColor Red
    $output | Where-Object { $_ -match 'error CS' } | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "  OK: $exe" -ForegroundColor Green

# ── Run ──────────────────────────────────────────────────────
Write-Host "`nRunning..." -ForegroundColor Yellow
& $exe 2>&1
$exit = $LASTEXITCODE

# ── Cleanup ──────────────────────────────────────────────────
Remove-Item $exe -Force -ErrorAction SilentlyContinue

if ($exit -eq 0) {
    Write-Host "`n=== ALL SHARED TESTS PASSED ===" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n=== SHARED TESTS FAILED ===" -ForegroundColor Red
    exit 1
}
