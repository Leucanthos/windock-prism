using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ============================================================
// W — prism-specific Win32 desktop pinning & bar helpers
// ============================================================
public static partial class W
{
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    const uint SWP_NOSIZE=0x0001, SWP_NOMOVE=0x0002, SWP_NOZORDER=0x0004;
    const uint SWP_NOACTIVATE=0x0010, SWP_SHOWWINDOW=0x0040;
    const int SW_SHOW=5, GWL_EXSTYLE=-20;
    const int WS_EX_NOACTIVATE=0x08000000, WS_EX_TOOLWINDOW=0x80;
    static readonly IntPtr HWND_BOTTOM = (IntPtr)1;

    // ── Desktop placement ────────────────────────────────────
    public static void EnsureWorkerW() { }

    /// <summary>Place form at desktop level: bottom Z-order, no activate, no taskbar.</summary>
    public static void PinToDesktop(Form f)
    {
        try
        {
            int ex = GetWindowLong(f.Handle, GWL_EXSTYLE);
            SetWindowLong(f.Handle, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            SetWindowPos(f.Handle, HWND_BOTTOM, f.Left, f.Top, f.Width, f.Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
            EventLog.Info("Pin '" + (f.Text ?? "?") + "' → HWND_BOTTOM");
        }
        catch (Exception ex) { EventLog.Error("Pin: " + ex.Message); }
    }

    /// <summary>Ensure form stays at bottom of Z-order.</summary>
    public static void RepinIfNeeded(Form f)
    {
        try
        {
            if (f.IsDisposed || !f.IsHandleCreated) return;
            SetWindowPos(f.Handle, HWND_BOTTOM, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch (Exception ex) { EventLog.Error("Repin: " + ex.Message); }
    }

    // ── Bar / progress UI helpers ────────────────────────────
    public static Panel Bar(Color fg, int w, int h, int x, int y)
    {
        var p = new Panel { Size = new Size(w, h), Location = new Point(x, y), BackColor = Theme.BarBg };
        var f2 = new Panel { Size = new Size(0, h), Location = new Point(0, 0), BackColor = fg };
        p.Controls.Add(f2); p.Tag = f2;
        return p;
    }

    public static void BarSet(Panel bar, int pct)
    {
        if (bar != null && bar.Tag != null)
            ((Panel)bar.Tag).Width = pct * bar.Width / 100;
    }

    public static void BarColor(Panel bar, Color fg)
    {
        if (bar != null)
        {
            bar.BackColor = Theme.BarBg;
            if (bar.Tag != null) ((Panel)bar.Tag).BackColor = fg;
        }
    }
}
