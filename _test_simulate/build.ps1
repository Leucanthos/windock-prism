# build.ps1 — compile all SimMouse test harnesses
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).FullName

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    Write-Host "ERROR: csc.exe not found at $csc" -ForegroundColor Red
    Write-Host "Try: C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe (32-bit)" -ForegroundColor Yellow
    exit 1
}

Write-Host "=== Building Mouse Simulation Test Suite ===" -ForegroundColor Cyan
Write-Host "Compiler: $csc"
Write-Host ""

$refs = @(
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Drawing.dll"
)
$refsStr = $refs -join " "

$failed = @()
$passed = @()

# ---- Test 1: test-simulate.exe (unit tests) ----
$test1 = @{
    Name    = "test-simulate"
    Sources = @("$base\MouseSimulator.cs", "$base\test-simulate.cs")
    Desc    = "SimMouse unit tests (structs, movement, clicks, drag, scroll)"
}
# ---- Test 2: test-dock-simulate.exe (integration test) ----
$test2 = @{
    Name    = "test-dock-simulate"
    Sources = @("$base\MouseSimulator.cs", "$base\test-dock-simulate.cs")
    Desc    = "WinDock integration test (hover, click, alignment, event log)"
}

$tests = @($test1, $test2)

foreach ($test in $tests) {
    $exeFile = "$base\$($test.Name).exe"
    $sourcesStr = ($test.Sources | ForEach-Object { """$_""" }) -join " "

    Write-Host "Building $($test.Name).exe..." -ForegroundColor Gray
    Write-Host "  $($test.Desc)" -ForegroundColor Gray

    $output = Invoke-Expression "& ""$csc"" /target:winexe /out:""$exeFile"" $refsStr $sourcesStr 2>&1"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FAIL: compilation error" -ForegroundColor Red
        Write-Host "  $output" -ForegroundColor Red
        $failed += $test.Name
    } elseif (Test-Path $exeFile) {
        $size = (Get-Item $exeFile).Length
        Write-Host "  OK: $exeFile ($([math]::Round($size / 1KB, 1)) KB)" -ForegroundColor Green
        $passed += $test.Name
    } else {
        Write-Host "  FAIL: no output file" -ForegroundColor Red
        $failed += $test.Name
    }
}

Write-Host ""
Write-Host "=== Build Summary ===" -ForegroundColor Cyan
Write-Host "Passed: $($passed.Count)" -ForegroundColor Green
if ($passed.Count -gt 0) {
    $passed | ForEach-Object { Write-Host "  $_" -ForegroundColor Green }
}
Write-Host "Failed: $($failed.Count)" -ForegroundColor $(if ($failed.Count -gt 0) { 'Red' } else { 'Green' })
if ($failed.Count -gt 0) {
    $failed | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
exit 0
