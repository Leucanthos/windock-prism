# codecheck.ps1 — detect obsolete APIs, hardcoded values, fragile patterns
# Pass = no deprecated APIs, no screen-size-dependent hardcodes
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName

$issues = @()

# 1. WebClient usage (obsolete)
$srcFiles = Get-ChildItem "$base\*.cs", "$base\Common\*.cs", "$base\Components\*.cs"
foreach ($f in $srcFiles) {
    if (Select-String -Path $f.FullName -Pattern 'new WebClient\(\)' -Quiet) {
        $issues += "$($f.Name): Uses WebClient (obsolete, no timeout, no modern TLS)"
    }
}

# 2. Manual JSON parsing with IndexOf (fragile)
$file = "$base\Components\NetworkWidget.cs"
if (Select-String -Path $file -Pattern 'IndexOf.*json' -Quiet -or
    Select-String -Path $file -Pattern 'ExtractJson' -Quiet) {
    $issues += "NetworkWidget.cs: Manual JSON parsing with IndexOf — fragile"
}

# 3. Hardcoded screen positions that assume 1600+ px width
$file = "$base\settings.ini"
$hasStaticX = Select-String -Path $file -Pattern 'TopX=\d{4}' -Quiet
if ($hasStaticX) {
    $issues += "settings.ini: Hardcoded X position (assumes 1600+ px screen width)"
}

# 4. INI parsing with long if-else chain (>10 branches)
$file = "$base\Common\Settings.cs"
$ifCount = (Select-String -Path $file -Pattern 'else if\(k==' -AllMatches).Matches.Count
if ($ifCount -gt 10) {
    $issues += "Settings.cs: $ifCount else-if branches in INI parser — fragile, hard to extend"
}

Write-Host "`n=== Code Quality Test ===" -ForegroundColor Cyan
if ($issues.Count -eq 0) {
    Write-Host "  PASS: No code quality issues found" -ForegroundColor Green
    exit 0
} else {
    foreach ($i in $issues) { Write-Host "  FAIL: $i" -ForegroundColor Red }
    Write-Host "  ($($issues.Count) issue(s))" -ForegroundColor Red
    exit 1
}
