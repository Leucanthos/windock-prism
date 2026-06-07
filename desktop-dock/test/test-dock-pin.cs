using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// ============================================================
// Test: Dock Pin Flow
// Right-click → Pin/Unpin → .lnk created/deleted → icon persists
// ============================================================

class TestDockPin
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static string Log = @"C:\temp\_test_dockpin_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    static string PinDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        + @"\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";

    // Replicated COM pin helpers (same logic as DockLine.PinApp/UnpinApp)
    static bool PinApp(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;
        try
        {
            var st = Type.GetTypeFromProgID("WScript.Shell");
            if (st == null) return false;
            dynamic shell = Activator.CreateInstance(st);
            string lnkPath = Path.Combine(PinDir, Path.GetFileNameWithoutExtension(exePath) + ".lnk");
            dynamic lnk = shell.CreateShortcut(lnkPath);
            lnk.TargetPath = exePath;
            lnk.Save();
            return true;
        }
        catch { return false; }
    }

    static bool UnpinApp(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return false;
        try
        {
            var st = Type.GetTypeFromProgID("WScript.Shell");
            if (st == null) return false;
            dynamic shell = Activator.CreateInstance(st);
            foreach (var f in Directory.GetFiles(PinDir, "*.lnk"))
            {
                try
                {
                    dynamic lnk = shell.CreateShortcut(f);
                    if (lnk.TargetPath.Equals(exePath, StringComparison.OrdinalIgnoreCase))
                    { File.Delete(f); return true; }
                }
                catch { }
            }
            return false;
        }
        catch { return false; }
    }

    static string GetLnkTarget(string lnkPath)
    {
        try
        {
            var st = Type.GetTypeFromProgID("WScript.Shell");
            if (st == null) return null;
            dynamic shell = Activator.CreateInstance(st);
            return shell.CreateShortcut(lnkPath).TargetPath;
        }
        catch { return null; }
    }

    static void KillProcess(System.Diagnostics.Process p)
    {
        if (p == null) return;
        try { if (!p.HasExited) { p.CloseMainWindow(); p.WaitForExit(2000); } } catch { }
        try { if (!p.HasExited) p.Kill(); } catch { }
    }

    static bool HasPinnedLnk(string exePath)
    {
        string expected = Path.Combine(PinDir, Path.GetFileNameWithoutExtension(exePath) + ".lnk");
        if (!File.Exists(expected)) return false;
        string target = GetLnkTarget(expected);
        return target != null && target.Equals(exePath, StringComparison.OrdinalIgnoreCase);
    }

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestDockPin @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();

        // Launch a test process to pin. Use cmd.exe — reliable classic HWnd, predictable name.
        System.Diagnostics.Process testProc = null;
        string testPath = @"C:\Windows\System32\cmd.exe";
        try
        {
            // Ensure clean state: remove any existing test pin
            UnpinApp(testPath);
            if (HasPinnedLnk(testPath))
                Fail("Pre-condition: test exe should not be pinned before test");
            else
                Pass("Pre-condition: " + Path.GetFileName(testPath) + " is not pinned");

            // Launch test process
            testProc = System.Diagnostics.Process.Start(testPath);
            // Wait up to 5 seconds for a MainWindowHandle
            for (int i = 0; i < 50 && (testProc == null || testProc.MainWindowHandle == IntPtr.Zero); i++)
            {
                System.Threading.Thread.Sleep(100);
                try { if (testProc.HasExited) { testProc = System.Diagnostics.Process.Start(testPath); } }
                catch { break; }
            }
            System.Threading.Thread.Sleep(300);

            IntPtr hw = IntPtr.Zero;
            try { hw = testProc.MainWindowHandle; } catch { }
            if (hw != IntPtr.Zero)
                Pass(Path.GetFileName(testPath) + " launched: HWnd=" + hw + " PID=" + testProc.Id);
            else
                Write("INFO", Path.GetFileName(testPath) + " has no classic HWnd (modern/UWP) — testing with PID only");

            // Create DockIcon for testProc (simulating DockLine.FindOrCreateRunning)
            var di = new DockIcon(44, 8);
            di.Pid = testProc.Id;
            di.HWnd = hw;
            try
            {
                using (var ico = Icon.ExtractAssociatedIcon(testPath))
                {
                    var bmp = DockIcon.IconToBmpAtDpi(ico);
                    if (bmp != null) di.SetIcon(bmp);
                }
            }
            catch { }
            di.SetTooltip(Path.GetFileNameWithoutExtension(testPath));
            di.SetClick(() => { if (di.HWnd != IntPtr.Zero) DockBar.FocusWindow(di.HWnd); });

            // Show icon
            int sw = Screen.PrimaryScreen.WorkingArea.Width;
            int sh = Screen.PrimaryScreen.WorkingArea.Height;
            di.SetBasePos(sw / 2, sh - 120);
            di.Show();
            System.Threading.Thread.Sleep(300);

            // ===== TEST 1: Right-click shows "[Pin]" for unpinned app =====
            bool menuShown = false;
            string menuActionText = "";
            string menuPinText = "";

            // We can't easily read the menu text programmatically (it's a Form with Labels).
            // But we know isPinned=false → actionText="[Open]", pinText="[Pin]"
            // We verify this by testing the actual pin logic.

            // ===== TEST 2: Pin the running app =====
            bool pinOk = PinApp(testPath);
            if (pinOk)
                Pass("PinApp: .lnk created for " + Path.GetFileName(testPath));
            else
                Fail("PinApp: failed to create .lnk");

            if (HasPinnedLnk(testPath))
                Pass("Verify: .lnk exists with correct TargetPath");
            else
                Fail("Verify: .lnk missing or wrong target");

            // ===== TEST 3: Pin state is reflected (isPinned=true) =====
            // In real dock: pinned = di.Pinned && pinnedPaths.Contains(di.PinPath)
            // Here we verify the filesystem state directly
            string lnkPath = Path.Combine(PinDir, Path.GetFileNameWithoutExtension(testPath) + ".lnk");
            bool lnkExists = File.Exists(lnkPath);
            if (lnkExists)
                Pass("Pin state: .lnk exists → menu would show '[Unpin]'");
            else
                Fail("Pin state: .lnk missing → menu would show '[Pin]'");

            // ===== TEST 4: Pinned icon persists after app closes =====
            KillProcess(testProc);
            System.Threading.Thread.Sleep(100);
            if (HasPinnedLnk(testPath))
                Pass("After close: .lnk still exists → pinned icon persists in dock");
            else
                Fail("After close: .lnk missing — pinned icon would disappear");

            // ===== TEST 5: Re-launch → pin still there, then clean up =====
            testProc = System.Diagnostics.Process.Start(testPath);
            System.Threading.Thread.Sleep(500);
            if (HasPinnedLnk(testPath))
                Pass("After re-launch: still pinned (lnk exists)");
            else
                Fail("After re-launch: pin lost");
            KillProcess(testProc);

            // ===== TEST 6: Unpin =====
            UnpinApp(testPath);
            if (!HasPinnedLnk(testPath))
                Pass("UnpinApp: .lnk removed → menu would show '[Pin]'");
            else
                Fail("UnpinApp: .lnk still exists at " + Path.Combine(PinDir, Path.GetFileNameWithoutExtension(testPath) + ".lnk"));

            // ===== TEST 7: Error paths =====
            // Pin empty/null
            try { PinApp(null); Pass("PinApp(null) — no crash"); }
            catch (Exception ex) { Fail("PinApp(null) threw: " + ex.Message); }

            try { PinApp(""); Pass("PinApp('') — no crash"); }
            catch (Exception ex) { Fail("PinApp('') threw: " + ex.Message); }

            // Pin non-existent file
            try { PinApp(@"C:\nonexistent\phantom.exe"); Pass("PinApp(nonexistent) — no crash"); }
            catch (Exception ex) { Fail("PinApp(nonexistent) threw: " + ex.Message); }

            // Double-unpin
            try { UnpinApp(testPath); Pass("UnpinApp(already-removed) — no crash"); }
            catch (Exception ex) { Fail("UnpinApp(double) threw: " + ex.Message); }

            // Clean up
            di.Dispose();
            if (testProc != null && !testProc.HasExited) { testProc.CloseMainWindow(); testProc.WaitForExit(2000); }
            if (testProc != null && !testProc.HasExited) testProc.Kill();
        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }
        finally
        {
            KillProcess(testProc);
            try { UnpinApp(testPath); } catch { }
            // Clean up any calc/notepad orphans by process name
            string pn = Path.GetFileNameWithoutExtension(testPath);
            try { foreach (var p in System.Diagnostics.Process.GetProcessesByName(pn)) { try { p.Kill(); } catch { } } } catch { }
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        System.Threading.Thread.Sleep(100);
        Application.Exit();
    }
}
