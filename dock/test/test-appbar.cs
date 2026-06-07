using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ============================================================
// Test: Work Area Management (AppBar alternative)
//
// Windows taskbar AppBar is owned by explorer.exe and can't
// be deregistered externally. Strategy:
//   1. Hide taskbar visually (ShowWindow/SW_HIDE)
//   2. Use SPI_SETWORKAREA to reserve dock space
//   3. Register as AppBar for ABN_POSCHANGED notifications
//   4. On exit: ABM_REMOVE + SPI_SETWORKAREA restore + show taskbar
// ============================================================

class TestAppBar
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("shell32.dll")] static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);

    const uint ABM_NEW      = 0x00000000;
    const uint ABM_REMOVE   = 0x00000001;
    const uint ABM_QUERYPOS = 0x00000002;
    const uint ABM_SETPOS   = 0x00000003;
    const uint ABM_GETSTATE = 0x00000004;
    const uint ABE_BOTTOM   = 3;
    const uint SPI_GETWORKAREA = 48;
    const uint SPI_SETWORKAREA = 47;
    const uint SPIF_UPDATEINIFILE = 0x01;
    const uint SPIF_SENDCHANGE    = 0x02;

    struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width  { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
        public override string ToString() { return string.Format("({0},{1})-({2},{3}) {4}x{5}", Left, Top, Right, Bottom, Width, Height); }
    }

    struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    static string Log = @"C:\temp\_test_appbar_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestAppBar @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();

        try
        {
            // --- 1. Save original state ---
            RECT origWorkArea = new RECT();
            SystemParametersInfo(SPI_GETWORKAREA, 0, ref origWorkArea, 0);
            Pass("Original work area: " + origWorkArea);

            int scrW = Screen.PrimaryScreen.Bounds.Width;
            int scrH = Screen.PrimaryScreen.Bounds.Height;
            Write("INFO", "Full screen: " + scrW + "x" + scrH);
            Write("INFO", "Taskbar occupies: " + (scrH - origWorkArea.Bottom) + "px");

            // --- 2. Hide taskbar ---
            IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero)
            {
                ShowWindow(taskbarHwnd, 0); // SW_HIDE
                System.Threading.Thread.Sleep(300);
                Pass("Taskbar hidden");
            }
            else
                Write("INFO", "No taskbar found (unusual)");

            // --- 3. Create dock form ---
            int dockHeight = 65;
            using (var form = new Form
            {
                Text = "TestDock",
                FormBorderStyle = FormBorderStyle.None,
                TopMost = true,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                BackColor = Color.FromArgb(30, 30, 50),
            })
            {
                form.Show();
                System.Threading.Thread.Sleep(200);

                // Position at bottom
                form.Left = 0;
                form.Top = scrH - dockHeight;
                form.Width = scrW;
                form.Height = dockHeight;
                System.Threading.Thread.Sleep(200);

                // --- 4. Register as AppBar (for notifications, not work area) ---
                var abd = new APPBARDATA();
                abd.cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA));
                abd.hWnd = form.Handle;
                abd.uEdge = ABE_BOTTOM;
                abd.rc.Left = 0;
                abd.rc.Top = scrH - dockHeight;
                abd.rc.Right = scrW;
                abd.rc.Bottom = scrH;

                uint result = SHAppBarMessage(ABM_NEW, ref abd);
                Write("INFO", "ABM_NEW: result=" + result);

                SHAppBarMessage(ABM_QUERYPOS, ref abd);
                Write("INFO", "QUERYPOS: " + abd.rc);

                SHAppBarMessage(ABM_SETPOS, ref abd);
                Write("INFO", "SETPOS: " + abd.rc);

                // Update form position from what system told us
                form.Left = abd.rc.Left;
                form.Top = abd.rc.Top;
                form.Width = abd.rc.Width;
                form.Height = abd.rc.Height;
                System.Threading.Thread.Sleep(200);

                // --- 5. Manually set work area to exclude dock ---
                RECT newWorkArea = new RECT();
                newWorkArea.Left = 0;
                newWorkArea.Top = 0;
                newWorkArea.Right = scrW;
                newWorkArea.Bottom = scrH - abd.rc.Height; // exclude dock

                bool setOk = SystemParametersInfo(SPI_SETWORKAREA, 0, ref newWorkArea, SPIF_SENDCHANGE);
                System.Threading.Thread.Sleep(300);

                // --- 6. Verify work area shrunk ---
                RECT verifyArea = new RECT();
                SystemParametersInfo(SPI_GETWORKAREA, 0, ref verifyArea, 0);
                Write("INFO", "After SPI_SETWORKAREA: " + verifyArea);

                int expectedBottom = scrH - abd.rc.Height;
                if (Math.Abs(verifyArea.Bottom - expectedBottom) <= 3)
                    Pass("Work area shrunk: Bottom=" + verifyArea.Bottom + " ≈ " + expectedBottom + " (reserved " + abd.rc.Height + "px for dock)");
                else
                    Fail("Work area Bottom=" + verifyArea.Bottom + " expected " + expectedBottom);

                // --- 7. Simulate maximized window check ---
                // A maximized window should fill the work area, not cover the dock
                using (var maxWin = new Form
                {
                    Text = "Simulated Maximized",
                    FormBorderStyle = FormBorderStyle.Sizable,
                    WindowState = FormWindowState.Maximized,
                    TopMost = false,
                })
                {
                    maxWin.Show();
                    System.Threading.Thread.Sleep(300);

                    RECT maxRect;
                    GetWindowRect(maxWin.Handle, out maxRect);
                    Write("INFO", "Maximized window rect: " + maxRect);

                    // A maximized window should NOT cover the dock area
                    if (maxRect.Bottom <= verifyArea.Bottom + 5)
                        Pass("Maximized window respects dock area (Bottom=" + maxRect.Bottom + " ≤ " + verifyArea.Bottom + ")");
                    else
                        Fail("Maximized window covers dock! Bottom=" + maxRect.Bottom + " > " + verifyArea.Bottom);

                    maxWin.Close();
                }
                System.Threading.Thread.Sleep(200);

                // --- 8. Remove AppBar ---
                SHAppBarMessage(ABM_REMOVE, ref abd);
                System.Threading.Thread.Sleep(200);

                // --- 9. Restore work area ---
                RECT restoredArea = new RECT();
                restoredArea.Left = 0;
                restoredArea.Top = 0;
                restoredArea.Right = scrW;
                restoredArea.Bottom = scrH; // full screen (taskbar still hidden)

                SystemParametersInfo(SPI_SETWORKAREA, 0, ref restoredArea, SPIF_SENDCHANGE);
                System.Threading.Thread.Sleep(200);

                RECT afterRestore = new RECT();
                SystemParametersInfo(SPI_GETWORKAREA, 0, ref afterRestore, 0);
                if (Math.Abs(afterRestore.Bottom - scrH) <= 3)
                    Pass("Work area restored to full screen (" + afterRestore.Bottom + " ≈ " + scrH + ")");
                else
                    Fail("Work area not restored: Bottom=" + afterRestore.Bottom);

                form.Close();
            }

            // --- 10. Restore taskbar ---
            if (taskbarHwnd != IntPtr.Zero)
            {
                ShowWindow(taskbarHwnd, 5); // SW_SHOW
                System.Threading.Thread.Sleep(500);

                // Force the system to recalculate work area
                // (the taskbar will re-register itself as AppBar when shown)
                RECT finalArea = new RECT();
                SystemParametersInfo(SPI_GETWORKAREA, 0, ref finalArea, 0);

                // Send a fake screen change to force explorer to recalculate
                // In practice, just waiting is enough
                System.Threading.Thread.Sleep(1000);

                SystemParametersInfo(SPI_GETWORKAREA, 0, ref finalArea, 0);
                Write("INFO", "Final work area: " + finalArea + " (original: " + origWorkArea + ")");

                // The taskbar might re-register at a slightly different height
                // We just verify it's close to original
                if (Math.Abs(finalArea.Bottom - origWorkArea.Bottom) <= 20)
                    Pass("Work area restored after taskbar shown (Δ=" + Math.Abs(finalArea.Bottom - origWorkArea.Bottom) + ")");
                else
                    Fail("Work area mismatch: " + finalArea.Bottom + " vs original " + origWorkArea.Bottom);
            }
        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        System.Threading.Thread.Sleep(100);
        Application.Exit();
    }
}
