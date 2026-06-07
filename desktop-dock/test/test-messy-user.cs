using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// ============================================================
// Test: Messy User — simulate real-world chaotic user actions
// ============================================================

class TestMessyUser
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);

    struct RECT { public int Left, Top, Right, Bottom; public int Width { get { return Right - Left; } } public int Height { get { return Bottom - Top; } } }
    struct POINT { public int X, Y; }

    static string Log = @"C:\temp\_test_messy_user_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    static FieldInfo f_targetScale, f_curScale, f_badgeCount, f_posSet;
    static int sw, sh, fw, iconY;

    static void WaitPump(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(5); }
    }

    static RECT GetRect(Form f) { RECT r; GetWindowRect(f.Handle, out r); return r; }

    static void MoveMouseTo(int screenX, int screenY)
    {
        SetCursorPos(screenX, screenY);
        WaitPump(30); // let WM_MOUSEMOVE propagate
    }

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestMessyUser @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();

        var t = typeof(DockIcon);
        f_targetScale = t.GetField("targetScale", BindingFlags.NonPublic | BindingFlags.Instance);
        f_curScale    = t.GetField("curScale",    BindingFlags.NonPublic | BindingFlags.Instance);
        f_badgeCount  = t.GetField("badgeCount",  BindingFlags.NonPublic | BindingFlags.Instance);
        f_posSet      = t.GetField("_posSet",     BindingFlags.NonPublic | BindingFlags.Instance);

        sw = Screen.PrimaryScreen.WorkingArea.Width;
        sh = Screen.PrimaryScreen.WorkingArea.Height;
        fw = (int)(44 * DockIcon.DpiX / 96f);
        iconY = sh - fw - 20;

        try
        {
            Test_RapidToggle();           // 疯狂最小化/恢复
            Test_HoverJitter();           // 鼠标在图标边缘快速抖动
            Test_RightClickWhileHovered();// 在一个图标放大时右击另一个
            Test_MultiWindowSpawn();      // 快速创建/销毁多个图标
            Test_HoverAcrossAllIcons();   // 鼠标扫过全部图标
            Test_ToggleWhileHovered();    // 在hover状态下最小化dock
            Test_EmptyDockRecovery();     // 0图标状态下操作
            Test_RefreshLockStress();     // RefreshLock期间的乱序事件
            Test_DisposeRecreateCycle();  // 反复销毁重建
            Test_EdgeIconHover();         // 鼠标在第一个/最后一个图标边缘
        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        WaitPump(100);
        Application.Exit();
    }

    static DockIcon MakeIcon()
    {
        var di = new DockIcon(44, 8);
        di.SetIcon(DockIcon.IconToBmpAtDpi(SystemIcons.Application));
        return di;
    }

    // ===== 1: Rapid toggle (minimize/restore) =====
    static void Test_RapidToggle()
    {
        Write("INFO", "=== Rapid Toggle ===");
        var icons = new List<DockIcon>();
        for (int i = 0; i < 5; i++)
        {
            var di = MakeIcon();
            int x = (sw - (5 * fw + 4 * 14)) / 2 + i * (fw + 14);
            di.SetBasePos(x, iconY);
            di.Show();
            icons.Add(di);
        }
        WaitPump(200);

        bool crashed = false;
        try
        {
            for (int cycle = 0; cycle < 10; cycle++)
            {
                // Hide all
                foreach (var di in icons) di.Hide();
                WaitPump(30);
                // Show all
                foreach (var di in icons) di.Show();
                WaitPump(30);
            }
        }
        catch (Exception ex) { crashed = true; Fail("RapidToggle crashed: " + ex.Message); }
        if (!crashed) Pass("Rapid toggle (10 cycles): no crash");

        foreach (var di in icons) di.Dispose();
        WaitPump(100);
    }

    // ===== 2: Hover jitter at icon boundary =====
    static void Test_HoverJitter()
    {
        Write("INFO", "=== Hover Jitter ===");
        var di = MakeIcon();
        int cx = sw / 2;
        di.SetBasePos(cx, iconY);
        di.Show();
        WaitPump(200);

        var r = GetRect(di.Form);
        int iconCenterX = r.Left + r.Width / 2;
        int iconCenterY = r.Top + r.Height / 2;
        int iconLeft = r.Left;
        int iconRight = r.Right;

        // Jitter mouse rapidly between inside and outside of icon
        bool crashed = false;
        try
        {
            for (int j = 0; j < 20; j++)
            {
                MoveMouseTo(iconLeft - 5, iconCenterY);  // outside left
                MoveMouseTo(iconLeft + 5, iconCenterY);  // just inside
                MoveMouseTo(iconRight - 5, iconCenterY); // just inside right
                MoveMouseTo(iconRight + 5, iconCenterY); // outside right
            }
            WaitPump(200);
            float scale = (float)f_curScale.GetValue(di);
            Write("INFO", "Scale after jitter: " + scale.ToString("F3"));
        }
        catch (Exception ex) { crashed = true; Fail("HoverJitter crashed: " + ex.Message); }
        if (!crashed) Pass("Hover jitter (20 crosses): no crash");

        di.Dispose();
        WaitPump(100);
    }

    // ===== 3: Right-click while another icon is magnified =====
    static void Test_RightClickWhileHovered()
    {
        Write("INFO", "=== Right-Click While Another Icon Magnified ===");
        var iconA = MakeIcon();
        var iconB = MakeIcon();
        int cx = sw / 2;
        iconA.SetBasePos(cx - fw - 14, iconY);
        iconB.SetBasePos(cx, iconY);
        iconA.Show(); iconB.Show();
        WaitPump(200);

        bool crashed = false;
        try
        {
            // Hover icon A
            var rA = GetRect(iconA.Form);
            MoveMouseTo(rA.Left + rA.Width / 2, rA.Top + rA.Height / 2);
            WaitPump(100);
            // Now right-click icon B while A is still magnified
            var rB = GetRect(iconB.Form);
            MoveMouseTo(rB.Left + rB.Width / 2, rB.Top + rB.Height / 2);
            WaitPump(50);
            // Right-click simulated via direct menu open
            DockIcon.MenuOpen = true;
            WaitPump(50);
            DockIcon.MenuOpen = false;
            WaitPump(100);

            float scaleA = (float)f_curScale.GetValue(iconA);
            Write("INFO", "Icon A scale after B right-click: " + scaleA.ToString("F3"));
        }
        catch (Exception ex) { crashed = true; Fail("RightClickWhileHovered crashed: " + ex.Message); }
        if (!crashed) Pass("Right-click while another magnified: no crash");

        iconA.Dispose(); iconB.Dispose();
        WaitPump(100);
    }

    // ===== 4: Multi-window spawn (rapid create/destroy) =====
    static void Test_MultiWindowSpawn()
    {
        Write("INFO", "=== Multi-Window Spawn ===");
        var icons = new List<DockIcon>();
        bool crashed = false;
        try
        {
            // Spawn 15 icons rapidly
            for (int i = 0; i < 15; i++)
            {
                var di = MakeIcon();
                di.SetBasePos(sw / 2 + (i - 7) * (fw + 14), iconY);
                di.Show();
                icons.Add(di);
                WaitPump(10);
            }
            WaitPump(200);

            // Now dispose every other one
            for (int i = icons.Count - 1; i >= 0; i -= 2)
            {
                icons[i].Dispose();
                icons.RemoveAt(i);
                WaitPump(10);
            }
            WaitPump(200);

            // Reposition surviving icons
            int cnt = icons.Count;
            int startX = (sw - (cnt * fw + (cnt - 1) * 14)) / 2;
            for (int i = 0; i < cnt; i++)
                icons[i].SetBasePos(startX + i * (fw + 14), iconY);
            WaitPump(200);
        }
        catch (Exception ex) { crashed = true; Fail("MultiSpawn crashed: " + ex.Message); }
        if (!crashed) Pass("Multi-window spawn/dispose (15→7): no crash");

        foreach (var di in icons) di.Dispose();
        WaitPump(100);
    }

    // ===== 5: Hover across all icons rapidly =====
    static void Test_HoverAcrossAllIcons()
    {
        Write("INFO", "=== Hover Across All Icons ===");
        int cnt = 8;
        var icons = new List<DockIcon>();
        int startX = (sw - (cnt * fw + (cnt - 1) * 14)) / 2;
        for (int i = 0; i < cnt; i++)
        {
            var di = MakeIcon();
            di.SetBasePos(startX + i * (fw + 14), iconY);
            di.Show();
            icons.Add(di);
        }
        WaitPump(300);

        bool crashed = false;
        try
        {
            // Sweep mouse left-to-right across all icons
            for (int i = 0; i < cnt; i++)
            {
                var r = GetRect(icons[i].Form);
                MoveMouseTo(r.Left + r.Width / 2, r.Top + r.Height / 2);
                WaitPump(40);
            }
            // Sweep right-to-left
            for (int i = cnt - 1; i >= 0; i--)
            {
                var r = GetRect(icons[i].Form);
                MoveMouseTo(r.Left + r.Width / 2, r.Top + r.Height / 2);
                WaitPump(40);
            }
            WaitPump(300);

            // All should be back to scale 1.0 (mouse left dock area)
            for (int i = 0; i < cnt; i++)
            {
                float s = (float)f_curScale.GetValue(icons[i]);
                if (s > 1.1f) Write("WARN", "Icon[" + i + "] scale=" + s.ToString("F2") + " (should be ~1.0 after hover sweep)");
            }
        }
        catch (Exception ex) { crashed = true; Fail("HoverSweep crashed: " + ex.Message); }
        if (!crashed) Pass("Hover sweep across 8 icons: no crash");

        foreach (var di in icons) di.Dispose();
        WaitPump(100);
    }

    // ===== 6: Toggle while hovered =====
    static void Test_ToggleWhileHovered()
    {
        Write("INFO", "=== Toggle While Hovered ===");
        var di = MakeIcon();
        di.SetBasePos(sw / 2, iconY);
        di.Show();
        WaitPump(200);

        var r = GetRect(di.Form);
        MoveMouseTo(r.Left + r.Width / 2, r.Top + r.Height / 2);
        WaitPump(50); // icon should be magnified

        bool crashed = false;
        try
        {
            // Simulate toggle: hide then show while mouse is over
            di.Hide();
            WaitPump(50);
            di.Show();
            WaitPump(200);
        }
        catch (Exception ex) { crashed = true; Fail("ToggleWhileHovered crashed: " + ex.Message); }
        if (!crashed) Pass("Toggle while hovered: no crash");

        di.Dispose();
        WaitPump(100);
    }

    // ===== 7: Empty dock recovery =====
    static void Test_EmptyDockRecovery()
    {
        Write("INFO", "=== Empty Dock Recovery ===");
        // Simulate icon list being cleared
        var icons = new List<DockIcon>();
        bool crashed = false;
        try
        {
            // Layout with 0 icons should not crash
            LayoutEngine.Apply(icons, sw, sh, false);
            // Show with 0 icons
            foreach (var di in icons) di.Show();
            // Hide with 0 icons
            foreach (var di in icons) di.Hide();
            // Add icon after empty state
            var newIcon = MakeIcon();
            newIcon.SetBasePos(sw / 2, iconY);
            newIcon.Show();
            WaitPump(100);
            newIcon.Dispose();
        }
        catch (Exception ex) { crashed = true; Fail("EmptyDock crashed: " + ex.Message); }
        if (!crashed) Pass("Empty dock recovery: no crash");
        WaitPump(100);
    }

    // ===== 8: RefreshLock stress =====
    static void Test_RefreshLockStress()
    {
        Write("INFO", "=== RefreshLock Stress ===");
        var icons = new List<DockIcon>();
        for (int i = 0; i < 5; i++)
        {
            var di = MakeIcon();
            int x = sw / 2 + (i - 2) * (fw + 14);
            di.SetBasePos(x, iconY);
            di.Show();
            icons.Add(di);
        }
        WaitPump(200);

        bool crashed = false;
        try
        {
            // Toggle RefreshLock rapidly (simulating many LayoutWithLock calls)
            for (int i = 0; i < 10; i++)
            {
                DockIcon.RefreshLock = true;
                WaitPump(10);
                // During lock: simulate mouse events
                var r = GetRect(icons[2].Form);
                MoveMouseTo(r.Left + r.Width / 2, r.Top + r.Height / 2);
                WaitPump(10);
                DockIcon.RefreshLock = false;
                WaitPump(20);
            }
            WaitPump(200);
        }
        catch (Exception ex) { crashed = true; Fail("RefreshLockStress crashed: " + ex.Message); }
        if (!crashed) Pass("RefreshLock stress (10 cycles): no crash");

        foreach (var di in icons) di.Dispose();
        WaitPump(100);
    }

    // ===== 9: Dispose-recreate cycle (simulating FullRefresh) =====
    static void Test_DisposeRecreateCycle()
    {
        Write("INFO", "=== Dispose-Recreate Cycle ===");
        bool crashed = false;
        try
        {
            for (int cycle = 0; cycle < 5; cycle++)
            {
                var icons = new List<DockIcon>();
                for (int i = 0; i < 8; i++)
                {
                    var di = MakeIcon();
                    int x = sw / 2 + (i - 4) * (fw + 14);
                    di.SetBasePos(x, iconY);
                    di.Show();
                    icons.Add(di);
                }
                WaitPump(100);

                // Hover some icons
                var r = GetRect(icons[3].Form);
                MoveMouseTo(r.Left + r.Width / 2, r.Top + r.Height / 2);
                WaitPump(50);

                // Move mouse outside dock area
                MoveMouseTo(sw / 2, sh - 5);
                WaitPump(50);

                // Dispose all (simulating FullRefresh dispose step)
                foreach (var di in icons) di.Dispose();
                WaitPump(50);
            }
        }
        catch (Exception ex) { crashed = true; Fail("DisposeRecreate crashed: " + ex.Message); }
        if (!crashed) Pass("Dispose-recreate (5 cycles of 8 icons): no crash");
        WaitPump(100);
    }

    // ===== 10: Edge icon hover (first/last icon boundary) =====
    static void Test_EdgeIconHover()
    {
        Write("INFO", "=== Edge Icon Hover ===");
        var icons = new List<DockIcon>();
        int cnt = 3;
        int startX = (sw - (cnt * fw + (cnt - 1) * 14)) / 2;
        for (int i = 0; i < cnt; i++)
        {
            var di = MakeIcon();
            di.SetBasePos(startX + i * (fw + 14), iconY);
            di.Show();
            icons.Add(di);
        }
        WaitPump(200);

        bool crashed = false;
        try
        {
            // Hover first icon (tests SpreadLens on left edge)
            var r0 = GetRect(icons[0].Form);
            MoveMouseTo(r0.Left + r0.Width / 2, r0.Top + r0.Height / 2);
            WaitPump(200);
            // Move outside left edge of dock
            MoveMouseTo(r0.Left - 10, r0.Top + r0.Height / 2);
            WaitPump(100);

            // Hover last icon (tests SpreadLens on right edge)
            var r2 = GetRect(icons[2].Form);
            MoveMouseTo(r2.Left + r2.Width / 2, r2.Top + r2.Height / 2);
            WaitPump(200);
            // Move outside right edge
            MoveMouseTo(r2.Right + 10, r2.Top + r2.Height / 2);
            WaitPump(100);
        }
        catch (Exception ex) { crashed = true; Fail("EdgeIconHover crashed: " + ex.Message); }
        if (!crashed) Pass("Edge icon hover (leftmost/rightmost): no crash");

        foreach (var di in icons) di.Dispose();
        WaitPump(100);
    }
}
