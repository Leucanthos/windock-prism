# build-tests.ps1 — compile all C# test harnesses
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName
$testDir = "$base\test"
if (Test-Path "$base\..\Shared") { $global:sharedFiles = @(Get-ChildItem "$base\..\Shared\*.cs" | ForEach-Object { $_.FullName }) } else { $global:sharedFiles = @() }
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    Write-Host "ERROR: csc.exe not found at $csc" -ForegroundColor Red
    Write-Host "Try: C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe (32-bit)" -ForegroundColor Yellow
    exit 1
}

Write-Host "=== Building WinDock Test Suite ===" -ForegroundColor Cyan
Write-Host "Compiler: $csc"
Write-Host "Source base: $base"
Write-Host ""

# Common source files needed by almost all tests
# GlassMenu.cs is needed because DockIcon.SetRightClickActions references it
$commonFiles = @(
    "$base\UI\DockIcon.cs",
    "$base\Common\Theme.cs",
    "$base\UI\GlassMenu.cs"
)

$dockBarFile = "$base\Core\DockBar.cs"
$layoutEngineFile = "$base\Core\LayoutEngine.cs"
$iconMenuFile = "$base\UI\IconMenu.cs"
$glassMenuFile = "$base\UI\GlassMenu.cs"

# Test definitions: name, extra source files
$tests = @(
    @{
        Name = "test-icon-lifecycle"
        ExtraSources = @($dockBarFile)
    },
    @{
        Name = "test-layout"
        ExtraSources = @($dockBarFile)
    },
    @{
        Name = "test-magnification"
        ExtraSources = @()
    },
    @{
        Name = "test-pin-unpin"
        ExtraSources = @()  # standalone COM, no dock sources needed
        Standalone = $true
    },
    @{
        Name = "test-theme-switch"
        ExtraSources = @()
    },
    @{
        Name = "test-taskbar-toggle"
        ExtraSources = @($dockBarFile)
    },
    @{
        Name = "test-context-menu"
        ExtraSources = @($iconMenuFile, $glassMenuFile)
    },
    @{
        Name = "test-badge"
        ExtraSources = @()
    },
    @{
        Name = "test-mutex-singleton"
        ExtraSources = @()
    },
    @{
        Name = "test-coordinates"
        ExtraSources = @()
    },
    @{
        Name = "test-dock-pin"
        ExtraSources = @($dockBarFile, $iconMenuFile, $glassMenuFile)
    },
    @{
        Name = "test-edge-cases"
        ExtraSources = @($dockBarFile, $layoutEngineFile, $iconMenuFile, $glassMenuFile)
    },
    @{
        Name = "test-ui-inspect"
        ExtraSources = @($dockBarFile, $layoutEngineFile)
    },
    @{
        Name = "test-realtime-sync"
        ExtraSources = @($dockBarFile, $iconMenuFile, $glassMenuFile)
    },
    @{
        Name = "test-appbar"
        ExtraSources = @($dockBarFile)
    },
    @{
        Name = "test-messy-user"
        ExtraSources = @($dockBarFile, $layoutEngineFile, $iconMenuFile, $glassMenuFile)
    }
)

$refs = @(
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Drawing.dll"
)
$refsStr = $refs -join " "

$failed = @()
$passed = @()

foreach ($test in $tests) {
    $csFile = "$testDir\$($test.Name).cs"
    $exeFile = "$testDir\$($test.Name).exe"

    if (-not (Test-Path $csFile)) {
        Write-Host "SKIP: $($test.Name).cs not found" -ForegroundColor Yellow
        $failed += $test.Name
        continue
    }

    if ($test.Standalone) {
        # Standalone test: only need the test's own .cs file
        $sources = @($csFile)
    } else {
        $sources = @($csFile) + $sharedFiles + $commonFiles + $test.ExtraSources
    }

    $sourcesStr = ($sources | ForEach-Object { """$_""" }) -join " "
    $cmd = "& ""$csc"" /target:winexe /out:""$exeFile"" $refsStr $sourcesStr 2>&1"

    Write-Host "Building $($test.Name)..." -ForegroundColor Gray
    Write-Host "  Sources: $($sources.Count) files" -ForegroundColor Gray

    $output = Invoke-Expression $cmd

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FAIL: compilation error" -ForegroundColor Red
        Write-Host "  $output" -ForegroundColor Red
        $failed += $test.Name
    } elseif (Test-Path $exeFile) {
        $size = (Get-Item $exeFile).Length
        Write-Host "  OK: $exeFile ($($size / 1KB) KB)" -ForegroundColor Green
        $passed += $test.Name
    } else {
        Write-Host "  FAIL: no output file" -ForegroundColor Red
        $failed += $test.Name
    }
}

Write-Host ""
Write-Host "=== Build Summary ===" -ForegroundColor Cyan
Write-Host "Passed: $($passed.Count)" -ForegroundColor Green
if ($passed.Count -gt 0) {
    $passed | ForEach-Object { Write-Host "  $_" -ForegroundColor Green }
}
Write-Host "Failed: $($failed.Count)" -ForegroundColor $(if ($failed.Count -gt 0) { 'Red' } else { 'Green' })
if ($failed.Count -gt 0) {
    $failed | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}

if ($failed.Count -gt 0) {
    exit 1
}
exit 0
