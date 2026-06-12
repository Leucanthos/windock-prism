# run-all.ps1 — master test runner for Prism (Desktop Widgets) test suite
$ErrorActionPreference = 'Continue'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName
$testDir = "$base\test"

Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Prism Test Suite Runner            ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Pre-cleanup: kill any leftover processes
Get-Process -Name "Prism" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 1

# Test definitions
$tests = @(
    # ─── Static analysis (fast, no dependencies) ───
    @{Name="Code Quality";        Type="ps1"; Path="$testDir\codecheck.ps1";      Timeout=60},
    @{Name="Security Audit";      Type="ps1"; Path="$testDir\security.ps1";       Timeout=60},
    @{Name="Polling Intervals";   Type="ps1"; Path="$testDir\polling.ps1";        Timeout=60},
    @{Name="Resource Leaks";      Type="ps1"; Path="$testDir\resources.ps1";      Timeout=60},
    @{Name="Debug Artifacts";     Type="ps1"; Path="$testDir\artifacts.ps1";      Timeout=60},
    # ─── Runtime tests (compile + launch widget windows) ───
    @{Name="Auto Startup";        Type="ps1"; Path="$testDir\auto-startup.ps1";   Timeout=30},
    @{Name="Main Test";           Type="ps1"; Path="$testDir\test.ps1";           Timeout=120},
    # ─── Performance ───
    @{Name="Performance";         Type="ps1"; Path="$testDir\perf.ps1";           Timeout=120},
    # ─── Integration / Pipeline (skipped by default, opt-in with -Full) ───
    @{Name="Pipeline (Full)";     Type="ps1"; Path="$testDir\pipeline.ps1";       Timeout=180; Skip=$true}
)

# Check if -Full was passed (include pipeline)
$runFull = $args -contains '-Full'

$total = $tests.Count
$passCount = 0
$failCount = 0
$skipCount = 0

$results = @()

foreach ($test in $tests) {
    Write-Host ("─" * 50) -ForegroundColor DarkGray
    Write-Host "[$($results.Count + 1)/$total] $($test.Name)" -ForegroundColor White

    if ($test.Skip -and -not $runFull) {
        Write-Host "  SKIP: use -Full to include $($test.Name)" -ForegroundColor Yellow
        $skipCount++
        $results += @{Name=$test.Name; Result="SKIP"; Reason="Use -Full to include"}
        continue
    }

    if (-not (Test-Path $test.Path)) {
        Write-Host "  SKIP: $($test.Path) not found" -ForegroundColor Yellow
        $skipCount++
        $results += @{Name=$test.Name; Result="SKIP"; Reason="File not found"}
        continue
    }

    try {
        # Run PowerShell script
        $psOutput = & powershell -NoProfile -ExecutionPolicy Bypass -File $test.Path 2>&1
        $psExit = $LASTEXITCODE

        if ($psExit -eq 0) {
            Write-Host "  PASS" -ForegroundColor Green
            $passCount++
            $results += @{Name=$test.Name; Result="PASS"; Reason=""}
        } else {
            Write-Host "  FAIL (exit code $psExit)" -ForegroundColor Red
            # Show first few lines of output
            $psOutput | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
            $failCount++
            $results += @{Name=$test.Name; Result="FAIL"; Reason="Exit code $psExit"}
        }
    } catch {
        Write-Host "  ERROR: $_" -ForegroundColor Red
        $failCount++
        $results += @{Name=$test.Name; Result="ERROR"; Reason=$_.Exception.Message}
    }

    Start-Sleep 0.5
}

# ===== Summary =====
Write-Host ""
Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Results                            ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

foreach ($r in $results) {
    $idx = "{0,2}" -f ($results.IndexOf($r) + 1)
    $name = "{0,-20}" -f $r.Name
    $color = switch($r.Result) { "PASS" { "Green" } "FAIL" { "Red" } "SKIP" { "Yellow" } default { "Magenta" } }
    Write-Host "  [$idx] $name " -NoNewline
    Write-Host $r.Result -ForegroundColor $color -NoNewline
    if ($r.Reason) { Write-Host "  ($($r.Reason))" -ForegroundColor DarkGray }
    else { Write-Host "" }
}

Write-Host ""
Write-Host "Total:   $total" -ForegroundColor White
Write-Host "Passed:  $passCount" -ForegroundColor $(if($passCount -gt 0){'Green'}else{'White'})
Write-Host "Failed:  $failCount" -ForegroundColor $(if($failCount -gt 0){'Red'}else{'White'})
Write-Host "Skipped: $skipCount" -ForegroundColor $(if($skipCount -gt 0){'Yellow'}else{'White'})

# Post-cleanup
Get-Process -Name "Prism" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if ($failCount -eq 0 -and $skipCount -eq 0) {
    Write-Host ""
    Write-Host "ALL TESTS PASSED" -ForegroundColor Green
    exit 0
} elseif ($failCount -eq 0) {
    Write-Host ""
    Write-Host "$passCount/$($passCount+$skipCount) passed ($skipCount skipped)" -ForegroundColor Yellow
    exit 0
} else {
    Write-Host ""
    Write-Host "$failCount TEST(S) FAILED" -ForegroundColor Red
    exit 1
}
