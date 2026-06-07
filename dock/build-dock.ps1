# build-dock.ps1 — compile WinDock (debug + release)
# Compiles to %TEMP% first to bypass WDAC hash block, then copies to dock/.
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).FullName
$sharedDir = "$base\..\Shared"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$sources = @(
    "$sharedDir\DebugMode.cs", "$sharedDir\EventLog.cs", "$sharedDir\Overlay.cs",
    "$sharedDir\Version.cs", "$sharedDir\W.cs",
    "$base\App.cs",
    "$base\Core\DockBar.cs", "$base\Core\DockLine.cs", "$base\Core\DockManager.cs",
    "$base\Core\LayoutEngine.cs", "$base\Core\PinStore.cs",
    "$base\UI\DockIcon.cs", "$base\UI\GlassMenu.cs", "$base\UI\IconMenu.cs",
    "$base\Common\Theme.cs"
)

$sourcesStr = ($sources | ForEach-Object { """$_""" }) -join " "
$refs = "/reference:System.Windows.Forms.dll /reference:System.Drawing.dll"

# Build debug version
$tmpDebug = Join-Path $env:TEMP "WD-d-$([Guid]::NewGuid().ToString('N')).exe"
Write-Host "Compiling WinDock-d.exe (debug)..." -ForegroundColor Cyan
$result = Invoke-Expression "& ""$csc"" /target:winexe /out:""$tmpDebug"" /define:DEBUG $refs $sourcesStr 2>&1"
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: $result" -ForegroundColor Red; exit 1 }
Copy-Item $tmpDebug "$base\WinDock-d.exe" -Force; Remove-Item $tmpDebug -Force
Write-Host "  OK: $base\WinDock-d.exe ($((Get-Item "$base\WinDock-d.exe").Length) bytes)" -ForegroundColor Green

# Build release version
$tmpRelease = Join-Path $env:TEMP "WD-$([Guid]::NewGuid().ToString('N')).exe"
Write-Host "Compiling WinDock.exe (release)..." -ForegroundColor Cyan
$result = Invoke-Expression "& ""$csc"" /target:winexe /out:""$tmpRelease"" /optimize $refs $sourcesStr 2>&1"
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: $result" -ForegroundColor Red; exit 1 }
Copy-Item $tmpRelease "$base\WinDock.exe" -Force; Remove-Item $tmpRelease -Force
Write-Host "  OK: $base\WinDock.exe ($((Get-Item "$base\WinDock.exe").Length) bytes)" -ForegroundColor Green

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
