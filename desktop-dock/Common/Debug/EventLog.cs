using System;
using System.IO;

// ============================================================
// EventLog — always-on structured event log for diagnosing
// real-world user interaction issues.
// ============================================================

static class EventLog
{
    static string path = @"C:\temp\_dock_events.txt";
    static object _lock = new object();

    public static void Info(string msg)
    {
        Write("INFO", msg);
    }

    public static void Warn(string msg)
    {
        Write("WARN", msg);
    }

    public static void Error(string msg)
    {
        Write("ERROR", msg);
    }

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

    /// <summary>Write current icon state snapshot (called from DockManager)</summary>
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
