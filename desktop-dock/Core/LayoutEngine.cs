using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ============================================================
// LayoutEngine — dock positioning + glow line rendering
// ============================================================

static class LayoutEngine
{
    const int GAP = 14;

    static int lastStartX = -1, lastIconY = -1, lastCnt = -1;

    public static int IconSize
    {
        get { return (int)(44 * DockIcon.DpiX / 96f); }
    }

    public static int TotalWidth(int iconCount)
    {
        if (iconCount <= 0) return 0;
        int fw = IconSize;
        return iconCount * fw + (iconCount - 1) * GAP;
    }

    public static int StartX(int iconCount, int screenWidth)
    {
        return (screenWidth - TotalWidth(iconCount)) / 2;
    }

    public static int IconY(int screenHeight, bool debugMode)
    {
        int fw = IconSize;
        return debugMode ? 40 : screenHeight - fw - 20;
    }

    /// <summary>Position all icons. Returns true if layout changed.</summary>
    public static bool Apply(List<DockIcon> icons, int screenW, int screenH, bool debugMode)
    {
        if (icons.Count < 2) return false;
        int cnt = icons.Count;
        int fw = IconSize;
        int startX = StartX(cnt, screenW);
        int iconY = IconY(screenH, debugMode);

        bool layoutChanged = (startX != lastStartX || iconY != lastIconY || cnt != lastCnt);
        if (layoutChanged)
        {
            lastStartX = startX; lastIconY = iconY; lastCnt = cnt;

            for (int i = 0; i < cnt; i++)
            {
                int newX = startX + i * (fw + GAP);
                if (Math.Abs(icons[i].BaseX - newX) > 2 || icons[i].Form.Top != iconY)
                    icons[i].SetBasePos(newX, iconY);
                else
                    icons[i].BaseX = newX;
            }
        }
        return layoutChanged;
    }

}
