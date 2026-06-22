using System;
using System.Runtime.InteropServices;

// ============================================================
// Structs — shared Win32 struct definitions
// ============================================================

[StructLayout(LayoutKind.Sequential)]
struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
struct RECT
{
    public int Left, Top, Right, Bottom;
    public int Width { get { return Right - Left; } }
    public int Height { get { return Bottom - Top; } }
}

[StructLayout(LayoutKind.Sequential)]
struct MOUSEINPUT
{
    public int dx, dy;
    public uint mouseData, dwFlags, time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
struct INPUT
{
    public uint type;
    public MOUSEINPUT mi;
}

[StructLayout(LayoutKind.Sequential)]
struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public int pt_x;
    public int pt_y;
}
