using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

// ============================================================
// DockBar — public utility methods shared by DockManager
// ============================================================

static class DockBar
{
    // P/Invoke used by utility methods
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out int pid);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
    [DllImport("kernel32.dll")] static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr hObject);

    const uint WM_CLOSE = 0x0010;
    const uint GW_HWNDNEXT = 2;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const int SW_RESTORE = 9, SW_SHOW = 5;

    public static IntPtr FindNextWindow(IntPtr a) { return a == IntPtr.Zero ? GetTopWindow(IntPtr.Zero) : GetWindow(a, GW_HWNDNEXT); }
    public static int GetPid(IntPtr hWnd) { int pid; GetWindowThreadProcessId(hWnd, out pid); return pid; }
    public static void GetWindowThreadProcessId2(IntPtr h, out int o) { GetWindowThreadProcessId(h, out o); }

    public static void FocusWindow(IntPtr hWnd)
    {
        if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
        else if (!IsWindowVisible(hWnd)) ShowWindow(hWnd, SW_SHOW);
        SwitchToThisWindow(hWnd, true);
    }

    public static void CloseWindow(IntPtr hWnd) { PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
    public static void KillProcess(int pid) { try { Process.GetProcessById(pid).Kill(); } catch { } }
    public static string GetExePath(int pid) { try { return Process.GetProcessById(pid).MainModule.FileName; } catch { return null; } }
    public static string GetWinTitle(IntPtr hWnd) { var sb = new System.Text.StringBuilder(256); GetWindowText(hWnd, sb, 256); return sb.ToString(); }
    public static bool IsVisibleWindow(IntPtr hWnd) { if (!IsWindowVisible(hWnd)) return false; var sb = new System.Text.StringBuilder(256); GetWindowText(hWnd, sb, 256); return sb.Length > 0; }

    public static string GetProcessPathSafe(int pid)
    {
        try { return Process.GetProcessById(pid).MainModule.FileName; }
        catch
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new System.Text.StringBuilder(260);
                uint size = 260;
                if (QueryFullProcessImageName(h, 0, sb, ref size))
                    return sb.ToString();
            }
            finally { CloseHandle(h); }
            return null;
        }
    }
}
