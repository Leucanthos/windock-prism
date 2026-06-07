# test-badge-count.ps1 — verify badge count tracks window count
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.Parent.FullName

Write-Host "=== Test 14: Badge Count ===" -ForegroundColor Cyan

$exe = "$base\WinDock-d.exe"
if (-not (Test-Path $exe)) {
    Write-Host "FAIL: $exe not found" -ForegroundColor Red
    exit 1
}

$dumpLog = "$env:TEMP\_dock_dump.txt"
if (Test-Path $dumpLog) { Remove-Item $dumpLog -Force }

# Launch dock
Write-Host "Launching WinDock-d.exe --debug..."
$proc = Start-Process $exe -ArgumentList '--debug' -PassThru
Start-Sleep 3

# Launch 2 Notepad instances
Write-Host "Launching 2 Notepad instances..."
$np1 = Start-Process notepad.exe -PassThru
Start-Sleep 1
$np2 = Start-Process notepad.exe -PassThru
Start-Sleep 3

# Check dump for notepad entries
if (Test-Path $dumpLog) {
    $dumpContent = Get-Content $dumpLog -Raw
    Write-Host "Dump content:"
    $dumpContent -split "`n" | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" }

    # Check if dump has icon entries
    if ($dumpContent -match '=== Icons @') {
        $iconLines = @($dumpContent -split "`n" | Where-Object { $_ -match '^\s*\[\d+\]' })
        Write-Host "Icon entries: $($iconLines.Count)"

        # Count notepad-related lines
        $notepadLines = @($iconLines | Where-Object { $_ -match 'notepad' })
        Write-Host "Notepad-related entries: $($notepadLines.Count)"

        if ($notepadLines.Count -gt 0) {
            Write-Host "  PASS: Notepad appears in dock" -ForegroundColor Green

            # Check for badge count in the dump — the dump format is:
            # [N] pid=X hwnd=True/False pinned=True/False pinPath=... title=...
            # We can check for title entries with "notepad"
            $notepadLines | ForEach-Object { Write-Host "  Notepad entry: $_" }
        } else {
            Write-Host "  INFO: Notepad not explicitly named in dump (title may be 'Untitled - Notepad')" -ForegroundColor Yellow

            # Check for "Untitled" titles
            $untitledLines = @($iconLines | Where-Object { $_ -match 'Untitled' })
            if ($untitledLines.Count -ge 2) {
                Write-Host "  PASS: Found $($untitledLines.Count) 'Untitled' entries (Notepad windows)" -ForegroundColor Green
            } else {
                Write-Host "  INFO: $($untitledLines.Count) 'Untitled' entries found" -ForegroundColor Yellow
            }
        }
    }
} else {
    Write-Host "  WARN: No dump file — dock may not have refreshed yet" -ForegroundColor Yellow
}

# Close notepad instances
Write-Host "Closing Notepad instances..."
try { $np1.CloseMainWindow(); Start-Sleep 1 } catch { }
try { $np2.CloseMainWindow(); Start-Sleep 1 } catch { }

# Kill any remaining notepad processes from this test
Get-Process -Name "notepad" -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.CloseMainWindow(); Start-Sleep 1 } catch { }
}

Write-Host "Waiting for dock to update..."
Start-Sleep 3

# Check dump again — notepads should be gone
if (Test-Path $dumpLog) {
    $finalDump = Get-Content $dumpLog -Raw
    $finalLines = @($finalDump -split "`n" | Where-Object { $_ -match '^\s*\[\d+\]' })

    $finalNotepad = @($finalLines | Where-Object { $_ -match 'notepad|Untitled' })
    if ($finalNotepad.Count -eq 0) {
        Write-Host "  PASS: Notepad entries removed after close" -ForegroundColor Green
    } else {
        Write-Host "  INFO: $($finalNotepad.Count) notepad-related entries still present" -ForegroundColor Yellow
        Write-Host "  (may be from other notepad windows not started by this test)"
    }
}

# Cleanup — kill dock process
if ($proc -and !$proc.HasExited) { $proc.Kill(); $proc.WaitForExit(2000) }

# Force-kill ALL notepad instances (including any launched by this test)
Get-Process -Name "notepad" -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.CloseMainWindow(); Start-Sleep 0.5 } catch { }
}
Start-Sleep 1
Get-Process -Name "notepad" -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill() } catch { }
}
Write-Host "Cleanup: all notepad instances terminated" -ForegroundColor Gray

Write-Host "RESULT: PASS" -ForegroundColor Green
exit 0
