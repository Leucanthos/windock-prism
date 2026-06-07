using System;
using System.IO;

// ============================================================
// Test 4: Pin / Unpin
// Verify COM WScript.Shell .lnk creation/deletion lifecycle
// Tests the same COM logic used by DockLine.PinApp/UnpinApp
// ============================================================

class TestPinUnpin
{
    static string Log = @"C:\temp\_test_pinunpin_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    static string PinDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        + @"\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";
    static string TempExe = @"C:\temp\_test_pin_dummy.exe";

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestPinUnpin @ " + DateTime.Now + "\n");

        try
        {
            // Ensure temp exe exists (create a 0-byte file as placeholder)
            Directory.CreateDirectory(@"C:\temp");
            File.WriteAllText(TempExe, "dummy"); // not a real exe, but path exists for COM
            Pass("Temp file created: " + TempExe);

            // Ensure pin dir exists
            if (!Directory.Exists(PinDir))
            {
                Fail("Pin dir doesn't exist: " + PinDir);
                Write("RESULT", "FAIL");
                return;
            }
            Pass("Pin dir exists: " + PinDir);

            // --- 1. Pin the temp exe ---
            string lnkPath = Path.Combine(PinDir, Path.GetFileNameWithoutExtension(TempExe) + ".lnk");

            // Clean up any previous test lnk
            if (File.Exists(lnkPath)) { File.Delete(lnkPath); Pass("Pre-cleaned old .lnk"); }

            bool pinOk = PinApp(TempExe);
            if (pinOk && File.Exists(lnkPath))
                Pass("PinApp: .lnk created at " + lnkPath);
            else
                Fail("PinApp: .lnk NOT created at " + lnkPath);

            // Verify .lnk TargetPath
            if (File.Exists(lnkPath))
            {
                string target = GetLnkTarget(lnkPath);
                if (target.Equals(TempExe, StringComparison.OrdinalIgnoreCase))
                    Pass("PinApp: TargetPath matches: " + target);
                else
                    Fail("PinApp: TargetPath=" + target + " expected " + TempExe);
            }

            // --- 2. Verify LoadPinnedApps picks it up ---
            var pinned = LoadPinnedApps();
            bool found = false;
            foreach (var p in pinned) { if (p.Equals(TempExe, StringComparison.OrdinalIgnoreCase)) { found = true; break; } }
            if (found)
                Pass("LoadPinnedApps: temp exe found in pinned set");
            else
                Fail("LoadPinnedApps: temp exe NOT found in pinned set");

            // --- 3. Unpin ---
            bool unpinOk = UnpinApp(TempExe);
            if (unpinOk && !File.Exists(lnkPath))
                Pass("UnpinApp: .lnk removed");
            else
                Fail("UnpinApp: .lnk still exists at " + lnkPath);

            // Verify removed from set
            pinned = LoadPinnedApps();
            found = false;
            foreach (var p in pinned) { if (p.Equals(TempExe, StringComparison.OrdinalIgnoreCase)) { found = true; break; } }
            if (!found)
                Pass("LoadPinnedApps: temp exe gone after unpin");
            else
                Fail("LoadPinnedApps: temp exe still present after unpin");

            // --- 4. Error paths ---
            // Pin non-existent file — should not throw
            try
            {
                PinApp(@"C:\nonexistent\file.exe");
                Pass("ErrorPath: PinApp non-existent — no crash");
            }
            catch (Exception ex) { Fail("ErrorPath: PinApp non-existent threw: " + ex.Message); }

            // Unpin non-pinned — should not throw
            try
            {
                UnpinApp(@"C:\nonexistent\other.exe");
                Pass("ErrorPath: UnpinApp non-pinned — no crash");
            }
            catch (Exception ex) { Fail("ErrorPath: UnpinApp non-pinned threw: " + ex.Message); }

            // Pin null/empty — should not throw
            try
            {
                PinApp(null);
                PinApp("");
                Pass("ErrorPath: PinApp null/empty — no crash");
            }
            catch (Exception ex) { Fail("ErrorPath: PinApp null/empty threw: " + ex.Message); }

        }
        catch (Exception ex)
        {
            Fail("UNHANDLED: " + ex.ToString());
        }
        finally
        {
            // Cleanup
            try
            {
                string lnkPath = Path.Combine(PinDir, Path.GetFileNameWithoutExtension(TempExe) + ".lnk");
                if (File.Exists(lnkPath)) File.Delete(lnkPath);
                if (File.Exists(TempExe)) File.Delete(TempExe);
            }
            catch { }
        }

        Write("RESULT", allPassed ? "PASS" : "FAIL");
        System.Threading.Thread.Sleep(100);
    }

    // Replicated from DockLine
    static bool PinApp(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;
            dynamic shell = Activator.CreateInstance(shellType);
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
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;
            dynamic shell = Activator.CreateInstance(shellType);
            foreach (var f in Directory.GetFiles(PinDir, "*.lnk"))
            {
                try
                {
                    dynamic lnk = shell.CreateShortcut(f);
                    if (lnk.TargetPath.Equals(exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(f);
                        return true;
                    }
                }
                catch { }
            }
            return false; // not found
        }
        catch { return false; }
    }

    static string[] LoadPinnedApps()
    {
        var paths = new System.Collections.Generic.List<string>();
        if (!Directory.Exists(PinDir)) return paths.ToArray();
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return paths.ToArray();
            dynamic shell = Activator.CreateInstance(shellType);
            foreach (var f in Directory.GetFiles(PinDir, "*.lnk"))
            {
                try
                {
                    dynamic lnk = shell.CreateShortcut(f);
                    string target = lnk.TargetPath;
                    if (!string.IsNullOrEmpty(target) && File.Exists(target))
                        paths.Add(target);
                }
                catch { }
            }
        }
        catch { }
        return paths.ToArray();
    }

    static string GetLnkTarget(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic lnk = shell.CreateShortcut(lnkPath);
            return lnk.TargetPath;
        }
        catch { return null; }
    }
}
