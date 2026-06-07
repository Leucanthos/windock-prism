using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// ============================================================
// SimMouse — real hardware-level mouse simulation via SendInput
// ============================================================
// Unlike SetCursorPos (which only moves the cursor without
// generating input events), SimMouse synthesizes real Windows
// input events that applications receive as genuine mouse
// activity. This is the same mechanism used by touchscreens,
// pen digitizers, and accessibility tools.
// ============================================================

public static class SimMouse
{
    // ===================================================================
    // Win32 P/Invoke
    // ===================================================================

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    // ===================================================================
    // Native structs
    // ===================================================================

    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int      dx;
        public int      dy;
        public uint     mouseData;
        public uint     dwFlags;
        public uint     time;
        public IntPtr   dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort   wVk;
        public ushort   wScan;
        public uint     dwFlags;
        public uint     time;
        public IntPtr   dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HARDWAREINPUT
    {
        public uint     uMsg;
        public ushort   wParamL;
        public ushort   wParamH;
    }

    // Union — all three overlay at offset 0
    [StructLayout(LayoutKind.Explicit)]
    struct INPUT_UNION
    {
        [FieldOffset(0)] public MOUSEINPUT     mi;
        [FieldOffset(0)] public KEYBDINPUT     ki;
        [FieldOffset(0)] public HARDWAREINPUT   hi;
    }

    struct INPUT
    {
        public uint         type;
        public INPUT_UNION  U;
    }

    // ===================================================================
    // Constants
    // ===================================================================

    const uint INPUT_MOUSE    = 0;
    const uint INPUT_KEYBOARD = 1;
    const uint INPUT_HARDWARE = 2;

    const uint MOUSEEVENTF_MOVE        = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN    = 0x0002;
    const uint MOUSEEVENTF_LEFTUP      = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN   = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP     = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN  = 0x0020;
    const uint MOUSEEVENTF_MIDDLEUP    = 0x0040;
    const uint MOUSEEVENTF_ABSOLUTE   = 0x8000;
    const uint MOUSEEVENTF_WHEEL       = 0x0800;
    const uint MOUSEEVENTF_HWHEEL      = 0x1000;

    const int SM_CXSCREEN = 0;
    const int SM_CYSCREEN = 1;
    const int SM_XVIRTUALSCREEN = 76;
    const int SM_YVIRTUALSCREEN = 77;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;

    static readonly int INPUT_SIZE = Marshal.SizeOf(typeof(INPUT));
    static readonly int MOUSEINPUT_SIZE = Marshal.SizeOf(typeof(MOUSEINPUT));

    static Random _rng = new Random();

    // ===================================================================
    // Public API — position
    // ===================================================================

    /// <summary>Get current cursor position in screen coordinates.</summary>
    public static Point GetPosition()
    {
        POINT p;
        GetCursorPos(out p);
        return new Point(p.X, p.Y);
    }

    /// <summary>Instant teleport to absolute screen coordinates via SendInput.</summary>
    public static void MoveTo(int x, int y)
    {
        var input = new INPUT { type = INPUT_MOUSE };
        input.U.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;
        input.U.mi.dx = ToAbsoluteX(x);
        input.U.mi.dy = ToAbsoluteY(y);
        SendInput(1, new[] { input }, INPUT_SIZE);
    }

    /// <summary>
    /// Human-like smooth mouse movement from current position to target.
    /// Uses cubic ease-in-out acceleration with micro-jitter.
    /// </summary>
    /// <param name="x">Target X (screen coordinates)</param>
    /// <param name="y">Target Y (screen coordinates)</param>
    /// <param name="durationMs">Total movement duration (default 400ms)</param>
    public static void MoveSmooth(int x, int y, int durationMs = 400)
    {
        var start = GetPosition();
        int dx = x - start.X;
        int dy = y - start.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        // If very close, just teleport
        if (dist < 4.0)
        {
            MoveTo(x, y);
            return;
        }

        // Steps: roughly 60 fps cadence
        int steps = Math.Max(3, (int)(durationMs / 16.0));
        double stepMs = (double)durationMs / steps;

        for (int i = 1; i <= steps; i++)
        {
            // Cubic ease-in-out
            double t = (double)i / steps;
            double eased = EaseInOutCubic(t);

            int cx = start.X + (int)(dx * eased);
            int cy = start.Y + (int)(dy * eased);

            // Human jitter: ±2px random
            if (dist > 20)
            {
                cx += _rng.Next(-2, 3);
                cy += _rng.Next(-2, 3);
            }

            // Clamp to screen
            cx = Math.Max(0, cx);
            cy = Math.Max(0, cy);

            MoveTo(cx, cy);
            Thread.Sleep((int)stepMs);
        }

        // Final precise position (no jitter)
        MoveTo(x, y);
    }

    // ===================================================================
    // Public API — buttons (raw)
    // ===================================================================

    public static void LeftDown()
    {
        SendMouseInput(MOUSEEVENTF_LEFTDOWN, 0, 0, 0);
    }

    public static void LeftUp()
    {
        SendMouseInput(MOUSEEVENTF_LEFTUP, 0, 0, 0);
    }

    public static void RightDown()
    {
        SendMouseInput(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0);
    }

    public static void RightUp()
    {
        SendMouseInput(MOUSEEVENTF_RIGHTUP, 0, 0, 0);
    }

    public static void MiddleDown()
    {
        SendMouseInput(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0);
    }

    public static void MiddleUp()
    {
        SendMouseInput(MOUSEEVENTF_MIDDLEUP, 0, 0, 0);
    }

    // ===================================================================
    // Public API — clicks
    // ===================================================================

