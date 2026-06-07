# run-all.ps1 — master test runner for WinDock test suite
$ErrorActionPreference = 'Continue'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName
$testDir = "$base\test"
$integrationDir = "$testDir\integration"

Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   WinDock Test Suite Runner         ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Pre-cleanup: kill any leftover processes from previous runs
$cleanupProcs = @("WinDock","test-visual-demo","test-icon-lifecycle","test-layout","test-magnification",
    "test-pin-unpin","test-theme-switch","test-taskbar-toggle","test-context-menu","test-badge",
    "test-mutex-singleton","test-coordinates","test-dock-pin","cmd","notepad","calc","CalculatorApp")
foreach ($name in $cleanupProcs) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
Start-Sleep 0.5

# Ensure temp directory exists
if (-not (Test-Path $env:TEMP)) { New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null }

# Test definitions
$tests = @(
    # C# executable tests — each writes to a specific result file
    @{Name="Icon Lifecycle";     Type="exe";  Path="$testDir\test-icon-lifecycle.exe";     ResultFile="$env:TEMP\_test_icon_lifecycle_result.txt";   Timeout=15},
    @{Name="Layout";             Type="exe";  Path="$testDir\test-layout.exe";             ResultFile="$env:TEMP\_test_layout_result.txt";           Timeout=15},
    @{Name="Magnification";      Type="exe";  Path="$testDir\test-magnification.exe";      ResultFile="$env:TEMP\_test_magnification_result.txt";    Timeout=20},
    @{Name="Pin/Unpin";          Type="exe";  Path="$testDir\test-pin-unpin.exe";          ResultFile="$env:TEMP\_test_pinunpin_result.txt";         Timeout=15},
    @{Name="Theme Switch";       Type="exe";  Path="$testDir\test-theme-switch.exe";       ResultFile="$env:TEMP\_test_theme_result.txt";            Timeout=15},
    @{Name="Taskbar Toggle";     Type="exe";  Path="$testDir\test-taskbar-toggle.exe";     ResultFile="$env:TEMP\_test_taskbar_result.txt";          Timeout=15},
    @{Name="Context Menu";       Type="exe";  Path="$testDir\test-context-menu.exe";       ResultFile="$env:TEMP\_test_contextmenu_result.txt";      Timeout=20},
    @{Name="Badge";              Type="exe";  Path="$testDir\test-badge.exe";              ResultFile="$env:TEMP\_test_badge_result.txt";            Timeout=15},
    @{Name="Mutex Singleton";    Type="exe";  Path="$testDir\test-mutex-singleton.exe";    ResultFile="$env:TEMP\_test_mutex_result.txt";            Timeout=20},
    @{Name="Coordinates";        Type="exe";  Path="$testDir\test-coordinates.exe";         ResultFile="$env:TEMP\_test_coordinates_result.txt";      Timeout=20},
    @{Name="Dock Pin";           Type="exe";  Path="$testDir\test-dock-pin.exe";            ResultFile="$env:TEMP\_test_dockpin_result.txt";          Timeout=30},
    # PowerShell integration tests — check their own output internally
    @{Name="Line Align";         Type="ps1";  Path="$testDir\line-align.ps1";             Timeout=30},
    @{Name="Startup Dump";       Type="ps1";  Path="$integrationDir\test-startup-dump.ps1";  Timeout=20},
    @{Name="Toggle Cycle";       Type="ps1";  Path="$integrationDir\test-toggle-cycle.ps1";  Timeout=25},
    @{Name="Theme Detect";       Type="ps1";  Path="$integrationDir\test-theme-detect.ps1";  Timeout=20},
    @{Name="Badge Count";        Type="ps1";  Path="$integrationDir\test-badge-count.ps1";   Timeout=35},
    @{Name="Pin End-to-End";     Type="ps1";  Path="$integrationDir\test-pin-end-to-end.ps1"; Timeout=35}
)

$total = $tests.Count
$passCount = 0
$failCount = 0
$skipCount = 0

$results = @()

