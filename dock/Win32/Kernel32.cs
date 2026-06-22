using System;
using System.Runtime.InteropServices;

// ============================================================
// Kernel32 — kernel32.dll P/Invoke
// ============================================================

static class Kernel32
{
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
    [DllImport("kernel32.dll")] public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr hObject);

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
}
