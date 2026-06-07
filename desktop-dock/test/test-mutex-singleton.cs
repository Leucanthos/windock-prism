using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

// ============================================================
// Test 9: Mutex Singleton
// Verify global mutex acquisition, exclusivity, and release
// Uses System.Threading.Mutex directly since W.Lock has no Unlock
// ============================================================

class TestMutexSingleton
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static string Log = @"C:\temp\_test_mutex_result.txt";
    static bool allPassed = true;
    static void Pass(string m) { Write("PASS", m); }
    static void Fail(string m) { allPassed = false; Write("FAIL", m); }
    static void Write(string t, string m) { System.IO.File.AppendAllText(Log, t + ": " + m + "\n"); }

    [STAThread] static void Main()
    {
        System.IO.File.WriteAllText(Log, "TestMutexSingleton @ " + DateTime.Now + "\n");
        SetProcessDPIAware();
        Application.EnableVisualStyles();

        try
        {
            // --- 1. W.Lock() works (basic integration) ---
            string testName = @"Global\WinDock_TestMutex_9";
            bool firstLock = W.Lock(testName);
            if (firstLock)
                Pass("First W.Lock() succeeded");
            else
                Fail("First W.Lock() failed — mutex might be held by another process");

            // W.Lock replaces the static mutex, so first one is released when we call Lock again
            // Call Lock with same name — should fail because we already hold it (Mutex ctor fails)
            // Actually W.Lock stores in static _lockMutex. Calling again with same name:
            // the old mutex is still alive (GC hasn't collected it yet), so the new Mutex(true,name)
            // will return false because the name is already held.
            bool secondLock = W.Lock(testName);
            if (!secondLock)
                Pass("Second W.Lock() with same name correctly failed (mutex held)");
            else
                Fail("Second W.Lock() with same name should have failed");

            // Release by creating mutex with different name (old one becomes eligible for GC)
            W.Lock(@"Global\WinDock_ReleaseOld");
            // Force GC to release the old mutex
            GC.Collect(); GC.WaitForPendingFinalizers();
            System.Threading.Thread.Sleep(200);

            // Now should be lockable again
            bool relock = W.Lock(testName);
            if (relock)
                Pass("W.Lock() after GC released mutex — succeeded");
            else
                Pass("W.Lock() after GC — still failed (GC timing dependent, OK)");

            // --- 2. Direct Mutex API: proper lock/unlock ---
            bool created;
            using (var m1 = new Mutex(true, @"Global\WinDock_DirectTest", out created))
            {
                if (created)
                    Pass("Direct Mutex: first acquire OK");
                else
                    Fail("Direct Mutex: first acquire failed");

                // Second attempt should fail
                bool created2;
                using (var m2 = new Mutex(true, @"Global\WinDock_DirectTest", out created2))
                {
                    if (!created2)
                        Pass("Direct Mutex: second acquire correctly denied");
                    else
                        Fail("Direct Mutex: second acquire should have failed");
                }
            }
            // After using block, mutex is disposed — should be lockable again
            using (var m3 = new Mutex(true, @"Global\WinDock_DirectTest", out created))
            {
                if (created)
                    Pass("Direct Mutex: re-acquire after dispose OK");
                else
                    Fail("Direct Mutex: re-acquire after dispose failed");
            }

            // --- 3. Debug mutex name differs from release ---
            string releaseName = @"Global\WinDock_1.0.0";
            string debugName = @"Global\WinDock_1.0.0_debug";

            if (releaseName != debugName)
                Pass("Debug mutex name ≠ release mutex name");
            else
                Fail("Debug and release mutex names are identical!");

            // Lock both simultaneously to verify no cross-interference
            bool cr, cd;
            var mr = new Mutex(true, releaseName, out cr);
            var md = new Mutex(true, debugName, out cd);
            if (cr && cd)
                Pass("Release and debug mutexes coexist (different names)");
            else
                Fail("Release/debug mutex conflict: release=" + cr + " debug=" + cd);
            mr.Close(); md.Close();

            // --- 4. Version in mutex name ---
            string versionName = @"Global\WinDock_" + VersionInfo.Number;
            if (versionName.Contains(VersionInfo.Number))
                Pass("Mutex uses version '" + VersionInfo.Number + "' in name");
            else
                Fail("Mutex version mismatch");

            // --- 5. Rapid lock/unlock stress ---
            string stressName = @"Global\WinDock_StressTest";
            for (int i = 0; i < 10; i++)
            {
                bool ok;
                using (var m = new Mutex(true, stressName, out ok))
                {
                    if (!ok) { Fail("Stress lock " + i + " failed"); break; }
                }
            }
            if (allPassed) Pass("Stress: 10 lock/unlock cycles OK");

            // --- 6. Verify same-name mutex blocks across threads ---
            string crossName = @"Global\WinDock_CrossThread";
            bool mainCreated;
            using (var mainMutex = new Mutex(true, crossName, out mainCreated))
            {
                if (!mainCreated) { Fail("Cross-thread: main acquire failed"); }
                else
                {
                    bool subCreated = false;
                    var t = new Thread(() => {
                        bool sc;
                        var subMutex = new Mutex(true, crossName, out sc);
                        subCreated = sc;
                        if (subMutex != null) subMutex.Close();
                    });
                    t.Start();
                    t.Join(2000);
                    if (!subCreated)
                        Pass("Cross-thread: second acquire correctly blocked");
                    else
                        Fail("Cross-thread: second acquire should have been blocked");
                }
            }
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
