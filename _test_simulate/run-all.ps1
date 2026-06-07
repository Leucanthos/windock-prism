# run-all.ps1 — master test runner for SimMouse test suite
param(
    [switch]$Full,      # Include dock integration test (takes longer, needs WinDock)
    [switch]$NoBuild    # Skip build step
)
$ErrorActionPreference = 'Continue'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).FullName
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  SimMouse Test Suite" -ForegroundColor Cyan
if ($Full) { Write-Host "  MODE: Full (includes WinDock integration)" -ForegroundColor Yellow }
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build
if (-not $NoBuild) {
    Write-Host "[BUILD]" -ForegroundColor Yellow
    $buildResult = & "$base\build.ps1" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host $buildResult
        Write-Host "BUILD FAILED — aborting" -ForegroundColor Red
        exit 1
    }
}

# Define tests
$tests = @(
    @{
        Name = "test-simulate"
        Exe  = "$base\test-simulate.exe"
        ResultFile = Join-Path $env:TEMP "_test_simulate_result.txt"
        Timeout = 30
        Desc = "SimMouse unit tests"
    }
)

if ($Full) {
    $tests += @{
        Name = "test-dock-simulate"
        Exe  = "$base\test-dock-simulate.exe"
        ResultFile = Join-Path $env:TEMP "_test_dock_simulate_result.txt"
        Timeout = 60
        Desc = "WinDock integration test"
    }
}

$totalPassed = 0
$totalFailed = 0
$totalWarn = 0

foreach ($test in $tests) {
    Write-Host ""
    Write-Host "--- $($test.Desc) ---" -ForegroundColor Cyan

    # Clean previous result
    if (Test-Path $test.ResultFile) {
        Remove-Item $test.ResultFile -Force -ErrorAction SilentlyContinue
    }
    # Also clean trace file
    $traceFile = $test.ResultFile -replace '_result\.txt$', '_trace.txt'
    if (Test-Path $traceFile) {
        Remove-Item $traceFile -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Running $($test.Name).exe..." -ForegroundColor Gray
    $proc = Start-Process -FilePath $test.Exe -PassThru -WindowStyle Minimized

    $waited = $proc.WaitForExit($test.Timeout * 1000)
    if (-not $waited) {
        Write-Host "TIMEOUT: $($test.Name) did not finish within $($test.Timeout)s — killing" -ForegroundColor Red
        $proc.Kill()
        $totalFailed++
        continue
    }

    # Print result
    if (-not (Test-Path $test.ResultFile)) {
        Write-Host "FAIL: no result file at $($test.ResultFile)" -ForegroundColor Red
        $totalFailed++
        continue
    }

    $lines = Get-Content $test.ResultFile
    $passCount = 0
    $failCount = 0
    $warnCount = 0

    foreach ($line in $lines) {
        if ($line -match "^PASS:") {
            Write-Host "  $line" -ForegroundColor Green
            $passCount++
        } elseif ($line -match "^FAIL:") {
            Write-Host "  $line" -ForegroundColor Red
            $failCount++
        } elseif ($line -match "^WARN:") {
            Write-Host "  $line" -ForegroundColor Yellow
            $warnCount++
        } elseif ($line -match "^RESULT:") {
            # printed below
        } elseif ($line -match "^===") {
            Write-Host "  $line" -ForegroundColor DarkCyan
        }
    }

    $lastLine = $lines[-1]
    if ($lastLine -match "RESULT:\s*PASS") {
        Write-Host "  >>> $($test.Name): PASS ($passCount passed, $failCount failed, $warnCount warnings) <<<" -ForegroundColor Green
        $totalPassed++
    } else {
        Write-Host "  >>> $($test.Name): FAIL ($passCount passed, $failCount failed, $warnCount warnings) <<<" -ForegroundColor Red
        $totalFailed++
    }

    # Show trace file path if it exists
    if (Test-Path $traceFile) {
        Write-Host "  Trace: $traceFile" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Suite Summary" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Tests passed:  $totalPassed" -ForegroundColor Green
Write-Host "Tests failed:  $totalFailed" -ForegroundColor $(if ($totalFailed -gt 0) { 'Red' } else { 'Green' })
Write-Host "Warnings:      $totalWarn" -ForegroundColor $(if ($totalWarn -gt 0) { 'Yellow' } else { 'Green' })

if ($totalFailed -eq 0) {
    Write-Host "ALL TESTS PASSED" -ForegroundColor Green
    exit 0
} else {
    Write-Host "SOME TESTS FAILED" -ForegroundColor Red
    exit 1
}
