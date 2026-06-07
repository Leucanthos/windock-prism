using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// ============================================================
// DockLine — dynamic dock with full window sync + pinned apps
// ============================================================

static class DockLine
{
    static Form lineForm;
    static List<DockIcon> icons = new List<DockIcon>();
    static System.Windows.Forms.Timer refreshTimer, themePoll;
    const int gap = 14, maxApps = 20;
    static NotifyIcon trayIcon;
    static bool dockVisible = true;

    // Pinned app paths (from taskbar pins folder)
    static HashSet<string> pinnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static Form Create()
    {
        LoadPinnedApps();

        lineForm = new Form { Size = new Size(1, 1), StartPosition = FormStartPosition.Manual,
            FormBorderStyle = FormBorderStyle.None, TopMost = false, ShowInTaskbar = true,
            BackColor = Theme.FormBg, BackgroundImage = Theme.GlassBmp, BackgroundImageLayout = ImageLayout.Stretch };
        lineForm.Paint += OnPaintLine;
        lineForm.FormClosed += (s, e) => { var tb = FindWindow("Shell_TrayWnd", null); if (tb != IntPtr.Zero) ShowWindow(tb, 5); foreach (var ic in icons) ic.Dispose(); };
        lineForm.Shown += (s, e) => {
            RefreshAllIcons(); Layout();
            // Tray icon
            lineForm.BeginInvoke((Action)delegate {
                trayIcon = new System.Windows.Forms.NotifyIcon { Text = "WinDock", Icon = SystemIcons.Shield, Visible = true };
                trayIcon.DoubleClick += (s2, e2) => ToggleDock();
            });
        };

        // No auto-refresh — only refresh on startup and toggle
        // This prevents spurious MouseLeave during refresh cycles

        themePoll = new System.Windows.Forms.Timer { Interval = 800 };
        themePoll.Tick += (s, e) => {
            var light = (int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) ?? 0) == 1;
            if (light != Theme.IsLight) { Theme.IsLight = light; Theme.Init(); lineForm.BackColor = Theme.FormBg; lineForm.BackgroundImage = Theme.GlassBmp; lineForm.Invalidate(); UpdateSpecialIcons(); foreach (var di in icons) di.UpdateTheme(); }
        };
        themePoll.Start();

