using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ============================================================
// Visual Demo — interactive dock simulation
// Shows: hover magnification, glow line, badge, right-click menu
// Closes on ESC key or clicking the minimize icon (first icon)
// ============================================================

class VisualDemo
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    static List<DockIcon> icons = new List<DockIcon>();
    static Form lineForm;
    static bool demoRunning = true;
    const int gap = 14;

    [STAThread] static void Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();

        int sw = Screen.PrimaryScreen.WorkingArea.Width;
        int sh = Screen.PrimaryScreen.WorkingArea.Height;

        // --- Create icons ---
        // Icon 0: Minimize (special)
        var minimizeIcon = new DockIcon(44, 8);
        minimizeIcon.Pid = -1;
        var mBmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        using (var g = Graphics.FromImage(mBmp))
        {
            g.Clear(Color.Transparent);
            int sz = mBmp.Width, m = sz / 4;
            Color c = Theme.IsLight ? Color.Black : Color.White;
            using (var p = new Pen(c, Math.Max(2, sz / 16)))
            {
                g.DrawLine(p, m, sz - m, sz / 2, sz / 2);
                g.DrawLine(p, sz / 2, sz / 2, sz - m, sz - m);
            }
        }
        minimizeIcon.SetIcon(mBmp);
        minimizeIcon.SetTooltip("WinDock — Minimize/Restore");
        minimizeIcon.SetClick(() => { demoRunning = false; Application.Exit(); });
        minimizeIcon.BindClicks();
        icons.Add(minimizeIcon);

        // Icon 1: Start
        var startIcon = new DockIcon(44, 8);
        startIcon.Pid = -2;
        var sBmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        using (var g = Graphics.FromImage(sBmp))
        {
            g.Clear(Color.Transparent);
            int sz = sBmp.Width, g2 = Math.Max(2, sz / 20), sq = (sz - g2 * 3) / 2, m = (sz - sq * 2 - g2) / 2;
            Color c = Theme.IsLight ? Color.Black : Color.White;
            using (var br = new SolidBrush(c))
            {
                g.FillRectangle(br, m, m, sq, sq);
                g.FillRectangle(br, m + sq + g2, m, sq, sq);
                g.FillRectangle(br, m, m + sq + g2, sq, sq);
                g.FillRectangle(br, m + sq + g2, m + sq + g2, sq, sq);
            }
        }
        startIcon.SetIcon(sBmp);
        startIcon.SetTooltip("Start Menu");
        startIcon.SetClick(() => SendKeys.Send("^{ESC}"));
        startIcon.BindClicks();
        icons.Add(startIcon);

        // Icons 2-5: Real app icons extracted from system EXEs
        // Use known system paths — these always exist and have proper icons
        string[] sysExes = {
            @"C:\Windows\explorer.exe",           // File Explorer
            @"C:\Windows\System32\notepad.exe",    // Notepad
            @"C:\Windows\System32\calc.exe",       // Calculator
            @"C:\Windows\System32\cmd.exe",         // Command Prompt
        };
        string[] sysLabels = { "Explorer", "Notepad", "Calculator", "CMD" };
        for (int i = 0; i < sysExes.Length; i++)
        {
            var di = new DockIcon(44, 8);
            di.Pid = 100 + i;
            bool ok = false;
            if (System.IO.File.Exists(sysExes[i]))
            {
                try
                {
                    using (var ico = Icon.ExtractAssociatedIcon(sysExes[i]))
                    {
                        var bmp = DockIcon.IconToBmpAtDpi(ico);
                        if (bmp != null) { di.SetIcon(bmp); ok = true; }
                    }
                }
                catch { }
            }
            if (!ok)
            {
                // Last resort: use application icon
                var bmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
                di.SetIcon(bmp);
            }
            string exePath = sysExes[i]; // capture for closure
            di.PinPath = exePath; // store path for pin tracking
            di.SetTooltip(sysLabels[i]);
            if (i < 2) di.SetBadge(i + 1); // badge on first 2
            di.SetClick(() => {
                try { System.Diagnostics.Process.Start(exePath); }
                catch { }
            });
            di.SetRightClick(pos =>
            {
                string ep = exePath;
                bool isPinned = DemoPinHelper.HasPin(ep);
                IconMenu.Show(pos, false, isPinned,
                    onClose: () => {
                        try { System.Diagnostics.Process.Start(ep); }
                        catch { }
                    },
                    onTogglePin: () => {
                        if (DemoPinHelper.HasPin(ep))
                            DemoPinHelper.Unpin(ep);
                        else
                            DemoPinHelper.Pin(ep);
                        di.Pinned = DemoPinHelper.HasPin(ep);
                    },
                    onClosed: di.OnMenuClosed
                );
            });
            di.BindClicks();
            icons.Add(di);
        }

        // --- Layout ---
        // IMPORTANT: Don't use Form.Height before Show() — it returns default 300.
        // Use DPI-calculated fw instead (matches what SetBasePos sets).
        int fw = (int)(44 * DockIcon.DpiX / 96f);
        // Also normalize all icon sizes before positioning
        for (int i = 0; i < icons.Count; i++) { icons[i].Form.Size = new Size(fw, fw); }
        int totalW = icons.Count * fw + (icons.Count - 1) * gap;
        int startX = (sw - totalW) / 2;
        int iconY = sh - fw - 20;

        for (int i = 0; i < icons.Count; i++)
        {
            icons[i].SetBasePos(startX + i * (fw + gap), iconY);
        }

        // --- Glow line form ---
        int lineH = 10;
        lineForm = new Form
        {
            Size = new Size(totalW, lineH),
            StartPosition = FormStartPosition.Manual,
            FormBorderStyle = FormBorderStyle.None,
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = Theme.FormBg,
            BackgroundImage = Theme.GlassBmp,
            BackgroundImageLayout = ImageLayout.Stretch,
            Location = new Point(startX, iconY + fw / 2 - lineH / 2),
        };
        lineForm.Paint += (s, e) =>
        {
            if (icons.Count < 2) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int midY = lineForm.Height / 2;
            float x0 = icons[0].BaseX + icons[0].Form.Width / 2f;
            float x1 = icons[icons.Count - 1].BaseX + icons[icons.Count - 1].Form.Width / 2f;
            var c = Theme.IsLight ? Color.FromArgb(240, 220, 140) : Color.FromArgb(100, 140, 220);
            using (var p = new Pen(c, 2f))
                g.DrawLine(p, x0, midY, x1, midY);
        };

        // --- ESC key handler ---
        lineForm.KeyPreview = true;
        lineForm.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { demoRunning = false; Application.Exit(); } };

        // Show everything
        lineForm.Show();
        foreach (var di in icons) di.Show();

        Console.WriteLine("=== WinDock Visual Demo ===");
        Console.WriteLine("Hover over icons to see magnification zoom");
        Console.WriteLine("Right-click icons to see context menu");
        Console.WriteLine("Click the minimize icon (arrow) or press ESC to exit");
        Console.WriteLine("Theme: " + (Theme.IsLight ? "Light" : "Dark"));
        Console.WriteLine("Icons: " + icons.Count);

        Application.Run(lineForm);
    }

    // Pin helpers — same COM logic as DockLine.PinApp/UnpinApp
    static class DemoPinHelper
    {
        static string PinDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            + @"\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";

        public static bool HasPin(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            string lnk = System.IO.Path.Combine(PinDir, System.IO.Path.GetFileNameWithoutExtension(exePath) + ".lnk");
            if (!System.IO.File.Exists(lnk)) return false;
            try
            {
                var st = Type.GetTypeFromProgID("WScript.Shell");
                if (st == null) return false;
                dynamic shell = Activator.CreateInstance(st);
                string target = shell.CreateShortcut(lnk).TargetPath;
                return target != null && target.Equals(exePath, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool Pin(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath)) return false;
            try
            {
                var st = Type.GetTypeFromProgID("WScript.Shell");
                if (st == null) return false;
                dynamic shell = Activator.CreateInstance(st);
                string lnkPath = System.IO.Path.Combine(PinDir, System.IO.Path.GetFileNameWithoutExtension(exePath) + ".lnk");
                dynamic lnk = shell.CreateShortcut(lnkPath);
                lnk.TargetPath = exePath;
                lnk.Save();
                return true;
            }
            catch { return false; }
        }

        public static bool Unpin(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            try
            {
                var st = Type.GetTypeFromProgID("WScript.Shell");
                if (st == null) return false;
                dynamic shell = Activator.CreateInstance(st);
                foreach (var f in System.IO.Directory.GetFiles(PinDir, "*.lnk"))
                {
                    try
                    {
                        dynamic lnk = shell.CreateShortcut(f);
                        if (lnk.TargetPath.Equals(exePath, StringComparison.OrdinalIgnoreCase))
                        { System.IO.File.Delete(f); return true; }
                    }
                    catch { }
                }
                return false;
            }
            catch { return false; }
        }
    }
}
