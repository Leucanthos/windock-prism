# pipeline.ps1 — one-click compile + full test suite
# Usage: .\test\pipeline.ps1
# Exit 0 = all pass, Exit 1 = one or more failures

$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = "$base\Prism.cs"
$exe = "$base\Prism.exe"

$testDir = "$base\test"
$startTime = Get-Date

Write-Host "`n" -NoNewline
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Prism Test Pipeline" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Root : $base"
Write-Host "  Time : $($startTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor DarkGray

# ============================================================
# Stage 1: Compile
# ============================================================
Write-Host "`n--- Stage 1: Compile ---" -ForegroundColor Yellow

try { taskkill /F /IM Prism.exe 2>$null | Out-Null } catch {}
Start-Sleep 1
Remove-Item $exe -Force -ErrorAction SilentlyContinue

$buildResult = & $csc /target:winexe /out:$exe /win32manifest:"$base\app.manifest" `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /reference:Microsoft.CSharp.dll `
    /reference:System.Management.dll /reference:Microsoft.VisualBasic.dll `
    /reference:"$base\native\OpenHardwareMonitorLib.dll" `
    "$base\Common\*.cs" "$base\Common\Debug\Visual\*.cs" "$base\Common\Debug\Info\*.cs" "$base\Components\*.cs" $src 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "  FATAL: Compile failed" -ForegroundColor Red
    $buildResult | Where-Object { $_ -match 'error CS' } | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 2
}
$exeSize = [math]::Round((Get-Item $exe).Length / 1KB)
Write-Host "  PASS: Prism.exe ($exeSize KB)" -ForegroundColor Green

# ============================================================
# Stage 2: Smoke test (launch + window detection)
# ============================================================
Write-Host "`n--- Stage 2: Smoke Test ---" -ForegroundColor Yellow
$smokeResult = & powershell -NoProfile -ExecutionPolicy Bypass -File "$testDir\test.ps1" 2>&1
$smokeOk = ($LASTEXITCODE -eq 0)
if ($smokeOk) {
    Write-Host "  PASS: Smoke test" -ForegroundColor Green
} else {
    Write-Host "  FAIL: Smoke test" -ForegroundColor Red
    $smokeResult | Where-Object { $_ -match 'FAIL|MISS|error' } | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}

# ============================================================
# Stage 3: Static analysis tests
# ============================================================
$tests = @(
    @{Name="Perf";       File="perf.ps1"},
    @{Name="Polling";    File="polling.ps1"},
    @{Name="Security";   File="security.ps1"},
    @{Name="Resources";  File="resources.ps1"},
    @{Name="Artifacts";  File="artifacts.ps1"},
    @{Name="CodeQuality";File="codecheck.ps1"}
)

$passed = 0
$failed = 0
$failDetails = @()

Write-Host "`n--- Stage 3: Static Analysis ---" -ForegroundColor Yellow
foreach ($test in $tests) {
    $result = & powershell -NoProfile -ExecutionPolicy Bypass -File "$testDir\$($test.File)" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  PASS: $($test.Name)" -ForegroundColor Green
        $passed++
    } else {
        Write-Host "  FAIL: $($test.Name)" -ForegroundColor Red
        $result | Where-Object { $_ -match 'FAIL:' } | ForEach-Object { Write-Host "       $_" -ForegroundColor DarkRed }
        $failed++
        $failDetails += $test.Name
    }
}

# ============================================================
# Summary
# ============================================================
$elapsed = (Get-Date) - $startTime

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  Results" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Smoke : $(if($smokeOk){'PASS'}else{'FAIL'})"
Write-Host "  Static: $passed pass, $failed fail"
Write-Host "  Time  : $([math]::Round($elapsed.TotalSeconds,1))s"
Write-Host ""

if (-not $smokeOk -or $failed -gt 0) {
    Write-Host "  => FAILED" -ForegroundColor Red
    exit 1
} else {
    Write-Host "  => ALL PASS" -ForegroundColor Green
    exit 0
}
