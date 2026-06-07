using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// ============================================================
// DockManager — lifecycle, real-time sync, multi-monitor, crash recovery
// ============================================================

static class DockManager
{
    // ===== P/Invoke for window enumeration =====
    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int c);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int idx, int val);
    [DllImport("user32.dll")] static extern IntPtr GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);

    const uint GW_HWNDNEXT = 2;
    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;

    struct POINT { public int X, Y; }

    // ===== State =====
    static Form lineForm;
    static List<DockIcon> icons = new List<DockIcon>();
    static System.Windows.Forms.Timer themePoll, watchdogTimer;
    static NotifyIcon trayIcon;
    static bool dockVisible = true;
    static int foregroundPid;
    static int ownPid = System.Diagnostics.Process.GetCurrentProcess().Id;

    class WindowInfo { public IntPtr HWnd; public int Pid; public string Title; }

    public static Form Create()
    {
        PinStore.Load();
        DockIcon.SetFormIcon(AppDomain.CurrentDomain.BaseDirectory + @"Windock-icon.ico");

        // Invisible anchor form — only serves as message pump host
        Icon appIcon = null;
        try { appIcon = new Icon(AppDomain.CurrentDomain.BaseDirectory + @"Windock.ico"); } catch { }
        lineForm = new Form
        {
            Size = new Size(1, 1),
            StartPosition = FormStartPosition.Manual,
            FormBorderStyle = FormBorderStyle.None,
            TopMost = false,
            ShowInTaskbar = false,
            ShowIcon = false,
            Icon = appIcon,
            BackColor = Color.Black,
            TransparencyKey = Color.Black,
            Opacity = 0,
            Text = "WinDock",
        };

        lineForm.FormClosed += (s, e) =>
        {
            // D4: Always restore taskbar on close (including crash path via Dispose)
            RestoreTaskbar();
            foreach (var ic in icons) ic.Dispose();
            icons.Clear();
        };

        lineForm.Shown += (s, e) =>
        {
            HideTaskbar();
            FullRefresh(); Layout();
            lineForm.BeginInvoke((Action)delegate {
                trayIcon = new NotifyIcon { Text = "WinDock", Icon = appIcon ?? SystemIcons.Shield, Visible = true };
                trayIcon.DoubleClick += (s2, e2) => Toggle();
            });
            // V3: Subscribe to hover events for elastic lens
            DockIcon.HoverChanged += SpreadLensEffect;
            // Start theme + foreground poll AFTER initial refresh (avoid race)
            if (themePoll != null) themePoll.Start();
        };

        // Theme + foreground polling (started in Shown after initial refresh)
        themePoll = new System.Windows.Forms.Timer { Interval = 400 };
        themePoll.Tick += (s, e) =>
        {
            // Theme check
            var light = (int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) ?? 0) == 1;
            if (light != Theme.IsLight)
            {
                Theme.IsLight = light; Theme.Init();
                UpdateSpecialIcons();
                foreach (var di in icons) di.UpdateTheme();
            }

            // V1: Check for closed windows (stale HWnd)
            var toRemove = new List<DockIcon>();
            foreach (var di in icons)
            {
                if (di.Pid <= 0 || di.HWnd == IntPtr.Zero) continue;
                // IsWindow returns false only for truly invalid handles
                // Do NOT check IsWindowVisible — minimized windows are not visible but still valid
                if (!IsWindow(di.HWnd))
                {
                    if (di.Pinned)
                    {
                        // Pinned: keep icon, clear running state
                        di.HWnd = IntPtr.Zero;
                        di.SetTooltip(System.IO.Path.GetFileNameWithoutExtension(di.PinPath ?? ""));
                        di.SetBadge(0);
                    }
                    else
                    {
                        toRemove.Add(di);
                    }
                }
            }
            foreach (var di in toRemove)
            {
                EventLog.Info("StaleRemove pid=" + di.Pid + " pinPath=" + (di.PinPath ?? "-"));
                icons.Remove(di);
                di.Dispose();
            }
            if (toRemove.Count > 0)
            {
                LayoutWithLock();
            }

            // V1: Track foreground app — incremental add if new app appears
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                int fgPid = GetPid(fg);
                if (fgPid != foregroundPid && fgPid > 0)
                {
                    foregroundPid = fgPid;
                    // foreground changed (no visual indicator without line form)

                    // If foreground is a new app, add it. But first check if a pinned
                    // inactive icon matches — if so, just update that icon to running.
                    bool known = false;
                    DockIcon matchedPinned = null;
                    string fgExePath = "";
                    try { fgExePath = Process.GetProcessById(fgPid).MainModule.FileName; } catch { }

                    foreach (var di in icons)
                    {
                        if (di.Pid == fgPid) { known = true; break; }
                        // Match pinned-inactive icon by PinPath
                        if (di.Pinned && di.HWnd == IntPtr.Zero && !string.IsNullOrEmpty(di.PinPath)
                            && di.PinPath.Equals(fgExePath, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedPinned = di;
                        }
                    }

                    if (matchedPinned != null)
                    {
                        // Update existing pinned-inactive icon to running
                        matchedPinned.HWnd = fg;
                        matchedPinned.Pid = fgPid;
                        matchedPinned.SetTooltip(GetWinTitle(fg));
                        matchedPinned.SetBadge(CountWindows(fgPid));
                        LayoutWithLock();
                    }
                    else if (!known && !IsWidgetWindow(GetWinTitle(fg)))
                    {
                        try {
                            var p = Process.GetProcessById(fgPid);
                            string pn = p.ProcessName.ToLower();
                            // V2: unified exclusion list (keep in sync with FullRefresh below)
                            if (pn != "explorer" && pn != "searchapp" && pn != "textinputhost"
                                && pn != "shellexperiencehost" && pn != "clicktodo" && pn != "systemsettings"
                                && pn != "steamwebhelper" && pn != "steamservice")
                            {
                                // V2: check if new foreground process is a child/helper of an already-tracked app.
                                // If any existing icon's pinned path basename is a prefix of this process name
                                // AND they share the same directory, skip — it's a helper process.
                                bool isHelperOfTracked = false;
                                foreach (var existing in icons)
                                {
                                    if (existing.Pid <= 0) continue;
                                    try
                                    {
                                        var ep = Process.GetProcessById(existing.Pid);
                                        string en = ep.ProcessName.ToLower();
                                        string eDir = "";
                                        try { eDir = System.IO.Path.GetDirectoryName(ep.MainModule.FileName); } catch { }
                                        string fDir = "";
                                        try { fDir = System.IO.Path.GetDirectoryName(fgExePath); } catch { }
                                        // Same process name → already tracked by PID check above
                                        // Child detection: process name starts with tracked app's name + same directory
                                        if (!string.IsNullOrEmpty(eDir) && !string.IsNullOrEmpty(fDir)
                                            && eDir.Equals(fDir, StringComparison.OrdinalIgnoreCase)
                                            && (pn.StartsWith(en) || en.StartsWith(pn)))
                                        {
                                            isHelperOfTracked = true;
                                            break;
                                        }
                                    }
                                    catch { }
                                }
                                if (!isHelperOfTracked)
                                {
                                    // V2: Only create icon if foreground window IS the process main window.
                                    // This filters out dialog boxes, popups, and child windows.
                                    IntPtr mainHwnd = IntPtr.Zero;
                                    try { mainHwnd = p.MainWindowHandle; } catch { }
                                    if (mainHwnd != IntPtr.Zero && fg == mainHwnd)
                                    {
                                        var di = CreateRunningIcon(fg, fgPid);
                                        if (di != null)
                                        {
                                            EventLog.Info("FgAdd pid=" + fgPid + " proc=" + pn + " exe=" + (fgExePath ?? "?"));
                                            di.SetTooltip(GetWinTitle(fg));
                                            di.SetBadge(CountWindows(fgPid));
                                            BindRightClick(di);
                                            icons.Add(di);
                                            di.Show();
                                            LayoutWithLock();
                                        }
                                    }
                                }
                            }
                        } catch { }
                    }
                }
            }
        };

        // D4: Watchdog timer — ensures taskbar is restored even on unexpected crash
        watchdogTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        watchdogTimer.Tick += (s, e) =>
        {
            if (dockVisible) return;
            // If dock says it's hidden but dock forms are visible, something went wrong
            var tb = FindWindow("Shell_TrayWnd", null);
            if (tb != IntPtr.Zero && !IsWindowVisible(tb))
                RestoreTaskbar(); // safety restore
        };
        watchdogTimer.Start();

        // D4: Global exception handler
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            RestoreTaskbar();
            System.IO.File.AppendAllText(@"C:\temp\_dock_crash.txt",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " CRASH: " + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "unknown") + "\n");
        };

        // Force handle creation and set TOOLWINDOW so dock never shows its own windows
        var lfHandle = lineForm.Handle; // forces creation
        SetWindowLong(lfHandle, GWL_EXSTYLE, GetWindowLong(lfHandle, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);

        return lineForm;
    }

    // ===== D2: Multi-monitor support =====
    static Screen GetActiveScreen()
    {
        // Check which screen the mouse is on
        POINT cursor;
        if (GetCursorPos(out cursor))
        {
            foreach (var scr in Screen.AllScreens)
            {
                if (scr.Bounds.Contains(cursor.X, cursor.Y))
                    return scr;
            }
        }
        return Screen.PrimaryScreen;
    }

    static int[] GetScreenMetrics()
    {
        var scr = GetActiveScreen();
        return new[] { scr.WorkingArea.Width, scr.WorkingArea.Height, scr.Bounds.Width, scr.Bounds.Height };
    }

    // ===== Separator =====
    /// <summary>Find first non-special, non-pinned icon — separator goes before it.</summary>
    static int GetFirstRunningNonPinnedIndex()
    {
        for (int i = 0; i < icons.Count; i++)
        {
            if (icons[i].Pid > 0 && !icons[i].Pinned)
                return i;
        }
        return -1; // all pinned or no running — no separator
    }

    // ===== V3: Elastic lens =====
    static void SpreadLensEffect(DockIcon hovered, bool entering)
    {
        int idx = icons.IndexOf(hovered);
        if (idx < 0) return;
        // Nudge profile: center=1.35, immediate neighbors=1.18, next=1.06
        float[] profile = { 1.0f, 1.06f, 1.18f, 1.35f, 1.18f, 1.06f, 1.0f };
        int center = 3;

        for (int di = -3; di <= 3; di++)
        {
            int ni = idx + di;
            if (ni < 0 || ni >= icons.Count || ni == idx) continue;

            if (entering)
            {
                float nudge = profile[center + di];
                // Only nudge if not already fully zoomed by another hover
                if (nudge > icons[ni].targetScale && icons[ni].targetScale < 1.30f)
                    icons[ni].targetScale = nudge;
            }
            else
            {
                // On leave: reset to 1.0 UNLESS this neighbor is itself hovered
                if (icons[ni].targetScale < 1.30f)
                    icons[ni].targetScale = 1.0f;
            }
        }
    }

    // ===== Layout =====
    static void Layout()
    {
        var metrics = GetScreenMetrics();
        LayoutEngine.Apply(icons, metrics[0], metrics[1], DebugMode.On);
    }

    /// <summary>Layout with RefreshLock held 150ms after, so queued MouseLeave events are suppressed.</summary>
    static void LayoutWithLock()
    {
        DockIcon.RefreshLock = true;
        Layout();
        // Keep lock for 150ms so any queued WM_MOUSELEAVE from SetWindowPos is suppressed
        var unlockTimer = new System.Windows.Forms.Timer { Interval = 150 };
        unlockTimer.Tick += (s2, e2) => { unlockTimer.Stop(); unlockTimer.Dispose(); DockIcon.RefreshLock = false; };
        unlockTimer.Start();
    }

    // ===== Full refresh (startup / toggle) =====
    static void FullRefresh()
    {
        if (!dockVisible) return;
        EventLog.Info("FullRefresh START oldCount=" + icons.Count);
        PinStore.Load();
        var newIcons = new List<DockIcon>();
        var seenPids = new HashSet<int>();

        EnsureMinimizeIcon(newIcons);
        EnsureStartIcon(newIcons);

        // Enumerate windows
        var winInfo = new Dictionary<int, WindowInfo>();
        IntPtr hWnd = IntPtr.Zero;
        while ((hWnd = FindNextWindow(hWnd)) != IntPtr.Zero)
        {
            if (!IsVisibleWindow(hWnd)) continue;
            int pid = GetPid(hWnd); if (pid == 0) continue;
            var t = GetWinTitle(hWnd);
            if (IsWidgetWindow(t)) continue;
            try
            {
                var p = Process.GetProcessById(pid);
                string pn = p.ProcessName.ToLower();
                if (pn == "explorer" || pn == "searchapp" || pn == "textinputhost"
                    || pn == "shellexperiencehost" || pn == "clicktodo" || pn == "systemsettings"
                    || pn == "steamwebhelper" || pn == "steamservice") continue;
            }
            catch { continue; }
            if (!winInfo.ContainsKey(pid) || t.Length > winInfo[pid].Title.Length)
                winInfo[pid] = new WindowInfo { HWnd = hWnd, Pid = pid, Title = t };
        }

        // Match pinned paths
        var pinnedPids = new HashSet<int>();
        foreach (var path in PinStore.PinnedPaths)
        {
            string pinName = System.IO.Path.GetFileNameWithoutExtension(path);
            bool matched = false;
            foreach (var kv in winInfo)
            {
                try
                {
                    var proc = Process.GetProcessById(kv.Key);
                    string pn = proc.ProcessName;
                    string pf = "";
                    try { pf = proc.MainModule.FileName; } catch { }
                    // Match by full exe path, or exact process name match.
                    // Do NOT use StartsWith — it causes false matches like
                    // "steamwebhelper" starting with "steam", creating duplicates.
                    if (pf.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                        pn.Equals(pinName, StringComparison.OrdinalIgnoreCase))
                    { pinnedPids.Add(kv.Key); matched = true; break; }
                }
                catch { }
            }
            if (!matched)
            {
                var di = FindOrCreatePinnedInactive(path, icons, newIcons);
                if (di != null && !newIcons.Contains(di)) newIcons.Add(di);
            }
        }

        // Create icons for running apps
        foreach (var kv in winInfo)
        {
            if (pinnedPids.Contains(kv.Key)) continue;
            seenPids.Add(kv.Key);
            var di = FindOrCreateRunning(kv.Value.HWnd, kv.Key, icons, newIcons, winInfo);
            if (di != null && !newIcons.Contains(di)) newIcons.Add(di);
        }

        // Also add pinned-and-running icons
        foreach (var kv in winInfo)
        {
            if (!pinnedPids.Contains(kv.Key)) continue;
            foreach (var path in PinStore.PinnedPaths)
            {
                try
                {
                    var proc = Process.GetProcessById(kv.Key);
                    string pf = "";
                    try { pf = proc.MainModule.FileName; } catch { }
                    if (pf.Equals(path, StringComparison.OrdinalIgnoreCase))
                    {
                        var di = FindOrCreatePinnedIcon(proc, path, icons, newIcons);
                        if (di != null && !newIcons.Contains(di)) newIcons.Add(di);
                        break;
                    }
                }
                catch { }
            }
        }

        // Update badges, tooltips, menus
        foreach (var di in newIcons)
        {
            if (di.Pid > 0) di.SetBadge(CountWindows(di.Pid));
            if (di.HWnd != IntPtr.Zero) di.SetTooltip(GetWinTitle(di.HWnd));
            else if (!string.IsNullOrEmpty(di.PinPath)) di.SetTooltip(System.IO.Path.GetFileNameWithoutExtension(di.PinPath));
        }
        BindRightClickMenus(newIcons);

        // D1: Dispose removed icons (incremental-safe: only icons not in new list)
        foreach (var ic in icons) { if (!newIcons.Contains(ic)) ic.Dispose(); }
        icons = newIcons;
        Layout();
        foreach (var di in icons) if (!di.Form.Visible) di.Show();

        EventLog.Info("FullRefresh DONE newCount=" + icons.Count);
        var snapLines = new string[icons.Count];
        for (int si = 0; si < icons.Count; si++)
        {
            var d = icons[si];
            bool disp = d.Form == null || d.Form.IsDisposed;
            snapLines[si] = string.Format("[{0}] pid={1} hwnd={2} pinned={3} disposed={4} pos=({5},{6}) pin={7}",
                si, d.Pid, d.HWnd != IntPtr.Zero, d.Pinned, disp,
                disp ? -1 : d.Form.Left, disp ? -1 : d.Form.Top, d.PinPath ?? "-");
        }
        EventLog.DumpIconState(snapLines);
        if (DebugMode.On) DumpIcons();
    }

    // ===== Toggle =====
    static void Toggle()
    {
        dockVisible = !dockVisible;
        EventLog.Info("Toggle → visible=" + dockVisible + " iconCount=" + icons.Count);
        IntPtr tb = FindWindow("Shell_TrayWnd", null);
        if (dockVisible)
        {
            HideTaskbar();
            lineForm.WindowState = FormWindowState.Normal;
            // FullRefresh creates, shows, and layouts all icons — no need to Show old ones first
            FullRefresh();
        }
        else
        {
            RestoreTaskbar();
            lineForm.WindowState = FormWindowState.Minimized;
            foreach (var di in icons) {
                if (di == null || di.Form == null || di.Form.IsDisposed) continue;
                di.Hide(); ShowWindow(di.Form.Handle, 0);
            }
        }
    }

    // ===== Taskbar =====
    static void HideTaskbar()
    {
        if (DebugMode.On) return;
        var tb = FindWindow("Shell_TrayWnd", null);
        if (tb != IntPtr.Zero) ShowWindow(tb, 0);
    }

    static void RestoreTaskbar()
    {
        var tb = FindWindow("Shell_TrayWnd", null);
        if (tb != IntPtr.Zero) ShowWindow(tb, 5);
    }

    // ===== Special icons =====
    static void EnsureMinimizeIcon(List<DockIcon> newIcons)
    {
        if (icons.Count > 0 && icons[0].Pid == -1) { newIcons.Add(icons[0]); return; }
        var mi = new DockIcon(44, 8); mi.Pid = -1;
        var mBmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        using (var g = Graphics.FromImage(mBmp)) {
            g.Clear(Color.Transparent); int sz = mBmp.Width, m = sz / 4;
            Color c = Theme.IsLight ? Color.Black : Color.White;
            using (var p = new Pen(c, Math.Max(2, sz / 16)))
            { g.DrawLine(p, m, sz - m, sz / 2, sz / 2); g.DrawLine(p, sz / 2, sz / 2, sz - m, sz - m); }
        }
        mi.SetIcon(mBmp); mi.SetClick(() => Toggle()); mi.SetTooltip("WinDock — Minimize/Restore"); newIcons.Insert(0, mi);
    }

    static void EnsureStartIcon(List<DockIcon> newIcons)
    {
        for (int i = icons.Count - 1; i >= 0; i--) { if (icons[i].Pid == -2) { newIcons.Insert(1, icons[i]); return; } }
        var si = new DockIcon(44, 8); si.Pid = -2;
        var bmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Transparent); int sz = bmp.Width, g2 = Math.Max(2, sz / 20), sq = (sz - g2 * 3) / 2, m = (sz - sq * 2 - g2) / 2;
            Color c = Theme.IsLight ? Color.Black : Color.White;
            using (var br = new SolidBrush(c)) { g.FillRectangle(br, m, m, sq, sq); g.FillRectangle(br, m + sq + g2, m, sq, sq); g.FillRectangle(br, m, m + sq + g2, sq, sq); g.FillRectangle(br, m + sq + g2, m + sq + g2, sq, sq); }
        }
        si.SetIcon(bmp); si.SetClick(() => SendKeys.Send("^{ESC}")); si.SetTooltip("Start"); newIcons.Insert(1, si);
    }

    static void UpdateSpecialIcons()
    {
        if (icons.Count < 2) return;
        var si = icons[1];
        var bmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Transparent); int sz = bmp.Width, g2 = Math.Max(2, sz / 20), sq = (sz - g2 * 3) / 2, m = (sz - sq * 2 - g2) / 2;
            Color c = Theme.IsLight ? Color.Black : Color.White;
            using (var br = new SolidBrush(c)) { g.FillRectangle(br, m, m, sq, sq); g.FillRectangle(br, m + sq + g2, m, sq, sq); g.FillRectangle(br, m, m + sq + g2, sq, sq); g.FillRectangle(br, m + sq + g2, m + sq + g2, sq, sq); }
        }
        si.SetIcon(bmp);

        var mi = icons[0];
        var mBmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        using (var g = Graphics.FromImage(mBmp)) {
            g.Clear(Color.Transparent); int sz2 = mBmp.Width, m2 = sz2 / 4;
            Color c2 = Theme.IsLight ? Color.Black : Color.White;
            using (var p = new Pen(c2, Math.Max(2, sz2 / 16))) { g.DrawLine(p, m2, sz2 - m2, sz2 / 2, sz2 / 2); g.DrawLine(p, sz2 / 2, sz2 / 2, sz2 - m2, sz2 - m2); }
        }
        mi.SetIcon(mBmp);
    }

    // ===== Icon factory methods =====
    static DockIcon FindOrCreatePinnedIcon(Process proc, string path, List<DockIcon> oldIcons, List<DockIcon> newIcons)
    {
        foreach (var ic in oldIcons) if (ic.PinPath == path) { ic.Pid = proc.Id; ic.HWnd = proc.MainWindowHandle; return ic; }
        foreach (var ic in newIcons) if (ic.PinPath == path) { ic.Pid = proc.Id; ic.HWnd = proc.MainWindowHandle; return ic; }
        var di = new DockIcon(44, 8); di.Pinned = true; di.PinPath = path; di.HWnd = proc.MainWindowHandle; di.Pid = proc.Id;
        try { using (var ico = Icon.ExtractAssociatedIcon(path)) { var b = DockIcon.IconToBmpAtDpi(ico); if (b == null) return null; di.SetIcon(b); } } catch { return null; }
        di.SetClick(() => {
            if (di.HWnd != IntPtr.Zero && IsWindow(di.HWnd)) { DockBar.FocusWindow(di.HWnd); }
            else { Process.Start(path); var t = new System.Windows.Forms.Timer { Interval = 150 }; t.Tick += (s2, e2) => { t.Stop(); t.Dispose(); ApplyForegroundToIcon(di); }; t.Start(); }
        });
        return di;
    }

    static DockIcon FindOrCreatePinnedInactive(string path, List<DockIcon> oldIcons, List<DockIcon> newIcons)
    {
        foreach (var ic in oldIcons) { if (ic.PinPath != null && ic.PinPath.Equals(path, StringComparison.OrdinalIgnoreCase)) return ic; }
        foreach (var ic in newIcons) { if (ic.PinPath != null && ic.PinPath.Equals(path, StringComparison.OrdinalIgnoreCase)) return ic; }
        var di = new DockIcon(44, 8); di.Pinned = true; di.PinPath = path;
        try { using (var ico = Icon.ExtractAssociatedIcon(path)) { var b = DockIcon.IconToBmpAtDpi(ico); if (b == null) return null; di.SetIcon(b); } } catch { return null; }
        di.SetClick(() => {
            if (!string.IsNullOrEmpty(di.PinPath))
            {
                Process.Start(di.PinPath);
                // Instant feedback: update icon state after short launch delay
                var t = new System.Windows.Forms.Timer { Interval = 150 };
                t.Tick += (s2, e2) => {
                    t.Stop(); t.Dispose();
                    ApplyForegroundToIcon(di);
                };
                t.Start();
            }
        });
        return di;
    }

    /// <summary>After launching from a pinned-inactive icon, immediately find the new process and update.</summary>
    static void ApplyForegroundToIcon(DockIcon di)
    {
        if (string.IsNullOrEmpty(di.PinPath)) return;
        try
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(di.PinPath);
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.MainModule.FileName.Equals(di.PinPath, StringComparison.OrdinalIgnoreCase)
                        && p.MainWindowHandle != IntPtr.Zero)
                    {
                        di.HWnd = p.MainWindowHandle;
                        di.Pid = p.Id;
                        di.SetTooltip(p.MainWindowTitle);
                        di.SetBadge(CountWindows(p.Id));
                        foregroundPid = p.Id;
                        lineForm.Invalidate();
                        return;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    static DockIcon FindOrCreateRunning(IntPtr hWnd, int pid, List<DockIcon> oldIcons, List<DockIcon> newIcons, Dictionary<int, WindowInfo> winInfo)
    {
        foreach (var ic in oldIcons) { if (!ic.Pinned && ic.Pid == pid) { ic.HWnd = hWnd; return ic; } }
        foreach (var ic in newIcons) { if (!ic.Pinned && ic.Pid == pid) { ic.HWnd = hWnd; return ic; } }
        foreach (var ic in oldIcons) { if (ic.Pinned && ic.Pid == pid && !PinStore.IsPinned(ic.PinPath)) { ic.Pinned = false; ic.PinPath = null; ic.HWnd = hWnd; return ic; } }
        return CreateRunningIcon(hWnd, pid);
    }

    static DockIcon CreateRunningIcon(IntPtr hWnd, int pid)
    {
        var di = new DockIcon(44, 8);
        di.HWnd = hWnd; di.Pid = pid;
        try {
            var p = Process.GetProcessById(pid);
            using (var ico = Icon.ExtractAssociatedIcon(p.MainModule.FileName)) {
                var bmp = DockIcon.IconToBmpAtDpi(ico); if (bmp == null) return null; di.SetIcon(bmp);
            }
        } catch { return null; }
        di.SetClick(() => DockBar.FocusWindow(di.HWnd));
        return di;
    }

    // ===== Right-click binding =====
    static void BindRightClickMenus(List<DockIcon> iconList)
    {
        foreach (var di in iconList)
        {
            if (di.Pid < 0) { di.BindClicks(); continue; }
            BindRightClick(di);
            di.BindClicks();
        }
    }

    static void BindRightClick(DockIcon di)
    {
        var pid = di.Pid;
        di.SetRightClick(pos => {
            bool running = di.HWnd != IntPtr.Zero;
            bool pinned = di.Pinned && !string.IsNullOrEmpty(di.PinPath) && PinStore.IsPinned(di.PinPath);
            var _hwnd = di.HWnd; var _pinPath = di.PinPath; var _pid = di.Pid;
            IconMenu.Show(pos, running, pinned,
                onClose: () => {
                    if (_hwnd != IntPtr.Zero) DockBar.CloseWindow(_hwnd);
                    else if (!string.IsNullOrEmpty(_pinPath)) Process.Start(_pinPath);
                    ScheduleRefresh();
                },
                onTogglePin: () => {
                    if (string.IsNullOrEmpty(_pinPath)) { string exe = DockBar.GetExePath(_pid); if (!string.IsNullOrEmpty(exe)) PinStore.Pin(exe); }
                    else if (PinStore.IsPinned(_pinPath)) PinStore.Unpin(_pinPath);
                    else PinStore.Pin(_pinPath);
                    ScheduleRefresh();
                },
                onClosed: di.OnMenuClosed,
                onNewWindow: running ? (Action)(() => {
                    string exe = !string.IsNullOrEmpty(_pinPath) ? _pinPath : DockBar.GetExePath(_pid);
                    if (!string.IsNullOrEmpty(exe)) { Process.Start(exe); ScheduleRefresh(); }
                }) : null
            );
        });
    }

    static void ScheduleRefresh()
    {
        var t = new System.Windows.Forms.Timer { Interval = 800 };
        t.Tick += (s2, e2) => { FullRefresh(); t.Stop(); t.Dispose(); };
        t.Start();
    }

    // ===== Helpers =====
    static IntPtr FindNextWindow(IntPtr a) { return a == IntPtr.Zero ? GetTopWindow(IntPtr.Zero) : GetWindow(a, GW_HWNDNEXT); }

    static bool IsVisibleWindow(IntPtr hWnd)
    {
        if (!IsWindowVisible(hWnd)) return false;
        int ex = GetWindowLong(hWnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0) return false;
        var sb = new System.Text.StringBuilder(256); GetWindowText(hWnd, sb, 256);
        if (sb.Length == 0) return false;
        // Exclude own windows by title
        string t = sb.ToString();
        if (t.StartsWith("WinDock") || t.StartsWith("WinPrism")) return false;
        return true;
    }

    static int GetPid(IntPtr hWnd) { int pid; GetWindowThreadProcessId(hWnd, out pid); return pid; }

    static string GetWinTitle(IntPtr hWnd) { var sb = new System.Text.StringBuilder(256); GetWindowText(hWnd, sb, 256); return sb.ToString(); }

    static bool IsWidgetWindow(string t)
    {
        return t == "TopBar" || t.StartsWith("WinDock") || t == "System" || t == "Disk" || t == "Network" || t == "Battery" || t == "Recycle Bin" || t == "WiFi Panel" || t == "Audio" || t == "Settings" || t == "设置";
    }

    static int CountWindows(int pid)
    {
        int c = 0; IntPtr hw = IntPtr.Zero;
        while ((hw = FindNextWindow(hw)) != IntPtr.Zero) { int pp; GetWindowThreadProcessId(hw, out pp); if (pp == pid && IsVisibleWindow(hw)) c++; }
        return c;
    }

    static void DumpIcons()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Icons @ " + DateTime.Now.ToString("HH:mm:ss") + " ===");
            for (int i = 0; i < icons.Count; i++)
            {
                var di = icons[i];
                string title = ""; if (di.HWnd != IntPtr.Zero) try { title = GetWinTitle(di.HWnd); } catch { }
                sb.AppendLine(string.Format("  [{0}] pid={1} hwnd={2} pinned={3} pinPath={4} title={5}", i, di.Pid, di.HWnd != IntPtr.Zero, di.Pinned, di.PinPath ?? "-", title.Length > 20 ? title.Substring(0, 20) : title));
            }
            System.IO.File.AppendAllText(@"C:\temp\_dock_dump.txt", sb.ToString());
        }
        catch { }
    }
}
