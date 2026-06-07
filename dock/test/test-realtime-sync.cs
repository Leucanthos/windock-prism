using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

// ============================================================
// Test: Realtime Icon Sync (incremental)
//
// Verify that adding/removing icons does NOT disrupt
// the hover magnification state of existing icons.
// This is the key constraint for event-driven updates.
// ============================================================

class TestRealtimeSync
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static string Log = @"C:\temp\_test_sync_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    static FieldInfo f_targetScale, f_curScale;
    static float GetCurScale(DockIcon di) { return (float)f_curScale.GetValue(di); }
    static float GetTargetScale(DockIcon di) { return (float)f_targetScale.GetValue(di); }

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestRealtimeSync @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();

        var t = typeof(DockIcon);
        f_targetScale = t.GetField("targetScale", BindingFlags.NonPublic | BindingFlags.Instance);
        f_curScale    = t.GetField("curScale",    BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            int sw = Screen.PrimaryScreen.WorkingArea.Width;
            int sh = Screen.PrimaryScreen.WorkingArea.Height;
            int fw = (int)(44 * DockIcon.DpiX / 96f);
            int gap = 14;

            // --- 1. Create 4 initial icons ---
            var icons = new List<DockIcon>();
            for (int i = 0; i < 4; i++)
            {
                var di = MkIcon(i);
                int x = (sw - (4 * fw + 3 * gap)) / 2 + i * (fw + gap);
                int y = sh - fw - 20;
                di.SetBasePos(x, y);
                di.Show();
                icons.Add(di);
            }
            // Let message pump run so timers start
            Pump(300);
            Pass("Created 4 icons");

            // --- 2. Hover icon[1] (simulate MouseEnter) ---
            SetTarget(icons[1], 1.35f);
            Pump(600); // let lerp converge
            float scaleAfterHover = GetCurScale(icons[1]);
            if (Math.Abs(scaleAfterHover - 1.35f) < 0.02f)
                Pass("Icon[1] zoomed to 1.35 after hover (actual=" + scaleAfterHover.ToString("F3") + ")");
            else
                Fail("Icon[1] not zoomed: curScale=" + scaleAfterHover.ToString("F3"));

            // --- 3. INCREMENTAL ADD: add icon[4] without touching existing icons ---
            var newIcon = MkIcon(4);
            int newX = (sw - (5 * fw + 4 * gap)) / 2 + 4 * (fw + gap);
            int newY = sh - fw - 20;
            newIcon.SetBasePos(newX, newY);
            newIcon.Show();
            icons.Add(newIcon);
            Pump(200);

            // --- 4. Verify icon[1] still hovered (scale not reset) ---
            float scaleAfterAdd = GetCurScale(icons[1]);
            if (Math.Abs(scaleAfterAdd - 1.35f) < 0.02f)
                Pass("Icon[1] STILL zoomed after ADD (actual=" + scaleAfterAdd.ToString("F3") + ") — no disruption");
            else
                Fail("Icon[1] scale reset to " + scaleAfterAdd.ToString("F3") + " after ADD — MouseLeave triggered!");

            // Verify new icon exists
            if (icons.Count == 5)
                Pass("Icon count = 5 after incremental add");
            else
                Fail("Icon count = " + icons.Count + " expected 5");

            // --- 5. Re-layout: reposition all icons for new count ---
            Relayout(icons, fw, gap, sw, sh);
            Pump(200);

            // Icon[1] scale should STILL be 1.35 after layout
            float scaleAfterLayout = GetCurScale(icons[1]);
            if (Math.Abs(scaleAfterLayout - 1.35f) < 0.02f)
                Pass("Icon[1] STILL zoomed after re-layout (actual=" + scaleAfterLayout.ToString("F3") + ")");
            else
                Fail("Icon[1] scale reset to " + scaleAfterLayout.ToString("F3") + " after re-layout");

            // Target scale should still be 1.35
            float targetAfterLayout = GetTargetScale(icons[1]);
            if (Math.Abs(targetAfterLayout - 1.35f) < 0.02f)
                Pass("Icon[1] targetScale still 1.35");
            else
                Fail("Icon[1] targetScale reset to " + targetAfterLayout.ToString("F3"));

            // --- 6. INCREMENTAL REMOVE: remove icon[3] ---
            var toRemove = icons[3];
            icons.RemoveAt(3);
            toRemove.Dispose();
            Pump(300);

            // Icon[1] (still index 1) should still be zoomed
            float scaleAfterRemove = GetCurScale(icons[1]);
            if (Math.Abs(scaleAfterRemove - 1.35f) < 0.02f)
                Pass("Icon[1] STILL zoomed after REMOVE (actual=" + scaleAfterRemove.ToString("F3") + ")");
            else
                Fail("Icon[1] scale reset to " + scaleAfterRemove.ToString("F3") + " after REMOVE");

            if (icons.Count == 4)
                Pass("Icon count = 4 after incremental remove");
            else
                Fail("Icon count = " + icons.Count + " expected 4");

            // --- 7. Full refresh simulation (should preserve state) ---
            // The FULL refresh that DockLine currently does IS disruptive.
            // This test documents the expected behavior difference.
            var savedIcon = icons[1]; // keep reference
            // Simulate full refresh by Dispose + recreate (what old code does)
            var replacement = MkIcon(10);
            replacement.SetBasePos(savedIcon.BaseX, sh - fw - 20);
            replacement.Show();
            int oldIdx = icons.IndexOf(savedIcon);
            icons[oldIdx] = replacement;
            savedIcon.Dispose();
            Pump(200);

            // After full replace, scale SHOULD be reset (this is the OLD behavior)
            float scaleAfterReplace = GetCurScale(replacement);
            Write("INFO", "After full replace (old behavior): replacement scale=" + scaleAfterReplace.ToString("F3"));
            // This is expected to be 1.0 because it's a new icon
            if (Math.Abs(scaleAfterReplace - 1.0f) < 0.1f)
                Pass("Full replace: new icon at 1.0x (expected — this is the problem incremental fixes)");
            else
                Write("INFO", "Replace scale = " + scaleAfterReplace.ToString("F3"));

            // Cleanup
            foreach (var di in icons) di.Dispose();
        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        Pump(100);
        Application.Exit();
    }

    // Helpers
    static DockIcon MkIcon(int id)
    {
        var di = new DockIcon(44, 8);
        di.Pid = 100 + id;
        var bmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        // Draw a colored square
        Color[] cols = { Color.Coral, Color.SteelBlue, Color.SeaGreen, Color.Goldenrod, Color.MediumPurple, Color.Orange };
        using (var g = Graphics.FromImage(bmp))
        using (var br = new SolidBrush(cols[id % cols.Length]))
            g.FillRectangle(br, 4, 4, bmp.Width - 8, bmp.Height - 8);
        di.SetIcon(bmp);
        di.SetTooltip("Icon " + id);
        return di;
    }

    static void SetTarget(DockIcon di, float v) { f_targetScale.SetValue(di, v); }

    static void Pump(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
    }

    static void Relayout(List<DockIcon> icons, int fw, int gap, int sw, int sh)
    {
        int cnt = icons.Count;
        int totalW = cnt * fw + (cnt - 1) * gap;
        int startX = (sw - totalW) / 2;
        int iconY = sh - fw - 20;
        for (int i = 0; i < cnt; i++)
            icons[i].SetBasePos(startX + i * (fw + gap), iconY);
    }
}
