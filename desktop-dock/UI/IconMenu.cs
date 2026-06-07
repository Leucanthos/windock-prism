using System;
using System.Drawing;
using System.Windows.Forms;

// ============================================================
// IconMenu — right-click popup (Close/Open + Pin/Unpin)
// ============================================================
static class IconMenu
{
    static Form popup;
    static bool canClose;
    static DateTime lastShow;

    public static void Show(Point screenPos, bool isRunning, bool isPinned, Action onClose, Action onTogglePin, Action onClosed=null, Action onNewWindow=null)
    {
        if ((DateTime.Now - lastShow).TotalMilliseconds < 300) return;
        lastShow = DateTime.Now;

        if (popup != null && !popup.IsDisposed) { popup.Close(); popup.Dispose(); }

        int rowCount = onNewWindow != null ? 3 : 2;
        int w = 240, h = 22 + rowCount * 30 + (rowCount - 1) * 4, pad = 12, rowH = 30, gap = 4;
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
        var hoverBg = Theme.IsLight ? Color.FromArgb(35, 0, 0, 0) : Color.FromArgb(35, 255, 255, 255);

        string actionText = isRunning ? "[Close]" : "[Open]";
        string pinText = isPinned ? "[Unpin from Dock]" : "[Pin to Dock]";

        // Row 1: Close/Open
        var actionBtn = new Label
        {
            Text = "  " + actionText,
            Location = new Point(pad, pad),
            Size = new Size(w - pad * 2, rowH),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = fg, BackColor = Color.Transparent, Font = font, Cursor = Cursors.Hand,
        };
        actionBtn.MouseEnter += (s2, e2) => { actionBtn.BackColor = hoverBg; };
        actionBtn.MouseLeave += (s2, e2) => { actionBtn.BackColor = Color.Transparent; };
        actionBtn.Click += (s2, e2) => { popup.Close(); onClose(); };
        popup.Controls.Add(actionBtn);

        // Optional Row 2: New Window (only for running apps)
        if (onNewWindow != null)
        {
            int newY = pad + rowH + gap / 2;
            var newBtn = new Label
            {
                Text = "  [New Window]",
                Location = new Point(pad, newY),
                Size = new Size(w - pad * 2, rowH),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = fg, BackColor = Color.Transparent, Font = font, Cursor = Cursors.Hand,
            };
            newBtn.MouseEnter += (s2, e2) => { newBtn.BackColor = hoverBg; };
            newBtn.MouseLeave += (s2, e2) => { newBtn.BackColor = Color.Transparent; };
            newBtn.Click += (s2, e2) => { popup.Close(); onNewWindow(); };
            popup.Controls.Add(newBtn);

            // Separator after New Window
            int sepY2 = newY + rowH + gap / 2;
            popup.Controls.Add(new Panel { Size = new Size(w - pad * 2, 1), Location = new Point(pad, sepY2), BackColor = Theme.SepColor });

            // Row 3: Pin/Unpin
            var pinBtn = new Label
            {
                Text = "  " + pinText,
                Location = new Point(pad, sepY2 + gap / 2 + 1),
                Size = new Size(w - pad * 2, rowH),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = fg, BackColor = Color.Transparent, Font = font, Cursor = Cursors.Hand,
            };
            pinBtn.MouseEnter += (s2, e2) => { pinBtn.BackColor = hoverBg; };
            pinBtn.MouseLeave += (s2, e2) => { pinBtn.BackColor = Color.Transparent; };
            pinBtn.Click += (s2, e2) => { popup.Close(); onTogglePin(); };
            popup.Controls.Add(pinBtn);
        }
        else
        {
            // Separator (no New Window row)
            int sepY = pad + rowH + gap / 2;
            popup.Controls.Add(new Panel { Size = new Size(w - pad * 2, 1), Location = new Point(pad, sepY), BackColor = Theme.SepColor });

            // Row 2: Pin/Unpin
            var pinBtn = new Label
            {
                Text = "  " + pinText,
                Location = new Point(pad, sepY + gap / 2 + 1),
                Size = new Size(w - pad * 2, rowH),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = fg, BackColor = Color.Transparent, Font = font, Cursor = Cursors.Hand,
            };
            pinBtn.MouseEnter += (s2, e2) => { pinBtn.BackColor = hoverBg; };
            pinBtn.MouseLeave += (s2, e2) => { pinBtn.BackColor = Color.Transparent; };
            pinBtn.Click += (s2, e2) => { popup.Close(); onTogglePin(); };
            popup.Controls.Add(pinBtn);
        }

        // Position: keep within screen
        var scr = Screen.FromPoint(screenPos).WorkingArea;
        int x = screenPos.X, y = screenPos.Y;
        if (x + w > scr.Right) x = scr.Right - w;
        if (y + h > scr.Bottom) y = scr.Bottom - h;
        popup.Location = new Point(x, y);

        popup.Show();
    }
}
