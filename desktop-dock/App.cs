using System;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    public static string BaseDir = AppDomain.CurrentDomain.BaseDirectory;

    static void RegisterStartup()
    {
        try
        {
            using(var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                string exe = System.IO.Path.Combine(BaseDir, "WinDock.exe");
                key.SetValue("WinDock", "\"" + exe + "\"");
            }
        }
        catch { }
    }

    [STAThread] static void Main(string[] args)
    {
        DebugMode.Init(args);
        System.IO.File.WriteAllText(@"C:\temp\_dock_startup.txt", "DebugMode="+DebugMode.On);
        SetProcessDPIAware();

        var mutexName = @"Global\WinDock_" + VersionInfo.Number + (DebugMode.On ? "_debug" : "");
        if (!W.Lock(mutexName))
        {
            try {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    try {
                        var n = p.ProcessName;
                        if (n.StartsWith("WD") || n.StartsWith("WinDock") || n == "TestIcon")
                        { p.Kill(); p.WaitForExit(2000); }
                    } catch { }
                }
            } catch { }
            System.Threading.Thread.Sleep(500);
            if (!W.Lock(mutexName))
            {
                MessageBox.Show("Already Running", VersionInfo.FullName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        Application.EnableVisualStyles();
        Theme.Init();

        if (!DebugMode.On) RegisterStartup();

        var dock = DockManager.Create();
        dock.Show();
        Application.Run();
    }
}
