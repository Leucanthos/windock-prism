using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// ============================================================
// DockBar — dock manager: collects DockIcons, layout, taskbar
// ============================================================

static class DockBar
{
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out int pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int idx);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    const uint WM_CLOSE = 0x0010;
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);

    const uint GW_HWNDNEXT=2;
    const int GWL_EXSTYLE=-20, WS_EX_TOOLWINDOW=0x80;
    const int SW_RESTORE=9, SW_SHOW=5;

    static Form shelf;
    static List<DockIcon> icons=new List<DockIcon>();
    static System.Windows.Forms.Timer refreshTimer;
    static IntPtr taskbarHwnd;
    static int logicalTileSize=44, tileGap=14, shelfH=4;

    public static Form Create(){
        Icon ai=null; try{ai=new Icon(AppDomain.CurrentDomain.BaseDirectory+@"assets\Windock.ico");}catch{}
        shelf=new Form{Text="WinDock",Size=new Size(1,shelfH),StartPosition=FormStartPosition.Manual,
            FormBorderStyle=FormBorderStyle.None,TopMost=true,ShowInTaskbar=true,ShowIcon=true,Icon=ai,
            BackColor=Theme.FormBg,BackgroundImage=Theme.GlassBmp,BackgroundImageLayout=ImageLayout.Stretch};
        shelf.Shown+=(s,e)=>{int c=2;DwmSetWindowAttribute(shelf.Handle,33,ref c,4);RefreshIcons();refreshTimer.Start();};
        shelf.FormClosed+=(s2,e2)=>{RestoreTaskbar();foreach(var ic in icons)ic.Dispose();};
        HideTaskbar(); TaskbarGuard();
        refreshTimer=new System.Windows.Forms.Timer{Interval=1500};refreshTimer.Tick+=(s,e)=>RefreshIcons();
        SystemEvents.UserPreferenceChanged+=(s,e)=>{if(e.Category==UserPreferenceCategory.General)CheckTheme();};
        return shelf;
    }

    // ===== Layout =====
    static void Reposition(){
        int fw = (int)(44 * DockIcon.DpiX / 96f);
        int sh=Screen.PrimaryScreen.WorkingArea.Height, sw=Screen.PrimaryScreen.WorkingArea.Width;
        int totalW=Math.Max(icons.Count*(fw+tileGap)-tileGap+20,60);
        shelf.Width=totalW; shelf.Left=(sw-totalW)/2; shelf.Top=sh-shelfH-16;
        int iconY=shelf.Top-fw+6;
        for(int i=0;i<icons.Count;i++)
            icons[i].SetBasePos(shelf.Left+10+i*(fw+tileGap),iconY);
    }

    // ===== Icons =====
    static void RefreshIcons(){
        var seen=new HashSet<int>(); var found=new List<DockIcon>();
        var pw=new Dictionary<int,IntPtr>(); var pt=new Dictionary<int,string>();
        var pwCount=new Dictionary<int,int>(); // window count per pid

        // Load pinned paths so we can merge running processes with their pinned icons
        var pinnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pinnedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            foreach (var path in PinStore.PinnedPaths) {
                pinnedPaths.Add(path);
                pinnedNames.Add(System.IO.Path.GetFileNameWithoutExtension(path));
            }
        } catch { }

        IntPtr hWnd=IntPtr.Zero;
        while((hWnd=FindNextWindow(hWnd))!=IntPtr.Zero){
            if(!IsWindowVisible(hWnd))continue;
            int ex=GetWindowLong(hWnd,GWL_EXSTYLE);if((ex&WS_EX_TOOLWINDOW)!=0)continue;
            var sb=new System.Text.StringBuilder(256);GetWindowText(hWnd,sb,256);if(sb.Length==0)continue;
            var t=sb.ToString();
            if(t=="TopBar"||t=="WinDock"||t=="System"||t=="Disk"||t=="Network"||t=="Battery"||t=="Recycle Bin"||t=="WiFi Panel"||t=="Volume"||t=="Brightness"||t=="Audio")continue;
            int pid;GetWindowThreadProcessId(hWnd,out pid);if(pid==0||seen.Contains(pid))continue;
            try{var p=Process.GetProcessById(pid);if(p.MainWindowHandle==IntPtr.Zero)continue;
                string pn=p.ProcessName.ToLower();if(pn=="explorer"||pn=="searchapp"||pn=="textinputhost")continue;

                // If this process belongs to a pinned app, skip it here and let the
                // pinned icon handle it (prevents duplicate icons for multi-process apps).
                if(pinnedNames.Contains(p.ProcessName)) { seen.Add(pid); continue; }
            }catch{continue;}
            if(!pw.ContainsKey(pid)||t.Length>pt[pid].Length){pw[pid]=hWnd;pt[pid]=t;}
            if(!pwCount.ContainsKey(pid))pwCount[pid]=1;else pwCount[pid]++;
        }
        foreach(var kv in pw){int pid=kv.Key;hWnd=kv.Value;seen.Add(pid);
            bool ex2=false;foreach(var ic in icons){if(!ic.Pinned&&ic.Pid==pid){ic.HWnd=hWnd;found.Add(ic);ex2=true;break;}}
            if(ex2)continue;
            var di=new DockIcon(logicalTileSize);
            di.HWnd=hWnd;di.Pid=pid;
            try{using(var ico=Icon.ExtractAssociatedIcon(Process.GetProcessById(pid).MainModule.FileName)){
                var bmp=DockIcon.IconToBmpAtDpi(ico);if(bmp==null)continue;di.SetIcon(bmp);}}catch{continue;}
            di.SetClick(()=>{if(di.HWnd!=IntPtr.Zero){if(IsIconic(di.HWnd))ShowWindow(di.HWnd,SW_RESTORE);SwitchToThisWindow(di.HWnd,true);}});
            found.Add(di);
        }

        // Preserve pinned icons from old list (both active and inactive).
        // This prevents pinned apps from disappearing when the periodic refresh
        // runs and no visible windows are found for them.
        foreach (var ic in icons)
        {
            if (ic.Pinned && !found.Contains(ic))
            {
                found.Add(ic);
            }
        }

        // Update badges
        foreach(var di in found){
            int pid=di.Pid;
            int cnt = 0;
            if (di.Pinned && pid > 0) {
                try {
                    var pp = Process.GetProcessById(pid);
                    string pn = pp.ProcessName;
                    if (pn.Equals("Weixin", StringComparison.OrdinalIgnoreCase) ||
                        pn.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                    { cnt = 1; }
                } catch { }
                // Always count windows via pin path (works even if the specific
                // PID died — multi-process apps recycle PIDs when windows close)
                if (cnt == 0 && !string.IsNullOrEmpty(di.PinPath))
                {
                    string pinName = System.IO.Path.GetFileNameWithoutExtension(di.PinPath);
                    if (pinName.Equals("Weixin", StringComparison.OrdinalIgnoreCase) ||
                        pinName.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                    {
                        cnt = (pid > 0) ? 1 : 0; // System-tray: 1 if was running
                    }
                    else
                    {
                        cnt = DockManager.CountWindowsForPin(di.PinPath);
                        if (cnt == 0) cnt = 1;
                    }
                }
            } else {
                cnt = pwCount.ContainsKey(pid) ? pwCount[pid] : 0;
            }
            di.SetBadge(cnt);
        }
        // Remove old (but keep pinned icons)
        foreach(var ic in icons)if(!found.Contains(ic)){ic.Dispose();}
        icons=found;
        if(icons.Count==0)return;
        Reposition();
        foreach(var ic in icons)if(!ic.Form.Visible)ic.Show();
    }

    // ===== Taskbar =====
    public static void HideTaskbar(){taskbarHwnd=FindWindow("Shell_TrayWnd",null);if(taskbarHwnd!=IntPtr.Zero)ShowWindow(taskbarHwnd,0);}
    public static void RestoreTaskbar(){if(taskbarHwnd!=IntPtr.Zero)ShowWindow(taskbarHwnd,5);}
    public static void TaskbarGuard(){var t=new System.Windows.Forms.Timer{Interval=2000};t.Tick+=(s,e)=>{if(taskbarHwnd!=IntPtr.Zero)ShowWindow(taskbarHwnd,0);};t.Start();}

    // ===== Theme =====
    static bool lastLight;
    static void CheckTheme(){
        bool light=(int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0)??0)==1;
        if(light==lastLight)return;lastLight=light;
        Theme.IsLight=light;Theme.Init();
        shelf.BackgroundImage=Theme.GlassBmp;shelf.BackColor=Theme.FormBg;
        foreach(var ic in icons)ic.UpdateTheme();
    }

    // ===== Public helpers =====
    public static int GetPid(IntPtr hWnd){int pid;GetWindowThreadProcessId(hWnd,out pid);return pid;}
    public static void GetWindowThreadProcessId2(IntPtr h,out int o){GetWindowThreadProcessId(h,out o);}
    public static void FocusWindow(IntPtr hWnd){
        // Restore minimized windows
        if(IsIconic(hWnd)) ShowWindow(hWnd,SW_RESTORE);
        // Show hidden windows (apps in system tray)
        else if(!IsWindowVisible(hWnd)) ShowWindow(hWnd,SW_SHOW);
        SwitchToThisWindow(hWnd,true);
    }
    public static void CloseWindow(IntPtr hWnd){PostMessage(hWnd,WM_CLOSE,IntPtr.Zero,IntPtr.Zero);}
    public static void KillProcess(int pid){try{Process.GetProcessById(pid).Kill();}catch{}}
    public static string GetExePath(int pid){try{return Process.GetProcessById(pid).MainModule.FileName;}catch{return null;}}
    public static string GetWinTitle(IntPtr hWnd){var sb=new System.Text.StringBuilder(256);GetWindowText(hWnd,sb,256);return sb.ToString();}
    public static IntPtr FindNextWindow(IntPtr a){return a==IntPtr.Zero?GetTopWindow(IntPtr.Zero):GetWindow(a,GW_HWNDNEXT);}
    public static bool IsVisibleWindow(IntPtr hWnd){if(!IsWindowVisible(hWnd))return false;var sb=new System.Text.StringBuilder(256);GetWindowText(hWnd,sb,256);return sb.Length>0;}
}
