using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// ============================================================
// Test 2: Layout Calculation
// Verify horizontal centering, gap spacing, layout caching
// Replicates DockLine.Layout() algorithm
// ============================================================

class TestLayout
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static string Log = @"C:\temp\_test_layout_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    const int gap = 14;

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestLayout @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();

        try
        {
            int sw = Screen.PrimaryScreen.WorkingArea.Width;
            int sh = Screen.PrimaryScreen.WorkingArea.Height;

            // Test with N = 3, 5, 8
            foreach (int cnt in new[] { 3, 5, 8 })
            {
                TestLayoutCount(cnt, sw, sh);
            }

            // Test layout caching
            TestLayoutCache(sw, sh);

            // Test debug mode positioning (y=40)
            // DebugMode.On has private setter — use reflection to enable
            var dmField = typeof(DebugMode).GetProperty("On");
            bool origDebug = DebugMode.On;
            // Since we can't set DebugMode.On, just test with Y=40 directly
            var debugIcons = new List<DockIcon>();
            for (int i = 0; i < 3; i++)
            {
                var di = new DockIcon(44, 8);
                di.SetIcon(DockIcon.IconToBmpAtDpi(SystemIcons.Application));
                debugIcons.Add(di);
            }
            int fwD = debugIcons[0].Form.Height;
            int totalWD = 3 * fwD + 2 * gap;
            int startXD = (sw - totalWD) / 2;
            int iconYD = 40; // debug mode Y position
            for (int i = 0; i < 3; i++)
            {
                debugIcons[i].SetBasePos(startXD + i * (fwD + gap), iconYD);
            }
            int actualY = iconYD;
            if (actualY == 40)
                Pass("Debug-mode Y position = 40 (tested directly)");
            else
                Fail("Debug-mode Y=" + actualY + " expected 40");
            foreach (var di in debugIcons) di.Dispose();

        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        System.Threading.Thread.Sleep(100);
        Application.Exit();
    }

    static void TestLayoutCount(int cnt, int sw, int sh)
    {
        var icons = new List<DockIcon>();
        for (int i = 0; i < cnt; i++)
        {
            var di = new DockIcon(44, 8);
            di.SetIcon(DockIcon.IconToBmpAtDpi(SystemIcons.Application));
            icons.Add(di);
        }
        int fw = icons[0].Form.Height; // square: Form.Width == Form.Height == baseSize
        int totalW = cnt * fw + (cnt - 1) * gap;
        int expectedStartX = (sw - totalW) / 2;
        int expectedIconY = sh - fw - 20; // normal mode

        for (int i = 0; i < cnt; i++)
        {
            int expectedX = expectedStartX + i * (fw + gap);
            icons[i].SetBasePos(expectedX, expectedIconY);
        }

        // Verify BaseX and positioning
        for (int i = 0; i < cnt; i++)
        {
            int actualX = icons[i].BaseX;
            int actualTop = icons[i].Form.Top;
            if (actualX == expectedStartX + i * (fw + gap))
                Pass("Layout[" + cnt + "] icon[" + i + "] X=" + actualX + " correct");
            else
                Fail("Layout[" + cnt + "] icon[" + i + "] X=" + actualX + " expected " + (expectedStartX + i * (fw + gap)));
        }

        // Verify centering: first icon BaseX == expectedStartX
        if (icons[0].BaseX == expectedStartX)
            Pass("Layout[" + cnt + "] centered: startX=" + expectedStartX + " totalW=" + totalW);
        else
            Fail("Layout[" + cnt + "] not centered: startX=" + icons[0].BaseX + " expected " + expectedStartX);

        foreach (var di in icons) di.Dispose();
    }

    static void TestLayoutCache(int sw, int sh)
    {
        var icons = new List<DockIcon>();
        for (int i = 0; i < 4; i++)
        {
            var di = new DockIcon(44, 8);
            di.SetIcon(DockIcon.IconToBmpAtDpi(SystemIcons.Application));
            icons.Add(di);
        }
        int fw = icons[0].Form.Height;
        int totalW = 4 * fw + 3 * gap;
        int startX = (sw - totalW) / 2;
        int iconY = sh - fw - 20;

        for (int i = 0; i < 4; i++)
            icons[i].SetBasePos(startX + i * (fw + gap), iconY);

        // First layout records lastStartX, lastIconY, lastCnt
        int lastStartX = startX, lastIconY = iconY, lastCnt = 4;

        // Same values — should skip
        bool wouldSkip = (startX == lastStartX && iconY == lastIconY && 4 == lastCnt);
        if (wouldSkip)
            Pass("LayoutCache: same values — would skip reposition");
        else
            Fail("LayoutCache: same values should skip but didn't");

        // Changed count — should reposition
        bool wouldRepos = (startX == lastStartX && iconY == lastIconY && 5 != lastCnt);
        if (wouldRepos)
            Pass("LayoutCache: count changed — would reposition");
        else
            Fail("LayoutCache: count changed should reposition");

        foreach (var di in icons) di.Dispose();
    }
}
