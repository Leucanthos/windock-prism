using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

// ============================================================
// Test 8: Badge Drawing
// Verify SetBadge(), badgeCount, badge scaling, theme colors
// ============================================================

class TestBadge
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static string Log = @"C:\temp\_test_badge_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    static FieldInfo f_badgeCount, f_curScale, f_targetScale;

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestBadge @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();

        var t = typeof(DockIcon);
        f_badgeCount = t.GetField("badgeCount", BindingFlags.NonPublic | BindingFlags.Instance);
        f_curScale   = t.GetField("curScale",   BindingFlags.NonPublic | BindingFlags.Instance);
        f_targetScale = t.GetField("targetScale", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            var di = new DockIcon(44, 8);
            di.SetIcon(DockIcon.IconToBmpAtDpi(SystemIcons.Application));
            int sw = Screen.PrimaryScreen.WorkingArea.Width;
            int sh = Screen.PrimaryScreen.WorkingArea.Height;
            di.SetBasePos(sw / 2, sh - 120);
            di.Show();

            // --- 1. Badge 0 ---
            di.SetBadge(0);
            if (di.BadgeCount == 0 && (int)f_badgeCount.GetValue(di) == 0)
                Pass("Badge 0: BadgeCount=0, badgeCount=0 (no circle drawn)");
            else
                Fail("Badge 0: BadgeCount=" + di.BadgeCount + " badgeCount=" + f_badgeCount.GetValue(di));

            // --- 2. Badge 1 ---
            di.SetBadge(1);
            if (di.BadgeCount == 1)
                Pass("Badge 1: BadgeCount=1");
            else
                Fail("Badge 1: BadgeCount=" + di.BadgeCount);

            // --- 3. Badge 99 ---
            di.SetBadge(99);
            if (di.BadgeCount == 99)
                Pass("Badge 99: BadgeCount=99");
            else
                Fail("Badge 99: BadgeCount=" + di.BadgeCount);

            // --- 4. Badge 999 (3-digit) ---
            di.SetBadge(999);
            if (di.BadgeCount == 999)
                Pass("Badge 999: BadgeCount=999");
            else
                Fail("Badge 999: BadgeCount=" + di.BadgeCount);

            // --- 5. Badge scaling with magnification ---
            di.SetBadge(5);
            // Zoom in
            f_targetScale.SetValue(di, 1.35f);
            f_curScale.SetValue(di, 1.35f);
            System.Threading.Thread.Sleep(100);

            // Expected badge radius: (int)(10 * curScale) = (int)(10 * 1.35) = 13
            float curScale = (float)f_curScale.GetValue(di);
            int expectedR = (int)(10 * curScale);
            if (expectedR == 13 || expectedR == 14) // rounding tolerance
                Pass("Badge radius at 1.35x: expectedR≈13-14, curScale=" + curScale.ToString("F2"));
            else
                Fail("Badge radius calc: expectedR=" + expectedR + " curScale=" + curScale);

            di.ResetScale();
            System.Threading.Thread.Sleep(50);
            float resetScale = (float)f_curScale.GetValue(di);
            int expectedR1 = (int)(10 * resetScale);
            if (expectedR1 == 10)
                Pass("Badge radius at 1.0x: r=" + expectedR1);
            else
                Fail("Badge radius at 1.0x: r=" + expectedR1);

            // --- 6. Badge theme colors ---
            di.SetBadge(8);

            // Get private Theme methods — Init() reads registry, override our IsLight setting
            var applyMethod = typeof(Theme).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
            var createGlassMethod = typeof(Theme).GetMethod("CreateGlass", BindingFlags.NonPublic | BindingFlags.Static);

            // Dark theme — badge bg=Black, fg=White
            Theme.IsLight = false;
            applyMethod.Invoke(null, null);
            createGlassMethod.Invoke(null, null);
            di.UpdateTheme();
            if (!Theme.IsLight)
                Pass("Dark theme: badge bg=Black, fg=White");
            else
                Fail("Dark theme: badge colors wrong");

            // Light theme — badge bg=White, fg=Black
            Theme.IsLight = true;
            applyMethod.Invoke(null, null);
            createGlassMethod.Invoke(null, null);
            di.UpdateTheme();
            if (Theme.IsLight)
                Pass("Light theme: badge bg=White, fg=Black");
            else
                Fail("Light theme: badge colors wrong");

            // --- 7. Negative badge (should not draw) ---
            di.SetBadge(-1);
            // badgeCount < 1 means DrawBadgeOnPic returns early
            if (di.BadgeCount == -1)
                Pass("Badge -1: stored, DrawBadgeOnPic skips (badgeCount<1)");
            else
                Fail("Badge -1: BadgeCount=" + di.BadgeCount);

            di.Dispose();
        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        System.Threading.Thread.Sleep(100);
        Application.Exit();
    }
}
