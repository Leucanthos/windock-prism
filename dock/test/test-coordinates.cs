using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

// ============================================================
// Test: Coordinate Verification
// Position icons, read back actual screen coordinates,
// verify alignment and spacing match expected values
// ============================================================

class TestCoordinates
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    struct RECT { public int Left, Top, Right, Bottom; }

    static string Log = @"C:\temp\_test_coordinates_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    const int gap = 14;

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestCoordinates @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();

        int sw = Screen.PrimaryScreen.WorkingArea.Width;
        int sh = Screen.PrimaryScreen.WorkingArea.Height;

        try
        {
            foreach (int cnt in new[] { 3, 6 })
            {
                TestAlignment(cnt, sw, sh);
            }

            TestLineAlignment(sw, sh);
            TestIndividualIconPosition(sw, sh);
        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        System.Threading.Thread.Sleep(100);
        Application.Exit();
    }

    // Get REAL screen coordinates via Win32 GetWindowRect
    static RECT GetScreenRect(Form f)
    {
        RECT r;
        // Force handle creation
        var h = f.Handle;
        GetWindowRect(h, out r);
        return r;
    }

    static void TestAlignment(int cnt, int sw, int sh)
    {
        var icons = new List<DockIcon>();
        for (int i = 0; i < cnt; i++)
        {
            var di = new DockIcon(44, 8);
            di.SetIcon(DockIcon.IconToBmpAtDpi(SystemIcons.Application));
            icons.Add(di);
        }

        // CRITICAL: Calculate fw from DPI, NOT from Form.Height (which is 300 before Show)
        int fw = (int)(44 * DockIcon.DpiX / 96f);
        int totalW = cnt * fw + (cnt - 1) * gap;
        int expectedStartX = (sw - totalW) / 2;
        int expectedIconY = sh - fw - 20;

        // Position icons using calculated values
        for (int i = 0; i < cnt; i++)
        {
            int x = expectedStartX + i * (fw + gap);
            icons[i].SetBasePos(x, expectedIconY);
        }

        // Show them briefly to get real coordinates
        foreach (var di in icons) di.Show();
        System.Threading.Thread.Sleep(300); // Let HandleCreated event fire

        Write("INFO", "--- Testing " + cnt + " icons ---");
        Write("INFO", "DPI=" + DockIcon.DpiX + " fw(calculated)=" + fw +
              " Form.Height(before Show) would be wrong, correct is " + fw);

        int dpi = DockIcon.DpiX;
        float scale = dpi / 96f;
        int logicalFw = (int)(44 * scale);

        // Read back real positions
        for (int i = 0; i < cnt; i++)
        {
            var r = GetScreenRect(icons[i].Form);
            int actualX = r.Left;
            int actualY = r.Top;
            int actualW = r.Right - r.Left;
            int actualH = r.Bottom - r.Top;

            int expectedX = expectedStartX + i * (fw + gap);

            Write("INFO", string.Format("  Icon[{0}]: pos=({1},{2}) size={3}x{4} expectedX={5} BaseX={6}",
                i, actualX, actualY, actualW, actualH, expectedX, icons[i].BaseX));

            // Check X position (within 2px tolerance for rounding)
            if (Math.Abs(actualX - expectedX) <= 2)
                Pass(string.Format("Icon[{0}] X={1} ≈ expected {2} (Δ={3})",
                    i, actualX, expectedX, Math.Abs(actualX - expectedX)));
            else
                Fail(string.Format("Icon[{0}] X={1} ≠ expected {2} (Δ={3})",
                    i, actualX, expectedX, Math.Abs(actualX - expectedX)));

            // Check Y position
            if (Math.Abs(actualY - expectedIconY) <= 2)
                Pass(string.Format("Icon[{0}] Y={1} ≈ expected {2}", i, actualY, expectedIconY));
            else
                Fail(string.Format("Icon[{0}] Y={1} ≠ expected {2} (Δ={3})",
                    i, actualY, expectedIconY, Math.Abs(actualY - expectedIconY)));

            // Check size (should match expected physical size)
            if (Math.Abs(actualW - logicalFw) <= 2 && Math.Abs(actualH - logicalFw) <= 2)
                Pass(string.Format("Icon[{0}] size={1}x{2} ≈ expected {3}x{3}",
                    i, actualW, actualH, logicalFw));
            else
                Fail(string.Format("Icon[{0}] size={1}x{2} ≠ expected {3}x{3}",
                    i, actualW, actualH, logicalFw));
        }

        // Check horizontal centering: first icon X == last icon X + totalW - fw
        int firstX = GetScreenRect(icons[0].Form).Left;
        int lastX = GetScreenRect(icons[cnt - 1].Form).Left;
        int actualSpan = lastX - firstX;
        int expectedSpan = (cnt - 1) * (fw + gap);
        if (Math.Abs(actualSpan - expectedSpan) <= 2)
            Pass("Span: first-to-last = " + actualSpan + " ≈ expected " + expectedSpan);
        else
            Fail("Span: first-to-last = " + actualSpan + " ≠ expected " + expectedSpan);

        // Check gap between adjacent icons
        for (int i = 0; i < cnt - 1; i++)
        {
            var r0 = GetScreenRect(icons[i].Form);
            var r1 = GetScreenRect(icons[i + 1].Form);
            int actualGap = r1.Left - r0.Right;
            if (Math.Abs(actualGap - gap) <= 2)
                Pass(string.Format("Gap[{0}→{1}] = {2} ≈ expected {3}", i, i + 1, actualGap, gap));
            else
                Fail(string.Format("Gap[{0}→{1}] = {2} ≠ expected {3}", i, i + 1, actualGap, gap));
        }

        // Cleanup
        foreach (var di in icons) di.Dispose();
        System.Threading.Thread.Sleep(100);
    }

    static void TestLineAlignment(int sw, int sh)
    {
        Write("INFO", "--- Testing Line-Icon Alignment ---");

        var icons = new List<DockIcon>();
        for (int i = 0; i < 5; i++)
        {
            var di = new DockIcon(44, 8);
            di.SetIcon(DockIcon.IconToBmpAtDpi(SystemIcons.Application));
            icons.Add(di);
        }

        int fw = (int)(44 * DockIcon.DpiX / 96f);
        int totalW = 5 * fw + 4 * gap;
        int startX = (sw - totalW) / 2;
        int iconY = sh - fw - 20;

        for (int i = 0; i < 5; i++)
            icons[i].SetBasePos(startX + i * (fw + gap), iconY);
        foreach (var di in icons) di.Show();
        System.Threading.Thread.Sleep(300);

        // Read real icon centers
        float[] centers = new float[5];
        for (int i = 0; i < 5; i++)
        {
            var r = GetScreenRect(icons[i].Form);
            centers[i] = r.Left + (r.Right - r.Left) / 2f;
            Write("INFO", string.Format("  Icon[{0}] center={1:F1}", i, centers[i]));
        }

        // First and last icon centers should equal the line's theoretical endpoints
        float expectedX0 = startX + fw / 2f;
        float expectedX1 = startX + 4 * (fw + gap) + fw / 2f;

        if (Math.Abs(centers[0] - expectedX0) <= 2)
            Pass("Line X0: icon[0] center=" + centers[0].ToString("F1") + " ≈ " + expectedX0.ToString("F1"));
        else
            Fail("Line X0: icon[0] center=" + centers[0].ToString("F1") + " ≠ " + expectedX0.ToString("F1"));

        if (Math.Abs(centers[4] - expectedX1) <= 2)
            Pass("Line X1: icon[4] center=" + centers[4].ToString("F1") + " ≈ " + expectedX1.ToString("F1"));
        else
            Fail("Line X1: icon[4] center=" + centers[4].ToString("F1") + " ≠ " + expectedX1.ToString("F1"));

        // Verify line form position matches icon centers
        int lineH = 10;
        int lineTop = iconY + fw / 2 - lineH / 2;

        Write("INFO", string.Format("  Line would be: Left={0} Top={1} Width={2} Height={3}", startX, lineTop, totalW, lineH));
        Write("INFO", string.Format("  Icon[0].center={0:F1} Icon[4].center={1:F1}", centers[0], centers[4]));

        foreach (var di in icons) di.Dispose();
        System.Threading.Thread.Sleep(100);
    }

    static void TestIndividualIconPosition(int sw, int sh)
    {
        Write("INFO", "--- Testing Single Icon Position ---");

        var di = new DockIcon(44, 8);
        di.SetIcon(DockIcon.IconToBmpAtDpi(SystemIcons.Application));

        int fw = (int)(44 * DockIcon.DpiX / 96f);
        int testX = sw / 2;
        int testY = sh - fw - 50;

        di.SetBasePos(testX, testY);
        di.Show();
        System.Threading.Thread.Sleep(300);

        var r = GetScreenRect(di.Form);
        int actualX = r.Left;
        int actualY = r.Top;
        int actualW = r.Right - r.Left;
        int actualH = r.Bottom - r.Top;

        Write("INFO", string.Format("  SetBasePos({0},{1}) → actual: pos=({2},{3}) size={4}x{5}",
            testX, testY, actualX, actualY, actualW, actualH));

        // SetBasePos centers the icon on BaseX
        // BaseX = x, baseY = y, curSize = baseSize
        // sx = curSize==baseSize ? x : x-(curSize-baseSize)/2
        // sy = curSize==baseSize ? y : y-(curSize-baseSize)
        // SetWindowPos(Form.Handle, 0, sx, sy, baseSize, baseSize, ...)

        // When curSize==baseSize: sx = x, sy = y
        // So actualX should ≈ testX and actualY should ≈ testY
        if (Math.Abs(actualX - testX) <= 2)
            Pass("Single icon: X=" + actualX + " ≈ SetBasePos X=" + testX);
        else
            Fail("Single icon: X=" + actualX + " ≠ SetBasePos X=" + testX);

        if (Math.Abs(actualY - testY) <= 2)
            Pass("Single icon: Y=" + actualY + " ≈ SetBasePos Y=" + testY);
        else
            Fail("Single icon: Y=" + actualY + " ≠ SetBasePos Y=" + testY);

        // Size should be fw × fw (square)
        if (actualW == actualH && Math.Abs(actualW - fw) <= 2)
            Pass("Single icon: square " + actualW + "x" + actualH + " ≈ DPI-scaled " + fw);
        else
            Fail("Single icon: " + actualW + "x" + actualH + " expected " + fw + "x" + fw + " (square)");

        di.Dispose();
    }
}
