using System;
using System.Windows.Forms;
using Microsoft.Win32;

// ============================================================
// Desktop Widgets — entry point
// ============================================================

static class Program
{
    public static event Action<bool> ThemeChanged;
    public static void NotifyTheme(bool light){ if(ThemeChanged!=null)ThemeChanged(light); }
    public static string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    public static int SW;
    static Form[] _forms;   // keep for repin timer

    static void RegisterStartup(){
        string exe = System.IO.Path.Combine(BaseDir, "WinPrism.exe");
        string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        string entryName = "Prism";

        // Clean old stale entries (old path, old name)
        try{
            using(var hkcu=Registry.CurrentUser.OpenSubKey(keyPath,true)){
                if(hkcu.GetValue("DesktopWidgets")!=null) hkcu.DeleteValue("DesktopWidgets");
                string old = hkcu.GetValue("Prism") as string;
                if(old != null && old != exe) hkcu.DeleteValue("Prism");
            }
        }catch{}

        // Write to HKCU (no admin needed, works for current user logon)
        try{
            using(var key = Registry.CurrentUser.OpenSubKey(keyPath, true)){
                string current = key.GetValue(entryName) as string;
                if(current != exe) key.SetValue(entryName, exe);
            }
        }catch{}

        // Also try HKLM — boots earlier but requires admin; best-effort only
        try{
            using(var key = Registry.LocalMachine.OpenSubKey(keyPath, true)){
                string current = key.GetValue(entryName) as string;
                if(current != exe) key.SetValue(entryName, exe);
            }
        }catch{ /* no admin — skip, HKCU entry already set */ }
    }

    [STAThread] static void Main(string[] args)
    {
        DebugMode.Init(args);
        VersionInfo.Init("Prism");
        EventLog.Init("Prism");
        var mutexName = @"Global\Prism_" + VersionInfo.Number + (DebugMode.On ? "_debug" : "");
        // Retry: old instance may still be shutting down during font-toggle restart
        for(int retry=0; retry<5; retry++){
            if(W.Lock(mutexName)) break;
            if(retry==4){MessageBox.Show("Already Running",VersionInfo.FullName+(DebugMode.On?" (debug)":""),MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            System.Threading.Thread.Sleep(200);
        }

        // Auto-register for startup
        if(!DebugMode.On) RegisterStartup();

        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        SW = Screen.PrimaryScreen.WorkingArea.Width;

        var cfg = Settings.Load();

        if(DebugMode.On){
            // Debug: dock to LEFT side so it doesn't overlap release version
            SW = 260;                       // right-column widgets at SW-240 = 20
            cfg.TopX = SW + 20;             // left column at 280 (20px gap)
        }else{
            // Auto-position TopX: flush against right column (System at SW-240) with 20px gap
            if(cfg.TopX < 200 || cfg.TopX > SW - 300) cfg.TopX = SW - 520;
        }

        Theme.SmallFontMode = cfg.SmallFont;
        Theme.Init();

        var topBar = TopBarWidget.Create(cfg);
        var audio  = AudioWidget.Create(cfg, topBar);
        var rb     = RecycleBinWidget.Create(cfg, audio);
        var sys    = SystemWidget.Create(cfg, topBar); // align top to TopBar center
        var disk   = DiskWidget.Create(cfg, sys);
        var net    = NetworkWidget.Create(cfg, disk);
        var bat    = BatteryWidget.Create(cfg, net);

        _forms = new Form[]{topBar,audio,rb,sys,disk,net,bat};
        foreach(var f in _forms) if(f!=null) f.Show();
        // Pin to desktop AFTER all forms are shown and sized.
        // EnsureWorkerW toggles desktop icons ON if needed (matches original behavior).
        System.Threading.Thread.Sleep(200);
        W.EnsureWorkerW();
        foreach(var f in _forms) if(f!=null) W.PinToDesktop(f);

        // ── Repin watchdog ────────────────────────────────────
        // Explorer may recycle the WorkerW desktop layer when windows are
        // maximised / restored / snapped — the forms become invisible.
        // Fast poll (1s) checks ALL forms every tick, plus event-driven
        // immediate repin on display changes.
        EventLog.Info("Prism startup — " + _forms.Length + " forms pinned");
        var repinTimer = new Timer { Interval = 1000 };
        repinTimer.Tick += (s, e) => {
            for (int i = 0; i < _forms.Length; i++){
                var f = _forms[i];
                if (f == null || f.IsDisposed || !f.IsHandleCreated) continue;
                W.RepinIfNeeded(f);
            }
        };
        repinTimer.Start();

        // V2: Immediate repin on display change (maximize/restore/snap)
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => {
            EventLog.Info("DisplaySettingsChanged — immediate repin all");
            for (int i = 0; i < _forms.Length; i++){
                var f = _forms[i];
                if (f == null || f.IsDisposed || !f.IsHandleCreated) continue;
                W.RepinIfNeeded(f);
            }
        };

        Application.Run();
    }
}
