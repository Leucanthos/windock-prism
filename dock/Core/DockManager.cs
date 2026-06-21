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
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, System.Text.StringBuilder c, int n);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int idx, int val);
    [DllImport("user32.dll")] static extern IntPtr GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string t);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);

    const uint GW_HWNDNEXT = 2;
    const int GWL_EXSTYLE = -20, GWL_STYLE = -16, WS_EX_TOOLWINDOW = 0x80, WS_CHILD = 0x40000000, WS_CAPTION = 0x00C00000;

    struct POINT { public int X, Y; }
    struct RECT { public int Left, Top, Right, Bottom; public int Width { get { return Right - Left; } } public int Height { get { return Bottom - Top; } } }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr hWnd, bool f);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // WH_GETMESSAGE hook for intercepting WeChat WM_CLOSE
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn,
        IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);

    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    struct INPUT { public uint type; public MOUSEINPUT mi; }
    const uint INPUT_MOUSE = 0;
    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;

    static void SendMouseClick(int screenX, int screenY)
    {
        int sw = GetSystemMetrics(SM_CXSCREEN);
        int sh = GetSystemMetrics(SM_CYSCREEN);
        int absX = (int)((long)screenX * 65536 / sw);
        int absY = (int)((long)screenY * 65536 / sh);
        var inputs = new INPUT[2];
        // Atomic: move to position + press left button in one event
        inputs[0].type = INPUT_MOUSE;
        inputs[0].mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_LEFTDOWN;
        inputs[0].mi.dx = absX; inputs[0].mi.dy = absY;
        // Release
        inputs[1].type = INPUT_MOUSE;
        inputs[1].mi.dwFlags = MOUSEEVENTF_LEFTUP;
        SendInput(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT)));
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    static extern uint RegisterWindowMessageW(string msg);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr procId);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int pt_x; public int pt_y; }

    const int WH_GETMESSAGE = 3;
    const uint WM_SYSCOMMAND = 0x0112;
    const int SC_CLOSE = 0xF060;
    const int SC_MINIMIZE = 0xF020;
    static IntPtr wxMsgHookHandle;
    static HookProc wxMsgHookProc;
    static int _spyStartTick; // set in Create() for 90s spy window

    // ===== State =====
    static Form lineForm, edgeGuard;
    static List<DockIcon> icons = new List<DockIcon>();
    static System.Windows.Forms.Timer themePoll, watchdogTimer;
    static NotifyIcon trayIcon;
    static bool dockVisible = true;
    static bool dockOnTop = true;
    static int foregroundPid;
    static int lastPinCount = 0;
    static int ownPid = System.Diagnostics.Process.GetCurrentProcess().Id;

    class WindowInfo { public IntPtr HWnd; public int Pid; public string Title; }

    public static Form Create()
    {
        PinStore.Load();
        DockIcon.SetFormIcon(AppDomain.CurrentDomain.BaseDirectory + @"assets\Windock-icon.ico");

        // Invisible anchor form — only serves as message pump host
        Icon appIcon = null;
        try { appIcon = new Icon(AppDomain.CurrentDomain.BaseDirectory + @"assets\Windock.ico"); } catch { }
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
            if (wxMsgHookHandle != IntPtr.Zero) { UnhookWindowsHookEx(wxMsgHookHandle); wxMsgHookHandle = IntPtr.Zero; }
            RestoreTaskbar();
            foreach (var ic in icons) ic.Dispose();
            icons.Clear();
            if (edgeGuard != null) { edgeGuard.Close(); edgeGuard.Dispose(); edgeGuard = null; }
        };

        lineForm.Shown += (s, e) =>
        {
            HideTaskbar();
            Layout(); // position empty dock immediately
            // Edge guard: transparent 4px strip at bottom blocks mouse from reaching
            // the screen edge, preventing Explorer from showing the hidden taskbar
            var scr = Screen.PrimaryScreen.Bounds;
            edgeGuard = new Form { Size = new Size(scr.Width, 4), Location = new Point(0, scr.Height - 4),
                FormBorderStyle = FormBorderStyle.None, TopMost = true, ShowInTaskbar = false,
                BackColor = Color.Black, TransparencyKey = Color.Black };
            edgeGuard.Show();
            lineForm.BeginInvoke((Action)delegate {
                trayIcon = new NotifyIcon { Text = "WinDock", Icon = appIcon ?? SystemIcons.Shield, Visible = true };
                trayIcon.Click += (s2, e2) => Toggle();
            });
            // V3: Subscribe to hover events for elastic lens
            DockIcon.HoverChanged += SpreadLensEffect;
            // Start theme + foreground poll
            if (themePoll != null) themePoll.Start();
            // Defer heavy FullRefresh so dock shell appears instantly
            var deferTimer = new System.Windows.Forms.Timer { Interval = 200 };
            deferTimer.Tick += (s2, e2) => { deferTimer.Stop(); deferTimer.Dispose(); FullRefresh(); Layout(); };
            deferTimer.Start();
        };

        // Badge refresh — update counts periodically without full icon recreation
        var badgeTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        badgeTimer.Tick += (s2, e2) =>
        {
            foreach (var di in icons)
            {
                if (di.Pinned && !string.IsNullOrEmpty(di.PinPath))
                {
                    string pinName = System.IO.Path.GetFileNameWithoutExtension(di.PinPath);
                    if (pinName.Equals("Weixin", StringComparison.OrdinalIgnoreCase) ||
                        pinName.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                    {
                        // Use GetProcessesByName — PID may have changed or be 0 (not yet detected)
                        bool alive = false;
                        try { var procs = Process.GetProcessesByName(pinName);
                            if (procs.Length > 0) {
                                alive = true;
                                // Update PID if stale
                                if (di.Pid != procs[0].Id) { di.Pid = procs[0].Id; di.HWnd = procs[0].MainWindowHandle; }
                            }
                        } catch { }
                        di.SetBadge(alive ? 1 : 0);
                    }
                    else
                    {
                        int cnt = CountWindowsForPin(di.PinPath);
                        if (cnt == 0)
                        {
                            bool alive = false;
                            try { foreach (var p in Process.GetProcessesByName(pinName)) { alive = true; if (di.Pid == 0) di.Pid = p.Id; break; } } catch { }
                            cnt = alive ? 1 : 0;
                        }
                        di.SetBadge(cnt);
                    }
                }
                else if (di.Pid > 0 && !di.Pinned)
                {
                    di.SetBadge(CountWindows(di.Pid));
                }
            }
        };
        badgeTimer.Start();

        // Start spy window: 90s from NOW
        _spyStartTick = Environment.TickCount;

        // WeChat X-close interceptor: WH_GETMESSAGE hook on WeChat's UI thread.
        wxMsgHookProc = delegate(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                try
                {
                    MSG msg = (MSG)Marshal.PtrToStructure(lParam, typeof(MSG));
                    // Spy: log ALL messages during first 90s after dock start
                    if (Environment.TickCount - _spyStartTick < 90000)
                    {
                        // Log all non-trivial messages
                        if (msg.message != 0x0118 && msg.message != 0x000F && // skip WM_PAINT, WM_TIMER
                            msg.message != 0x0200) // skip WM_MOUSEMOVE (too many)
                        {
                            string n;
                            switch (msg.message) {
                                case 0x0006: n = "ACTIVATE"; break;
                                case 0x0007: n = "SETFOCUS"; break;
                                case 0x001C: n = "ACTIVATEAPP"; break;
                                case 0x0046: n = "WINPOSCHG"; break;
                                case 0x0047: n = "WINPOSCHD"; break;
                                case 0x0086: n = "NCACTIVATE"; break;
                                case 0x0112: n = "SYSCOMMAND"; break;
                                case 0x0018: n = "SHOWWINDOW"; break;
                                case 0x0005: n = "SIZE"; break;
                                case 0x0281: n = "IME_SETCTX"; break;
                                case 0x0024: n = "MINMAXINFO"; break;
                                default: n = "0x" + msg.message.ToString("X4"); break;
                            }
                            EventLog.Info("SPY " + n + " h=0x" + msg.hwnd.ToInt64().ToString("X") +
                                " w=0x" + msg.wParam.ToInt64().ToString("X") +
                                " l=0x" + msg.lParam.ToInt64().ToString("X"));
                        }
                    }
                    if (msg.message == WM_SYSCOMMAND && (int)msg.wParam == SC_CLOSE)
                    {
                        var sb = new System.Text.StringBuilder(256);
                        GetClassName(msg.hwnd, sb, 256);
                        if (sb.ToString() == "Qt51514QWindowIcon")
                        {
                            var tb = new System.Text.StringBuilder(256);
                            GetWindowText(msg.hwnd, tb, 256);
                            if (tb.ToString() == "微信")
                            {
                                Marshal.WriteInt32(lParam, 16, SC_MINIMIZE);
                                EventLog.Info("WeChat: WM_SYSCOMMAND SC_CLOSE → SC_MINIMIZE");
                            }
                        }
                    }
                }
                catch { }
            }
            return CallNextHookEx(wxMsgHookHandle, code, wParam, lParam);
        };

        // Find WeChat window and install hook on its thread
        try
        {
            IntPtr wxHwnd = FindWindow("Qt51514QWindowIcon", "微信");
            if (wxHwnd != IntPtr.Zero)
            {
                uint wxThreadId = GetWindowThreadProcessId(wxHwnd, IntPtr.Zero);
                if (wxThreadId != 0)
                {
                    wxMsgHookHandle = SetWindowsHookEx(WH_GETMESSAGE, wxMsgHookProc,
                        IntPtr.Zero, wxThreadId);
                    EventLog.Info("WeChat WH_GETMESSAGE hook installed, thread=" + wxThreadId);
                }
            }
        }
        catch { }

        // Theme + foreground polling (started in Shown after initial refresh)
        themePoll = new System.Windows.Forms.Timer { Interval = 400 };
        themePoll.Tick += (s, e) =>
        {
            // Taskbar guard: keep hidden (Explorer may show it on mouse edge)
            if (dockVisible) { var tb = FindWindow("Shell_TrayWnd", null); if (tb != IntPtr.Zero && IsWindowVisible(tb)) ShowWindow(tb, 0); }

            // Pin watch: reload PinStore periodically, refresh if pins changed
            int prevCount = 0; foreach (var _ in PinStore.PinnedPaths) prevCount++;
            if (prevCount != lastPinCount) { lastPinCount = prevCount; PinStore.Load(); int newCount = 0; foreach (var _ in PinStore.PinnedPaths) newCount++; if (newCount != prevCount) { FullRefresh(); Layout(); } }

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
                    string fgExePath = DockBar.GetProcessPathSafe(fgPid) ?? "";

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
                                && pn != "steamwebhelper" && pn != "steamservice"
                                && pn != "searchhost" && pn != "windock"
                                && pn != "lsf" && pn != "smartenginetray"
                                && pn != "lockapp")
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
                                    // PID match + valid exe + has caption + not child + reasonable size
                                    // (Don't require fg == MainWindowHandle — Taskmgr reports a different one)
                                    int fgPidCheck; GetWindowThreadProcessId(fg, out fgPidCheck);
                                    if (fgPidCheck == fgPid && !string.IsNullOrEmpty(fgExePath))
                                    {
                                        int fgStyle = GetWindowLong(fg, GWL_STYLE);
                                        RECT fgRect; GetWindowRect(fg, out fgRect);
                                        if ((fgStyle & WS_CHILD) == 0
                                            && fgRect.Width >= 150 && fgRect.Height >= 80)
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
                            }
                        } catch { }
                    }
                }
            }

            // Auto-layer (MUST run last — after FgAdd/StaleRemove may create new TopMost=true icons)
            if (dockVisible) {
                bool anyMax = false; string maxTitle = "";
                IntPtr hw = IntPtr.Zero;
                while ((hw = FindNextWindow(hw)) != IntPtr.Zero) {
                    int pid; GetWindowThreadProcessId(hw, out pid);
                    if (pid == 0 || pid == ownPid) continue;
                    if (IsWindowVisible(hw) && IsZoomed(hw)) {
                        anyMax = true;
                        var sb = new System.Text.StringBuilder(256); GetWindowText(hw, sb, 256); maxTitle = sb.ToString();
                        break;
                    }
                }
                bool wantVisible = !anyMax;
                int changed = 0;
                double targetOpacity = wantVisible ? 0.82 : 0.0;
                foreach (var di in icons) {
                    try {
                        if (di.Form == null || di.Form.IsDisposed) continue;
                        if (System.Math.Abs(di.Form.Opacity - targetOpacity) > 0.01) {
                            di.Form.Opacity = targetOpacity; changed++;
                        }
                    } catch { }
                }
                if (changed > 0) EventLog.Info("Layer: anyMax=" + anyMax + " changed=" + changed + " opacity=" + targetOpacity + " maxWin=" + (maxTitle.Length > 30 ? maxTitle.Substring(0,30) : maxTitle));
                dockOnTop = wantVisible;
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
                    || pn == "steamwebhelper" || pn == "steamservice"
                    || pn == "lsf" || pn == "smartenginetray"
                    || pn == "lockapp") continue;
            }
            catch { continue; }
            if (!winInfo.ContainsKey(pid) || t.Length > winInfo[pid].Title.Length)
                winInfo[pid] = new WindowInfo { HWnd = hWnd, Pid = pid, Title = t };
        }

        // Match pinned paths — collect ALL PIDs matching each pin.
        // Multi-process apps (VS Code, Chrome) have many PIDs; we pin them all
        // so they don't leak into "running apps" as duplicate icons.
        var pinnedPids = new HashSet<int>();
        var pinnedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in PinStore.PinnedPaths)
        {
            string pinName = System.IO.Path.GetFileNameWithoutExtension(path);
            pinnedNames.Add(pinName);
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
                    { pinnedPids.Add(kv.Key); matched = true; /* no break — collect all PIDs */ }
                }
                catch { }
            }
            if (!matched)
            {
                // Fallback: check if process is running in background (no visible windows).
                // Common for apps that minimize to system tray (WeChat, Steam, etc.)
                bool bgMatched = false;
                try
                {
                    var procs = Process.GetProcessesByName(pinName);
                    // For multi-process apps (VS Code, Chrome), pick the process
                    // with the most visible windows — not just the first one.
                    int bestWndCount = -1;
                    Process bestProc = null;
                    IntPtr bestHwnd = IntPtr.Zero;
                    foreach (var proc in procs)
                    {
                        try
                        {
                            string pf = "";
                            try { pf = proc.MainModule.FileName; } catch { }
                            if (!string.IsNullOrEmpty(pf) &&
                                !pf.Equals(path, StringComparison.OrdinalIgnoreCase))
                                continue;

                            IntPtr bgHwnd = proc.MainWindowHandle;
                            if (bgHwnd == IntPtr.Zero)
                            {
                                // Find best window: skip system/tray-helper windows,
                                // prefer windows with meaningful titles and reasonable size.
                                IntPtr hw = IntPtr.Zero; IntPtr bestFallback = IntPtr.Zero;
                                int bestScore = -1;
                                while ((hw = FindNextWindow(hw)) != IntPtr.Zero)
                                {
                                    int pp; GetWindowThreadProcessId(hw, out pp);
                                    if (pp != proc.Id) continue;
                                    var tsb = new System.Text.StringBuilder(256);
                                    GetWindowText(hw, tsb, 256);
                                    string tt = tsb.ToString();
                                    // Skip known system helper windows
                                    if (tt == "MSCTFIME UI" || tt == "Default IME" ||
                                        tt == "OleMainThreadWndName" ||
                                        tt.StartsWith("CiceroUIWndFrame") ||
                                        tt.Contains("TrayIcon") || tt.Contains("IME"))
                                        continue;
                                    // Score: prefer larger area + longer title
                                    RECT tr; GetWindowRect(hw, out tr);
                                    int area = tr.Width * tr.Height;
                                    // Penalize extreme aspect ratios (tray helpers are very wide)
                                    int arPenalty = 0;
                                    if (tr.Height > 0 && tr.Width > 0)
                                    {
                                        double ar = (double)tr.Width / tr.Height;
                                        if (ar > 5 || ar < 0.2) arPenalty = -1000;
                                    }
                                    int score = area + tt.Length * 100 + arPenalty;
                                    if (score > bestScore) { bestScore = score; bestFallback = hw; }
                                }
                                if (bestFallback != IntPtr.Zero) bgHwnd = bestFallback;
                            }

                            int wc = CountWindows(proc.Id);
                            if (wc > bestWndCount)
                            {
                                bestWndCount = wc;
                                bestProc = proc;
                                bestHwnd = bgHwnd;
                            }
                        }
                        catch { }
                    }

                    if (bestProc != null)
                    {
                        pinnedPids.Add(bestProc.Id);
                        bgMatched = true;
                        winInfo[bestProc.Id] = new WindowInfo
                        {
                            HWnd = bestHwnd,
                            Pid = bestProc.Id,
                            Title = bestProc.MainWindowTitle
                        };
                        EventLog.Info("BackgroundProcess matched: " + pinName +
                            " pid=" + bestProc.Id + " hwnd=0x" + bestHwnd.ToInt64().ToString("X") +
                            " windows=" + bestWndCount);
                    }
                }
                catch { }

                if (!bgMatched)
                {
                    var di = FindOrCreatePinnedInactive(path, icons, newIcons);
                    if (di != null && !newIcons.Contains(di)) newIcons.Add(di);
                }
            }
        }

        // Create icons for running apps (skip PIDs belonging to pinned apps —
        // multi-process apps like VS Code have many PIDs, but only one icon)
        foreach (var kv in winInfo)
        {
            if (pinnedPids.Contains(kv.Key)) continue;
            // Also skip if process name matches a pinned app (catches remaining
            // PIDs for multi-process pinned apps like VS Code / Chrome)
            bool isPinnedProcess = false;
            try
            {
                var p = Process.GetProcessById(kv.Key);
                if (pinnedNames.Contains(p.ProcessName)) isPinnedProcess = true;
            }
            catch { }
            if (isPinnedProcess) continue;
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
                        if (di != null)
                        {
                            // For background processes, proc.MainWindowHandle may be IntPtr.Zero.
                            // Use the HWnd from winInfo (found during window enumeration or fallback).
                            if (di.HWnd == IntPtr.Zero && kv.Value.HWnd != IntPtr.Zero)
                                di.HWnd = kv.Value.HWnd;
                            if (!newIcons.Contains(di)) newIcons.Add(di);
                        }
                        break;
                    }
                }
                catch { }
            }
        }

        // Update badges, tooltips, menus
        foreach (var di in newIcons)
        {
            if (di.Pid > 0)
            {
                int cnt = 0;
                if (di.Pinned && !string.IsNullOrEmpty(di.PinPath))
                {
                    string pinName = System.IO.Path.GetFileNameWithoutExtension(di.PinPath);
                    // WeChat, Steam: system-tray apps, always 1 if alive
                    if (pinName.Equals("Weixin", StringComparison.OrdinalIgnoreCase) ||
                        pinName.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                    {
                        bool alive = false;
                        try { using (var p = Process.GetProcessById(di.Pid)) { alive = !p.HasExited; } } catch { }
                        cnt = alive ? 1 : 0;
                    }
                    else
                    {
                        // Count windows across ALL processes for this pinned app
                        cnt = CountWindowsForPin(di.PinPath);
                        if (cnt == 0)
                        {
                            // Check if any process for this pin is still alive
                            bool alive = false;
                            try {
                                foreach (var p in Process.GetProcessesByName(pinName))
                                { try { if (!p.HasExited) { alive = true; break; } } catch { } }
                            } catch { }
                            cnt = alive ? 1 : 0;
                        }
                    }
                }
                else if (!di.Pinned)
                {
                    cnt = CountWindows(di.Pid);
                }
                di.SetBadge(cnt);
            }
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
            string hwndStr = d.HWnd != IntPtr.Zero ? "0x" + d.HWnd.ToInt64().ToString("X") : "0";
            snapLines[si] = string.Format("[{0}] pid={1} hwnd={2} pinned={3} badge={4} pos=({5},{6}) pin={7}",
                si, d.Pid, hwndStr, d.Pinned, d.BadgeCount,
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
            FullRefresh();
            Layout();
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
    [DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
    const uint ABM_REMOVE = 1, ABM_NEW = 0;
    const uint ABE_BOTTOM = 3;
    struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

    static void HideTaskbar()
    {
        if (DebugMode.On) return;
        var tb = FindWindow("Shell_TrayWnd", null);
        if (tb != IntPtr.Zero)
        {
            // Unregister taskbar as appbar → releases reserved work area
            var abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = tb;
            SHAppBarMessage(ABM_REMOVE, ref abd);
            // Move off-screen so Explorer can't show it even if it tries
            ShowWindow(tb, 0);
            int h = Screen.PrimaryScreen.Bounds.Height;
            SetWindowPos(tb, IntPtr.Zero, 0, h + 100, 0, 0, 0x0001 | 0x0004 | 0x0010);
        }
    }

    static void RestoreTaskbar()
    {
        var tb = FindWindow("Shell_TrayWnd", null);
        if (tb != IntPtr.Zero)
        {
            // Re-register taskbar as bottom-edge appbar
            var abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = tb;
            abd.uEdge = ABE_BOTTOM;
            SHAppBarMessage(ABM_NEW, ref abd);
            ShowWindow(tb, 5);
        }
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
            // WeChat (Qt5) minimizes its window to 158x26 when going to tray.
            // The stored HWnd is unreliable (may point to tray helper).
            // Use FindWindow by Qt class name to get the real window.
            string pn = "";
            try { pn = System.IO.Path.GetFileNameWithoutExtension(path); } catch { }
            if (pn.Equals("Weixin", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr wxHwnd = FindWindow("Qt51514QWindowIcon", "微信");
                if (wxHwnd == IntPtr.Zero) wxHwnd = FindWindow("WeChatMainWndForPC", null);
                if (wxHwnd != IntPtr.Zero && IsWindow(wxHwnd) && IsIconic(wxHwnd))
                {
                    // Minimized — live window, just restore
                    int wxPid; GetWindowThreadProcessId(wxHwnd, out wxPid);
                    ShowWindow(wxHwnd, 9);
                    AllowSetForegroundWindow(wxPid);
                    SwitchToThisWindow(wxHwnd, true);
                    EventLog.Info("WeChat click: SW_RESTORE (minimized)");
                }
                else
                {
                    // Expert: tray callbacks use WM_USER (0x0400), not RegisterWindowMessage.
                    // Send NIN_SELECT(0) and NIN_KEYSELECT(1) to tray window first.
                    IntPtr trayWnd = FindWindow("Qt51514WxTrayIconMessageWindowClass", null);
                    if (trayWnd != IntPtr.Zero)
                    {
                        for (int iconId = 0; iconId <= 101; iconId++)
                        {
                            PostMessage(trayWnd, 0x0400, (IntPtr)iconId, IntPtr.Zero);
                            PostMessage(trayWnd, 0x0400, (IntPtr)iconId, (IntPtr)1);
                        }
                        EventLog.Info("WeChat: WM_USER NIN_SELECT sent");
                    }
                    // Show snapshot, use SendInput (atomic hardware click) on minimize btn.
                    // SendInput is immune to user mouse interference unlike Cursor.Position.
                    RECT r; GetWindowRect(wxHwnd, out r);
                    ShowWindow(wxHwnd, 1); // SW_SHOWNORMAL
                    var save = Cursor.Position;
                    SendMouseClick(r.Right - 188, r.Top + 18); // atomic move+click
                    Cursor.Position = save;                    // restore instantly
                    System.Threading.Thread.Sleep(1);           // let Qt5 process minimize (1ms verified minimum)
                    // Now SW_RESTORE the live window
                    ShowWindow(wxHwnd, 9); // SW_RESTORE
                    int pid2; GetWindowThreadProcessId(wxHwnd, out pid2);
                    AllowSetForegroundWindow(pid2);
                    SwitchToThisWindow(wxHwnd, true);
                    EventLog.Info("WeChat click: snapshot→minimize click→restore (automated 2-step)");
                }
            }
            else if (di.HWnd != IntPtr.Zero && IsWindow(di.HWnd))
            {
                DockBar.FocusWindow(di.HWnd);
            }
            else
            {
                Process.Start(path);
                var t = new System.Windows.Forms.Timer { Interval = 300 };
                t.Tick += (s2, e2) => { t.Stop(); t.Dispose(); ApplyForegroundToIcon(di); };
                t.Start();
            }
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
                    if (p.MainModule.FileName.Equals(di.PinPath, StringComparison.OrdinalIgnoreCase))
                    {
                        di.Pid = p.Id;
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            di.HWnd = p.MainWindowHandle;
                            di.SetTooltip(p.MainWindowTitle);
                        }
                        // Apply badge: Steam/WeChat always 1, others by visible count
                        string pn = p.ProcessName;
                        if (pn.Equals("Weixin", StringComparison.OrdinalIgnoreCase) ||
                            pn.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                            di.SetBadge(1);
                        else
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
        var exePath = DockBar.GetProcessPathSafe(pid);
        if (exePath == null) return null;
        try {
            using (var ico = Icon.ExtractAssociatedIcon(exePath)) {
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
            bool hasVisibleWindow = di.HWnd != IntPtr.Zero && IsWindow(di.HWnd) && IsWindowVisible(di.HWnd);
            bool isRunning = di.Pid > 0;
            bool pinned = di.Pinned && !string.IsNullOrEmpty(di.PinPath) && PinStore.IsPinned(di.PinPath);
            var _hwnd = di.HWnd; var _pinPath = di.PinPath; var _pid = di.Pid;
            IconMenu.Show(pos, hasVisibleWindow, isRunning, pinned,
                onCloseWindow: () => {
                    if (_hwnd != IntPtr.Zero) DockBar.CloseWindow(_hwnd);
                    ScheduleRefresh();
                },
                onEndTask: () => {
                    if (_pid > 0) DockBar.KillProcess(_pid);
                    ScheduleRefresh();
                },
                onTogglePin: () => {
                    if (string.IsNullOrEmpty(_pinPath)) { string exe = DockBar.GetExePath(_pid); if (!string.IsNullOrEmpty(exe)) PinStore.Pin(exe); }
                    else if (PinStore.IsPinned(_pinPath)) PinStore.Unpin(_pinPath);
                    else PinStore.Pin(_pinPath);
                    ScheduleRefresh();
                },
                onClosed: di.OnMenuClosed,
                onNewWindow: isRunning ? (Action)(() => {
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

    /// <summary>Public wrapper — used by DockBar for pinned badge counts.</summary>
    public static int CountWindowsFor(int pid) { return CountWindows(pid); }

    /// <summary>Count visible windows across ALL processes matching a pinned path.
    /// Multi-process apps (Edge, Chrome) spread windows across many PIDs.</summary>
    public static int CountWindowsForPin(string pinPath)
    {
        if (string.IsNullOrEmpty(pinPath)) return 0;
        string pinName = System.IO.Path.GetFileNameWithoutExtension(pinPath);
        int total = 0;
        // Count visible windows across ALL PIDs that match this pin.
        // For each PID, first verify the process matches, then count its windows.
        var countedPids = new HashSet<int>();
        IntPtr hw = IntPtr.Zero;
        while ((hw = FindNextWindow(hw)) != IntPtr.Zero)
        {
            if (!IsWindowVisible(hw)) continue;
            int ex = GetWindowLong(hw, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0) continue;
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hw, sb, 256);
            if (sb.Length == 0) continue;

            int pp; GetWindowThreadProcessId(hw, out pp);
            if (countedPids.Contains(pp)) continue;

            try
            {
                var p = Process.GetProcessById(pp);
                if (!p.ProcessName.Equals(pinName, StringComparison.OrdinalIgnoreCase)) continue;
                string pf = "";
                try { pf = p.MainModule.FileName; } catch { }
                if (!string.IsNullOrEmpty(pf) && !pf.Equals(pinPath, StringComparison.OrdinalIgnoreCase)) continue;

                countedPids.Add(pp);
                int wc = CountWindows(pp);
                total += wc;
                EventLog.Info("CountWindowsForPin: " + pinName + " pid=" + pp +
                    " windows=" + wc + " total=" + total);
            }
            catch { }
        }
        EventLog.Info("CountWindowsForPin: " + pinName + " FINAL total=" + total);
        return total;
    }

    /// <summary>Find the best visible-capable window for a pinned app.
    /// Skips system/tray helper windows, prefers app-like windows by title and size.</summary>
    static IntPtr FindBestWindowForPin(string pinPath)
    {
        if (string.IsNullOrEmpty(pinPath)) return IntPtr.Zero;
        string pinName = System.IO.Path.GetFileNameWithoutExtension(pinPath);
        IntPtr best = IntPtr.Zero; int bestScore = -1;
        IntPtr hw = IntPtr.Zero;
        while ((hw = FindNextWindow(hw)) != IntPtr.Zero)
        {
            int pp; GetWindowThreadProcessId(hw, out pp);
            var tsb = new System.Text.StringBuilder(256);
            GetWindowText(hw, tsb, 256);
            string tt = tsb.ToString();
            // Skip system/tray helper titles
            if (tt == "MSCTFIME UI" || tt == "Default IME" || tt == "OleMainThreadWndName" ||
                tt.Contains("TrayIcon") || tt.Contains("IME") || tt.Length == 0)
                continue;

            try
            {
                var p = Process.GetProcessById(pp);
                if (!p.ProcessName.Equals(pinName, StringComparison.OrdinalIgnoreCase)) continue;
            }
            catch { continue; }

            RECT tr; GetWindowRect(hw, out tr);
            int area = tr.Width * tr.Height;
            // Score: prefer medium-large windows (app windows are 400x300+)
            // over tiny stubs (37x14) or oversized helpers (1234x716 tray window)
            int score = area + tt.Length * 200;
            if (tr.Width > 300 && tr.Height > 200 && tr.Width < 1200 && tr.Height < 900)
                score += 10000; // bonus for likely app window dimensions
            if (score > bestScore) { bestScore = score; best = hw; }
        }
        return best;
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
