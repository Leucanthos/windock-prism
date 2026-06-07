# identify-icons.ps1 — list all dock icon windows with their process names
Add-Type -Name Win32 -Namespace API -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr ch, string c, string t);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int m);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
'@ -StructLayout 'public struct RECT { public int Left,Top,Right,Bottom; }'

$sb = New-Object System.Text.StringBuilder(256)
$rect = New-Object API+RECT

$found = @()
$h = [API.Win32]::FindWindow([NullString]::Value, [NullString]::Value)
while ($h -ne [IntPtr]::Zero) {
    [API.Win32]::GetWindowRect($h, [ref]$rect)
    $w = $rect.Right - $rect.Left
    $h2 = $rect.Bottom - $rect.Top
    if ($w -eq 77 -and $h2 -eq 77 -and $rect.Top -gt 1600) {
        [API.Win32]::GetWindowText($h, $sb, 256)
        $pidv = 0; [API.Win32]::GetWindowThreadProcessId($h, [ref]$pidv)
        try { $pname = (Get-Process -Id $pidv -ErrorAction Stop).ProcessName } catch { $pname = '?' }
        $found += "hwnd=$($h.ToInt64()) pid=$pidv proc=$pname X=$($rect.Left)"
    }
    $h = [API.Win32]::FindWindowEx([IntPtr]::Zero, $h, [NullString]::Value, [NullString]::Value)
}

$found | Sort-Object { [int]($_ -replace '.*X=','') } | ForEach-Object { Write-Host $_ }
