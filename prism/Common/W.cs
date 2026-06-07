using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public static class W
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
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

    public static void Round(Form f) { int c=2; DwmSetWindowAttribute(f.Handle,33,ref c,4); }

    // ── Desktop placement ────────────────────────────────────
    // Instead of SetParent (which Windows 11 aggressively resets),
    // place forms at the bottom of the Z-order and keep them there.

    public static void EnsureWorkerW() { } // no-op for this approach

    /// <summary>Place form at desktop level: bottom Z-order, no activate, no taskbar.</summary>
    public static void PinToDesktop(Form f)
    {
        try
        {
            // Ensure tool window (no taskbar) + no activate on click
            int ex = GetWindowLong(f.Handle, GWL_EXSTYLE);
            SetWindowLong(f.Handle, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

            // Place at bottom of Z-order
            SetWindowPos(f.Handle, HWND_BOTTOM, f.Left, f.Top, f.Width, f.Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
            EventLog.Info("Pin '"+ (f.Text??"?") +"' → HWND_BOTTOM");
        }
        catch (Exception ex) { EventLog.Error("Pin: "+ex.Message); }
    }

    /// <summary>Ensure form stays at bottom of Z-order.</summary>
    public static void RepinIfNeeded(Form f)
    {
        try
        {
            if (f.IsDisposed || !f.IsHandleCreated) return;
            // Keep at bottom — windows maximize above it naturally
            SetWindowPos(f.Handle, HWND_BOTTOM, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch (Exception ex) { EventLog.Error("Repin: "+ex.Message); }
    }

    // ── UI helpers ───────────────────────────────────────────

    public static Label Lbl(string t,Font f,Color c,int w,int h,int x,int y){return new Label{Text=t,ForeColor=c,Font=f,AutoSize=false,Size=new Size(w,h),Location=new Point(x,y)};}
    public static Panel Bar(Color fg,int w,int h,int x,int y){var p=new Panel{Size=new Size(w,h),Location=new Point(x,y),BackColor=Theme.BarBg};var f2=new Panel{Size=new Size(0,h),Location=new Point(0,0),BackColor=fg};p.Controls.Add(f2);p.Tag=f2;return p;}
    public static void BarSet(Panel bar, int pct){ if(bar!=null&&bar.Tag!=null)((Panel)bar.Tag).Width=pct*bar.Width/100;}
    public static void BarColor(Panel bar, Color fg){if(bar!=null){bar.BackColor=Theme.BarBg;if(bar.Tag!=null)((Panel)bar.Tag).BackColor=fg;}}
    static bool dragging; static Point lastPos;
    public static void MakeDraggable(Form f, params Control[] extras){MouseEventHandler s=(o,e)=>{if(e.Button==MouseButtons.Left){dragging=true;lastPos=Cursor.Position;}};MouseEventHandler m=(o,e)=>{if(dragging){var c=Cursor.Position;f.Location=new Point(f.Location.X+c.X-lastPos.X,f.Location.Y+c.Y-lastPos.Y);lastPos=c;}};MouseEventHandler u=(o,e)=>dragging=false;f.MouseDown+=s;f.MouseMove+=m;f.MouseUp+=u;foreach(var c in extras){c.MouseDown+=s;c.MouseMove+=m;c.MouseUp+=u;}}
    static Mutex _lockMutex;
    public static bool Lock(string name){bool ok;_lockMutex=new Mutex(true,name,out ok);return ok;}
    public static void Unlock(){if(_lockMutex!=null){try{_lockMutex.Close();}catch{}}}
}