foreach ($test in $tests) {
    Write-Host ("─" * 50) -ForegroundColor DarkGray
    Write-Host "[$($results.Count + 1)/$total] $($test.Name)" -ForegroundColor White

    # Use explicit result file path if defined, else derive from test name (for PS tests)
    $resultFile = if ($test.ResultFile) { $test.ResultFile } else { "$env:TEMP\_test_$($test.Name -replace '\s+','_')_result.txt" }
    # Clean previous result
    if (Test-Path $resultFile) { Remove-Item $resultFile -Force }

    if (-not (Test-Path $test.Path)) {
        Write-Host "  SKIP: $($test.Path) not found — run build-tests.ps1 first" -ForegroundColor Yellow
        $skipCount++
        $results += @{Name=$test.Name; Result="SKIP"; Reason="File not found"}
        continue
    }

    try {
        if ($test.Type -eq "exe") {
            # Run C# executable
            $proc = Start-Process $test.Path -PassThru -WindowStyle Hidden
            $finished = $proc.WaitForExit($test.Timeout * 1000)

            if (-not $finished) {
                Write-Host "  TIMEOUT after $($test.Timeout)s — killing" -ForegroundColor Yellow
                try { $proc.Kill() } catch { }
                $failCount++
                $results += @{Name=$test.Name; Result="TIMEOUT"; Reason="Exceeded $($test.Timeout)s"}
                continue
            }

            # Read result file
            if (Test-Path $resultFile) {
                $content = Get-Content $resultFile -Raw
                if ($content -match 'RESULT:\s*PASS') {
                    Write-Host "  PASS" -ForegroundColor Green
                    $passCount++
                    $results += @{Name=$test.Name; Result="PASS"; Reason=""}
                } elseif ($content -match 'RESULT:\s*FAIL') {
                    Write-Host "  FAIL" -ForegroundColor Red
                    # Show failure details
                    $failLines = $content -split "`n" | Where-Object { $_ -match '^FAIL:' }
                    $failLines | Select-Object -First 3 | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
                    $failCount++
                    $results += @{Name=$test.Name; Result="FAIL"; Reason="See $resultFile"}
                } else {
                    Write-Host "  FAIL (no RESULT line, exit code=$($proc.ExitCode))" -ForegroundColor Red
                    Write-Host "  Content: $content" -ForegroundColor Gray
                    $failCount++
                    $results += @{Name=$test.Name; Result="FAIL"; Reason="No RESULT line, content=$content"}
                }
            } else {
                Write-Host "  FAIL (no result file, exit code=$($proc.ExitCode))" -ForegroundColor Red
                $failCount++
                $results += @{Name=$test.Name; Result="FAIL"; Reason="No result file"}
            }
        } else {
            # Run PowerShell script
            $psOutput = & powershell -NoProfile -ExecutionPolicy Bypass -File $test.Path 2>&1
            $psExit = $LASTEXITCODE

            if ($psExit -eq 0) {
                Write-Host "  PASS" -ForegroundColor Green
                $passCount++
                $results += @{Name=$test.Name; Result="PASS"; Reason=""}
            } else {
                Write-Host "  FAIL (exit code $psExit)" -ForegroundColor Red
                $failCount++
                $results += @{Name=$test.Name; Result="FAIL"; Reason="Exit code $psExit"}
            }
        }
    } catch {
        Write-Host "  ERROR: $_" -ForegroundColor Red
        $failCount++
        $results += @{Name=$test.Name; Result="ERROR"; Reason=$_.Exception.Message}
    }

    # Brief pause between tests
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
    $color = switch($r.Result) { "PASS" { "Green" } "FAIL" { "Red" } "SKIP" { "Yellow" } "TIMEOUT" { "Yellow" } default { "Magenta" } }
    Write-Host "  [$idx] $name " -NoNewline
    Write-Host $r.Result -ForegroundColor $color -NoNewline
    if ($r.Reason) { Write-Host "  ($($r.Reason))" -ForegroundColor DarkGray }
    else { Write-Host "" }
}

Write-Host ""
Write-Host "Total:  $total" -ForegroundColor White
Write-Host "Passed: $passCount" -ForegroundColor $(if($passCount -gt 0){'Green'}else{'White'})
Write-Host "Failed: $failCount" -ForegroundColor $(if($failCount -gt 0){'Red'}else{'White'})
Write-Host "Skipped: $skipCount" -ForegroundColor $(if($skipCount -gt 0){'Yellow'}else{'White'})

# Post-cleanup: kill any leftover test processes
foreach ($name in $cleanupProcs) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

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
