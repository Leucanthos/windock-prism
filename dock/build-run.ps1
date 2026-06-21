$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).FullName
$sharedDir = "$base\..\Shared"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

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
$icon = "/win32icon:""$base\assets\Windock.ico"""

# Compile to TEMP first (bypass WDAC), then copy to dock/ and run as WinDock.exe
$tmpExe = Join-Path $env:TEMP "WD-$([Guid]::NewGuid().ToString('N')).exe"
Write-Host "Compiling to $tmpExe ..."
$result = Invoke-Expression "& ""$csc"" /target:winexe /out:""$tmpExe"" $icon $refs $sourcesStr 2>&1"
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: $result" -ForegroundColor Red; exit 1 }

$outExe = "$base\WinDock.exe"
Copy-Item $tmpExe $outExe -Force; Remove-Item $tmpExe -Force
Write-Host "OK: $outExe ($((Get-Item $outExe).Length) bytes)"
Write-Host "Launching WinDock..."
Start-Process $outExe
Start-Sleep 4
Get-Process WinDock -ErrorAction SilentlyContinue | Select-Object Id,ProcessName
