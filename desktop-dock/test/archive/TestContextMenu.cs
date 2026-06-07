using System;
using System.Drawing;
using System.Windows.Forms;

// ============================================================
// TestContextMenu — standalone test for right-click popup
// ============================================================
class TestContextMenu
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread] static void Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Theme.Init();
        // Test: show the menu at center screen, clicking outside should close it
        IconMenu.Show(Cursor.Position, true, true, ()=>MessageBox.Show("Close"), ()=>MessageBox.Show("TogglePin"));
        Application.Run();
    }
}
