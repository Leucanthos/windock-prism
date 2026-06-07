# polling.ps1 — detect excessive/expensive polling patterns
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName

$issues = @()

# 1. AudioWidget: brightness WMI must be on its own timer >= 3000ms
$file = "$base\Components\AudioWidget.cs"
$hasBriTimer = Select-String -Path $file -Pattern 'briTimer' -Quiet
$hasVolTimer1s = Select-String -Path $file -Pattern 'volTimer.*Interval=1000' -Quiet
$briIntervalLine = Select-String -Path $file -Pattern 'briTimer.*Interval=(\d+)'
if ($briIntervalLine) {
    $val = [int]$briIntervalLine.Matches.Groups[1].Value
    if ($val -lt 3000) {
        $issues += "AudioWidget.cs: Brightness timer interval=$val ms — should be >= 3000ms"
    }
} else {
    $issues += "AudioWidget.cs: Brightness WMI shares volume's 1s timer — split to slower timer"
}
# Volume at 1s is expected and fine — no check

# 2. RecycleBin: must cache count to skip full COM enum when unchanged
$file = "$base\Components\RecycleBinWidget.cs"
$hasForeach = Select-String -Path $file -Pattern 'foreach.*i in items' -Quiet
$hasCache = Select-String -Path $file -Pattern 'lastCount' -Quiet
if ($hasForeach -and -not $hasCache) {
    $issues += "RecycleBinWidget.cs: Full COM enumeration every refresh without count-change guard"
}

Write-Host "`n=== Polling Test ===" -ForegroundColor Cyan
if ($issues.Count -eq 0) {
    Write-Host "  PASS: Polling intervals are reasonable" -ForegroundColor Green
    exit 0
} else {
    foreach ($i in $issues) { Write-Host "  FAIL: $i" -ForegroundColor Red }
    Write-Host "  ($($issues.Count) issue(s))" -ForegroundColor Red
    exit 1
}
