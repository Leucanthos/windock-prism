using System;
using System.Runtime.InteropServices;

// ============================================================
// Shell32 — shell32.dll P/Invoke + AppBar constants/structs
// ============================================================

static class Shell32
{
    [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    public const uint ABM_NEW = 0;
    public const uint ABM_REMOVE = 1;
    public const uint ABM_SETSTATE = 0x0000000A;
    public const uint ABE_BOTTOM = 3;
    public const uint ABS_AUTOHIDE = 0x00000001;
}

[StructLayout(LayoutKind.Sequential)]
struct APPBARDATA
{
    public int cbSize;
    public IntPtr hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public IntPtr lParam;
}
