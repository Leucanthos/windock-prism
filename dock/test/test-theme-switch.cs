using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

// ============================================================
// Test 5: Theme Switch
// Verify dark/light theme detection, GlassBmp regeneration, icon update
// ============================================================

class TestThemeSwitch
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static string Log = @"C:\temp\_test_theme_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestThemeSwitch @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();

        // Save original theme state
        bool originalIsLight;
        try
        {
            originalIsLight = (int)(Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0) ?? 0) == 1;
            Pass("Registry read: SystemUsesLightTheme=" + originalIsLight);
        }
        catch (Exception ex)
        {
            Fail("Cannot read registry: " + ex.Message);
            Write("RESULT", "FAIL");
            return;
        }

        // Get private Apply() and CreateGlass() methods
        // Init() always reads registry, so to test both themes we call Apply+CreateGlass directly
        var applyMethod = typeof(Theme).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
        var createGlassMethod = typeof(Theme).GetMethod("CreateGlass", BindingFlags.NonPublic | BindingFlags.Static);

        try
        {
            // --- 1. Test Light Theme ---
            Theme.IsLight = true;
            applyMethod.Invoke(null, null);
            createGlassMethod.Invoke(null, null);
            Color lightBg = Theme.FormBg;
            Pass("Light FormBg=(" + lightBg.R + "," + lightBg.G + "," + lightBg.B + ")");

            // Check light theme colors are warm/beige
            bool lightWarm = lightBg.R > 200 && lightBg.G > 200 && lightBg.B > 180;
            if (lightWarm)
                Pass("Light theme: warm/beige tones (R>200,G>200,B>180)");
            else
                Fail("Light theme: RGB=(" + lightBg.R + "," + lightBg.G + "," + lightBg.B + ") expected warm tones");

            Bitmap lightBmp = Theme.GlassBmp;
            if (lightBmp != null)
                Pass("Light GlassBmp: " + lightBmp.Width + "x" + lightBmp.Height);
            else
                Fail("Light GlassBmp: null");

            // --- 2. Test Dark Theme ---
            Theme.IsLight = false;
            applyMethod.Invoke(null, null);
            createGlassMethod.Invoke(null, null);
            Color darkBg = Theme.FormBg;
            Pass("Dark FormBg=(" + darkBg.R + "," + darkBg.G + "," + darkBg.B + ")");

            // Check dark theme colors are cool/navy
            bool darkCool = darkBg.R < 50 && darkBg.G < 50 && darkBg.B < 80;
            if (darkCool)
                Pass("Dark theme: cool/navy tones (R<50,G<50,B<80)");
            else
                Fail("Dark theme: RGB=(" + darkBg.R + "," + darkBg.G + "," + darkBg.B + ") expected cool tones");

            Bitmap darkBmp = Theme.GlassBmp;
            if (darkBmp != null)
                Pass("Dark GlassBmp: " + darkBmp.Width + "x" + darkBmp.Height);
            else
                Fail("Dark GlassBmp: null");

            // --- 3. Different bitmaps ---
            bool different = !object.ReferenceEquals(lightBmp, darkBmp);
            if (different)
                Pass("GlassBmp: different objects for light vs dark");
            else
                Fail("GlassBmp: same object reference for light and dark");

            // Sample center pixel to confirm visual difference
            if (lightBmp != null && darkBmp != null)
            {
                int cx = lightBmp.Width / 2, cy = lightBmp.Height / 2;
                Color lp = lightBmp.GetPixel(cx, cy);
                Color dp = darkBmp.GetPixel(cx, cy);
                int diff = Math.Abs(lp.R - dp.R) + Math.Abs(lp.G - dp.G) + Math.Abs(lp.B - dp.B);
                if (diff > 20)
                    Pass("GlassBmp center pixel differs by " + diff + " (visually distinct)");
                else
                    Fail("GlassBmp center pixel only differs by " + diff + " (should be visually distinct)");
            }

            // --- 4. DockIcon theme update ---
            Theme.IsLight = false;
            applyMethod.Invoke(null, null);
            createGlassMethod.Invoke(null, null);
            var icon = new DockIcon(44, 8);
            icon.SetIcon(DockIcon.IconToBmpAtDpi(SystemIcons.Application));
            Bitmap beforeUpdate = (Bitmap)icon.Form.BackgroundImage;

            Theme.IsLight = true;
            applyMethod.Invoke(null, null);
            createGlassMethod.Invoke(null, null);
            icon.UpdateTheme();
            Bitmap afterUpdate = (Bitmap)icon.Form.BackgroundImage;

            if (!object.ReferenceEquals(beforeUpdate, afterUpdate))
                Pass("DockIcon.UpdateTheme: BackgroundImage updated to new GlassBmp");
            else
                Fail("DockIcon.UpdateTheme: BackgroundImage NOT updated");

            icon.Dispose();

            // --- 5. Theme.IsLight toggles correctly ---
            Theme.IsLight = true;
            if (Theme.IsLight == true)
                Pass("Theme.IsLight = true works");
            else
                Fail("Theme.IsLight = true failed");

            Theme.IsLight = false;
            if (Theme.IsLight == false)
                Pass("Theme.IsLight = false works");
            else
                Fail("Theme.IsLight = false failed");

        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }
        finally
        {
            // Restore original theme
            Theme.IsLight = originalIsLight;
            Theme.Init();
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        System.Threading.Thread.Sleep(100);
        Application.Exit();
    }
}