        return lineForm;
    }

    // ===== Pinned apps =====
    static string PinDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";

    static void LoadPinnedApps()
    {
        pinnedPaths.Clear();
        if (!Directory.Exists(PinDir)) return;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            foreach (var f in Directory.GetFiles(PinDir, "*.lnk"))
            {
                try
                {
                    dynamic lnk = shell.CreateShortcut(f);
                    string target = lnk.TargetPath;
                    if (!string.IsNullOrEmpty(target) && File.Exists(target))
                        pinnedPaths.Add(target);
                }
                catch { }
            }
        }
        catch { /* COM failed — dock still works with running apps */ }
    }

    static void PinApp(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            string lnkPath = Path.Combine(PinDir, Path.GetFileNameWithoutExtension(exePath) + ".lnk");
            dynamic lnk = shell.CreateShortcut(lnkPath);
            lnk.TargetPath = exePath;
            lnk.Save();
        }
        catch { }
    }

    static void UnpinApp(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            foreach (var f in Directory.GetFiles(PinDir, "*.lnk"))
            {
                try
                {
                    dynamic lnk = shell.CreateShortcut(f);
                    if (lnk.TargetPath.Equals(exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(f);
                        break;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    // ===== Icon management =====
    static void RefreshAllIcons()
    {
        if (!dockVisible) return;
        LoadPinnedApps(); // re-scan each cycle
        var seenPids = new HashSet<int>();
        var newIcons = new List<DockIcon>();

        // 1. Always keep minimize + start
        EnsureMinimizeIcon(newIcons);
        EnsureStartIcon(newIcons);

        // 2. Enumerate all visible windows (including pinned apps)
        IntPtr hWnd = IntPtr.Zero;
        var winInfo = new Dictionary<int, WindowInfo>(); // pid -> info
        while ((hWnd = DockBar.FindNextWindow(hWnd)) != IntPtr.Zero)
        {
            if (!DockBar.IsVisibleWindow(hWnd)) continue;
            int pid = DockBar.GetPid(hWnd); if (pid == 0) continue;
            var t = DockBar.GetWinTitle(hWnd);
            if (IsWidgetWindow(t)) continue;
            try { var p = Process.GetProcessById(pid);
                string pn = p.ProcessName.ToLower(); if (pn == "explorer" || pn == "searchapp" || pn == "textinputhost" || pn == "shellexperiencehost" || pn == "clicktodo" || pn == "systemsettings") continue; } catch { continue; }
            if (!winInfo.ContainsKey(pid) || t.Length > winInfo[pid].Title.Length)
                winInfo[pid] = new WindowInfo { HWnd = hWnd, Pid = pid, Title = t };
        }

        // 3. Match pinned paths to running processes
        var pinnedPids = new HashSet<int>();
        foreach (var path in pinnedPaths)
        {
            string pinName = Path.GetFileNameWithoutExtension(path);
            bool matched = false;
            foreach (var kv in winInfo)
            {
                try
                {
                    var proc = Process.GetProcessById(kv.Key);
                    string pn = proc.ProcessName;
                    string pf = "";
                    try { pf = proc.MainModule.FileName; } catch { }
                    if (pf.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                        pn.Equals(pinName, StringComparison.OrdinalIgnoreCase) ||
                        pn.StartsWith(pinName, StringComparison.OrdinalIgnoreCase))
                    {
                        pinnedPids.Add(kv.Key); matched = true;
                        var di = FindOrCreatePinnedIcon(proc, path);
                        if (di != null && !newIcons.Contains(di)) newIcons.Add(di);
                        break;
                    }
                }
                catch { }
            }
            if (!matched)
            {
                // Pinned but not running
                var di = FindOrCreatePinnedInactive(path);
                if (di != null && !newIcons.Contains(di)) newIcons.Add(di);
            }
        }

        // 4. Remaining running apps (not pinned)
        foreach (var kv in winInfo)
        {
            if (pinnedPids.Contains(kv.Key)) continue;
            seenPids.Add(kv.Key);
            var di = FindOrCreateRunning(kv.Value.HWnd, kv.Key, newIcons);
            if (di != null) newIcons.Add(di);
        }

        // 5. Update badges + tooltips
        foreach (var di in newIcons)
        {
            if (di.Pid > 0) di.SetBadge(CountWindows(di.Pid));
            if (di.HWnd != IntPtr.Zero) di.SetTooltip(DockBar.GetWinTitle(di.HWnd));
            else if (!string.IsNullOrEmpty(di.PinPath)) di.SetTooltip(Path.GetFileNameWithoutExtension(di.PinPath));
        }

        // 5b. Setup right-click popup
        foreach (var di in newIcons)
        {
            if (di.Pid < 0) { di.BindClicks(); continue; } // special icons: bind left-click only
            var pid = di.Pid;
            di.SetRightClick(pos => {
                bool running = di.HWnd != IntPtr.Zero;
                bool pinned = di.Pinned && !string.IsNullOrEmpty(di.PinPath) && pinnedPaths.Contains(di.PinPath);
                var _hwnd = di.HWnd;
                var _pinPath = di.PinPath;
                var _pid = di.Pid;
                IconMenu.Show(pos, running, running, pinned,
                    onCloseWindow: () => {
                        if (_hwnd != IntPtr.Zero) DockBar.CloseWindow(_hwnd);
                        var t = new System.Windows.Forms.Timer{Interval=800}; t.Tick+=(s2,e2)=>{RefreshAllIcons();t.Stop();t.Dispose();}; t.Start();
                    },
                    onEndTask: () => {
                        if (_pid > 0) DockBar.KillProcess(_pid);
                        var t = new System.Windows.Forms.Timer{Interval=800}; t.Tick+=(s2,e2)=>{RefreshAllIcons();t.Stop();t.Dispose();}; t.Start();
                    },
                    onTogglePin: () => {
                        if (string.IsNullOrEmpty(_pinPath)) { string exe = DockBar.GetExePath(_pid); if (!string.IsNullOrEmpty(exe)) PinApp(exe); }
                        else if (pinnedPaths.Contains(_pinPath)) UnpinApp(_pinPath);
                        else PinApp(_pinPath);
                        var t = new System.Windows.Forms.Timer{Interval=800}; t.Tick+=(s2,e2)=>{RefreshAllIcons();t.Stop();t.Dispose();}; t.Start();
                    },
                    onClosed: di.OnMenuClosed
                );
            });
            di.BindClicks();
        }

        // 6. Clean up removed icons (dispose any icon no longer needed)
        foreach (var ic in icons) { if (!newIcons.Contains(ic)) { System.IO.File.AppendAllText(@"C:\temp\_dock_dispose.txt",DateTime.Now.ToString("HH:mm:ss.fff")+" DISPOSE pid="+ic.Pid+"\n"); ic.Dispose(); } }
        icons = newIcons;
        Layout();
        // Show all icons after positioning (prevents flicker)
        foreach (var di in icons) if (!di.Form.Visible) di.Show();

        if (DebugMode.On) DumpIcons();
    }

    // Silent refresh: data sync only, no UI changes
    static void RefreshAllIconsSilent()
    {
        if (!dockVisible) return;
        LoadPinnedApps();
        var newIcons = new List<DockIcon>();
        // ... (same as RefreshAllIcons but without Layout/Show/Dispose)
        RefreshAllIcons(); // temporary: run full to test
    }

    // Minimal refresh: only sync icons without triggering mouse events
    static void RefreshAllIconsNoSideEffects()
    {
        if (!dockVisible) return;
        LoadPinnedApps();
        var newIcons = new List<DockIcon>();
        EnsureMinimizeIcon(newIcons);
        EnsureStartIcon(newIcons);

        IntPtr hWnd = IntPtr.Zero;
        var winInfo = new Dictionary<int, WindowInfo>();
        while ((hWnd = DockBar.FindNextWindow(hWnd)) != IntPtr.Zero)
        {
            if (!DockBar.IsVisibleWindow(hWnd)) continue;
            int pid = DockBar.GetPid(hWnd); if (pid == 0) continue;
            var t = DockBar.GetWinTitle(hWnd);
            if (IsWidgetWindow(t)) continue;
            try { var p = Process.GetProcessById(pid);
                string pn = p.ProcessName.ToLower(); if (pn == "explorer" || pn == "searchapp" || pn == "textinputhost" || pn == "shellexperiencehost" || pn == "clicktodo" || pn == "systemsettings") continue; } catch { continue; }
            if (!winInfo.ContainsKey(pid) || t.Length > winInfo[pid].Title.Length)
                winInfo[pid] = new WindowInfo { HWnd = hWnd, Pid = pid, Title = t };
        }

        var pinnedPids = new HashSet<int>();
        foreach (var path in pinnedPaths)
        {
            string pinName = Path.GetFileNameWithoutExtension(path);
            foreach (var kv in winInfo)
            {
                try
                {
                    var proc = Process.GetProcessById(kv.Key);
                    string pn = proc.ProcessName; string pf = "";
                    try { pf = proc.MainModule.FileName; } catch { }
                    if (pf.Equals(path, StringComparison.OrdinalIgnoreCase) || pn.Equals(pinName, StringComparison.OrdinalIgnoreCase) || pn.StartsWith(pinName, StringComparison.OrdinalIgnoreCase))
                    { pinnedPids.Add(kv.Key); var di = FindOrCreatePinnedIcon(proc, path); if (di != null && !newIcons.Contains(di)) newIcons.Add(di); break; }
                }
                catch { }
            }
        }
        foreach (var kv in winInfo)
        {
            if (pinnedPids.Contains(kv.Key)) continue;
            var di = FindOrCreateRunning(kv.Value.HWnd, kv.Key, newIcons);
            if (di != null) newIcons.Add(di);
        }

        foreach (var ic in icons) { if (!newIcons.Contains(ic)) { System.IO.File.AppendAllText(@"C:\temp\_dock_dispose.txt",DateTime.Now.ToString("HH:mm:ss.fff")+" DISPOSE pid="+ic.Pid+"\n"); ic.Dispose(); } }
        icons = newIcons;
        Layout();
        foreach (var di in icons) if (!di.Form.Visible) { System.IO.File.AppendAllText(@"C:\temp\_dock_show.txt",DateTime.Now.ToString("HH:mm:ss.fff")+" SHOW pid="+di.Pid+"\n"); di.Show(); }
    }

    static DockIcon FindOrCreatePinnedIcon(Process proc, string path)
    {
        foreach (var ic in icons) if (ic.PinPath == path) { ic.Pid = proc.Id; ic.HWnd = proc.MainWindowHandle; return ic; }
        var di = new DockIcon(44, 8); di.Pinned = true; di.PinPath = path; di.HWnd = proc.MainWindowHandle; di.Pid = proc.Id;
        try { using (var ico = Icon.ExtractAssociatedIcon(path)) { var b = DockIcon.IconToBmpAtDpi(ico); if (b == null) return null; di.SetIcon(b); } } catch { return null; }
        di.SetClick(() => { if (di.HWnd != IntPtr.Zero) DockBar.FocusWindow(di.HWnd); else Process.Start(path); });
        return di;
    }

    class WindowInfo { public IntPtr HWnd; public int Pid; public string Title; }

    static DockIcon FindOrCreateRunning(IntPtr hWnd, int pid, List<DockIcon> newIcons)
    {
        foreach (var ic in icons) { if (!ic.Pinned && ic.Pid == pid) { ic.HWnd = hWnd; return ic; } }
        foreach (var ic in newIcons) { if (!ic.Pinned && ic.Pid == pid) { ic.HWnd = hWnd; return ic; } }
        // Reclaim formerly-pinned icon that was just unpinned from taskbar
        foreach (var ic in icons) { if (ic.Pinned && ic.Pid == pid && !pinnedPaths.Contains(ic.PinPath)) { ic.Pinned = false; ic.PinPath = null; ic.HWnd = hWnd; return ic; } }
        var di = new DockIcon(44, 8);
        di.HWnd = hWnd; di.Pid = pid;
        try { var p = Process.GetProcessById(pid); using (var ico = Icon.ExtractAssociatedIcon(p.MainModule.FileName)) {
            var bmp = DockIcon.IconToBmpAtDpi(ico); if (bmp == null) return null; di.SetIcon(bmp); } } catch { return null; }
        di.SetClick(() => DockBar.FocusWindow(di.HWnd));
        return di;
    }

    static DockIcon FindOrCreatePinned(Process proc, string path)
    {
        foreach (var ic in icons) { if (ic.PinPath != null && ic.PinPath.Equals(path, StringComparison.OrdinalIgnoreCase)) { ic.HWnd = proc.MainWindowHandle; ic.Pid = proc.Id; return ic; } }
        var di = new DockIcon(44, 8);
        di.Pinned = true; di.PinPath = path; di.HWnd = proc.MainWindowHandle; di.Pid = proc.Id;
        try { using (var ico = Icon.ExtractAssociatedIcon(path)) {
            var bmp = DockIcon.IconToBmpAtDpi(ico); if (bmp == null) return null; di.SetIcon(bmp); } } catch { return null; }
        di.SetClick(() => { if (di.HWnd != IntPtr.Zero) DockBar.FocusWindow(di.HWnd); else if (!string.IsNullOrEmpty(di.PinPath)) Process.Start(di.PinPath); });
        return di;
    }

    static DockIcon FindOrCreatePinnedInactive(string path)
    {
        foreach (var ic in icons) { if (ic.PinPath != null && ic.PinPath.Equals(path, StringComparison.OrdinalIgnoreCase)) return ic; }
        var di = new DockIcon(44, 8);
        di.Pinned = true; di.PinPath = path;
        try { using (var ico = Icon.ExtractAssociatedIcon(path)) {
            var bmp = DockIcon.IconToBmpAtDpi(ico); if (bmp == null) return null; di.SetIcon(bmp); } } catch { return null; }
        di.SetClick(() => { if (!string.IsNullOrEmpty(di.PinPath)) Process.Start(di.PinPath); });
        return di;
    }

    static Process FindProcessByPath(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        Process best = null;
        foreach (var p in Process.GetProcesses()) {
            try { if (p.MainModule.FileName.Equals(path, StringComparison.OrdinalIgnoreCase)) { if (p.MainWindowHandle != IntPtr.Zero) return p; else best = p; } } catch { }
        }
        if (best != null) return best;
        // Fallback: match by process name
        foreach (var p in Process.GetProcesses()) {
            try { if (p.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)) { if (p.MainWindowHandle != IntPtr.Zero) return p; else if (best == null) best = p; } } catch { }
        }
        return best;
    }

    static int CountWindows(int pid) { int c = 0; IntPtr hw = IntPtr.Zero; while ((hw = DockBar.FindNextWindow(hw)) != IntPtr.Zero) { int pp; DockBar.GetWindowThreadProcessId2(hw, out pp); if (pp == pid && DockBar.IsVisibleWindow(hw)) c++; } return c; }

    static bool IsWidgetWindow(string t) { return t == "TopBar" || t == "WinDock" || t == "System" || t == "Disk" || t == "Network" || t == "Battery" || t == "Recycle Bin" || t == "WiFi Panel" || t == "Audio" || t == "Settings" || t == "设置"; }

    // ===== Fixed special icons =====
    static void EnsureMinimizeIcon(List<DockIcon> newIcons)
    {
        if (icons.Count > 0 && icons[0].Pid == -1) { newIcons.Add(icons[0]); return; }
        var mi = new DockIcon(44, 8); mi.Pid = -1; // special ID
        var mBmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        using (var g = Graphics.FromImage(mBmp)) { g.Clear(Color.Transparent); int sz = mBmp.Width, m = sz / 4;
            Color c = Theme.IsLight ? Color.Black : Color.White;
            using (var p = new Pen(c, Math.Max(2, sz / 16))) { g.DrawLine(p, m, sz - m, sz / 2, sz / 2); g.DrawLine(p, sz / 2, sz / 2, sz - m, sz - m); }
        }
        mi.SetIcon(mBmp); mi.SetClick(() => ToggleDock()); mi.SetTooltip("WinDock — Minimize/Restore"); newIcons.Insert(0, mi);
    }

    static void EnsureStartIcon(List<DockIcon> newIcons)
    {
        for (int i = icons.Count - 1; i >= 0; i--) { if (icons[i].Pid == -2) { newIcons.Insert(1, icons[i]); return; } } // -2 = start
        var si = new DockIcon(44, 8); si.Pid = -2;
        var bmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); int sz = bmp.Width, g2 = Math.Max(2, sz / 20), sq = (sz - g2 * 3) / 2, m = (sz - sq * 2 - g2) / 2;
            Color c = Theme.IsLight ? Color.Black : Color.White;
            using (var br = new SolidBrush(c)) { g.FillRectangle(br, m, m, sq, sq); g.FillRectangle(br, m + sq + g2, m, sq, sq); g.FillRectangle(br, m, m + sq + g2, sq, sq); g.FillRectangle(br, m + sq + g2, m + sq + g2, sq, sq); }
        }
        si.SetIcon(bmp); si.SetClick(() => SendKeys.Send("^{ESC}")); si.SetTooltip("Start"); newIcons.Insert(1, si);
    }

    static void UpdateSpecialIcons()
    {
        if (icons.Count < 2) return;
        // Regenerate Start icon for theme
        var si = icons[1];
        var bmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); int sz = bmp.Width, g2 = Math.Max(2, sz / 20), sq = (sz - g2 * 3) / 2, m = (sz - sq * 2 - g2) / 2;
            Color c = Theme.IsLight ? Color.Black : Color.White;
            using (var br = new SolidBrush(c)) { g.FillRectangle(br, m, m, sq, sq); g.FillRectangle(br, m + sq + g2, m, sq, sq); g.FillRectangle(br, m, m + sq + g2, sq, sq); g.FillRectangle(br, m + sq + g2, m + sq + g2, sq, sq); }
        }
        si.SetIcon(bmp);
        // Regenerate minimize icon too
        var mi = icons[0];
        var mBmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        using (var g = Graphics.FromImage(mBmp)) { g.Clear(Color.Transparent); int sz2 = mBmp.Width, m2 = sz2 / 4;
            Color c2 = Theme.IsLight ? Color.Black : Color.White;
            using (var p = new Pen(c2, Math.Max(2, sz2 / 16))) { g.DrawLine(p, m2, sz2 - m2, sz2 / 2, sz2 / 2); g.DrawLine(p, sz2 / 2, sz2 / 2, sz2 - m2, sz2 - m2); }
        }
        mi.SetIcon(mBmp);
    }

    // ===== Layout =====
    static int lastStartX = -1, lastIconY = -1, lastCnt = -1;
    static void Layout()
    {
        if (icons.Count < 2) return;
        // Use DPI-based calculation, NOT Form.Height (which is 300 before Show!)
        int fw = (int)(44 * DockIcon.DpiX / 96f);
        for (int i = 0; i < icons.Count; i++) { icons[i].Form.Size = new Size(fw, fw); }
        int cnt = icons.Count;
        int totalW = cnt * fw + (cnt - 1) * gap;
        int sw = Screen.PrimaryScreen.WorkingArea.Width, sh = Screen.PrimaryScreen.WorkingArea.Height;
        int startX = (sw - totalW) / 2;
        int iconY;
        if(DebugMode.On) iconY = 40; else iconY = sh - fw - 20;

        int lineH = 10;

        if (startX != lastStartX || iconY != lastIconY || cnt != lastCnt)
        {
            lastStartX = startX; lastIconY = iconY; lastCnt = cnt;
            lineForm.Width = totalW; lineForm.Height = lineH;
            lineForm.Left = startX; lineForm.Top = iconY + fw/2 - lineH/2;
            for (int i = 0; i < cnt; i++)
                icons[i].SetBasePos(startX + i * (fw + gap), iconY);
            lineForm.Invalidate();
        }
    }

    // ===== Toggle dock ⇄ taskbar =====
    static void ToggleDock()
    {
        dockVisible = !dockVisible;
        IntPtr tb = FindWindow("Shell_TrayWnd", null);
        if (dockVisible) { if (!DebugMode.On && tb != IntPtr.Zero) ShowWindow(tb, 0); lineForm.WindowState = FormWindowState.Normal; lineForm.ShowInTaskbar = true; foreach (var di in icons) di.Show(); RefreshAllIcons(); }
        else { if (!DebugMode.On && tb != IntPtr.Zero) ShowWindow(tb, 5); lineForm.WindowState = FormWindowState.Minimized; lineForm.ShowInTaskbar = false; foreach (var di in icons) di.Hide(); }
    }

    static void DumpIcons(){
        try{
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Icons @ "+DateTime.Now.ToString("HH:mm:ss")+" ===");
            for(int i=0;i<icons.Count;i++){
                var di=icons[i];
                string title="";
                if(di.HWnd!=IntPtr.Zero)try{title=DockBar.GetWinTitle(di.HWnd);}catch{}
                sb.AppendLine(string.Format("  [{0}] pid={1} hwnd={2} pinned={3} pinPath={4} title={5}",i,di.Pid,di.HWnd!=IntPtr.Zero,di.Pinned,di.PinPath??"-",title.Length>20?title.Substring(0,20):title));
            }
            System.IO.File.AppendAllText(@"C:\temp\_dock_dump.txt",sb.ToString());
        }catch{}
    }

    [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint fl);

    static void DrawGlow(Graphics g, float x0, float y0, float x1, float y1, Color[] colors, int[] widths){
        for(int i = 0; i < colors.Length; i++)
            using(var p = new Pen(colors[i], widths[i]))
                g.DrawLine(p, x0, y0, x1, y1);
    }

    // ===== Paint glow line =====
    static void OnPaintLine(object s, PaintEventArgs e)
    {
        if (icons.Count < 2) return;
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        int midY = lineForm.Height / 2;

        // Use actual icon centers
        float x0 = icons[0].BaseX + icons[0].Form.Width / 2f;
        float x1 = icons[icons.Count-1].BaseX + icons[icons.Count-1].Form.Width / 2f;

        var c = Theme.IsLight ? Color.FromArgb(240, 220, 140) : Color.FromArgb(100, 140, 220);
        using (var p = new Pen(c, 2f))
            g.DrawLine(p, x0, midY, x1, midY);

        // Dump icon centers vs line endpoints
        if(DebugMode.On){
            var sb=new System.Text.StringBuilder();
            sb.AppendLine("LINE: x0="+x0+" x1="+x1+" midY="+midY);
            for(int i=0;i<icons.Count;i++)
                sb.AppendLine(" ICON["+i+"] BaseX="+icons[i].BaseX+" W="+icons[i].Form.Width+" Center="+(icons[i].BaseX+icons[i].Form.Width/2f));
            System.IO.File.WriteAllText(@"C:\temp\_dock_line.txt",sb.ToString());
        }
    }

}
