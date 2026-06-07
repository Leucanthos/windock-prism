using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// ============================================================
// Test 3: Magnification Animation
// Verify 60fps zoom lerp, targetScale transitions, bottom-edge anchoring
// Uses reflection to read private fields of DockIcon
// ============================================================

class TestMagnification
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static string Log = @"C:\temp\_test_magnification_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    static FieldInfo f_targetScale, f_curScale, f_curSize, f_baseSize, f_baseY;
    static FieldInfo f_badgeCount;

    // Message pump helper — System.Windows.Forms.Timer needs a running message loop
    static void WaitWithPump(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestMagnification @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();

        // Cache reflection fields
        var t = typeof(DockIcon);
        f_targetScale = t.GetField("targetScale", BindingFlags.NonPublic | BindingFlags.Instance);
        f_curScale    = t.GetField("curScale",    BindingFlags.NonPublic | BindingFlags.Instance);
        f_curSize     = t.GetField("curSize",     BindingFlags.NonPublic | BindingFlags.Instance);
        f_baseSize    = t.GetField("baseSize",    BindingFlags.NonPublic | BindingFlags.Instance);
        f_baseY       = t.GetField("baseY",       BindingFlags.NonPublic | BindingFlags.Instance);
        f_badgeCount  = t.GetField("badgeCount",  BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            TestZoomUp();
            TestZoomDown();
            TestBottomEdgeAnchoring();
            TestScaleReset();
        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        WaitWithPump(100);
        Application.Exit();
    }

    static float GetCurScale(DockIcon di)  { return (float)f_curScale.GetValue(di); }
    static float GetTargetScale(DockIcon di) { return (float)f_targetScale.GetValue(di); }
    static int GetCurSize(DockIcon di)     { return (int)f_curSize.GetValue(di); }
    static int GetBaseSize(DockIcon di)    { return (int)f_baseSize.GetValue(di); }
    static int GetBaseY(DockIcon di)       { return (int)f_baseY.GetValue(di); }
    static void SetTarget(DockIcon di, float v) { f_targetScale.SetValue(di, v); }
    static void SetCurScale(DockIcon di, float v) { f_curScale.SetValue(di, v); }

    static DockIcon MakeIcon()
    {
        var di = new DockIcon(44, 8);
        var bmp = DockIcon.IconToBmpAtDpi(SystemIcons.Application);
        di.SetIcon(bmp);
        return di;
    }

    static void TestZoomUp()
    {
        var di = MakeIcon();
        int sw = Screen.PrimaryScreen.WorkingArea.Width;
        int sh = Screen.PrimaryScreen.WorkingArea.Height;
        di.SetBasePos(sw / 2, sh - 120);
        di.Show(); // starts magTimer (16ms interval)

        float startScale = GetCurScale(di);
        if (Math.Abs(startScale - 1.0f) < 0.01f)
            Pass("ZoomUp: initial curScale = 1.0");
        else
            Fail("ZoomUp: initial curScale = " + startScale + " expected 1.0");

        // Simulate MouseEnter: set targetScale to 1.35
        SetTarget(di, 1.35f);
        // Let animation run for ~500ms (30+ ticks at 16ms)
        WaitWithPump(600);

        float endScale = GetCurScale(di);
        if (Math.Abs(endScale - 1.35f) < 0.01f)
            Pass("ZoomUp: converged to 1.35 (actual=" + endScale.ToString("F3") + ")");
        else
            Fail("ZoomUp: curScale=" + endScale.ToString("F3") + " expected ~1.35 after 600ms");

        di.Dispose();
    }

    static void TestZoomDown()
    {
        var di = MakeIcon();
        int sw = Screen.PrimaryScreen.WorkingArea.Width;
        int sh = Screen.PrimaryScreen.WorkingArea.Height;
        di.SetBasePos(sw / 2, sh - 120);
        di.Show();

        // Start from zoomed-in state
        SetTarget(di, 1.35f);
        SetCurScale(di, 1.35f);
        WaitWithPump(50);

        // Simulate MouseLeave: set targetScale back to 1.0
        SetTarget(di, 1.0f);
        WaitWithPump(600);

        float endScale = GetCurScale(di);
        if (Math.Abs(endScale - 1.0f) < 0.01f)
            Pass("ZoomDown: returned to 1.0 (actual=" + endScale.ToString("F3") + ")");
        else
            Fail("ZoomDown: curScale=" + endScale.ToString("F3") + " expected ~1.0 after 600ms");

        int curSize = GetCurSize(di);
        int baseSize = GetBaseSize(di);
        if (curSize == baseSize)
            Pass("ZoomDown: curSize(" + curSize + ") == baseSize(" + baseSize + ")");
        else
            Fail("ZoomDown: curSize(" + curSize + ") != baseSize(" + baseSize + ")");

        di.Dispose();
    }

    static void TestBottomEdgeAnchoring()
    {
        var di = MakeIcon();
        int sw = Screen.PrimaryScreen.WorkingArea.Width;
        int sh = Screen.PrimaryScreen.WorkingArea.Height;
        int baseX = sw / 2;
        int baseYVal = sh - 120;
        di.SetBasePos(baseX, baseYVal);
        di.Show();

        int baseSize = GetBaseSize(di);
        int initialBottom = di.Form.Top + di.Form.Height;
        int expectedBottom = baseYVal + baseSize; // SetBasePos sets Form.Top = baseY

        if (Math.Abs(initialBottom - expectedBottom) <= 2)
            Pass("BottomEdge: initial Form bottom at baseY+baseSize=" + expectedBottom + " actual=" + initialBottom);
        else
            Fail("BottomEdge: initial bottom=" + initialBottom + " expected " + expectedBottom);

        // Zoom in — bottom edge should stay fixed (expand upward)
        SetTarget(di, 1.35f);
        WaitWithPump(600);

        int zoomedBottom = di.Form.Top + di.Form.Height;
        if (Math.Abs(zoomedBottom - expectedBottom) <= 3)
            Pass("BottomEdge: after zoom, bottom=" + zoomedBottom + " ≈ initial=" + expectedBottom + " (expanded upward)");
        else
            Fail("BottomEdge: after zoom, bottom=" + zoomedBottom + " drifted from " + expectedBottom);

        di.Dispose();
    }

    static void TestScaleReset()
    {
        var di = MakeIcon();
        int sw = Screen.PrimaryScreen.WorkingArea.Width;
        int sh = Screen.PrimaryScreen.WorkingArea.Height;
        di.SetBasePos(sw / 2, sh - 120);
        di.Show();

        // Zoom in
        SetTarget(di, 1.35f);
        WaitWithPump(500);
        float zoomed = GetCurScale(di);

        // Call ResetScale
        di.ResetScale();
        float reset = GetCurScale(di);
        int resetSize = GetCurSize(di);
        int baseSz = GetBaseSize(di);

        if (Math.Abs(reset - 1.0f) < 0.01f)
            Pass("ResetScale: curScale = 1.0");
        else
            Fail("ResetScale: curScale = " + reset);

        if (resetSize == baseSz)
            Pass("ResetScale: curSize(" + resetSize + ") == baseSize(" + baseSz + ")");
        else
            Fail("ResetScale: curSize=" + resetSize + " != baseSize=" + baseSz);

        di.Dispose();
    }
}
