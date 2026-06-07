# artifacts.ps1 — detect debug/debug artifacts in production code
# Pass = no debug dump calls, no temp-file debug output
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName

$issues = @()

$file = "$base\Common\Theme.cs"

# 1. DumpGlassDebug() writes to C:\temp\_theme_debug.txt
if (Select-String -Path $file -Pattern 'DumpGlassDebug' -Quiet) {
    $issues += "Theme.cs: DumpGlassDebug() writes debug output to C:\temp\_theme_debug.txt"
}

# 2. SampleBmp() used only for debug
if (Select-String -Path $file -Pattern 'void SampleBmp' -Quiet) {
    $issues += "Theme.cs: SampleBmp() is debug helper — should be removed or #if DEBUG"
}

# 3. Any File.AppendAllText/WriteAllText to C:\temp (debug logging)
$srcFiles = Get-ChildItem "$base\*.cs", "$base\Common\*.cs", "$base\Components\*.cs"
foreach ($f in $srcFiles) {
    $tempWrites = Select-String -Path $f.FullName -Pattern 'C:\\temp\\' | Where-Object { $_.Line -match 'Write|Append' }
    foreach ($m in $tempWrites) {
        $issues += "$($f.Name):$($m.LineNumber): Debug write to C:\temp\ — $($m.Line.Trim())"
    }
}

# 4. Console.WriteLine debug output in production
foreach ($f in $srcFiles) {
    $consoleLines = Select-String -Path $f.FullName -Pattern 'Console\.Write' | Where-Object { $_.Line -notmatch '//.*Console' }
    foreach ($m in $consoleLines) {
        $issues += "$($f.Name):$($m.LineNumber): Console output in production — $($m.Line.Trim())"
    }
}

Write-Host "`n=== Artifacts Test ===" -ForegroundColor Cyan
if ($issues.Count -eq 0) {
    Write-Host "  PASS: No debug artifacts found" -ForegroundColor Green
    exit 0
} else {
    foreach ($i in $issues) { Write-Host "  FAIL: $i" -ForegroundColor Red }
    Write-Host "  ($($issues.Count) issue(s))" -ForegroundColor Red
    exit 1
}
