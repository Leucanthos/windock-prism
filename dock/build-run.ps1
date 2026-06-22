$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).FullName
$sharedDir = "$base\..\Shared"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

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

$outExe = "$base\WinDock.exe"
Write-Host "Compiling $outExe ..."
$result = Invoke-Expression "& ""$csc"" /target:winexe /out:""$outExe"" /optimize $icon $refs $sourcesStr 2>&1"
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: $result" -ForegroundColor Red; exit 1 }

Write-Host "OK: $outExe ($((Get-Item $outExe).Length) bytes)"
Write-Host "Launching..."
Get-Process WinDock -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1
Start-Process $outExe
Start-Sleep 4
Get-Process WinDock -ErrorAction SilentlyContinue | Select-Object Id,ProcessName
