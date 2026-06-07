using System;
using System.Drawing;
using System.Windows.Forms;

// ============================================================
// Test 1: Icon Lifecycle
// Verify DockIcon create / show / hide / dispose / DPI scaling
// ============================================================

class TestIconLifecycle
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static string Log = @"C:\temp\_test_icon_lifecycle_result.txt";
    static bool allPassed = true;

    static void Pass(string msg)  { Write("PASS", msg); }
    static void Fail(string msg)  { allPassed = false; Write("FAIL", msg); }
    static void Write(string tag, string msg) { System.IO.File.AppendAllText(Log, tag + ": " + msg + "\n"); }

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestIconLifecycle @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();

        try
        {
            // --- 1. Create 3 DockIcons ---
            var icons = new DockIcon[3];
            for (int i = 0; i < 3; i++)
            {
                icons[i] = new DockIcon(44, 8);
                var bmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
                icons[i].SetIcon(bmp);
                icons[i].Pid = 100 + i;
                Pass("Create[" + i + "] — " + icons[i].Dump());
            }

            // --- 2. DPI scaling ---
            float expectedBase = 44f * DockIcon.DpiX / 96f;
            int sw = Screen.PrimaryScreen.WorkingArea.Width;
            int sh = Screen.PrimaryScreen.WorkingArea.Height;

            for (int i = 0; i < 3; i++)
            {
                int x = sw / 2 - 100 + i * 80;
                int y = sh - 120;
                icons[i].SetBasePos(x, y);
                var d = icons[i].Dump();
                // baseSize should be within 2px of expected DPI-scaled size
                int bs = ExtractBaseSize(d);
                if (Math.Abs(bs - (int)expectedBase) <= 2)
                    Pass("DPI[" + i + "] baseSize=" + bs + " ≈ expected=" + (int)expectedBase);
                else
                    Fail("DPI[" + i + "] baseSize=" + bs + " expected≈" + (int)expectedBase + " DpiX=" + DockIcon.DpiX);
            }

            // --- 3. Show ---
            for (int i = 0; i < 3; i++) { icons[i].Show(); }
            System.Threading.Thread.Sleep(200);
            for (int i = 0; i < 3; i++)
            {
                // Need BeginInvoke or just check the Form handle
                if (icons[i].Form.Visible)
                    Pass("Show[" + i + "] visible=true");
                else
                    Fail("Show[" + i + "] visible=false");
            }

            // --- 4. Hide ---
            for (int i = 0; i < 3; i++) { icons[i].Hide(); }
            System.Threading.Thread.Sleep(200);
            for (int i = 0; i < 3; i++)
            {
                if (!icons[i].Form.Visible)
                    Pass("Hide[" + i + "] visible=false");
                else
                    Fail("Hide[" + i + "] visible=true");
            }

            // --- 5. Dispose ---
            for (int i = 0; i < 3; i++)
            {
                try { icons[i].Dispose(); Pass("Dispose[" + i + "] no exception"); }
                catch (Exception ex) { Fail("Dispose[" + i + "] threw: " + ex.Message); }
            }

            // --- 6. BadgeCount property ---
            var badgeIcon = new DockIcon(44, 8);
            badgeIcon.SetBadge(5);
            if (badgeIcon.BadgeCount == 5)
                Pass("BadgeCount property = 5");
            else
                Fail("BadgeCount property = " + badgeIcon.BadgeCount + " expected 5");
            badgeIcon.Dispose();
        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        System.Threading.Thread.Sleep(100);
        Application.Exit();
    }

    static int ExtractBaseSize(string dump)
    {
        // Format: "DockIcon: baseSize=47 curSize=47 pad=8 scale=1.00 target=1.00 ..."
        int i = dump.IndexOf("baseSize=");
        if (i < 0) return -1;
        i += 9;
        int end = dump.IndexOf(' ', i);
        if (end < 0) end = dump.Length;
        int v; int.TryParse(dump.Substring(i, end - i), out v);
        return v;
    }
}
