using System;
using System.IO;

// ============================================================
// EventLog — always-on structured event log.
// Call Init("WinDock") or Init("Prism") to set log file path.
// ============================================================
static class EventLog
{
    static string path;
    static object _lock = new object();

    public static void Init(string appName)
    {
        var tmp = Environment.GetEnvironmentVariable("TEMP") ?? @"C:\temp";
        path = tmp + @"\" + appName + "_events.txt";
    }

    public static void Info(string msg) { Write("INFO", msg); }
    public static void Warn(string msg) { Write("WARN", msg); }
    public static void Error(string msg) { Write("ERROR", msg); }

    static void Write(string level, string msg)
    {
        try
        {
            lock (_lock)
            {
                File.AppendAllText(path,
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + level + " " + msg + "\n");
            }
        }
        catch { }
    }

    /// <summary>Write state snapshot (used by DockManager / widget monitors)</summary>
    public static void DumpIconState(string[] lines)
    {
        try
        {
            lock (_lock)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== ICONS @ " + DateTime.Now.ToString("HH:mm:ss.fff") + " ===");
                foreach (var line in lines)
                    sb.AppendLine("  " + line);
                File.AppendAllText(path, sb.ToString());
            }
        }
        catch { }
    }
}
