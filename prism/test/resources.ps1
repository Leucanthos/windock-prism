# resources.ps1 — detect GDI/Font handle leaks
# Pass = no undisposed Font allocations in theme init
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName

$issues = @()

$file = "$base\Common\Theme.cs"

# 1. CreateFonts() creates Font objects without Dispose
# Check: new Font(...) in CreateFonts, no corresponding Dispose before next Init/Toggle
$hasCreateFonts = Select-String -Path $file -Pattern 'static void CreateFonts' -Quiet
$hasDispose = Select-String -Path $file -Pattern 'Dispose\(\)' -Quiet

if ($hasCreateFonts -and -not $hasDispose) {
    $issues += "Theme.cs: CreateFonts() creates Font objects without Dispose — GDI leak on theme toggle"
}

# 2. new Font(...) in widget code without static caching
$srcFiles = Get-ChildItem "$base\Components\*.cs"
foreach ($f in $srcFiles) {
    $count = (Select-String -Path $f.FullName -Pattern 'new Font\(' -AllMatches).Matches.Count
    $fontDisposeCount = (Select-String -Path $f.FullName -Pattern '\.Dispose\(\)' -AllMatches).Matches.Count
    # Rough heuristic: more than 0 new Font with no font-specific dispose
    if ($count -gt 0) {
        $fontDisposeCount += (Select-String -Path $f.FullName -Pattern 'using\s*\(.*Font' -AllMatches).Matches.Count
        if ($fontDisposeCount -lt $count) {
            $issues += "$($f.Name): $count new Font() calls — may leak GDI handles"
        }
    }
}

Write-Host "`n=== Resources Test ===" -ForegroundColor Cyan
if ($issues.Count -eq 0) {
    Write-Host "  PASS: No resource leak patterns found" -ForegroundColor Green
    exit 0
} else {
    foreach ($i in $issues) { Write-Host "  FAIL: $i" -ForegroundColor Red }
    Write-Host "  ($($issues.Count) issue(s))" -ForegroundColor Red
    exit 1
}
