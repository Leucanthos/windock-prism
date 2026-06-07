# test-badge.ps1 — targeted badge accuracy test
# Checks that dock badge matches actual visible window count.
# Usage: .\test-badge.ps1 [appName]  (default: msedge)

param([string]$AppName = "msedge")

$ErrorActionPreference = 'Stop'
Add-Type -Name Win32 -Namespace Test -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int idx);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwPid);
'@

$GWL_EXSTYLE = -20
$WS_EX_TOOLWINDOW = 0x80

# Count visible titled non-toolwindow windows for processes matching $AppName
function Count-VisibleWindows {
    param([string]$ProcName)
    $seenPids = @{}
    $total = 0
    $hw = [Test.Win32]::GetTopWindow([IntPtr]::Zero)
    while ($hw -ne [IntPtr]::Zero) {
        if (-not [Test.Win32]::IsWindowVisible($hw)) { $hw = [Test.Win32]::GetWindow($hw, 2); continue }
        $ex = [Test.Win32]::GetWindowLong($hw, $GWL_EXSTYLE)
        if ($ex -band $WS_EX_TOOLWINDOW) { $hw = [Test.Win32]::GetWindow($hw, 2); continue }
        $sb = New-Object System.Text.StringBuilder 256
        [Test.Win32]::GetWindowText($hw, $sb, 256) | Out-Null
        if ($sb.Length -eq 0) { $hw = [Test.Win32]::GetWindow($hw, 2); continue }
        $ppid = 0
        [Test.Win32]::GetWindowThreadProcessId($hw, [ref]$ppid) | Out-Null
        if ($seenPids.ContainsKey($ppid)) { $hw = [Test.Win32]::GetWindow($hw, 2); continue }
        try {
            $p = Get-Process -Id $ppid -ErrorAction Stop
            if ($p.ProcessName -eq $ProcName) {
                $seenPids[$ppid] = $true
                # Count ALL visible titled non-toolwindow windows for this PID
                $wp = 0; $hw2 = [Test.Win32]::GetTopWindow([IntPtr]::Zero)
                while ($hw2 -ne [IntPtr]::Zero) {
                    if ([Test.Win32]::IsWindowVisible($hw2)) {
                        $ex2 = [Test.Win32]::GetWindowLong($hw2, $GWL_EXSTYLE)
                        if (-not ($ex2 -band $WS_EX_TOOLWINDOW)) {
                            $sb2 = New-Object System.Text.StringBuilder 256
                            [Test.Win32]::GetWindowText($hw2, $sb2, 256) | Out-Null
                            if ($sb2.Length -gt 0) {
                                $pid2 = 0
                                [Test.Win32]::GetWindowThreadProcessId($hw2, [ref]$pid2) | Out-Null
                                if ($pid2 -eq $ppid) { $wp++ }
                            }
                        }
                    }
                    $hw2 = [Test.Win32]::GetWindow($hw2, 2)
                }
                $total += $wp
                Write-Host "  pid=$ppid windows=$wp total=$total" -ForegroundColor Gray
            }
        } catch { }
        $hw = [Test.Win32]::GetWindow($hw, 2)
    }
    return $total
}

# Read dock badge from event log
function Get-DockBadge {
    param([string]$ProcName)
    $log = Join-Path $env:TEMP "WinDock_events.txt"
    if (-not (Test-Path $log)) { return -1 }
    $lines = Get-Content $log
    $inIcons = $false
    $badge = -1
    foreach ($line in $lines) {
        if ($line -like "=== ICONS*") { $inIcons = $true; continue }
        if (-not $inIcons) { continue }
        if (-not $line.Trim().StartsWith("[")) { break }
        # Parse badge= and pin=
        if ($line -match "badge=(\d+).*pin=(.*)") {
            $pinPath = $Matches[2].Trim()
            $pinName = [System.IO.Path]::GetFileNameWithoutExtension($pinPath)
            if ($pinName -eq $ProcName) {
                $badge = [int]$Matches[1]
                break
            }
        }
    }
    return $badge
}

Write-Host "=== Badge Test: $AppName ===" -ForegroundColor Cyan

$actual = Count-VisibleWindows -ProcName $AppName
$badge = Get-DockBadge -ProcName $AppName

Write-Host ""
Write-Host "Actual visible windows: $actual" -ForegroundColor $(if ($actual -gt 0) { 'Green' } else { 'Yellow' })
Write-Host "Dock badge:            $badge" -ForegroundColor $(if ($badge -eq $actual) { 'Green' } else { 'Red' })

if ($badge -eq -1) {
    Write-Host "RESULT: FAIL — badge not found in event log" -ForegroundColor Red
    exit 1
} elseif ($badge -eq $actual) {
    Write-Host "RESULT: PASS — badge matches window count" -ForegroundColor Green
    exit 0
} else {
    Write-Host "RESULT: FAIL — expected $actual, got $badge" -ForegroundColor Red
    exit 1
}
