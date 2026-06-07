using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

// ============================================================
// PinStore — COM-based pin/unpin persistence
// ============================================================

static class PinStore
{
    static string PinDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        + @"\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";

    static HashSet<string> pinnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public static IEnumerable<string> PinnedPaths { get { return pinnedPaths; } }
    public static bool IsPinned(string exePath) { return !string.IsNullOrEmpty(exePath) && pinnedPaths.Contains(exePath); }

    public static void Load()
    {
        pinnedPaths.Clear();
        if (!Directory.Exists(PinDir)) return;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            foreach (var f in Directory.GetFiles(PinDir, "*.lnk"))
            {
                try
                {
                    dynamic lnk = shell.CreateShortcut(f);
                    string target = lnk.TargetPath;
                    if (!string.IsNullOrEmpty(target) && File.Exists(target))
                        pinnedPaths.Add(target);
                }
                catch { }
            }
        }
        catch { /* COM failed — dock still works with running apps */ }
    }

    public static void Pin(string exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            string lnkPath = Path.Combine(PinDir, Path.GetFileNameWithoutExtension(exePath) + ".lnk");
            dynamic lnk = shell.CreateShortcut(lnkPath);
            lnk.TargetPath = exePath;
            lnk.Save();
            pinnedPaths.Add(exePath);
        }
        catch { }
    }

    public static void Unpin(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return;
        pinnedPaths.Remove(exePath);
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            foreach (var f in Directory.GetFiles(PinDir, "*.lnk"))
            {
                try
                {
                    dynamic lnk = shell.CreateShortcut(f);
                    if (lnk.TargetPath.Equals(exePath, StringComparison.OrdinalIgnoreCase))
                    { File.Delete(f); break; }
                }
                catch { }
            }
        }
        catch { }
    }
}
