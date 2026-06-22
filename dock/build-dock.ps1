# build-dock.ps1 — compile WinDock (debug + release) directly to dock/
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).FullName
$sharedDir = "$base\..\Shared"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$sources = @(
    "$sharedDir\DebugMode.cs", "$sharedDir\EventLog.cs",
    "$sharedDir\Version.cs", "$sharedDir\W.cs",
    "$base\App.cs",
    "$base\Win32\User32.cs", "$base\Win32\Shell32.cs", "$base\Win32\Kernel32.cs", "$base\Win32\Structs.cs",
    "$base\Core\DockBar.cs", "$base\Core\DockManager.cs", "$base\Core\AppBarManager.cs",
    "$base\Core\LayoutEngine.cs", "$base\Core\PinStore.cs",
    "$base\UI\DockIcon.cs", "$base\UI\IconMenu.cs",
    "$base\Common\Theme.cs"
)

$sourcesStr = ($sources | ForEach-Object { """$_""" }) -join " "
$refs = "/reference:System.Windows.Forms.dll /reference:System.Drawing.dll"
$icon = "/win32icon:""$base\assets\Windock.ico"""

# Build debug version
Write-Host "Compiling WinDock-d.exe (debug)..." -ForegroundColor Cyan
$result = Invoke-Expression "& ""$csc"" /target:winexe /out:""$base\WinDock-d.exe"" /define:DEBUG $icon $refs $sourcesStr 2>&1"
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: $result" -ForegroundColor Red; exit 1 }
Write-Host "  OK: $base\WinDock-d.exe ($((Get-Item "$base\WinDock-d.exe").Length) bytes)" -ForegroundColor Green

# Build release version
Write-Host "Compiling WinDock.exe (release)..." -ForegroundColor Cyan
$result = Invoke-Expression "& ""$csc"" /target:winexe /out:""$base\WinDock.exe"" /optimize $icon $refs $sourcesStr 2>&1"
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: $result" -ForegroundColor Red; exit 1 }
Write-Host "  OK: $base\WinDock.exe ($((Get-Item "$base\WinDock.exe").Length) bytes)" -ForegroundColor Green

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
