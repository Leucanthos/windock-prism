using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

// ============================================================
// TestIcon — standalone single DockIcon test
// ============================================================

class TestIcon
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread] static void Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();

        // Find a usable app window
        IntPtr hWnd = IntPtr.Zero;
        string[] apps = { "msedge", "chrome", "firefox", "Code", "explorer", "notepad" };
        foreach (var name in apps)
        {
            try
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var p in procs)
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        hWnd = p.MainWindowHandle;
                        Console.WriteLine("Found: " + p.ProcessName + " title=\"" + DockBar.GetWinTitle(hWnd) + "\"");
                        break;
                    }
                }
            }
            catch { }
            if (hWnd != IntPtr.Zero) break;
        }

        if (hWnd == IntPtr.Zero)
        {
            Console.WriteLine("No app window found! Launching notepad...");
            var p = Process.Start("notepad.exe");
            p.WaitForInputIdle(3000);
            hWnd = p.MainWindowHandle;
        }

        // Get icon bitmap
        Bitmap iconBmp = null;
        try
        {
            int pid = DockBar.GetPid(hWnd);
            var proc = Process.GetProcessById(pid);
            using (var ico = Icon.ExtractAssociatedIcon(proc.MainModule.FileName))
            {
                if (ico != null) iconBmp = DockIcon.IconToBmpAtDpi(ico);
            }
        }
        catch (Exception ex) { Console.WriteLine("Icon failed: " + ex.Message); return; }

        // Create single DockIcon
        var icon = new DockIcon(logicalSize: 44, padding: 6);
        icon.HWnd = hWnd;
        icon.SetIcon(iconBmp);

        // Count windows for this process as badge
        try{
            int pid = DockBar.GetPid(hWnd);
            int winCount=0;
            IntPtr hw=IntPtr.Zero;
            while((hw=DockBar.FindNextWindow(hw))!=IntPtr.Zero){
                int pp;DockBar.GetWindowThreadProcessId2(hw,out pp);
                if(pp==pid&&DockBar.IsVisibleWindow(hw))winCount++;
            }
            icon.SetBadge(winCount);
            System.IO.File.AppendAllText(@"C:\temp\_icon_debug.txt",
                string.Format("Badge: winCount={0}\n",winCount));
        }catch(Exception ex){
            System.IO.File.AppendAllText(@"C:\temp\_icon_debug.txt",
                string.Format("Badge ERROR: {0}\n",ex.Message));
        }
        icon.SetClick(() =>
        {
            Console.WriteLine("Clicked! Focusing window...");
            DockBar.FocusWindow(icon.HWnd);
        });

        // Position Edge icon
        icon.SetBasePos(
            Screen.PrimaryScreen.WorkingArea.Width / 2 + 30,
            Screen.PrimaryScreen.WorkingArea.Height - 100
        );
        icon.Show();

        // === Special Start icon (Win11 style) ===
        var startIcon = new DockIcon(logicalSize: 44, padding: 6);
        // Draw Win11 4-pane logo (dark=white, light=black)
        var winBmp=DockIcon.IconToBmpAtDpi(SystemIcons.Application); // correct DPI size
        using(var g=Graphics.FromImage(winBmp)){g.Clear(Color.Transparent);
            int sz=winBmp.Width, gap=Math.Max(2,sz/20), sq=(sz-gap*3)/2, m=(sz-sq*2-gap)/2;
            Color c=Theme.IsLight?Color.Black:Color.White;
            using(var br=new SolidBrush(c)){
                g.FillRectangle(br,m,m,sq,sq);
                g.FillRectangle(br,m+sq+gap,m,sq,sq);
                g.FillRectangle(br,m,m+sq+gap,sq,sq);
                g.FillRectangle(br,m+sq+gap,m+sq+gap,sq,sq);
            }
        }
        startIcon.SetIcon(winBmp);
        startIcon.SetClick(() => {
            // Send Win key to open Start menu
            SendKeys.SendWait("^{ESC}");
        });
        startIcon.SetBasePos(
            Screen.PrimaryScreen.WorkingArea.Width / 2 - 80,
            Screen.PrimaryScreen.WorkingArea.Height - 100
        );
        startIcon.SetBadge(1); // no badge for Start
        startIcon.Show();
        startIcon.Form.FormClosed += (s, e) => Application.Exit();

        // Debug: dump state every second to file
        var debugTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        debugTimer.Tick += (s, e) => {
            var info = icon.Dump() + string.Format(" HWnd={0}", icon.HWnd);
            System.IO.File.AppendAllText(@"C:\temp\_icon_debug.txt", info + "\n");
        };
        debugTimer.Start();
        System.IO.File.WriteAllText(@"C:\temp\_icon_debug.txt", "TestIcon started\n");
        System.IO.File.AppendAllText(@"C:\temp\_icon_debug.txt",
            string.Format("Theme: IsLight={0} FormBg=({1},{2},{3})\n",Theme.IsLight,Theme.FormBg.R,Theme.FormBg.G,Theme.FormBg.B));

        // Periodically update badge count
        var badgeTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        badgeTimer.Tick += (s, e) => {
            try{
                int pid = DockBar.GetPid(icon.HWnd);
                int cnt=0; IntPtr hw2=IntPtr.Zero;
                while((hw2=DockBar.FindNextWindow(hw2))!=IntPtr.Zero){
                    int pp; DockBar.GetWindowThreadProcessId2(hw2,out pp);
                    if(pp==pid&&DockBar.IsVisibleWindow(hw2))cnt++;
                }
                icon.SetBadge(cnt);
            }catch{}
        };
        badgeTimer.Start();

        icon.Form.FormClosed += (s, e) => Application.Exit();
        Application.Run();
    }
}
