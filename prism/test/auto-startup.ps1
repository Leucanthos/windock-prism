# auto-startup.ps1 -- Prism auto-start test harness
# Exit 0 = all pass, non-0 = failure
param([switch]$SimulateLogon,[switch]$Fix,[switch]$Clean)
$ErrorActionPreference='Stop'
$base=(Get-Item(Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName
$exe="$base\WinPrism.exe"
$exeD="$base\WinPrism-d.exe"
$rp='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$rn='Prism'
$csc='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$ok=0;$fail=0

function Step($t){Write-Host "
--- $t ---" -ForegroundColor Yellow}
function Check($n,$b){try{&$b;Write-Host "  PASS: $n" -ForegroundColor Green;$script:ok++}catch{Write-Host "  FAIL: $n - $_" -ForegroundColor Red;$script:fail++}}

if($Clean){
    Write-Host '=== Clean ===' -ForegroundColor Cyan
    try{taskkill/F/IM WinPrism.exe 2>$null|Out-Null}catch{}
    try{taskkill/F/IM WinPrism-d.exe 2>$null|Out-Null}catch{}
    Start-Sleep 1
    try{Remove-ItemProperty -Path $rp -Name $rn -ErrorAction SilentlyContinue}catch{}
    try{Remove-ItemProperty -Path $rp -Name DesktopWidgets -ErrorAction SilentlyContinue}catch{}
    Write-Host '
=== Done ===' -ForegroundColor Green;exit 0
}

if($Fix){
    Write-Host '=== Fix Registry ===' -ForegroundColor Cyan
    if(-not(Test-Path $exe)){Write-Host "FAIL: $exe missing" -ForegroundColor Red;exit 1}
    try{Remove-ItemProperty -Path $rp -Name DesktopWidgets -ErrorAction SilentlyContinue}catch{}
    Set-ItemProperty -Path $rp -Name $rn -Value $exe
    $r=(Get-ItemProperty -Path $rp -Name $rn).$rn
    if($r-eq$exe){Write-Host "  SET: $rn = $exe" -ForegroundColor Green}else{Write-Host 'FAIL: registry write' -ForegroundColor Red;exit 1}
    Write-Host '
=== Done ===' -ForegroundColor Green;Write-Host 'Test: .\test\auto-startup.ps1 -SimulateLogon' -ForegroundColor White;exit 0
}

if($SimulateLogon){
    Write-Host '=== Simulate Logon ===' -ForegroundColor Cyan
    $re=try{(Get-ItemProperty -Path $rp -Name $rn -ErrorAction Stop).$rn}catch{Write-Host "no $rn in registry" -ForegroundColor Red;exit 1}
    Write-Host "  Registry: $re" -ForegroundColor DarkGray
    $te=$re-replace'^"|"$',''
    if(-not(Test-Path $te)){Write-Host "FAIL: $te missing" -ForegroundColor Red;exit 1}
    try{taskkill/F/IM(Split-Path $te-Leaf)2>$null|Out-Null}catch{}
    Start-Sleep 1;Write-Host "  Launch: $te" -ForegroundColor White
    $proc=Start-Process $te -PassThru;Start-Sleep 4;$proc.Refresh()
    if($proc.HasExited){Write-Host "FAIL: exit $($proc.ExitCode)" -ForegroundColor Red;exit 1}
    Write-Host "  OK: PID $($proc.Id)" -ForegroundColor Green
    Add-Type -Name WT -Namespace T -MemberDefinition @'
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWinProc lpEnumFunc, IntPtr lParam);
    public delegate bool EnumWinProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
'@
    $ft=New-Object Collections.Generic.List[string]
    $cb={param($h,$l)$sb=New-Object Text.StringBuilder(256);[T.WT]::GetWindowText($h,$sb,256);$t=$sb.ToString();if($t.Length-gt0){$ft.Add($t)};return $true}
    [T.WT]::EnumWindows($cb,[IntPtr]::Zero)
    $af=$true;foreach($w in @('TopBar','System','Disk','Network','Recycle Bin')){if($ft-match[regex]::Escape($w)){Write-Host "  $w" -ForegroundColor Green}else{Write-Host "  $w MISS" -ForegroundColor Yellow;$af=$false}}
    Start-Sleep 2;$proc.Refresh()
    if($proc.HasExited){Write-Host 'FAIL: died' -ForegroundColor Red;exit 1}
    Write-Host '  STABLE' -ForegroundColor Green;Write-Host '
=== Done ===' -ForegroundColor Green
    if(-not$af){Write-Host 'Some windows may be hidden' -ForegroundColor Yellow}
    Write-Host 'Ctrl+C to stop' -ForegroundColor DarkGray
    try{while($true){Start-Sleep 1;$proc.Refresh();if($proc.HasExited){break}}}catch{};exit 0
}

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  Prism Auto-Start Test' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host "  $base"
Write-Host "  $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor DarkGray
try{taskkill/F/IM WinPrism.exe 2>$null|Out-Null}catch{}
try{taskkill/F/IM WinPrism-d.exe 2>$null|Out-Null}catch{}
Start-Sleep 1

Step 'Step 1: Source'
Check 'Prism.cs exists'{if(-not(Test-Path "$base\Prism.cs")){throw'missing'};$true}
Check 'Uses HKCU'{$c=Get-Content "$base\Prism.cs" -Raw;if($c-notmatch'Registry\.CurrentUser'){throw'no HKCU'};$true}

Step 'Step 2: Compile'
Remove-Item $exe -Force -ErrorAction SilentlyContinue
Remove-Item $exeD -Force -ErrorAction SilentlyContinue
&$csc /target:winexe /out:$exe /win32manifest:"$base\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll /reference:System.Management.dll /reference:Microsoft.VisualBasic.dll /reference:"$base\native\OpenHardwareMonitorLib.dll" "$base\Common\*.cs" "$base\Common\Debug\Visual\*.cs" "$base\Common\Debug\Info\*.cs" "$base\Components\*.cs" "$base\Prism.cs" 2>&1
if($LASTEXITCODE-ne0-or-not(Test-Path $exe)){Write-Host 'FAIL: compile' -ForegroundColor Red;exit 1}
Write-Host "  OK: $([math]::Round((Get-Item $exe).Length/1KB)) KB" -ForegroundColor Green
&$csc /target:winexe /out:$exeD /win32manifest:"$base\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll /reference:System.Management.dll /reference:Microsoft.VisualBasic.dll /reference:"$base\native\OpenHardwareMonitorLib.dll" "$base\Common\*.cs" "$base\Common\Debug\Visual\*.cs" "$base\Common\Debug\Info\*.cs" "$base\Components\*.cs" "$base\Prism.cs" 2>&1
if($LASTEXITCODE-eq0-and(Test-Path $exeD)){Write-Host "  OK-d: $([math]::Round((Get-Item $exeD).Length/1KB)) KB" -ForegroundColor Green}else{Write-Host '  OK-d: skip' -ForegroundColor Yellow}

Step 'Step 3: Registry'
try{Remove-ItemProperty -Path $rp -Name $rn -ErrorAction SilentlyContinue}catch{}
Check 'HKCU write'{Set-ItemProperty -Path $rp -Name $rn -Value $exe;$r=(Get-ItemProperty -Path $rp -Name $rn).$rn;if($r-ne$exe){throw'mismatch'};$true}
Check 'File exists'{if(-not(Test-Path $exe)){throw'missing'};$true}
Check 'Debug skips reg'{$p=Start-Process $exeD -ArgumentList '--debug' -PassThru -WindowStyle Hidden;Start-Sleep 3;$p.Refresh();$alive=-not$p.HasExited;$ra=(Get-ItemProperty -Path $rp -Name $rn -ErrorAction SilentlyContinue).$rn;try{$p.Kill()}catch{};if($ra-ne$exe){throw'debug changed reg!'};if(-not$alive){throw'debug failed to start'};$true}

Step 'Step 4: Launch'
Check 'Start'{$p=Start-Process $exe -PassThru -WindowStyle Hidden;Start-Sleep 4;$p.Refresh();if($p.HasExited){throw"exit $($p.ExitCode)"};$true}

Step 'Step 5: Windows'
Add-Type -Name WT2 -Namespace T2 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWinProc lpEnumFunc, IntPtr lParam);
public delegate bool EnumWinProc(IntPtr hWnd, IntPtr lParam);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
'@
$ft=New-Object Collections.Generic.List[string]
$cb={param($h,$l)$sb=New-Object Text.StringBuilder(256);[T2.WT2]::GetWindowText($h,$sb,256);$t=$sb.ToString();if($t.Length-gt0){$ft.Add($t)};return $true}
[T2.WT2]::EnumWindows($cb,[IntPtr]::Zero)
$af=$true;foreach($w in @('TopBar','System','Disk','Network','Recycle Bin')){if($ft-match[regex]::Escape($w)){Write-Host "  $w" -ForegroundColor Green}else{Write-Host "  $w MISS" -ForegroundColor Yellow;$af=$false}}

Step 'Step 6: Stability'
$p=Get-Process -Name WinPrism -ErrorAction SilentlyContinue
if(-not$p){Write-Host 'FAIL: not running' -ForegroundColor Red;$fail++}else{Start-Sleep 3;$p.Refresh();if($p.HasExited){Write-Host 'FAIL: died' -ForegroundColor Red;$fail++}else{Write-Host "  OK: PID $($p.Id)" -ForegroundColor Green;$ok++}}

Step 'Step 7: Persist'
Check 'Reg entry kept'{$r=(Get-ItemProperty -Path $rp -Name $rn -ErrorAction SilentlyContinue).$rn;if($r-ne$exe){throw'removed'};$true}

Step 'Cleanup'
try{taskkill/F/IM WinPrism.exe 2>$null|Out-Null}catch{}
try{taskkill/F/IM WinPrism-d.exe 2>$null|Out-Null}catch{}
Write-Host '  KEPT registry (auto-start boot)' -ForegroundColor DarkGray

Write-Host '========================================' -ForegroundColor Cyan
Write-Host "  $ok pass, $fail fail" -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
if($fail-gt0){Write-Host 'FAILURES' -ForegroundColor Red;exit 1}else{Write-Host 'ALL PASS' -ForegroundColor Green;exit 0}
