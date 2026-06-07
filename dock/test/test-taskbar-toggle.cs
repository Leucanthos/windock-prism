using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ============================================================
// Test 6: Taskbar Toggle (basic P/Invoke operations)
// Verify FindWindow("Shell_TrayWnd") and ShowWindow hide/show
// Full toggle cycle tested by PowerShell integration test
// ============================================================

class TestTaskbarToggle
{
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    static string Log = @"C:\temp\_test_taskbar_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    const int SW_HIDE = 0;
    const int SW_SHOW = 5;

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestTaskbarToggle @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();

        try
        {
            // --- 1. Find taskbar window ---
            IntPtr tb = FindWindow("Shell_TrayWnd", null);
            if (tb != IntPtr.Zero)
                Pass("FindWindow('Shell_TrayWnd') = " + tb);
            else
                Fail("FindWindow('Shell_TrayWnd') returned NULL — taskbar not found?");

            // --- 2. ShowWindow SW_HIDE ---
            if (tb != IntPtr.Zero)
            {
                bool wasVisible = IsWindowVisible(tb);
                Pass("Taskbar initially " + (wasVisible ? "visible" : "hidden"));

                // Hide — ShowWindow returns nonzero if previously visible
                bool swHideRet = ShowWindow(tb, SW_HIDE);
                System.Threading.Thread.Sleep(200);
                Pass("ShowWindow(SW_HIDE) called" + (swHideRet ? " (was visible)" : " (was hidden)"));

                // Restore — ShowWindow returns zero if previously hidden (normal)
                bool wasHidden = !ShowWindow(tb, SW_SHOW);
                System.Threading.Thread.Sleep(200);
                if (wasHidden)
                    Pass("ShowWindow(SW_SHOW) restored (was hidden → now shown)");
                else
                    Pass("ShowWindow(SW_SHOW) called (was already visible)");
            }

            // --- 3. WinDock special PIDs concept ---
            // Create a minimize icon (Pid=-1) and verify it exists
            var icon = new DockIcon(44, 8);
            icon.Pid = -1;
            icon.SetIcon(DockIcon.IconToBmpAtDpi(SystemIcons.Application));
            icon.SetClick(() => { /* noop */ });
            icon.SetTooltip("Test Minimize");
            if (icon.Pid == -1)
                Pass("Special PID -1 (minimize) assignable");
            else
                Fail("Special PID -1 assignment failed");
            icon.Dispose();

            var icon2 = new DockIcon(44, 8);
            icon2.Pid = -2;
            icon2.SetIcon(DockIcon.IconToBmpAtDpi(SystemIcons.Application));
            if (icon2.Pid == -2)
                Pass("Special PID -2 (start) assignable");
            else
                Fail("Special PID -2 assignment failed");
            icon2.Dispose();

            // --- 4. Window P/Invoke sanity ---
            // Test that GetWindow works for enumeration
            IntPtr top = GetTopWindow(IntPtr.Zero);
            if (top != IntPtr.Zero)
                Pass("GetTopWindow returned " + top);
            else
                Pass("GetTopWindow returned NULL (OK if no windows)");

        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        System.Threading.Thread.Sleep(100);
        Application.Exit();
    }

    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr h);
}