    /// <summary>Left click at current position.</summary>
    /// <param name="downMs">Time between down and up (default 50ms)</param>
    public static void LeftClick(int downMs = 50)
    {
        LeftDown();
        Thread.Sleep(Humanize(downMs));
        LeftUp();
    }

    /// <summary>Right click at current position.</summary>
    public static void RightClick(int downMs = 50)
    {
        RightDown();
        Thread.Sleep(Humanize(downMs));
        RightUp();
    }

    /// <summary>Middle click at current position.</summary>
    public static void MiddleClick(int downMs = 50)
    {
        MiddleDown();
        Thread.Sleep(Humanize(downMs));
        MiddleUp();
    }

    /// <summary>
    /// Double-click at current position.
    /// Uses GetDoubleClickTime() for authentic interval.
    /// </summary>
    public static void DoubleClick()
    {
        LeftClick();
        Thread.Sleep(GetDoubleClickTimeMs());
        LeftClick();
    }

    // ===================================================================
    // Public API — drag
    // ===================================================================

    /// <summary>
    /// Left-drag from (fromX,fromY) to (toX,toY).
    /// Moves to start, presses button, moves smoothly to end, releases.
    /// </summary>
    public static void LeftDrag(int fromX, int fromY, int toX, int toY, int durationMs = 400)
    {
        MoveTo(fromX, fromY);
        Thread.Sleep(Humanize(30));
        LeftDown();
        Thread.Sleep(Humanize(30));
        MoveSmooth(toX, toY, durationMs);
        Thread.Sleep(Humanize(30));
        LeftUp();
    }

    // ===================================================================
    // Public API — scroll
    // ===================================================================

    /// <summary>Vertical scroll. Positive = up (away from user).</summary>
    /// <param name="clicks">Scroll wheel clicks (120 = 1 detent)</param>
    public static void Scroll(int clicks)
    {
        SendMouseInput(MOUSEEVENTF_WHEEL, 0, 0, (uint)clicks);
    }

    /// <summary>Horizontal scroll. Positive = right.</summary>
    public static void ScrollH(int clicks)
    {
        SendMouseInput(MOUSEEVENTF_HWHEEL, 0, 0, (uint)clicks);
    }

    // ===================================================================
    // Public API — convenience combos
    // ===================================================================

    /// <summary>Move to (x,y) then left-click.</summary>
    public static void MoveAndClick(int x, int y, bool smooth = true, int moveMs = 300)
    {
        if (smooth) MoveSmooth(x, y, moveMs);
        else MoveTo(x, y);
        Thread.Sleep(Humanize(30));
        LeftClick();
    }

    /// <summary>Move to (x,y) then right-click.</summary>
    public static void MoveAndRightClick(int x, int y, bool smooth = true)
    {
        if (smooth) MoveSmooth(x, y);
        else MoveTo(x, y);
        Thread.Sleep(Humanize(30));
        RightClick();
    }

    /// <summary>Move to (x,y) then double-click.</summary>
    public static void MoveAndDoubleClick(int x, int y, bool smooth = true)
    {
        if (smooth) MoveSmooth(x, y);
        else MoveTo(x, y);
        Thread.Sleep(Humanize(30));
        DoubleClick();
    }

    // ===================================================================
    // Public API — utility
    // ===================================================================

    /// <summary>
    /// Apply human-like jitter to a base delay.
    /// Returns baseMs ± ~30% randomized.
    /// </summary>
    public static int Humanize(int baseMs)
    {
        if (baseMs <= 0) return 0;
        int jitter = (int)(baseMs * 0.3);
        return baseMs + _rng.Next(-jitter, jitter + 1);
    }

    /// <summary>Get system double-click time in milliseconds.</summary>
    public static int GetDoubleClickTimeMs()
    {
        // Typical Windows default is 500ms
        return SystemInformation.DoubleClickTime;
    }

    /// <summary>Get the INPUT struct size (useful for diagnostics).</summary>
    public static int GetInputStructSize() { return INPUT_SIZE; }

    /// <summary>Get the MOUSEINPUT struct size (useful for diagnostics).</summary>
    public static int GetMouseInputStructSize() { return MOUSEINPUT_SIZE; }

    /// <summary>Get virtual screen bounds.</summary>
    public static Rectangle GetVirtualScreen()
    {
        int left   = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top    = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width  = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return new Rectangle(left, top, width, height);
    }

    // ===================================================================
    // Internal helpers
    // ===================================================================

    static void SendMouseInput(uint flags, int dx, int dy, uint data)
    {
        var input = new INPUT { type = INPUT_MOUSE };
        input.U.mi.dwFlags = flags;
        input.U.mi.dx = dx;
        input.U.mi.dy = dy;
        input.U.mi.mouseData = data;
        uint sent = SendInput(1, new[] { input }, INPUT_SIZE);
        // Log for debugging if needed
        // System.Diagnostics.Debug.WriteLine("SendInput returned " + sent);
    }

    /// <summary>
    /// Convert screen pixel X to normalized absolute coordinate (0–65535).
    /// Note: uses 65536 multiplier (not 65535) per MSDN —
    /// the range [0,65535] maps 0..65536 to the full screen width.
    /// </summary>
    static int ToAbsoluteX(int screenX)
    {
        int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        if (w == 0) w = 1920; // fallback
        return (int)((long)screenX * 65536 / w);
    }

    static int ToAbsoluteY(int screenY)
    {
        int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (h == 0) h = 1080; // fallback
        return (int)((long)screenY * 65536 / h);
    }

    // ===================================================================
    // Easing functions
    // ===================================================================

    /// <summary>Cubic ease-in-out: slow start, fast middle, slow end.</summary>
    static double EaseInOutCubic(double t)
    {
        if (t < 0.5) return 4.0 * t * t * t;
        return 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
    }
}
