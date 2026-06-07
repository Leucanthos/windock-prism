using System;
using System.Drawing;
using System.Windows.Forms;

// ============================================================
// IconMenu — right-click popup menu for dock icons
// Rows: [Close Window] [End Task] [Pin/Unpin]
// ============================================================
static class IconMenu
{
    static Form popup;
    static bool canClose;
    static DateTime lastShow;

    /// <summary>
    /// Show right-click context menu.
    /// </summary>
    /// <param name="hasVisibleWindow">true if app has a visible window to close</param>
    /// <param name="isRunning">true if the process is alive</param>
    /// <param name="isPinned">true if pinned to dock</param>
    public static void Show(Point screenPos, bool hasVisibleWindow, bool isRunning, bool isPinned,
        Action onCloseWindow, Action onEndTask, Action onTogglePin,
        Action onClosed = null, Action onNewWindow = null)
    {
        if ((DateTime.Now - lastShow).TotalMilliseconds < 300) return;
        lastShow = DateTime.Now;

        if (popup != null && !popup.IsDisposed) { popup.Close(); popup.Dispose(); }

        // Row count: CloseWindow + EndTask + optional NewWindow + Pin/Unpin
        int rowCount = 2 + (onNewWindow != null ? 1 : 0) + 1;
        int w = 260, h = 22 + rowCount * 30 + (rowCount - 1) * 4, pad = 12, rowH = 30, gap = 4;
        popup = new Form
        {
            Size = new Size(w, h),
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = Theme.FormBg,
            BackgroundImage = Theme.GlassBmp,
            BackgroundImageLayout = ImageLayout.Stretch,
            Opacity = 0.85,
        };
        popup.Shown += (s, e) => W.Round(popup);
        popup.Deactivate += (s, e) => { if (canClose) popup.Close(); };
        popup.FormClosed += (s, e) => { if (onClosed != null) onClosed(); };

        var ct = new System.Windows.Forms.Timer { Interval = 200 };
        ct.Tick += (s2, e2) => { canClose = true; ct.Stop(); ct.Dispose(); };
        ct.Start();

        var font = new Font("Trebuchet MS", 9, FontStyle.Regular);
        var fg = Theme.Fg;
        var dim = Theme.Dim;
        var hoverBg = Theme.IsLight ? Color.FromArgb(35, 0, 0, 0) : Color.FromArgb(35, 255, 255, 255);

        int y = pad;

        // === Row 1: Close Window ===
        var closeBtn = new Label
        {
            Text = "  Close Window",
            Location = new Point(pad, y),
            Size = new Size(w - pad * 2, rowH),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = hasVisibleWindow ? fg : dim,
            BackColor = Color.Transparent, Font = font,
            Cursor = hasVisibleWindow ? Cursors.Hand : Cursors.Default,
        };
        if (hasVisibleWindow)
        {
            closeBtn.MouseEnter += (s2, e2) => { closeBtn.BackColor = hoverBg; };
            closeBtn.MouseLeave += (s2, e2) => { closeBtn.BackColor = Color.Transparent; };
            closeBtn.Click += (s2, e2) => { popup.Close(); onCloseWindow(); };
        }
        popup.Controls.Add(closeBtn);
        y += rowH + gap / 2;

        // === Row 2: End Task ===
        var killBtn = new Label
        {
            Text = "  End Task",
            Location = new Point(pad, y),
            Size = new Size(w - pad * 2, rowH),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = isRunning ? fg : dim,
            BackColor = Color.Transparent, Font = font,
            Cursor = isRunning ? Cursors.Hand : Cursors.Default,
        };
        if (isRunning)
        {
            killBtn.MouseEnter += (s2, e2) => { killBtn.BackColor = hoverBg; };
            killBtn.MouseLeave += (s2, e2) => { killBtn.BackColor = Color.Transparent; };
            killBtn.Click += (s2, e2) => { popup.Close(); onEndTask(); };
        }
        popup.Controls.Add(killBtn);
        y += rowH + gap / 2;

        // === Optional New Window row ===
        if (onNewWindow != null)
        {
            var newBtn = new Label
            {
                Text = "  New Window",
                Location = new Point(pad, y),
                Size = new Size(w - pad * 2, rowH),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = fg, BackColor = Color.Transparent, Font = font, Cursor = Cursors.Hand,
            };
            newBtn.MouseEnter += (s2, e2) => { newBtn.BackColor = hoverBg; };
            newBtn.MouseLeave += (s2, e2) => { newBtn.BackColor = Color.Transparent; };
            newBtn.Click += (s2, e2) => { popup.Close(); onNewWindow(); };
            popup.Controls.Add(newBtn);
            y += rowH + gap / 2;
        }

        // === Separator ===
        popup.Controls.Add(new Panel { Size = new Size(w - pad * 2, 1), Location = new Point(pad, y), BackColor = Theme.SepColor });
        y += gap / 2 + 1;

        // === Row: Pin/Unpin ===
        string pinText = isPinned ? "  Unpin from Dock" : "  Pin to Dock";
        var pinBtn = new Label
        {
            Text = pinText,
            Location = new Point(pad, y),
            Size = new Size(w - pad * 2, rowH),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = fg, BackColor = Color.Transparent, Font = font, Cursor = Cursors.Hand,
        };
        pinBtn.MouseEnter += (s2, e2) => { pinBtn.BackColor = hoverBg; };
        pinBtn.MouseLeave += (s2, e2) => { pinBtn.BackColor = Color.Transparent; };
        pinBtn.Click += (s2, e2) => { popup.Close(); onTogglePin(); };
        popup.Controls.Add(pinBtn);

        // Position: keep within screen
        var scr = Screen.FromPoint(screenPos).WorkingArea;
        int x = screenPos.X, yPos = screenPos.Y;
        if (x + w > scr.Right) x = scr.Right - w;
        if (yPos + h > scr.Bottom) yPos = scr.Bottom - h;
        popup.Location = new Point(x, yPos);

        popup.Show();
    }
}
