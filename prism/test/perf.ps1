# perf.ps1 — detect UI-blocking patterns
# Pass = no Thread.Sleep on UI thread in widget code
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName

$issues = @()
$pass = @()

# 1. Thread.Sleep in SystemWidget.cs (GPU counter warm-up)
$file = "$base\Components\SystemWidget.cs"
if (Select-String -Path $file -Pattern 'Thread\.Sleep' -Quiet) {
    $issues += "SystemWidget.cs: Thread.Sleep blocks UI thread (GPU perf counter warm-up)"
} else {
    $pass += "SystemWidget.cs: No Thread.Sleep"
}

# 2. Battery WMI polling without NoSystemBattery guard
$file = "$base\Components\BatteryWidget.cs"
$hasTimer = Select-String -Path $file -Pattern 'Interval=5000' -Quiet
$hasGuard = Select-String -Path $file -Pattern 'NoSystemBattery' -Quiet
if ($hasTimer -and -not $hasGuard) {
    $issues += "BatteryWidget.cs: Polls WMI every 5s even on desktops (no NoSystemBattery guard)"
} else {
    $pass += "BatteryWidget.cs: Has desktop battery guard"
}

# 3. ManagementObjectSearcher without using (dispose leak check)
$srcFiles = Get-ChildItem "$base\Components\*.cs"
foreach ($f in $srcFiles) {
    $lines = Select-String -Path $f.FullName -Pattern 'new ManagementObjectSearcher' | Where-Object { $_.Line -notmatch 'using\s*\(' }
    if ($lines) {
        foreach ($l in $lines) {
            $issues += "$($f.Name):$($l.LineNumber): ManagementObjectSearcher without 'using' — may leak"
        }
    }
}

Write-Host "`n=== Perf Test ===" -ForegroundColor Cyan
if ($issues.Count -eq 0) {
    Write-Host "  PASS: No UI-blocking patterns found" -ForegroundColor Green
    exit 0
} else {
    foreach ($i in $issues) { Write-Host "  FAIL: $i" -ForegroundColor Red }
    Write-Host "  ($($issues.Count) issue(s))" -ForegroundColor Red
    exit 1
}
