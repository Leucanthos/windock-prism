# security.ps1 — detect security-sensitive patterns
# Pass = no plaintext WiFi passwords, no HTTP geo-IP
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName

$issues = @()

# 1. WiFi password XML written to temp file
$file = "$base\Components\WiFiWidget.cs"
if (Select-String -Path $file -Pattern 'C:\\temp\\_wifi_profile\.xml' -Quiet) {
    $issues += "WiFiWidget.cs: WPA2 password written to temp file in plaintext"
}

# 2. HTTP (not HTTPS) for geo-IP lookup
$file = "$base\Components\NetworkWidget.cs"
if (Select-String -Path $file -Pattern 'http://ip-api\.com' -Quiet) {
    $issues += "NetworkWidget.cs: Uses HTTP (not HTTPS) for geo-IP lookup — MITM risk"
}

# 3. netsh redirect to temp file (should use stdout)
if (Select-String -Path $file -Pattern 'C:\\temp\\_wifi' -Quiet) {
    $issues += "NetworkWidget.cs/WiFiWidget.cs: netsh output redirected to temp file"
}

# 4. WebClient — note: standard in .NET Framework 4.x, migrate to HttpClient on .NET 8+
$srcFiles = Get-ChildItem "$base\Components\*.cs"
foreach ($f in $srcFiles) {
    if (Select-String -Path $f.FullName -Pattern 'new WebClient\(\)' -Quiet) {
        $issues += "$($f.Name): Uses WebClient (acceptable in .NET Framework 4.x; migrate to HttpClient on .NET 8+)"
    }
}

Write-Host "`n=== Security Test ===" -ForegroundColor Cyan
if ($issues.Count -eq 0) {
    Write-Host "  PASS: No security issues found" -ForegroundColor Green
    exit 0
} else {
    foreach ($i in $issues) { Write-Host "  FAIL: $i" -ForegroundColor Red }
    Write-Host "  ($($issues.Count) issue(s))" -ForegroundColor Red
    exit 1
}
