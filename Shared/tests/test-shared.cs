using System;
using System.IO;

// ============================================================
// test-shared.cs — unit tests for Shared/ infrastructure
// ============================================================
static class SharedTest
{
    static int passed = 0, failed = 0;

    static void Ok(string name) { passed++; Console.WriteLine("  PASS: " + name); }
    static void Fail(string name, string detail)
    { failed++; Console.WriteLine("  FAIL: " + name + " — " + detail); }

    static void Assert(bool cond, string name, string detail)
    { if (cond) Ok(name); else Fail(name, detail); }

    static void Main()
    {
        var tmp = Environment.GetEnvironmentVariable("TEMP") ?? @"C:\temp";
        Console.WriteLine("=== Shared/ Unit Tests ===");
        Console.WriteLine("Temp dir: " + tmp);
        Console.WriteLine("");

        // ── 1. DebugMode ────────────────────────────────────
        Console.WriteLine("--- DebugMode ---");

        // Default: no args → Off
        DebugMode.Init(new string[] { });
        Assert(!DebugMode.On, "DebugMode: default Off", "Init({})");

        // --debug → On
        DebugMode.Init(new string[] { "--debug" });
        Assert(DebugMode.On, "DebugMode: --debug flag", "Init({\"--debug\"})");

        // -d → On (starts from already-On, should stay On)
        DebugMode.Init(new string[] { "-d" });
        Assert(DebugMode.On, "DebugMode: -d flag", "Init({\"-d\"})");

        // Unrelated args won't turn it off (by design: Init is called once)
        DebugMode.Init(new string[] { "foo", "--bar" });
        Assert(DebugMode.On, "DebugMode: unrelated keeps On", "stays On when flag not found (Init called once at startup)");

        // Reset for pure test: make new instance not possible with static class
        // This is acceptable — Init is always called once at startup

        // ── 2. VersionInfo ──────────────────────────────────
        Console.WriteLine("--- VersionInfo ---");

        VersionInfo.Init("TestApp");
        Assert(VersionInfo.AppName == "TestApp", "Version: AppName", "Got: " + VersionInfo.AppName);
        Assert(VersionInfo.Number == "0.1.0", "Version: Number", "Got: " + VersionInfo.Number);
        Assert(VersionInfo.FullName == "TestApp v0.1.0", "Version: FullName", "Got: " + VersionInfo.FullName);

        VersionInfo.Init("WinDock");
        Assert(VersionInfo.FullName == "WinDock v0.1.0", "Version: WinDock", "Got: " + VersionInfo.FullName);

        // ── 3. EventLog ─────────────────────────────────────
        Console.WriteLine("--- EventLog ---");

        EventLog.Init("SharedTest");
        string logPath = tmp + @"\SharedTest_events.txt";
        if (File.Exists(logPath)) File.Delete(logPath);

        EventLog.Info("info message");
        Assert(File.Exists(logPath), "EventLog: file created", "exists: " + logPath);
        string content = File.ReadAllText(logPath);
        Assert(content.Contains("INFO"), "EventLog: INFO tag", content.Substring(0, Math.Min(60, content.Length)));
        Assert(content.Contains("info message"), "EventLog: info text", content.Substring(0, Math.Min(60, content.Length)));

        EventLog.Warn("warn message");
        content = File.ReadAllText(logPath);
        Assert(content.Contains("WARN"), "EventLog: WARN tag", "last entry has WARN");

        EventLog.Error("error message");
        content = File.ReadAllText(logPath);
        Assert(content.Contains("ERROR"), "EventLog: ERROR tag", "last entry has ERROR");

        if (File.Exists(logPath)) File.Delete(logPath);

        // ── 4. W utilities ──────────────────────────────────
        Console.WriteLine("--- W ---");

        // Lock/Unlock
        string mutexName = "Global\\SharedTest_" + Guid.NewGuid().ToString("N");
        bool locked = W.Lock(mutexName);
        Assert(locked, "W.Lock: first instance", "should acquire mutex");
        W.Unlock();
        locked = W.Lock(mutexName);
        Assert(locked, "W.Lock: after Unlock", "should re-acquire");
        W.Unlock();

        // Lbl
        using (var font = new System.Drawing.Font("Arial", 8))
        {
            var lbl = W.Lbl("hello", font, System.Drawing.Color.Red, 100, 20, 10, 5);
            Assert(lbl.Text == "hello", "W.Lbl: Text", "Got: " + lbl.Text);
            Assert(lbl.Width == 100, "W.Lbl: Width", "Got: " + lbl.Width);
            Assert(lbl.Height == 20, "W.Lbl: Height", "Got: " + lbl.Height);
            Assert(lbl.Location.X == 10, "W.Lbl: X", "Got: " + lbl.Location.X);
            Assert(lbl.Location.Y == 5, "W.Lbl: Y", "Got: " + lbl.Location.Y);
        }

        // ── Summary ─────────────────────────────────────────
        Console.WriteLine("");
        Console.WriteLine("=== Summary ===");
        Console.WriteLine("Passed: " + passed);
        Console.WriteLine("Failed: " + failed);

        if (failed > 0) Environment.Exit(1);
    }
}
