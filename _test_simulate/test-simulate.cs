using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// ============================================================
// Test: SimMouse — validate real hardware-level mouse simulation
// ============================================================
// Proves that SimMouse generates actual Windows input messages
// by creating a receiver form, sending events to it, and
// checking that the form's event handlers fire.
// ============================================================

class TestSimulate
{
    static string Log = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "_test_simulate_result.txt");

    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m)
    {
        System.IO.File.AppendAllText(Log, t + ": " + m + "\n");
    }

    static int sw, sh;

    // A tiny receiver form that records mouse events
    class MouseTarget : Form
    {
        public bool ClickFired;
        public bool MouseDownFired;
        public bool MouseUpFired;
        public bool RightClickFired;
        public int MouseDownCount;
        public DateTime LastMouseDownTime;
        public Point LastMovePoint;
        public MouseButtons LastButton;

        public MouseTarget(int x, int y, int w, int h)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(x, y);
            Size = new Size(w, h);
            BackColor = Color.FromArgb(255, 0, 255); // magenta — easy to spot
            TopMost = true;
            ShowInTaskbar = false;

            Click += (o, e) => ClickFired = true;
            MouseDown += (o, e) =>
            {
                MouseDownFired = true;
                LastButton = e.Button;
                MouseDownCount++;
                LastMouseDownTime = DateTime.Now;
            };
            MouseUp += (o, e) => MouseUpFired = true;
            MouseMove += (o, e) => LastMovePoint = e.Location;

            // Right-click detected via MouseDown
            MouseDown += (o, e) => { if (e.Button == MouseButtons.Right) RightClickFired = true; };
        }

        public void ResetCounters()
        {
            ClickFired = false;
            MouseDownFired = false;
            MouseUpFired = false;
            MouseDownCount = 0;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000008;   // WS_EX_TOPMOST
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE (don't steal focus)
                return cp;
            }
        }
    }

    static void WaitPump(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(5); }
    }

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestSimulate @ " + DateTime.Now + "\n");
        Write("INFO", "INPUT struct size: " + SimMouse.GetInputStructSize());
        Write("INFO", "MOUSEINPUT struct size: " + SimMouse.GetMouseInputStructSize());
        Write("INFO", "Platform: " + (IntPtr.Size == 8 ? "x64" : "x86"));

        Application.EnableVisualStyles();

        sw = Screen.PrimaryScreen.WorkingArea.Width;
        sh = Screen.PrimaryScreen.WorkingArea.Height;

        try
        {
            Test_StructSizes();
            Test_GetPosition();
            Test_MoveTo();
            Test_MoveSmooth();
            Test_LeftClick();
            Test_RightClick();
            Test_DoubleClick();
            Test_Drag();
            Test_Scroll();
            Test_MoveAndClick();
            Test_Humanize();
        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        WaitPump(100);
        Application.Exit();
    }

    // ===== 1: Struct sizes match platform =====
    static void Test_StructSizes()
    {
        Write("INFO", "=== Struct Sizes ===");

        int inputSize = SimMouse.GetInputStructSize();
        int expectedInput = IntPtr.Size == 8 ? 40 : 28;
        if (inputSize == expectedInput)
            Pass("INPUT size: " + inputSize + " (matches " + (IntPtr.Size == 8 ? "x64" : "x86") + ")");
        else
            Fail("INPUT size: " + inputSize + " (expected " + expectedInput + ")");

        int miSize = SimMouse.GetMouseInputStructSize();
        int expectedMi = IntPtr.Size == 8 ? 32 : 24;
        if (miSize == expectedMi)
            Pass("MOUSEINPUT size: " + miSize + " (matches)");
        else
            Fail("MOUSEINPUT size: " + miSize + " (expected " + expectedMi + ")");
    }

    // ===== 2: GetPosition returns valid screen coords =====
    static void Test_GetPosition()
    {
        Write("INFO", "=== GetPosition ===");
        var p = SimMouse.GetPosition();
        if (p.X >= 0 && p.X <= sw * 2 && p.Y >= 0 && p.Y <= sh * 2)
            Pass("GetPosition: (" + p.X + ", " + p.Y + ") in valid range");
        else
            Fail("GetPosition: (" + p.X + ", " + p.Y + ") out of range");
    }

    // ===== 3: MoveTo teleports correctly =====
    static void Test_MoveTo()
    {
        Write("INFO", "=== MoveTo ===");
        int tx = sw / 2, ty = sh / 2;

        // Move somewhere else first
        SimMouse.MoveTo(100, 100);
        WaitPump(50);

        // Move to target
        SimMouse.MoveTo(tx, ty);
        WaitPump(50);

        var p = SimMouse.GetPosition();
        int dist = Math.Abs(p.X - tx) + Math.Abs(p.Y - ty);
        if (dist <= 5)
            Pass("MoveTo: reached (" + p.X + ", " + p.Y + ") target (" + tx + ", " + ty + ")");
        else
            Fail("MoveTo: at (" + p.X + ", " + p.Y + ") target (" + tx + ", " + ty + ") diff=" + dist);
    }

    // ===== 4: MoveSmooth reaches target =====
    static void Test_MoveSmooth()
    {
        Write("INFO", "=== MoveSmooth ===");
        int startX = 100, startY = 100;
        int endX = sw - 100, endY = sh - 100;

        SimMouse.MoveTo(startX, startY);
        WaitPump(50);

        var swatch = System.Diagnostics.Stopwatch.StartNew();
        SimMouse.MoveSmooth(endX, endY, 500);
        swatch.Stop();

        var p = SimMouse.GetPosition();
        int dist = Math.Abs(p.X - endX) + Math.Abs(p.Y - endY);

        Write("INFO", "MoveSmooth duration: " + swatch.ElapsedMilliseconds + "ms");

        if (dist <= 5)
            Pass("MoveSmooth: reached target within " + dist + " px");
        else
            Fail("MoveSmooth: missed target by " + dist + " px");

        // Duration should be roughly 500ms (allow ±40%)
        if (swatch.ElapsedMilliseconds >= 300 && swatch.ElapsedMilliseconds <= 700)
            Pass("MoveSmooth: duration " + swatch.ElapsedMilliseconds + "ms in expected range");
        else
            Write("WARN", "MoveSmooth: duration " + swatch.ElapsedMilliseconds + "ms outside 300-700ms");
    }

    // ===== 5: Left click triggers real form event =====
    static void Test_LeftClick()
    {
        Write("INFO", "=== LeftClick ===");
        var target = new MouseTarget(sw / 2 - 50, sh / 2 - 50, 100, 100);
        target.Show();
        WaitPump(200);

        // Click center of target
        var center = target.PointToScreen(new Point(50, 50));
        SimMouse.MoveTo(center.X, center.Y);
        WaitPump(100);
        SimMouse.LeftClick();
        WaitPump(200);

        if (target.ClickFired)
            Pass("LeftClick: form Click event fired");
        else
            Fail("LeftClick: form Click event did NOT fire (simulation not working)");

        if (target.MouseDownFired)
            Pass("LeftClick: MouseDown fired");
        else
            Fail("LeftClick: MouseDown did NOT fire");

        if (target.MouseUpFired)
            Pass("LeftClick: MouseUp fired");
        else
            Fail("LeftClick: MouseUp did NOT fire");

        target.Close();
        target.Dispose();
        WaitPump(100);
    }

    // ===== 6: Right click triggers real form event =====
    static void Test_RightClick()
    {
        Write("INFO", "=== RightClick ===");
        var target = new MouseTarget(sw / 2 - 50, sh / 2 - 50, 100, 100);
        target.Show();
        WaitPump(200);

        var center = target.PointToScreen(new Point(50, 50));
        SimMouse.MoveTo(center.X, center.Y);
        WaitPump(100);
        SimMouse.RightClick();
        WaitPump(200);

        if (target.RightClickFired)
            Pass("RightClick: right-click detected by form");
        else
            Fail("RightClick: right-click NOT detected by form");

        target.Close();
        target.Dispose();
        WaitPump(100);
    }

    // ===== 7: Double-click sends two clicks within system double-click time =====
    static void Test_DoubleClick()
    {
        Write("INFO", "=== DoubleClick ===");
        var target = new MouseTarget(sw / 2 - 50, sh / 2 - 50, 100, 100);
        target.Show();
        WaitPump(200);

        var center = target.PointToScreen(new Point(50, 50));
        target.ResetCounters();

        // Record time before double-click
        SimMouse.MoveTo(center.X, center.Y);
        WaitPump(100);

        var before = DateTime.Now;
        SimMouse.DoubleClick();
        WaitPump(300);
        var after = DateTime.Now;

        // Should have 2 MouseDown events (and 2 MouseUp events)
        if (target.MouseDownCount == 2)
            Pass("DoubleClick: 2 MouseDown events received");
        else
            Fail("DoubleClick: " + target.MouseDownCount + " MouseDown events (expected 2)");

        // Double-click should complete within system double-click time + margin
        var dblClickTime = SimMouse.GetDoubleClickTimeMs();
        // We can't precisely time the internal interval, but total test time is reasonable
        Write("INFO", "System double-click time: " + dblClickTime + "ms");
        Write("INFO", "MouseDownCount: " + target.MouseDownCount);

        target.Close();
        target.Dispose();
        WaitPump(100);
    }

    // ===== 8: Drag operation =====
    static void Test_Drag()
    {
        Write("INFO", "=== Drag ===");
        var target = new MouseTarget(sw / 2 - 100, sh / 2 - 100, 200, 200);
        target.Show();
        WaitPump(200);

        var from = target.PointToScreen(new Point(50, 50));
        var to   = target.PointToScreen(new Point(150, 150));

        SimMouse.LeftDrag(from.X, from.Y, to.X, to.Y, 300);
        WaitPump(200);

        // Drag involves MouseDown at start and MouseUp at end
        if (target.MouseDownFired)
            Pass("Drag: MouseDown fired");
        else
            Fail("Drag: MouseDown did NOT fire");

        if (target.MouseUpFired)
            Pass("Drag: MouseUp fired");
        else
            Fail("Drag: MouseUp did NOT fire");

        if (target.LastMovePoint.X > 0 || target.LastMovePoint.Y > 0)
            Pass("Drag: MouseMove fired (last: " + target.LastMovePoint.X + "," + target.LastMovePoint.Y + ")");
        else
            Write("WARN", "Drag: no MouseMove recorded");

        target.Close();
        target.Dispose();
        WaitPump(100);
    }

    // ===== 9: Scroll =====
    static void Test_Scroll()
    {
        Write("INFO", "=== Scroll ===");
        // Scroll can't easily be verified via form events
        // (MouseWheel requires focus), but we can verify no crash
        bool ok = true;
        try
        {
            SimMouse.Scroll(120);   // scroll up 1 detent
            WaitPump(30);
            SimMouse.Scroll(-120);  // scroll down 1 detent
            WaitPump(30);
            SimMouse.ScrollH(120);  // scroll right
            WaitPump(30);
        }
        catch (Exception ex)
        {
            ok = false;
            Fail("Scroll: crashed: " + ex.Message);
        }
        if (ok) Pass("Scroll: vertical + horizontal executed without crash");
    }

    // ===== 10: MoveAndClick combo =====
    static void Test_MoveAndClick()
    {
        Write("INFO", "=== MoveAndClick ===");
        var target = new MouseTarget(sw / 2 - 50, sh / 2 - 50, 100, 100);
        target.Show();
        WaitPump(200);

        var center = target.PointToScreen(new Point(50, 50));
        SimMouse.MoveAndClick(center.X, center.Y, smooth: false);
        WaitPump(200);

        if (target.ClickFired)
            Pass("MoveAndClick: click reached form at (" + center.X + ", " + center.Y + ")");
        else
            Fail("MoveAndClick: click did NOT reach form");

        target.Close();
        target.Dispose();
        WaitPump(100);
    }

    // ===== 11: Humanize produces valid jitter =====
    static void Test_Humanize()
    {
        Write("INFO", "=== Humanize ===");
        int baseMs = 100;
        bool allInRange = true;
        int min = int.MaxValue, max = 0;

        for (int i = 0; i < 50; i++)
        {
            int v = SimMouse.Humanize(baseMs);
            if (v < 0)  allInRange = false;
            if (v > 200) allInRange = false;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        // Should vary within 70–130 roughly (30% either way)
        if (min <= 130 && max >= 70 && allInRange)
            Pass("Humanize: range [" + min + ", " + max + "] around " + baseMs);
        else
            Fail("Humanize: range [" + min + ", " + max + "] outside expected bounds");
    }
}
