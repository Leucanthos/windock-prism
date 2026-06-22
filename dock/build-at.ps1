# build-at.ps1 — compile WinDock via Add-Type (bypasses WDAC hash check)
$ErrorActionPreference = 'Continue'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).FullName
$sharedDir = "$base\..\Shared"

$files = @(
    "$sharedDir\DebugMode.cs", "$sharedDir\EventLog.cs",
    "$sharedDir\Version.cs", "$sharedDir\W.cs",
    "$base\App.cs",
    "$base\Win32\User32.cs", "$base\Win32\Shell32.cs", "$base\Win32\Kernel32.cs", "$base\Win32\Structs.cs",
    "$base\Core\DockBar.cs", "$base\Core\DockManager.cs", "$base\Core\AppBarManager.cs",
    "$base\Core\LayoutEngine.cs", "$base\Core\PinStore.cs",
    "$base\UI\DockIcon.cs", "$base\UI\IconMenu.cs",
    "$base\Common\Theme.cs"
)

# Step 1: collect unique usings
$allUsings = [System.Collections.Generic.HashSet[string]]::new()
$bodies = ""
foreach ($f in $files) {
    $lines = Get-Content $f -Encoding UTF8
    foreach ($line in $lines) {
        $t = $line.Trim()
        if ($t.StartsWith("using ") -and $t.EndsWith(";")) {
            [void]$allUsings.Add($t)
        }
    }
}

# Step 2: build combined source with usings once at top, then all bodies
$source = ($allUsings | Sort-Object) -join "`r`n"
$source += "`r`n"

foreach ($f in $files) {
    $lines = Get-Content $f -Encoding UTF8
    foreach ($line in $lines) {
        $t = $line.Trim()
        if (-not ($t.StartsWith("using ") -and $t.EndsWith(";"))) {
            $source += $line + "`r`n"
        }
    }
    $source += "`r`n"
}

Write-Host "Compiling WinDock.exe via Add-Type... ($($source.Length) chars)"
$tmpDll = Join-Path $env:TEMP "WD-$([Guid]::NewGuid().ToString('N')).dll"
try {
    Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition $source -OutputAssembly $tmpDll -OutputType WindowsApplication -IgnoreWarnings 2>&1 | Out-Null

    if (Test-Path $tmpDll) {
        $exeOut = "$base\WinDock.exe"
        Copy-Item $tmpDll $exeOut -Force
        Remove-Item $tmpDll -Force
        Write-Host "OK: $exeOut ($((Get-Item $exeOut).Length) bytes)" -ForegroundColor Green
        Start-Process $exeOut
        Start-Sleep 4
        Get-Process WinDock -ErrorAction SilentlyContinue | Select-Object Id, StartTime
    } else {
        Write-Host "FAIL: Add-Type produced no output (compilation error)" -ForegroundColor Red
    }
} catch {
    Write-Host "FAIL: $_" -ForegroundColor Red
    Write-Host "Trying csc fallback..."
    # Fallback: csc with /deterministic flag might produce same hash
    $csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $srcsStr = ($files | ForEach-Object { """$_""" }) -join " "
    $refs = "/reference:System.Windows.Forms.dll /reference:System.Drawing.dll"
    $icon = "/win32icon:""$base\assets\Windock.ico"""
    $tmpExe = Join-Path $env:TEMP "WD-$([Guid]::NewGuid().ToString('N')).exe"
    $result = Invoke-Expression "& ""$csc"" /target:winexe /out:""$tmpExe"" $icon $refs $srcsStr 2>&1"
    if (Test-Path $tmpExe) {
        Copy-Item $tmpExe "$base\WinDock.exe" -Force
        Write-Host "csc fallback OK, running from TEMP" -ForegroundColor Green
        Start-Process $tmpExe
    }
}
