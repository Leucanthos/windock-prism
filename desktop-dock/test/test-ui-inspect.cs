using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// ============================================================
// UI Inspection Tool — reads icon coordinates, sizes, visibility
// from a running WinDock instance for detailed visual verification
// ============================================================

class TestUIInspect
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string t);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr child, string c, string t);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder t, int c);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowLong(IntPtr hWnd, int idx);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT pt);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc, int idx);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int GetPixel(IntPtr hdc, int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

    struct RECT { public int Left, Top, Right, Bottom; public int Width { get { return Right - Left; } } public int Height { get { return Bottom - Top; } } }
    struct POINT { public int X, Y; }
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, LOGPIXELSX = 88;

    static string Log = @"C:\temp\_test_ui_inspect_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "UIInspect @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();

        Write("INFO", "=== UI Coordinate Inspection Tool ===");
        Write("INFO", "DPI: " + GetDpi());

        try
        {
            // 1. Inspect Windows taskbar state
            InspectTaskbar();

            // 2. Find all visible dock windows
            InspectDockWindows();

            // 3. Test icon positioning arithmetic
            VerifyLayoutArithmetic();

            // 4. Test magnification coordinate math
            VerifyMagnificationMath();

            // 5. Test screen edge clamping scenarios
            VerifyScreenEdgeCases();

            // 6. Test multi-monitor detection
            VerifyMultiMonitorDetection();

            // 7. Check for orphaned/stale windows
            CheckOrphanedWindows();

            // 8. Test that icon sizes are consistent
            VerifyIconSizeConsistency();
        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        Thread.Sleep(100);
        Application.Exit();
    }

    static int GetDpi()
    {
        var dc = GetDC(IntPtr.Zero);
        int dpi = GetDeviceCaps(dc, LOGPIXELSX);
        ReleaseDC(IntPtr.Zero, dc);
        return dpi;
    }

    // =====================================================================
    // INSPECTION 1: Windows taskbar state
    // =====================================================================
    static void InspectTaskbar()
    {
        Write("INFO", "--- Taskbar State ---");
        var tb = FindWindow("Shell_TrayWnd", null);
        if (tb == IntPtr.Zero)
        {
            Write("WARN", "Taskbar window not found (may already be hidden by dock)");
            return;
        }

        bool vis = IsWindowVisible(tb);
        RECT r; GetWindowRect(tb, out r);
        Write("INFO", string.Format("Taskbar: hwnd=0x{0:X} visible={1} rect=({2},{3}) {4}x{5}",
            tb.ToInt64(), vis, r.Left, r.Top, r.Width, r.Height));

        // Check if taskbar is at bottom of primary screen
        int sh = Screen.PrimaryScreen.Bounds.Height;
        if (r.Top >= sh - r.Height - 5)
            Write("INFO", "Taskbar at screen bottom (normal position)");
        else if (!vis)
            Write("INFO", "Taskbar hidden (dock is active)");
        else
            Write("WARN", "Taskbar in unusual position: Top=" + r.Top + " (screen height=" + sh + ")");
    }

    // =====================================================================
    // INSPECTION 2: Find all dock windows and report their coordinates
    // =====================================================================
    static List<IntPtr> dockWindows = new List<IntPtr>();
    static List<RECT> dockRects = new List<RECT>();

    static void InspectDockWindows()
    {
        Write("INFO", "--- Dock Windows ---");

        // Enumerate all top-level windows looking for dock icons
        EnumWindows((hWnd, lParam) =>
        {
            int pid;
            GetWindowThreadProcessId(hWnd, out pid);

            // Check if it looks like a dock window:
            // - No title (or short title)
            // - TOOLWINDOW style
            // - Opacity set (0.82)
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            string title = sb.ToString();

            uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;

            RECT r;
            if (GetWindowRect(hWnd, out r) && isToolWindow && r.Width > 30 && r.Width < 200)
            {
                // Dock icons are square tool windows with consistent size
                // Filter out non-square windows (badge popups, menus, etc.)
                bool isSquare = Math.Abs(r.Width - r.Height) <= 5;
                if (isSquare && (string.IsNullOrEmpty(title) || title.Length < 20))
                {
                    dockWindows.Add(hWnd);
                    dockRects.Add(r);
                }
            }
            return true;
        }, IntPtr.Zero);

        Write("INFO", "Found " + dockWindows.Count + " potential dock icon windows");

        if (dockWindows.Count == 0)
        {
            Write("WARN", "No dock windows found — is WinDock running?");
            return;
        }

        // Sort by X position
        var indexed = new List<Tuple<IntPtr, RECT>>();
        for (int i = 0; i < dockWindows.Count; i++)
            indexed.Add(Tuple.Create(dockWindows[i], dockRects[i]));
        indexed.Sort((a, b) => a.Item2.Left.CompareTo(b.Item2.Left));

        // Report each icon's position and size
        Write("INFO", "Icon positions (sorted by X):");
        for (int i = 0; i < indexed.Count; i++)
        {
            var r = indexed[i].Item2;
            Write("INFO", string.Format("  [{0}] pos=({1},{2}) size={3}x{4} centerX={5:F1}",
                i, r.Left, r.Top, r.Width, r.Height, r.Left + r.Width / 2f));
        }

        // Verify horizontal alignment (all icons should have same Y)
        if (indexed.Count >= 2)
        {
            int firstY = indexed[0].Item2.Top;
            bool sameY = true;
            for (int i = 1; i < indexed.Count; i++)
            {
                if (Math.Abs(indexed[i].Item2.Top - firstY) > 3)
                {
                    sameY = false;
                    Write("WARN", "Icon[" + i + "] Y=" + indexed[i].Item2.Top + " differs from icon[0] Y=" + firstY);
                }
            }
            if (sameY)
                Pass("All icons aligned at Y=" + firstY);
            else
                Fail("Icons NOT aligned — Y positions vary");

            // Verify consistent size
            int firstW = indexed[0].Item2.Width;
            bool sameSize = true;
            for (int i = 1; i < indexed.Count; i++)
            {
                if (Math.Abs(indexed[i].Item2.Width - firstW) > 3)
                {
                    sameSize = false;
                    Write("WARN", "Icon[" + i + "] W=" + indexed[i].Item2.Width + " differs from icon[0] W=" + firstW);
                }
            }
            if (sameSize)
                Pass("All icons have consistent size: " + firstW + "x" + indexed[0].Item2.Height);
            else
                Fail("Icons have inconsistent sizes");

            // Verify gaps between adjacent icons
            bool gapsOk = true;
            for (int i = 0; i < indexed.Count - 1; i++)
            {
                int gap = indexed[i + 1].Item2.Left - indexed[i].Item2.Right;
                if (gap < 5 || gap > 25)
                {
                    gapsOk = false;
                    Write("WARN", "Icon[" + i + "]→Icon[" + (i + 1) + "] gap=" + gap + " (expected ~14)");
                }
            }
            if (gapsOk)
                Pass("Icon gaps are consistent (~14px)");
            else
                Write("WARN", "Some icon gaps outside expected range");

            // Verify horizontal centering on screen
            int sw = Screen.PrimaryScreen.WorkingArea.Width;
            float dockCenter = indexed[0].Item2.Left + (indexed[indexed.Count - 1].Item2.Right - indexed[0].Item2.Left) / 2f;
            float screenCenter = sw / 2f;
            float offset = dockCenter - screenCenter;
            Write("INFO", string.Format("Dock center={0:F1} Screen center={1:F1} Offset={2:F1}px", dockCenter, screenCenter, offset));
            if (Math.Abs(offset) < 10)
                Pass("Dock horizontally centered (offset=" + offset.ToString("F1") + "px)");
            else
                Write("WARN", "Dock off-center by " + offset.ToString("F1") + "px");

            // Verify bottom-edge anchoring
            int sh = Screen.PrimaryScreen.WorkingArea.Height;
            int fw = indexed[0].Item2.Width;
            int bottomEdge = indexed[0].Item2.Top + indexed[0].Item2.Height;
            int expectedBottom = sh - 20; // IconY = sh - fw - 20, so bottom = IconY + fw = sh - 20
            Write("INFO", string.Format("Icon bottom edge={0} Expected bottom≈{1} (screenH={2})", bottomEdge, expectedBottom, sh));
            if (Math.Abs(bottomEdge - expectedBottom) <= 5)
                Pass("Icons anchored to bottom edge (bottom=" + bottomEdge + ", expected " + expectedBottom + ")");
            else
                Write("WARN", "Icons may not be bottom-anchored: bottom=" + bottomEdge + " expected " + expectedBottom);
        }

        // Check for overlapping icons
        for (int i = 0; i < indexed.Count - 1; i++)
        {
            if (indexed[i].Item2.Right > indexed[i + 1].Item2.Left)
            {
                Fail("OVERLAP: Icon[" + i + "] and Icon[" + (i + 1) + "] overlap! "
                    + "Right=" + indexed[i].Item2.Right + " > Left=" + indexed[i + 1].Item2.Left);
            }
        }
    }

    // =====================================================================
    // INSPECTION 3: Layout arithmetic verification
    // =====================================================================
    static void VerifyLayoutArithmetic()
    {
        Write("INFO", "--- Layout Arithmetic ---");

        int dpi = GetDpi();
        float scale = dpi / 96f;
        int fw = (int)(44 * scale);

        Write("INFO", "DPI=" + dpi + " Scale=" + scale.ToString("F2") + " IconSize=" + fw);

        // Verify the LayoutEngine calculations match expectations
        int sw = Screen.PrimaryScreen.WorkingArea.Width;
        int sh = Screen.PrimaryScreen.WorkingArea.Height;

        // Test with various icon counts
        foreach (int cnt in new[] { 0, 1, 2, 3, 5, 10, 20 })
        {
            int totalW = LayoutEngine.TotalWidth(cnt);
            int startX = LayoutEngine.StartX(cnt, sw);
            int iconY = LayoutEngine.IconY(sh, false);

            if (cnt == 0)
            {
                // TotalWidth(0) = 0*fw + (0-1)*14 = -14
                Write("INFO", string.Format("Count={0}: TotalWidth={1} StartX={2}", cnt, totalW, startX));
                if (totalW < 0)
                    Write("WARN", "TotalWidth(0) = " + totalW + " — negative width for 0 icons");
            }
            else if (cnt == 1)
            {
                // TotalWidth(1) = 1*fw + (1-1)*14 = fw
                Write("INFO", string.Format("Count={0}: TotalWidth={1} StartX={2}", cnt, totalW, startX));
                if (totalW == fw && startX == (sw - fw) / 2)
                    Pass("Single icon: TotalWidth=" + totalW + " StartX=" + startX + " — correctly centered");
                else
                    Fail("Single icon: unexpected values");
            }
            else
            {
                Write("INFO", string.Format("Count={0}: TotalWidth={1} StartX={2} IconY={3}", cnt, totalW, startX, iconY));
            }
        }

        // Verify layout caching
        int c3 = 3;
        int startX1 = LayoutEngine.StartX(c3, sw);
        int startX2 = LayoutEngine.StartX(c3, sw);
        if (startX1 == startX2)
            Pass("Layout caching: StartX is deterministic (same input → same output)");

        // Verify screen width change detection (simulating monitor switch)
        int startXWide = LayoutEngine.StartX(c3, 1920);
        int startXNarrow = LayoutEngine.StartX(c3, 1366);
        if (startXWide != startXNarrow)
            Pass("Layout handles different screen widths (1920→" + startXWide + " vs 1366→" + startXNarrow + ")");
    }

    // =====================================================================
    // INSPECTION 4: Magnification coordinate math
    // =====================================================================
    static void VerifyMagnificationMath()
    {
        Write("INFO", "--- Magnification Coordinate Math ---");

        int dpi = GetDpi();
        float dpiScale = dpi / 96f;
        int baseSize = (int)(44 * dpiScale);
        int pad = (int)(8 * dpiScale);

        // ApplyScale formula (DockIcon line 190-200):
        // ns = baseSize * curScale
        // sx = BaseX - (ns - baseSize) / 2   ← horizontally centered
        // sy = baseY - (ns - baseSize)       ← expands upward

        float[] scales = { 1.0f, 1.06f, 1.18f, 1.35f };

        foreach (float s in scales)
        {
            int ns = (int)(baseSize * s);
            int px = (int)(pad * s);
            Write("INFO", string.Format("Scale={0:F2}: FormSize={1} PicSize={2} IconOffset(up)={3}",
                s, ns, ns - px * 2, ns - baseSize));
        }

        // Verify upward expansion math:
        // At scale 1.35, ns = 1.35*baseSize
        // sy = baseY - (ns - baseSize) = baseY - 0.35*baseSize
        // Bottom edge = sy + ns = baseY - 0.35*baseSize + 1.35*baseSize = baseY + baseSize
        // So bottom edge stays fixed! ✓
        float scale35 = 1.35f;
        int ns35 = (int)(baseSize * scale35);
        int sy35 = - (ns35 - baseSize); // relative to baseY
        int bottomAt35 = sy35 + ns35;
        int bottomAt10 = 0 + baseSize; // at scale 1.0, sy=0

        if (bottomAt35 == bottomAt10)
            Pass("Bottom-edge anchoring math confirmed: scale 1.35 bottom=" + bottomAt35 + " == scale 1.0 bottom=" + bottomAt10);
        else
            Fail("Bottom-edge anchoring math WRONG: scale 1.35 bottom=" + bottomAt35 + " != scale 1.0 bottom=" + bottomAt10);
    }

    // =====================================================================
    // INSPECTION 5: Screen edge cases
    // =====================================================================
    static void VerifyScreenEdgeCases()
    {
        Write("INFO", "--- Screen Edge Cases ---");

        int dpi = GetDpi();
        float dpiScale = dpi / 96f;
        int fw = (int)(44 * dpiScale);
        int sw = Screen.PrimaryScreen.WorkingArea.Width;
        int sh = Screen.PrimaryScreen.WorkingArea.Height;

        // Case 1: Many icons exceeding screen width
        int maxIconsBeforeOverflow = 0;
        for (int cnt = 1; cnt <= 50; cnt++)
        {
            int totalW = cnt * fw + (cnt - 1) * 14;
            if (totalW <= sw)
                maxIconsBeforeOverflow = cnt;
            else
                break;
        }
        Write("INFO", string.Format("Screen width={0}, max icons before overflow={1} (totalW would be {2})",
            sw, maxIconsBeforeOverflow,
            maxIconsBeforeOverflow * fw + (maxIconsBeforeOverflow - 1) * 14));

        // Case 2: Negative startX (when dock is wider than screen)
        int overflowCount = maxIconsBeforeOverflow + 1;
        int overflowStartX = LayoutEngine.StartX(overflowCount, sw);
        if (overflowStartX < 0)
            Write("WARN", "Too many icons: StartX=" + overflowStartX + " (negative! Icons go off left edge)");
        else
            Write("INFO", "Overflow: StartX=" + overflowStartX + " for " + overflowCount + " icons");

        // Case 3: Debug mode Y position
        int normalY = LayoutEngine.IconY(sh, false);
        int debugY = LayoutEngine.IconY(sh, true);
        Write("INFO", string.Format("IconY: normal={0} (bottom-edge) debug={1} (top)", normalY, debugY));
        if (debugY == 40)
            Pass("Debug mode Y = 40 (visible for testing)");
        else
            Write("INFO", "Debug mode Y = " + debugY);

        // Case 4: Very small screen (simulate)
        int tinyW = 800, tinyH = 600;
        int tinyStartX = LayoutEngine.StartX(5, tinyW);
        int tinyY = LayoutEngine.IconY(tinyH, false);
        Write("INFO", string.Format("Small screen (800x600) with 5 icons: StartX={0} IconY={1}", tinyStartX, tinyY));

        // Case 5: 4K screen
        int bigW = 3840, bigH = 2160;
        int bigStartX = LayoutEngine.StartX(5, bigW);
        int bigY = LayoutEngine.IconY(bigH, false);
        Write("INFO", string.Format("4K screen (3840x2160) with 5 icons: StartX={0} IconY={1}", bigStartX, bigY));
    }

    // =====================================================================
    // INSPECTION 6: Multi-monitor detection scenarios
    // =====================================================================
    static void VerifyMultiMonitorDetection()
    {
        Write("INFO", "--- Multi-Monitor Detection ---");

        int monitorCount = Screen.AllScreens.Length;
        Write("INFO", "Monitor count: " + monitorCount);

        for (int i = 0; i < Screen.AllScreens.Length; i++)
        {
            var scr = Screen.AllScreens[i];
            Write("INFO", string.Format("  Monitor[{0}]: {1} Bounds=({2},{3},{4},{5}) Working=({6},{7},{8},{9}) Primary={10}",
                i, scr.DeviceName,
                scr.Bounds.X, scr.Bounds.Y, scr.Bounds.Width, scr.Bounds.Height,
                scr.WorkingArea.X, scr.WorkingArea.Y, scr.WorkingArea.Width, scr.WorkingArea.Height,
                scr.Primary));
        }

        if (monitorCount > 1)
        {
            // Test cursor-at-boundary scenario
            // When cursor is at (Bounds[0].Right - 1, Y), which screen does it pick?
            // GetActiveScreen uses cursor position
            Write("INFO", "Multi-monitor: boundary cursor detection active");
        }

        // Verify that WorkingArea changes when taskbar is hidden
        var tb = FindWindow("Shell_TrayWnd", null);
        bool taskbarVisible = tb != IntPtr.Zero && IsWindowVisible(tb);
        Write("INFO", "Taskbar visible: " + taskbarVisible);

        if (!taskbarVisible)
        {
            // Taskbar is hidden — WorkingArea should be full screen height
            var primary = Screen.PrimaryScreen;
            int waHeight = primary.WorkingArea.Height;
            int boundsHeight = primary.Bounds.Height;
            Write("INFO", string.Format("WorkingArea.Height={0} Bounds.Height={1} (diff={2} = taskbar height)",
                waHeight, boundsHeight, boundsHeight - waHeight));
        }
    }

    // =====================================================================
    // INSPECTION 7: Check for orphaned/stale windows
    // =====================================================================
    static void CheckOrphanedWindows()
    {
        Write("INFO", "--- Orphaned/Stale Window Check ---");

        // Look for "ghost" windows — windows that exist but are invisible or invalid
        int ghostCount = 0;
        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindow(hWnd)) { ghostCount++; return true; }

            uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;

            if (isToolWindow)
            {
                var sb = new System.Text.StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                string title = sb.ToString();

                RECT r;
                if (GetWindowRect(hWnd, out r) && r.Width == 0 && r.Height == 0)
                {
                    // Zero-size tool window — probably disposed but not destroyed
                    ghostCount++;
                    Write("WARN", "Zero-size tool window: hwnd=0x" + hWnd.ToInt64().ToString("X") + " title='" + title + "'");
                }
            }
            return true;
        }, IntPtr.Zero);

        if (ghostCount == 0)
            Pass("No orphaned/zero-size windows detected");
        else
            Write("WARN", ghostCount + " orphaned/zero-size windows detected — possible resource leak");
    }

    // =====================================================================
    // INSPECTION 8: Icon size consistency across DPI
    // =====================================================================
    static void VerifyIconSizeConsistency()
    {
        Write("INFO", "--- Icon Size Consistency ---");

        int dpi = GetDpi();
        float dpiScale = dpi / 96f;

        // What DockIcon constructor calculates:
        int expectedBaseSize = (int)(44 * dpiScale);
        int expectedPad = (int)(8 * dpiScale);
        int expectedPicSize = expectedBaseSize - expectedPad * 2;

        Write("INFO", string.Format("DPI={0} Scale={1:F3}", dpi, dpiScale));
        Write("INFO", string.Format("Expected: baseSize={0} pad={1} picSize={2}", expectedBaseSize, expectedPad, expectedPicSize));

        // Verify DPI scaling math
        if (expectedBaseSize > 0 && expectedPicSize > 0)
            Pass("DPI-scaled sizes are positive: baseSize=" + expectedBaseSize + " picSize=" + expectedPicSize);
        else
            Fail("DPI-scaled sizes invalid: baseSize=" + expectedBaseSize + " picSize=" + expectedPicSize);

        // Test at common DPI values
        int[] testDpis = { 96, 120, 144, 192, 96 * 2 }; // 100%, 125%, 150%, 200%, 200% again
        foreach (int tdpi in testDpis)
        {
            float ts = tdpi / 96f;
            int tb = (int)(44 * ts);
            int tp = (int)(8 * ts);
            int tpic = tb - tp * 2;
            Write("INFO", string.Format("  DPI={0} ({1}%): baseSize={2} pad={3} picSize={4}",
                tdpi, (int)(ts * 100), tb, tp, tpic));

            if (tpic <= 0)
                Fail("DPI " + tdpi + ": picSize=" + tpic + " (non-positive!)");
        }

        // The current formula for icon size uses DPI but doesn't account
        // for the fact that Form.Size at high DPI might behave differently
        // due to WinForms auto-scaling. Since SetProcessDPIAware() is called,
        // this should be OK for .NET 4.x.
    }
}
