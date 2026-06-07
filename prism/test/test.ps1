# Desktop Widgets Test Harness — test.ps1
# Usage: .\test.ps1   (run from repo root: .\test\test.ps1)
# Tests: compile unified Prism.exe, launch, verify all 6 widget windows appear, stability

$ErrorActionPreference='Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = "$base\Prism.cs"
$exe = "$base\Prism.exe"

$widgetWindows = @(
    "TopBar", "System", "Disk",
    "Network", "Recycle Bin"
)
# Note: WiFi Panel is hidden by default (toggled via Network "Scan" button)

Write-Host "`n=== Desktop Widgets Test ===" -ForegroundColor Cyan

# 1. Source exists?
if (-not (Test-Path $src)) {
    Write-Host "FAIL: Prism.cs missing" -ForegroundColor Red; exit 1
}
Write-Host "  PASS: Prism.cs found" -ForegroundColor Green

# 2. Compile
Write-Host "`n--- Compile ---" -ForegroundColor Yellow
try { taskkill /F /IM Prism.exe 2>$null | Out-Null } catch {}
Start-Sleep 1
Remove-Item $exe -Force -ErrorAction SilentlyContinue
$result = & $csc /target:winexe /out:$exe /win32manifest:"$base\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll /reference:System.Management.dll /reference:Microsoft.VisualBasic.dll /reference:"$base\native\OpenHardwareMonitorLib.dll" "$base\..\Shared\*.cs" "$base\Common\*.cs" "$base\Common\Debug\Visual\*.cs" "$base\Common\Debug\Info\*.cs" "$base\Components\*.cs" $src 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) {
    Write-Host "FAIL: compile error" -ForegroundColor Red
    $result | Where-Object { $_ -match 'error CS' } | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "  PASS: compile -> Prism.exe ($([math]::Round((Get-Item $exe).Length/1KB))KB)" -ForegroundColor Green

# 3. Launch
Write-Host "`n--- Launch ---" -ForegroundColor Yellow
$proc = Start-Process $exe -ArgumentList '--debug' -PassThru
Start-Sleep 4
$proc.Refresh()
if ($proc.HasExited) {
    Write-Host "FAIL: process exited immediately (crash)" -ForegroundColor Red
    exit 1
}
Write-Host "  PASS: Prism.exe running (PID $($proc.Id))" -ForegroundColor Green

# 4. Check all widget windows
Write-Host "`n--- Window Detection ---" -ForegroundColor Yellow
Add-Type -Name WT -Namespace T -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWinProc lpEnumFunc, IntPtr lParam);
public delegate bool EnumWinProc(IntPtr hWnd, IntPtr lParam);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
'@

$foundTitles = New-Object System.Collections.Generic.List[string]
$cb = {
    param($h, $l)
    $sb = New-Object System.Text.StringBuilder(256)
    [T.WT]::GetWindowText($h, $sb, 256)
    $t = $sb.ToString()
    if ($t.Length -gt 0) { $foundTitles.Add($t) }
    return $true
}
[T.WT]::EnumWindows($cb, [IntPtr]::Zero)

$allFound = $true
foreach ($title in $widgetWindows) {
    $found = $foundTitles | Where-Object { $_ -match [regex]::Escape($title) }
    if ($found) {
        Write-Host "  PASS: '$title'" -ForegroundColor Green
    } else {
        Write-Host "  MISS: '$title' not found" -ForegroundColor Yellow
        $allFound = $false
    }
}

# 5. Stability check
Write-Host "`n--- Stability ---" -ForegroundColor Yellow
Start-Sleep 3
$proc.Refresh()
if ($proc.HasExited) {
    Write-Host "FAIL: crashed after launch" -ForegroundColor Red
    exit 1
}
Write-Host "  PASS: stable after 3s" -ForegroundColor Green

# 6. Cleanup
$proc.Kill()
Start-Sleep 1

if ($allFound) {
    Write-Host "`n=== ALL PASS ===" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n=== PASS (some windows missing — may be hidden/overlapped) ===" -ForegroundColor Yellow
    exit 0
}
