// ============================================================
// Version — single source of truth for build version
// Call Init("WinDock") or Init("Prism") before use.
// ============================================================
static class VersionInfo
{
    public static string AppName { get; private set; }
    public const string Number = "0.1.1";
    public static string FullName { get { return AppName + " v" + Number; } }

    public static void Init(string appName)
    {
        AppName = appName;
    }
}
