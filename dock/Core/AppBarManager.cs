using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ============================================================
// AppBarManager — taskbar hide/show, work area, edge guard
// ============================================================

static class AppBarManager
{
    const uint ABM_REMOVE = 1, ABM_NEW = 0, ABE_BOTTOM = 3;
    [DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    static Form edgeGuard;
    public static Form EdgeGuard { get { return edgeGuard; } }

    public static void HideTaskbar()
    {
        if (DebugMode.On) return;
        var tb = User32.FindWindow("Shell_TrayWnd", null);
        if (tb != IntPtr.Zero)
        {
            // Unregister taskbar as appbar → releases reserved work area
            var abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = tb;
            SHAppBarMessage(ABM_REMOVE, ref abd);
            // Move off-screen so Explorer can't show it even if it tries
            User32.ShowWindow(tb, 0);
            int h = Screen.PrimaryScreen.Bounds.Height;
            User32.SetWindowPos(tb, IntPtr.Zero, 0, h + 100, 0, 0, User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_NOACTIVATE);
        }
    }

    public static void RestoreTaskbar()
    {
        var tb = User32.FindWindow("Shell_TrayWnd", null);
        if (tb != IntPtr.Zero)
        {
            // Re-register taskbar as bottom-edge appbar
            var abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = tb;
            abd.uEdge = ABE_BOTTOM;
            SHAppBarMessage(ABM_NEW, ref abd);
            User32.ShowWindow(tb, 5);
        }
    }

    /// <summary>Re-hide taskbar if Explorer shows it (called by 400ms poll)</summary>
    public static void GuardTaskbar()
    {
        var tb = User32.FindWindow("Shell_TrayWnd", null);
        if (tb != IntPtr.Zero && User32.IsWindowVisible(tb))
            User32.ShowWindow(tb, 0);
    }

    /// <summary>Create transparent 4px strip at screen bottom — blocks mouse from reaching
    /// the screen edge so Explorer doesn't detect it and show the hidden taskbar.</summary>
    public static void CreateEdgeGuard()
    {
        if (edgeGuard != null) return;
        var scr = Screen.PrimaryScreen.Bounds;
        edgeGuard = new Form
        {
            Size = new System.Drawing.Size(scr.Width, 4),
            Location = new System.Drawing.Point(0, scr.Height - 4),
            FormBorderStyle = FormBorderStyle.None,
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = System.Drawing.Color.Black,
            TransparencyKey = System.Drawing.Color.Black,
        };
        edgeGuard.Show();
    }

    public static void DestroyEdgeGuard()
    {
        if (edgeGuard != null) { edgeGuard.Close(); edgeGuard.Dispose(); edgeGuard = null; }
    }
}
