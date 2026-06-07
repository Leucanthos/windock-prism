// ============================================================
// DebugMode — toggle via --debug or -d flag
// ============================================================
static class DebugMode
{
    public static bool On { get; private set; }

    public static void Init(string[] args)
    {
        foreach (var a in args)
        {
            if (a == "--debug" || a == "-d")
            {
                On = true;
                return;
            }
        }
    }
}
