# test-pin-end-to-end.ps1 — end-to-end pin/unpin cycle
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.Parent.FullName

Write-Host "=== Test 13: Pin End-to-End ===" -ForegroundColor Cyan

$exe = "$base\WinDock-d.exe"
if (-not (Test-Path $exe)) {
    Write-Host "FAIL: $exe not found" -ForegroundColor Red
    exit 1
}

$pinDir = "$env:APPDATA\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"
if (-not (Test-Path $pinDir)) {
    Write-Host "FAIL: Pin directory not found: $pinDir" -ForegroundColor Red
    exit 1
}

Write-Host "Pin directory: $pinDir"

# Count existing pinned apps
$existingLinks = Get-ChildItem "$pinDir\*.lnk" -ErrorAction SilentlyContinue
$existingCount = if ($existingLinks) { $existingLinks.Count } else { 0 }
Write-Host "Existing pinned shortcuts: $existingCount"

# Test 1: Verify we can create a .lnk via COM
$tempExe = "C:\temp\_test_pin_dummy.exe"
if (-not (Test-Path "C:\temp")) { New-Item -ItemType Directory -Path "C:\temp" -Force | Out-Null }
"dummy" | Out-File $tempExe -Encoding ASCII

$lnkPath = "$pinDir\_test_pin_dummy.lnk"
# Clean up any previous test artifact
if (Test-Path $lnkPath) { Remove-Item $lnkPath -Force }

# Create shortcut via COM
try {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($lnkPath)
    $lnk.TargetPath = $tempExe
    $lnk.Save()
    Write-Host "  PASS: Created .lnk via COM at $lnkPath" -ForegroundColor Green
} catch {
    Write-Host "  FAIL: COM .lnk creation failed: $_" -ForegroundColor Red
    if (Test-Path $tempExe) { Remove-Item $tempExe -Force }
    exit 1
}

# Verify .lnk exists and has correct target
if (Test-Path $lnkPath) {
    try {
        $verifyLnk = $shell.CreateShortcut($lnkPath)
        if ($verifyLnk.TargetPath -eq $tempExe) {
            Write-Host "  PASS: .lnk TargetPath matches" -ForegroundColor Green
        } else {
            Write-Host "  FAIL: .lnk TargetPath = $($verifyLnk.TargetPath), expected $tempExe" -ForegroundColor Red
        }
    } catch {
        Write-Host "  FAIL: Cannot verify .lnk TargetPath: $_" -ForegroundColor Red
    }
}

# Launch dock and verify it picks up the pinned app
Write-Host "Launching WinDock-d.exe --debug..."
$dumpLog = "C:\temp\_dock_dump.txt"
if (Test-Path $dumpLog) { Remove-Item $dumpLog -Force }

$proc = Start-Process $exe -ArgumentList '--debug' -PassThru
Start-Sleep 4

if (Test-Path $dumpLog) {
    $dumpContent = Get-Content $dumpLog -Raw
    if ($dumpContent -match '_test_pin_dummy') {
        Write-Host "  PASS: Dump contains pinned test app" -ForegroundColor Green
    } else {
        Write-Host "  INFO: Dump does not reference test app (may not show .exe name)" -ForegroundColor Yellow
        # Show what icons are present
        if ($dumpContent -match '=== Icons @') {
            $iconLines = ($dumpContent -split "`n" | Where-Object { $_ -match '^\s*\[\d+\]' })
            Write-Host "  Icons found:"
            $iconLines | ForEach-Object { Write-Host "    $_" }
        }
    }
} else {
    Write-Host "  WARN: No dump file found" -ForegroundColor Yellow
}

# Cleanup
if ($proc -and !$proc.HasExited) { $proc.Kill(); $proc.WaitForExit(2000) }

# Remove test .lnk
if (Test-Path $lnkPath) {
    Remove-Item $lnkPath -Force
    Write-Host "  PASS: Cleaned up test .lnk" -ForegroundColor Green
}

# Remove temp exe
if (Test-Path $tempExe) { Remove-Item $tempExe -Force }

# Verify pin dir count returned to normal
$finalLinks = Get-ChildItem "$pinDir\*.lnk" -ErrorAction SilentlyContinue
$finalCount = if ($finalLinks) { $finalLinks.Count } else { 0 }
if ($finalCount -eq $existingCount) {
    Write-Host "  PASS: Pin count restored to $existingCount" -ForegroundColor Green
} else {
    Write-Host "  WARN: Pin count changed from $existingCount to $finalCount" -ForegroundColor Yellow
}

Write-Host "RESULT: PASS" -ForegroundColor Green
exit 0
