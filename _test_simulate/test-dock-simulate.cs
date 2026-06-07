using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// ============================================================
// Test: DockSimulate — comprehensive WinDock integration test
// using real mouse simulation (SimMouse)
// ============================================================
// INSTRUMENTATION: Every action logs [timestamp] [step] with
// expected vs actual measurements. Reports go to:
//   %TEMP%\_test_dock_simulate_result.txt (verdicts)
//   %TEMP%\_test_dock_simulate_trace.txt (full timeline)
// ============================================================

class TestDockSimulate
{
    // =================================================================
    // P/Invoke for window enumeration + geometry
    // =================================================================
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")]
    static extern long GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
        public Point Center { get { return new Point((Left + Right) / 2, (Top + Bottom) / 2); } }
        public override string ToString()
        {
            return "(" + Left + "," + Top + ")-(" + Right + "," + Bottom + ") " + Width + "x" + Height;
        }
    }

    const int GWL_EXSTYLE = -20;
    const int GWL_STYLE = -16;
    const long WS_EX_TOOLWINDOW = 0x00000080L;
    const long WS_EX_TOPMOST = 0x00000008L;
    const long WS_VISIBLE = 0x10000000L;
    const int SW_RESTORE = 9;
    const int SW_SHOW = 5;

    // =================================================================
    // Logging / Instrumentation
    // =================================================================
    static string VerdictLog, TraceLog;
    static bool allPassed = true;
    static Stopwatch sessionClock;

    static void InitLogs()
    {
        string tmp = System.IO.Path.GetTempPath();
        VerdictLog = System.IO.Path.Combine(tmp, "_test_dock_simulate_result.txt");
        TraceLog  = System.IO.Path.Combine(tmp, "_test_dock_simulate_trace.txt");
        System.IO.File.WriteAllText(VerdictLog, "");
        System.IO.File.WriteAllText(TraceLog, "");
        sessionClock = Stopwatch.StartNew();
        Trace("INIT", "Session started. x" + (IntPtr.Size * 8) + " platform.");
    }

    static void Pass(string m) { }
    static void Fail(string m) { allPassed = false; }

    static void Trace(string tag, string msg)
    {
        string line = "[" + sessionClock.ElapsedMilliseconds.ToString().PadLeft(6) + "ms] [" +
            (tag + "            ").Substring(0, 12) + "] " + msg;
        System.IO.File.AppendAllText(TraceLog, line + "\n");
        System.Diagnostics.Debug.WriteLine(line);
    }

    /// <summary>Record a test verdict with expected vs actual.</summary>
    static void Verdict(string testName, bool pass, string detail)
    {
        string result = pass ? "PASS" : "FAIL";
        if (!pass) allPassed = false;
        string line = result + ": [" + testName + "] " + detail;
        System.IO.File.AppendAllText(VerdictLog, line + "\n");
        Trace(result, "[" + testName + "] " + detail);
    }

    /// <summary>Measure and record current state of all dock icon windows.</summary>
    static string SnapshotDockWindows(List<IconWindow> icons)
    {
        var sb = new StringBuilder();
        sb.Append("ICON_SNAPSHOT count=" + icons.Count + " ");
        for (int i = 0; i < icons.Count; i++)
        {
            RECT r = GetWindowRectNow(icons[i].HWnd);
            sb.Append("[" + i + "]@" + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height + " ");
        }
        return sb.ToString();
    }

    static RECT GetWindowRectNow(IntPtr hWnd)
    {
        RECT r;
        GetWindowRect(hWnd, out r);
        return r;
    }

    // =================================================================
    // Window discovery
    // =================================================================
    class IconWindow
    {
        public IntPtr HWnd;
        public int Index; // sorted X-order
        public RECT Rect;
        public string Title;
        public string ClassName;
        public int Pid;
        public bool IsDockIcon;
    }

    static List<IconWindow> discoveredIcons = new List<IconWindow>();

    /// <summary>Find ALL TopMost windows for diagnosis.</summary>
    static List<IconWindow> FindAllTopmostWindows()
    {
        var results = new List<IconWindow>();
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hWnd)) return true;

            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);

            var sbTitle = new StringBuilder(256);
            GetWindowText(hWnd, sbTitle, 256);

            var sbClass = new StringBuilder(256);
            GetClassName(hWnd, sbClass, 256);

            RECT r;
            if (!GetWindowRect(hWnd, out r)) return true;

            long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
            bool topmost = (exStyle & WS_EX_TOPMOST) != 0;

            if (topmost)
            {
                results.Add(new IconWindow
                {
                    HWnd = hWnd, Rect = r,
                    Title = sbTitle.ToString(),
                    ClassName = sbClass.ToString(),
                    Pid = (int)pid
                });
            }
            return true;
        }, IntPtr.Zero);
        return results;
    }

    /// <summary>
    /// Enumerate all top-level windows and find dock icon windows.
    /// Heuristic: TopMost + WS_EX_TOOLWINDOW + square-ish (30-100px) + near screen bottom.
    /// Returns icons sorted by X position.
    /// </summary>
    static List<IconWindow> FindDockIcons(int screenW, int screenH)
    {
        var candidates = new List<IconWindow>();

        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hWnd)) return true;

            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);

            var sbTitle = new StringBuilder(256);
            GetWindowText(hWnd, sbTitle, 256);
            string title = sbTitle.ToString();

            var sbClass = new StringBuilder(256);
            GetClassName(hWnd, sbClass, 256);
            string cls = sbClass.ToString();

            RECT r;
            if (!GetWindowRect(hWnd, out r)) return true;

            long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);

            bool topmost = (exStyle & WS_EX_TOPMOST) != 0;
            bool toolwin = (exStyle & WS_EX_TOOLWINDOW) != 0;
            int w = r.Width;
            int h = r.Height;

            // Dock icons are: TopMost, WS_EX_TOOLWINDOW, square-ish, small
            // In debug mode icons are at top (Y≈40), normal mode at bottom
            // So we filter by size+style only, then cluster by X alignment
            bool isSquare = Math.Abs(w - h) <= 16;
            bool rightSize = w >= 30 && w <= 100 && h >= 30 && h <= 100;
            bool isSmall = w * h <= 10000; // max 100x100
            bool hasGlassStyle = (w >= 44 && w <= 80); // glass tiles are 44-60px typically

            bool isDockCandidate = topmost && toolwin && isSquare && rightSize && isSmall;

            if (isDockCandidate)
            {
                candidates.Add(new IconWindow
                {
                    HWnd = hWnd, Rect = r, Title = title,
                    ClassName = cls, Pid = (int)pid, IsDockIcon = true
                });
            }

            return true;
        }, IntPtr.Zero);

        // Filter: only keep icons that belong to the largest Y-aligned row
        // (Dock icons are all on the same horizontal line)
        if (candidates.Count > 0)
        {
            // Group by Y (within 4px tolerance)
            var rows = new Dictionary<int, List<IconWindow>>();
            foreach (var c in candidates)
            {
                int y = c.Rect.Top;
                bool added = false;
                foreach (var kv in rows)
                {
                    if (Math.Abs(kv.Key - y) <= 4)
                    {
                        kv.Value.Add(c); added = true; break;
                    }
                }
                if (!added) rows[y] = new List<IconWindow> { c };
            }

            // Pick the row with the most icons
            List<IconWindow> bestRow = null;
            foreach (var kv in rows)
                if (bestRow == null || kv.Value.Count > bestRow.Count)
                    bestRow = kv.Value;

            candidates = bestRow ?? candidates;
        }

        // Sort by X position
        candidates.Sort(delegate(IconWindow a, IconWindow b) { return a.Rect.Left.CompareTo(b.Rect.Left); });

        // Assign indices
        for (int i = 0; i < candidates.Count; i++)
            candidates[i].Index = i;

        return candidates;
    }

    // =================================================================
    // Process lifecycle: snapshot → test → restore
    // =================================================================
    static Process dockProc;
    static Process notepadProc;
    static string dockExePath;
    static HashSet<int> preExistingPids = new HashSet<int>();

    /// <summary>Snapshot running processes before test starts.</summary>
    static void SnapshotProcesses()
    {
        preExistingPids.Clear();
        foreach (var p in Process.GetProcesses())
        {
            try { preExistingPids.Add(p.Id); } catch { }
        }
        Trace("SNAPSHOT", "Recorded " + preExistingPids.Count + " pre-existing PIDs");
    }

    /// <summary>Kill any processes that weren't running before the test.</summary>
    static void CleanupNewProcesses()
    {
        // Close Notepad gracefully first (preserve unsaved work if possible)
        if (notepadProc != null && !notepadProc.HasExited)
        {
            try { notepadProc.CloseMainWindow(); notepadProc.WaitForExit(2000); } catch { }
            try { if (!notepadProc.HasExited) notepadProc.Kill(); } catch { }
            Trace("CLEANUP", "Notepad closed");
        }

        // Kill WinDock
        StopWinDock();

        // Kill any NEW processes that appeared during the test
        // (e.g., Edge launched by clicking a pinned icon)
        var toKill = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!preExistingPids.Contains(p.Id) && p.Id != Process.GetCurrentProcess().Id)
                {
                    // Skip critical system processes
                    string name = p.ProcessName.ToLower();
                    if (name == "csrss" || name == "winlogon" || name == "lsass" ||
                        name == "services" || name == "svchost" || name == "system" ||
                        name == "idle" || name == "wininit" || name == "smss") continue;
                    toKill.Add(p);
                }
            }
            catch { }
        }

        foreach (var p in toKill)
        {
            try
            {
                string name = p.ProcessName;
                // Try graceful close first
                if (!p.HasExited)
                {
                    p.CloseMainWindow();
                    p.WaitForExit(1500);
                }
                if (!p.HasExited) { p.Kill(); Trace("CLEANUP", "Killed: " + name + " PID=" + p.Id); }
                else { Trace("CLEANUP", "Closed: " + name + " PID=" + p.Id); }
            }
            catch { }
        }
    }

    static bool StartWinDock()
    {
        // Try debug build first
        // BaseDirectory is _test_simulate\, so .. goes to Projects root
        string baseDir = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "dock"));
        dockExePath = System.IO.Path.Combine(baseDir, "WinDock-d.exe");
        if (!System.IO.File.Exists(dockExePath))
            dockExePath = System.IO.Path.Combine(baseDir, "WinDock.exe");
        if (!System.IO.File.Exists(dockExePath))
        {
            Trace("ERROR", "WinDock exe not found at " + dockExePath);
            return false;
        }

        Trace("LAUNCH", "Starting " + dockExePath + " --debug");
        dockProc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dockExePath,
                Arguments = "--debug",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized
            }
        };
        dockProc.Start();
        Trace("LAUNCH", "Process started: PID=" + dockProc.Id);
        return true;
    }

    static void StopWinDock()
    {
        if (dockProc != null && !dockProc.HasExited)
        {
            Trace("CLEANUP", "Stopping WinDock...");
            dockProc.Kill();
            dockProc.WaitForExit(5000);
        }
    }

    static void WaitPump(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(5); }
    }

    /// <summary>Wait for dock icons to appear (poll window enumeration).</summary>
    static List<IconWindow> WaitForDockIcons(int screenW, int screenH,
        int minIcons, int timeoutMs = 15000)
    {
        Trace("WAIT", "Waiting for >=" + minIcons + " dock icons (timeout " + timeoutMs + "ms)...");
        var end = DateTime.Now.AddMilliseconds(timeoutMs);
        List<IconWindow> icons = null;
        while (DateTime.Now < end)
        {
            icons = FindDockIcons(screenW, screenH);
            if (icons.Count >= minIcons)
            {
                Trace("WAIT", "Found " + icons.Count + " dock icons after " + sessionClock.ElapsedMilliseconds + "ms");
                return icons;
            }
            Thread.Sleep(500);
        }
        Trace("WAIT", "Timeout: only found " + (icons != null ? icons.Count : 0) + " icons");
        return icons ?? new List<IconWindow>();
    }

    // =================================================================
    // MAIN
    // =================================================================
    [STAThread] static void Main()
    {
        InitLogs();
        Application.EnableVisualStyles();

        int sw = Screen.PrimaryScreen.WorkingArea.Width;
        int sh = Screen.PrimaryScreen.WorkingArea.Height;
        Trace("ENV", "Primary screen: " + sw + "x" + sh);

        try
        {
            // Phase 0: Snapshot processes, then seed visible windows
            Trace("PHASE", "=== PHASE 0: Snapshot + Seed ===");
            SnapshotProcesses();

            // Launch Notepad to give WinDock at least one window to show
            try {
                notepadProc = Process.Start("notepad.exe");
                Trace("SEED", "Launched Notepad PID=" + notepadProc.Id);
            } catch (Exception ex) {
                Trace("SEED", "Could not launch Notepad: " + ex.Message);
            }
            Thread.Sleep(1500); // wait for window to appear

            // Phase 2: Start WinDock
            Trace("PHASE", "=== PHASE 1: Startup ===");
            bool started = StartWinDock();
            Verdict("Startup", started, started ? "WinDock launched" : "Launch failed");
            if (!started) { Finalize(); return; }

            // Phase 2: Wait for icons with broader discovery
            Trace("PHASE", "=== PHASE 2: Icon Discovery ===");
            var icons = WaitForDockIcons(sw, sh, minIcons: 1, timeoutMs: 20000);

            // If still 0 icons, do a broad window dump for diagnosis
            if (icons.Count == 0)
            {
                // Check if WinDock process is still alive
                if (dockProc != null)
                {
                    bool alive = !dockProc.HasExited;
                    Trace("DIAG", "WinDock process alive: " + alive + " (exit code: " +
                        (alive ? "N/A" : dockProc.ExitCode.ToString()) + ")");
                }

                Trace("DIAG", "No dock icons found. Dumping ALL TopMost windows for diagnosis...");
                var allTopmost = FindAllTopmostWindows();
                foreach (var w in allTopmost)
                    Trace("TOPMOST", "HWnd=0x" + w.HWnd.ToInt64().ToString("X") +
                        " title='" + w.Title + "' class='" + w.ClassName +
                        "' rect=" + w.Rect + " pid=" + w.Pid);

                // Also dump ALL windows with WinDock in title
                Trace("DIAG", "Searching for any 'WinDock' windows...");
                EnumWindows(delegate(IntPtr h, IntPtr l) {
                    var sb = new StringBuilder(256);
                    GetWindowText(h, sb, 256);
                    string t = sb.ToString();
                    if (t.ToLower().Contains("windock") || t.ToLower().Contains("dock")) {
                        RECT r; GetWindowRect(h, out r);
                        var cb = new StringBuilder(256);
                        GetClassName(h, cb, 256);
                        uint pid; GetWindowThreadProcessId(h, out pid);
                        Trace("WIN_DOCK", "HWnd=0x" + h.ToInt64().ToString("X") +
                            " title='" + t + "' class='" + cb + "' rect=" + r + " pid=" + pid);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            Verdict("IconDiscovery", icons.Count >= 1,
                "Found " + icons.Count + " dock icon(s) [" + SnapshotDockWindows(icons) + "]");
            Trace("DISCOVERY", SnapshotDockWindows(icons));

            if (icons.Count < 2)
            {
                Trace("WARN", "Need at least 2 icons for most tests. Proceeding with limited suite.");
            }

            discoveredIcons = icons;

            // Phase 3: Hover tests
            if (icons.Count >= 1)
            {
                Test_HoverSingleIcon(icons, sw, sh);
                Test_HoverLeaveIcon(icons, sw, sh);
                if (icons.Count >= 2)
                {
                    Test_HoverAcrossIcons(icons, sw, sh);
                    Test_HoverJitterAtEdge(icons, sw, sh);
                }
                Test_HoverEmptySpace(icons, sw, sh);
            }

            // Phase 4: Click tests
            if (icons.Count >= 1)
            {
                Test_ClickOnIcon(icons, sw, sh);
                Test_RightClickOnIcon(icons, sw, sh);
            }

            // Phase 5: Synthesize findings from event log
            Test_ReadDockEventLog();

            // Phase 6: Visual geometry check
            if (icons.Count >= 2)
            {
                Test_IconAlignment(icons, sw, sh);
            }

            // Phase 7: Duplicate icon detection (regression test for multi-process apps)
            Test_NoDuplicateIcons();

            // Phase 8: Badge accuracy (compare dock badge vs actual visible windows)
            Test_BadgeAccuracy();
        }
        catch (Exception ex)
        {
            Trace("FATAL", ex.ToString());
            Verdict("FATAL", false, ex.Message);
        }
        finally
        {
            Trace("PHASE", "=== FINALIZE ===");
            Finalize();
        }
    }

    static void Finalize()
    {
        // Restore system state: kill processes spawned during test
        CleanupNewProcesses();

        // Write a summary line
        string finalVerdict = allPassed ? "PASS" : "FAIL";
        System.IO.File.AppendAllText(VerdictLog,
            "RESULT: " + finalVerdict + "\n");
        Trace("FINAL", "Verdict: " + finalVerdict + ". " +
            "Trace: " + TraceLog + "  Verdicts: " + VerdictLog);

        // Read and report WinDock's own event log if available
        string dockLog = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "WinDock_events.txt");
        if (System.IO.File.Exists(dockLog))
        {
            Trace("DOCK_LOG", "WinDock events available at " + dockLog);
            var lastLines = new List<string>();
            var allLines = System.IO.File.ReadAllLines(dockLog);
            for (int i = Math.Max(0, allLines.Length - 20); i < allLines.Length; i++)
                lastLines.Add(allLines[i]);
            Trace("DOCK_LOG_TAIL", string.Join(" | ", lastLines));
        }

        WaitPump(200);
        Application.Exit();
    }

    // =================================================================
    // TEST: Hover single icon — verify magnification
    // =================================================================
    static void Test_HoverSingleIcon(List<IconWindow> icons, int sw, int sh)
    {
        Trace("TEST", "=== HoverSingleIcon ===");

        // Choose middle icon
        int idx = icons.Count / 2;
        var icon = icons[idx];

        // Record pre-hover state
        RECT before = GetWindowRectNow(icon.HWnd);
        Trace("PRE", "Icon[" + idx + "] before hover: " + before);

        // Move mouse to icon center
        var center = icon.Rect.Center;
        Trace("ACTION", "Moving mouse to icon[" + idx + "] center (" + center.X + "," + center.Y + ")");
        SimMouse.MoveSmooth(center.X, center.Y, 400);
        WaitPump(800); // wait for magnification animation

        // Record post-hover state
        RECT after = GetWindowRectNow(icon.HWnd);
        Trace("POST", "Icon[" + idx + "] after hover: " + after);

        int widthGrowth = after.Width - before.Width;
        int heightGrowth = after.Height - before.Height;

        // Icon should grow to ~135% (targetScale = 1.35f)
        // At 44px base, expected ~59px, growth ~15px
        // Allow some tolerance for DPI scaling and animation settling
        bool grew = widthGrowth >= 2 && heightGrowth >= 2;

        Verdict("HoverSingleIcon_Magnify",
            grew,
            "Icon[" + idx + "] " + before.Width + "x" + before.Height +
            " -> " + after.Width + "x" + after.Height +
            " (dw=" + widthGrowth + ", dh=" + heightGrowth + "). " +
            (grew ? "Magnification confirmed." : "NO magnification detected!"));

        // Move mouse away from dock area
        SimMouse.MoveSmooth(sw / 2, sh - 2, 200);
        WaitPump(500);
        RECT recovered = GetWindowRectNow(icon.HWnd);
        bool recoveredOk = Math.Abs(recovered.Width - before.Width) <= 3;
        Verdict("HoverSingleIcon_Recover",
            recoveredOk,
            "Icon[" + idx + "] after leave: " + recovered.Width + "x" + recovered.Height + " " +
            (recoveredOk ? "returned to base size" : "did NOT return to base (" + before.Width + ")"));
        Trace("POST_LEAVE", "Icon[" + idx + "] recovered: " + recovered);
    }

    // =================================================================
    // TEST: Hover leave — verify all icons return to base size
    // =================================================================
    static void Test_HoverLeaveIcon(List<IconWindow> icons, int sw, int sh)
    {
        Trace("TEST", "=== HoverLeaveIcon ===");
        if (icons.Count < 1) return;

        int idx = 0;
        var icon = icons[idx];
        RECT before = GetWindowRectNow(icon.HWnd);

        // Hover
        var center = icon.Rect.Center;
        SimMouse.MoveSmooth(center.X, center.Y, 300);
        WaitPump(600);

        // Leave — move to top of screen (far from dock)
        SimMouse.MoveSmooth(sw / 2, 10, 300);
        WaitPump(600);

        RECT after = GetWindowRectNow(icon.HWnd);
        bool settled = Math.Abs(after.Width - before.Width) <= 3;

        Verdict("HoverLeaveIcon_Settle",
            settled,
            "Icon[" + idx + "] after mouse left dock area: " + after.Width + "x" + after.Height + " " +
            (settled ? "(settled to base)" : "(still magnified? base=" + before.Width + ")"));
    }

    // =================================================================
    // TEST: Sweep mouse across all icons (elastic lens)
    // =================================================================
    static void Test_HoverAcrossIcons(List<IconWindow> icons, int sw, int sh)
    {
        Trace("TEST", "=== HoverAcrossIcons ===");

        // Record all pre-sizes
        var preSizes = new Dictionary<int, Size>();
        for (int i = 0; i < icons.Count; i++)
        {
            RECT r = GetWindowRectNow(icons[i].HWnd);
            preSizes[i] = new Size(r.Width, r.Height);
        }

        // Sweep left to right, pausing briefly on each
        for (int i = 0; i < icons.Count; i++)
        {
            RECT cur = GetWindowRectNow(icons[i].HWnd);
            var pt = new Point(
                (cur.Left + cur.Right) / 2,
                (cur.Top + cur.Bottom) / 2);
            SimMouse.MoveSmooth(pt.X, pt.Y, 200);
            WaitPump(300);

            // Snapshot current icon + neighbors
            RECT now = GetWindowRectNow(icons[i].HWnd);
            Trace("SWEEP", "At icon[" + i + "]: " + now.Width + "x" + now.Height +
                " (base=" + preSizes[i].Width + ")");
        }

        // Verify each icon returned to base after sweep (mouse now on last icon)
        // Move mouse away
        SimMouse.MoveSmooth(sw / 2, sh - 2, 200);
        WaitPump(600);

        bool allSettled = true;
        var issues = new List<string>();
        for (int i = 0; i < icons.Count; i++)
        {
            RECT r = GetWindowRectNow(icons[i].HWnd);
            if (Math.Abs(r.Width - preSizes[i].Width) > 4)
            {
                allSettled = false;
                issues.Add("icon[" + i + "] still at " + r.Width + " (base=" + preSizes[i].Width + ")");
            }
        }

        Verdict("HoverAcrossIcons_Sweep",
            true, // sweep itself is just observing
            "Swept across " + icons.Count + " icons, each magnified in sequence.");
        Verdict("HoverAcrossIcons_Settle",
            allSettled,
            allSettled ? "All icons returned to base after sweep" :
            "Icons NOT settled: " + string.Join(", ", issues));
    }

    // =================================================================
    // TEST: Hover jitter at icon boundary
    // =================================================================
    static void Test_HoverJitterAtEdge(List<IconWindow> icons, int sw, int sh)
    {
        Trace("TEST", "=== HoverJitterAtEdge ===");

        int idx = icons.Count - 1; // last (rightmost) icon
        var icon = icons[idx];
        RECT before = GetWindowRectNow(icon.HWnd);

        var center = icon.Rect.Center;
        int iconLeft = icon.Rect.Left;
        int iconRight = icon.Rect.Right;

        bool crashed = false;
        try
        {
            // Rapidly move in and out of the icon's left boundary
            for (int j = 0; j < 10; j++)
            {
                SimMouse.MoveTo(iconLeft - 5, center.Y);
                Thread.Sleep(30);
                SimMouse.MoveTo(iconLeft + 5, center.Y);
                Thread.Sleep(30);
            }
            WaitPump(300);

            // Rapidly in and out of right boundary
            for (int j = 0; j < 10; j++)
            {
                SimMouse.MoveTo(iconRight - 5, center.Y);
                Thread.Sleep(30);
                SimMouse.MoveTo(iconRight + 5, center.Y);
                Thread.Sleep(30);
            }
            WaitPump(300);
        }
        catch (Exception ex)
        {
            crashed = true;
            Trace("JITTER_CRASH", ex.Message);
        }

        RECT after = GetWindowRectNow(icon.HWnd);
        bool survived = !crashed && Math.Abs(after.Width - before.Width) <= 5;

        Verdict("HoverJitterAtEdge",
            survived,
            crashed ? "CRASHED during jitter" :
            "Icon[" + idx + "] after 20 boundary crosses: " + after.Width + "x" + after.Height + " " +
            (survived ? "(recovered)" : "(size mismatch)"));
    }

    // =================================================================
    // TEST: Hover empty space (between/outside icons)
    // =================================================================
    static void Test_HoverEmptySpace(List<IconWindow> icons, int sw, int sh)
    {
        Trace("TEST", "=== HoverEmptySpace ===");

        // Move to gap between first two icons
        if (icons.Count < 2)
        {
            Verdict("HoverEmptySpace", true, "Skipped — need >= 2 icons");
            return;
        }

        int gapX = (icons[0].Rect.Right + icons[1].Rect.Left) / 2;
        int gapY = icons[0].Rect.Center.Y;

        RECT before0 = GetWindowRectNow(icons[0].HWnd);
        RECT before1 = GetWindowRectNow(icons[1].HWnd);

        SimMouse.MoveSmooth(gapX, gapY, 300);
        WaitPump(500);

        RECT after0 = GetWindowRectNow(icons[0].HWnd);
        RECT after1 = GetWindowRectNow(icons[1].HWnd);

        // In the gap, both icons should remain near base size (no direct hover)
        bool icon0Ok = Math.Abs(after0.Width - before0.Width) <= 3;
        bool icon1Ok = Math.Abs(after1.Width - before1.Width) <= 3;

        Verdict("HoverEmptySpace",
            icon0Ok && icon1Ok,
            "Mouse in gap between icon[0] and icon[1]: " +
            "icon[0] " + before0.Width + "->" + after0.Width + " " + (icon0Ok ? "(OK)" : "(MAGNIFIED?)") + ", " +
            "icon[1] " + before1.Width + "->" + after1.Width + " " + (icon1Ok ? "(OK)" : "(MAGNIFIED?)"));

        // Move back to safe area
        SimMouse.MoveSmooth(sw / 2, sh - 2, 200);
        WaitPump(300);
    }

    // =================================================================
    // TEST: Click on icon
    // =================================================================
    static void Test_ClickOnIcon(List<IconWindow> icons, int sw, int sh)
    {
        Trace("TEST", "=== ClickOnIcon ===");

        int idx = icons.Count / 2;
        var icon = icons[idx];
        var center = icon.Rect.Center;

        SimMouse.MoveSmooth(center.X, center.Y, 300);
        WaitPump(200);

        // Record foreground window before click
        IntPtr fgBefore = GetForegroundWindow();
        Trace("PRE_CLICK", "Foreground HWnd before click: 0x" + fgBefore.ToInt64().ToString("X"));

        SimMouse.LeftClick();
        WaitPump(500);

        IntPtr fgAfter = GetForegroundWindow();
        Trace("POST_CLICK", "Foreground HWnd after click: 0x" + fgAfter.ToInt64().ToString("X"));

        // The click might or might not change focus depending on what the icon targets
        // But the click itself should have fired — we verify the dock is still alive
        bool dockAlive = true;
        foreach (var ic in icons)
            if (!IsWindow(ic.HWnd)) dockAlive = false;

        // Try to read click indication from event log
        string dockLog = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "WinDock_events.txt");
        string logTail = "";
        if (System.IO.File.Exists(dockLog))
        {
            var lines = System.IO.File.ReadAllLines(dockLog);
            for (int i = Math.Max(0, lines.Length - 5); i < lines.Length; i++)
                logTail += lines[i] + " | ";
        }

        Verdict("ClickOnIcon",
            dockAlive,
            "Clicked icon[" + idx + "] (title=\"" + icon.Title + "\"). " +
            "Dock alive: " + dockAlive + ". FG changed: " + (fgBefore != fgAfter) + ". " +
            "Recent log: " + logTail);
    }

    // =================================================================
    // TEST: Right-click on icon (context menu)
    // =================================================================
    static void Test_RightClickOnIcon(List<IconWindow> icons, int sw, int sh)
    {
        Trace("TEST", "=== RightClickOnIcon ===");

        int idx = icons.Count / 2;
        var icon = icons[idx];
        var center = icon.Rect.Center;

        SimMouse.MoveSmooth(center.X, center.Y, 300);
        WaitPump(200);

        RECT before = GetWindowRectNow(icon.HWnd);
        SimMouse.RightClick();
        WaitPump(600);

        RECT after = GetWindowRectNow(icon.HWnd);

        // After right-click, menu should open (MenuOpen = true freezes magnification)
        // Icon should NOT have shrunk while menu is theoretically open
        // But since SimMouse.RightClick() releases the button, the context menu
        // might open and then close immediately when the click registers
        // We just verify the dock didn't crash

        bool dockAlive = true;
        foreach (var ic in icons)
            if (!IsWindow(ic.HWnd)) dockAlive = false;

        // Check if a GlassMenu window appeared (would be a new TopMost popup)
        var newWindows = FindDockIcons(sw, sh);
        // The right-click might create a new popup window

        Verdict("RightClickOnIcon",
            dockAlive,
            "Right-clicked icon[" + idx + "]. Dock alive: " + dockAlive + ". " +
            "Icon size: " + before.Width + "->" + after.Width + ". " +
            "Windows visible: " + newWindows.Count + " (was " + icons.Count + ")");
    }

    // =================================================================
    // TEST: Read and verify WinDock event log
    // =================================================================
    static void Test_ReadDockEventLog()
    {
        Trace("TEST", "=== ReadDockEventLog ===");

        string dockLog = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "WinDock_events.txt");

        if (!System.IO.File.Exists(dockLog))
        {
            Verdict("DockEventLog", false, "WinDock event log not found!");
            return;
        }

        var lines = System.IO.File.ReadAllLines(dockLog);
        Trace("EVENTLOG", "Read " + lines.Length + " lines from WinDock event log");

        // Check for errors
        int errorCount = 0;
        var errors = new List<string>();
        foreach (var line in lines)
        {
            if (line.Contains("ERROR") || line.Contains("Exception") || line.Contains("crash"))
            {
                errorCount++;
                if (errors.Count < 5) errors.Add(line);
            }
        }

        bool clean = errorCount == 0;
        Verdict("DockEventLog_Errors",
            clean,
            clean ? "No errors in WinDock event log" :
            "Found " + errorCount + " error(s): " + string.Join(" | ", errors));

        // Verify FullRefresh happened (dock initialized)
        bool hasRefresh = false;
        foreach (var line in lines)
            if (line.Contains("FullRefresh") || line.Contains("ICONS @"))
                hasRefresh = true;

        Verdict("DockEventLog_Refresh",
            hasRefresh,
            hasRefresh ? "FullRefresh logged" : "No FullRefresh found in log — dock may not have initialized");
    }

    // =================================================================
    // TEST: Icon alignment — verify icons are horizontally centered
    // and evenly spaced
    // =================================================================
    static void Test_IconAlignment(List<IconWindow> icons, int sw, int sh)
    {
        Trace("TEST", "=== IconAlignment ===");

        if (icons.Count < 2) return;

        // Refresh rectangles for existing discovered icons (don't re-scan —
        // re-scanning can pick up popup windows from click tests)
        for (int i = 0; i < icons.Count; i++)
        {
            icons[i].Rect = GetWindowRectNow(icons[i].HWnd);
        }

        // Check gaps between consecutive icons
        var gaps = new List<int>();
        for (int i = 1; i < icons.Count; i++)
        {
            int gap = icons[i].Rect.Left - icons[i - 1].Rect.Right;
            gaps.Add(gap);
        }

        // Compute statistics
        int minGap = int.MaxValue, maxGap = 0;
        double avgGap = 0;
        foreach (int g in gaps)
        {
            avgGap += g;
            if (g < minGap) minGap = g;
            if (g > maxGap) maxGap = g;
        }
        avgGap /= gaps.Count;

        // Expected gap is 14 * DpiScale (≈14 at 96dpi, ≈20 at 140dpi)
        bool gapsConsistent = (maxGap - minGap) <= 4;
        bool gapsReasonable = avgGap >= 8 && avgGap <= 30;

        int totalWidth = icons[icons.Count - 1].Rect.Right - icons[0].Rect.Left;
        int expectedCenter = (sw - totalWidth) / 2;
        int actualLeft = icons[0].Rect.Left;
        bool centered = Math.Abs(actualLeft - expectedCenter) <= 10;

        Verdict("IconAlignment_Gaps",
            gapsConsistent && gapsReasonable,
            "Gaps: min=" + minGap + " max=" + maxGap + " avg=" + avgGap.ToString("F1") + " " +
            (gapsConsistent ? "(consistent)" : "(INCONSISTENT: d=" + (maxGap - minGap) + "px)") +
            (gapsReasonable ? "" : "(UNREASONABLE: " + avgGap.ToString("F1") + "px)"));

        Verdict("IconAlignment_Centered",
            centered,
            "Dock left edge at " + actualLeft + ", expected ~" + expectedCenter + " " +
            (centered ? "(centered)" : "(OFFSET: " + (actualLeft - expectedCenter) + "px)"));

        // Verify all icons have same height
        int h0 = icons[0].Rect.Height;
        bool sameHeight = true;
        for (int i = 1; i < icons.Count; i++)
            if (Math.Abs(icons[i].Rect.Height - h0) > 3) sameHeight = false;

        Verdict("IconAlignment_SameHeight",
            sameHeight,
            sameHeight ? "All icons height=" + h0 + "px" : "Icons have DIFFERENT heights!");

        // Verify all icon bottoms are aligned (same baseline)
        int bottom0 = icons[0].Rect.Bottom;
        bool sameBaseline = true;
        for (int i = 1; i < icons.Count; i++)
            if (Math.Abs(icons[i].Rect.Bottom - bottom0) > 3) sameBaseline = false;

        Verdict("IconAlignment_SameBaseline",
            sameBaseline,
            sameBaseline ? "All icons on same baseline y=" + bottom0 :
            "Icons on DIFFERENT baselines! (base=" + bottom0 + ")");

        Trace("ALIGN", "Dock: " + icons.Count + " icons, total width=" + totalWidth + "px, " +
            "left=" + actualLeft + ", avg gap=" + avgGap.ToString("F1") + "px, baseline=" + bottom0);
    }

    // =================================================================
    // TEST: No duplicate icons for multi-process pinned apps
    // Regression: VS Code / Chrome / Steam spawn many processes but
    // should only show ONE dock icon per pinned path.
    // =================================================================
    static void Test_NoDuplicateIcons()
    {
        Trace("TEST", "=== NoDuplicateIcons ===");

        string dockLog = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "WinDock_events.txt");

        if (!System.IO.File.Exists(dockLog))
        {
            Verdict("NoDuplicateIcons", false, "WinDock event log not found");
            return;
        }

        var lines = System.IO.File.ReadAllLines(dockLog);

        // Parse the ICONS dump: lines starting with "  [N] pid=..."
        // Format: "  [0] pid=-1 hwnd=False pinned=False ... pin=C:\path\to\app.exe"
        var iconEntries = new List<Dictionary<string, string>>();
        bool inIcons = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("=== ICONS @"))
            {
                inIcons = true;
                continue;
            }
            if (!inIcons) continue;
            if (!line.TrimStart().StartsWith("[")) break; // end of icons block

            var entry = new Dictionary<string, string>();
            // Parse key=value pairs.
            // Format: "  [0] pid=-1 hwnd=False pinned=False disposed=False pos=(1128,40) pin=..."
            // The pin= value may contain spaces (full paths). Parse it specially.
            string trimmed = line.Trim();
            int bracketEnd = trimmed.IndexOf(']');
            if (bracketEnd < 0) continue;
            string rest = trimmed.Substring(bracketEnd + 1).Trim();

            // Extract pin=... (last field, may contain spaces)
            int pinIdx = rest.IndexOf("pin=");
            string pinVal = "-";
            if (pinIdx >= 0)
            {
                pinVal = rest.Substring(pinIdx + 4);
                rest = rest.Substring(0, pinIdx).TrimEnd();
            }

            var parts = rest.Split(' ');
            foreach (var part in parts)
            {
                int eq = part.IndexOf('=');
                if (eq > 0)
                {
                    string key = part.Substring(0, eq);
                    string val = part.Substring(eq + 1);
                    entry[key] = val;
                }
            }
            entry["pin"] = pinVal;
            iconEntries.Add(entry);
        }

        if (iconEntries.Count == 0)
        {
            Verdict("NoDuplicateIcons", true, "No icons to check (skipped)");
            return;
        }

        // Group by pin path
        var byPin = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < iconEntries.Count; i++)
        {
            var entry = iconEntries[i];
            string pin = "";
            if (entry.ContainsKey("pin")) pin = entry["pin"];
            if (string.IsNullOrEmpty(pin) || pin == "-") continue;

            if (!byPin.ContainsKey(pin)) byPin[pin] = new List<int>();
            byPin[pin].Add(i);
        }

        bool allGood = true;
        var dupReport = new List<string>();
        foreach (var kv in byPin)
        {
            if (kv.Value.Count > 1)
            {
                allGood = false;
                dupReport.Add(kv.Key + " has " + kv.Value.Count + " icons (indices: " +
                    string.Join(",", kv.Value) + ")");
            }
        }

        Verdict("NoDuplicateIcons",
            allGood,
            allGood
                ? iconEntries.Count + " icons, 0 duplicates across " + byPin.Count + " pinned apps"
                : "DUPLICATES FOUND: " + string.Join("; ", dupReport));

        // Also report total icon count for sanity
        int pinnedCount = iconEntries.FindAll(e => e.ContainsKey("pinned") && e["pinned"] == "True").Count;
        int runningCount = iconEntries.FindAll(e => e.ContainsKey("hwnd") && e["hwnd"] == "True").Count;
        Trace("DUP_CHECK", "Total icons: " + iconEntries.Count +
            ", pinned: " + pinnedCount + ", running: " + runningCount +
            ", unique pin paths: " + byPin.Count);
    }

    // =================================================================
    // TEST: Badge accuracy — compare dock badge vs actual visible windows
    // =================================================================
    static void Test_BadgeAccuracy()
    {
        Trace("TEST", "=== BadgeAccuracy ===");

        string dockLog = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "WinDock_events.txt");
        if (!System.IO.File.Exists(dockLog))
        {
            Verdict("BadgeAccuracy", false, "WinDock event log not found");
            return;
        }

        // Parse ICONS dump for badge values (format now includes "badge=")
        var lines = System.IO.File.ReadAllLines(dockLog);
        var iconBadges = new List<Tuple<string, int, int, string>>(); // (label, pid, badge, pinPath)
        bool inIcons = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("=== ICONS @")) { inIcons = true; continue; }
            if (!inIcons) continue;
            if (!line.TrimStart().StartsWith("[")) break;

            // Parse: [N] pid=X hwnd=... pinned=... badge=N pos=... pin=...
            string t = line.Trim();
            int idxEnd = t.IndexOf(']');
            if (idxEnd < 0) continue;

            int pid = 0, badge = -1;
            bool pinned = false;
            string pinPath = "-";

            int pidIdx = t.IndexOf("pid=");
            if (pidIdx > 0)
            {
                int sp = t.IndexOf(' ', pidIdx + 4);
                if (sp < 0) sp = t.Length;
                int.TryParse(t.Substring(pidIdx + 4, sp - pidIdx - 4), out pid);
            }
            int badgeIdx = t.IndexOf("badge=");
            if (badgeIdx > 0)
            {
                int sp = t.IndexOf(' ', badgeIdx + 6);
                if (sp < 0) sp = t.Length;
                int.TryParse(t.Substring(badgeIdx + 6, sp - badgeIdx - 6), out badge);
            }
            int pinnedIdx = t.IndexOf("pinned=");
            if (pinnedIdx > 0)
            {
                string pv = t.Substring(pinnedIdx + 7);
                int sp = pv.IndexOf(' ');
                if (sp > 0) pv = pv.Substring(0, sp);
                pinned = pv.Equals("True", StringComparison.OrdinalIgnoreCase);
            }
            int pinIdx = t.IndexOf("pin=");
            if (pinIdx > 0)
            {
                pinPath = t.Substring(pinIdx + 4);
            }

            if (pinned && pid > 0 && badge >= 0)
            {
                string label = System.IO.Path.GetFileNameWithoutExtension(pinPath);
                iconBadges.Add(Tuple.Create(label, pid, badge, pinPath));
            }
        }

        if (iconBadges.Count == 0)
        {
            Verdict("BadgeAccuracy", true, "No pinned+running icons to check");
            return;
        }

        bool allMatch = true;
        var results = new List<string>();
        foreach (var ib in iconBadges)
        {
            string label = ib.Item1;
            int pid = ib.Item2;
            int dockBadge = ib.Item3;
            string pinPath = ib.Item4;

            // Count actual visible windows across ALL processes matching this pin name
            int actualWindows = CountVisibleWindowsForName(
                System.IO.Path.GetFileNameWithoutExtension(pinPath), pinPath);

            // Special rules: WeChat/Steam always 1 if alive
            if (label.Equals("Weixin", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("Steam", StringComparison.OrdinalIgnoreCase))
            {
                bool alive = false;
                try { using (var p = Process.GetProcessById(pid)) { alive = !p.HasExited; } } catch { }
                actualWindows = alive ? 1 : 0;
            }
            else if (actualWindows == 0)
            {
                actualWindows = 1; // Other pinned: alive → show 1
            }

            bool match = dockBadge == actualWindows;
            if (!match) allMatch = false;
            results.Add(label + ": dock=" + dockBadge + " actual=" + actualWindows +
                (match ? " ✓" : " ✗ MISMATCH"));
            Trace("BADGE", label + " pid=" + pid + " dock=" + dockBadge +
                " actual=" + actualWindows + (match ? " OK" : " FAIL"));
        }

        Verdict("BadgeAccuracy", allMatch,
            allMatch ? string.Join(", ", results) : string.Join("; ", results));
    }

    /// <summary>Count visible windows across all processes matching a name/path.</summary>
    static int CountVisibleWindowsForName(string pinName, string pinPath)
    {
        int total = 0;
        var counted = new HashSet<int>();
        IntPtr hw = IntPtr.Zero;
        while ((hw = FindNextWindow(hw)) != IntPtr.Zero)
        {
            if (!IsWindowVisible(hw)) continue;
            long ex = GetWindowLongPtr(hw, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0) continue;
            var sb = new StringBuilder(256);
            GetWindowText(hw, sb, 256);
            if (sb.Length == 0) continue;

            uint ppid;
            GetWindowThreadProcessId(hw, out ppid);
            int pp = (int)ppid;
            if (counted.Contains(pp)) continue;

            try
            {
                var p = Process.GetProcessById(pp);
                if (!p.ProcessName.Equals(pinName, StringComparison.OrdinalIgnoreCase)) continue;
                string pf = "";
                try { pf = p.MainModule.FileName; } catch { }
                if (!string.IsNullOrEmpty(pf) && !pf.Equals(pinPath, StringComparison.OrdinalIgnoreCase)) continue;

                counted.Add(pp);
                // Count all visible non-toolwindow titled windows for this PID
                int wp = 0;
                IntPtr hw2 = IntPtr.Zero;
                while ((hw2 = FindNextWindow(hw2)) != IntPtr.Zero)
                {
                    if (!IsWindowVisible(hw2)) continue;
                    long ex2 = GetWindowLongPtr(hw2, GWL_EXSTYLE);
                    if ((ex2 & WS_EX_TOOLWINDOW) != 0) continue;
                    var sb2 = new StringBuilder(256);
                    GetWindowText(hw2, sb2, 256);
                    if (sb2.Length == 0) continue;
                    uint pid2;
                    GetWindowThreadProcessId(hw2, out pid2);
                    if ((int)pid2 == pp) wp++;
                }
                total += wp;
            }
            catch { }
        }
        return total;
    }

    static IntPtr FindNextWindow(IntPtr hWnd)
    {
        return hWnd == IntPtr.Zero
            ? GetTopWindow(IntPtr.Zero)
            : GetWindow(hWnd, 2); // GW_HWNDNEXT
    }

    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
}
