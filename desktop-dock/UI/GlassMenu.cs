using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// ============================================================
// GlassMenu — clean popup, no DWM artifacts
// ============================================================

class GlassMenu : IDisposable
{
    public class Item
    {
        public string Text;
        public Action Action;
        public bool Enabled;
        public Item(string text, Action action, bool enabled) { Text = text; Action = action; Enabled = enabled; }
    }

    Form popup;
    MessageFilter filter;
    bool canClose;
    int itemH = 30, padH = 14, padV = 6;
    int w = 186, radius = 10;

    public GlassMenu(params Item[] items)
    {
        int h = items.Length * itemH + padV * 2;

        popup = new Form
        {
            Size = new Size(w, h),
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            TopMost = false,
            ShowInTaskbar = false,
            Opacity = 0.85,
        };

        // True rounded corners via Region (no DWM involvement)
        SetRoundedRegion(h);

        popup.Paint += PaintBg;
        popup.Deactivate += (s, e) => { if (canClose) popup.Close(); };
        popup.LostFocus += (s, e) => { if (canClose) popup.Close(); };

        var ct = new System.Windows.Forms.Timer { Interval = 300 };
        ct.Tick += (s2, e2) => { canClose = true; ct.Stop(); ct.Dispose(); };
        ct.Start();

        filter = new MessageFilter(popup);
        Application.AddMessageFilter(filter);
        popup.FormClosed += (s2, e2) => Application.RemoveMessageFilter(filter);

        var fg = Theme.Fg;
        var dim = Theme.Dim;
        var hoverBg = Theme.IsLight ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255);
        var font = new Font("Trebuchet MS", 9, FontStyle.Regular);

        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var lbl = new Label
            {
                Text = "  " + item.Text,
                Location = new Point(padH, padV + i * itemH),
                Size = new Size(w - padH * 2, itemH),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = item.Enabled ? fg : dim,
                BackColor = Color.Transparent,
                Font = font,
                Cursor = item.Enabled ? Cursors.Hand : Cursors.Default,
            };
            lbl.MouseEnter += (s2, e2) => { lbl.BackColor = hoverBg; };
            lbl.MouseLeave += (s2, e2) => { lbl.BackColor = Color.Transparent; };
            lbl.Click += (s2, e2) =>
            {
                if (item.Enabled) { popup.Close(); item.Action(); }
            };
            popup.Controls.Add(lbl);

            if (i < items.Length - 1)
            {
                var sep = new Panel
                {
                    Size = new Size(w - padH * 2, 1),
                    Location = new Point(padH, padV + i * itemH + itemH),
                    BackColor = Theme.SepColor,
                };
                popup.Controls.Add(sep);
            }
        }
    }

    void SetRoundedRegion(int h)
    {
        using (var gp = new GraphicsPath())
        {
            int r = radius;
            gp.AddArc(0, 0, r * 2, r * 2, 180, 90);
            gp.AddArc(w - 1 - r * 2, 0, r * 2, r * 2, 270, 90);
            gp.AddArc(0, h - 1 - r * 2, r * 2, r * 2, 90, 90);
            gp.AddArc(w - 1 - r * 2, h - 1 - r * 2, r * 2, r * 2, 0, 90);
            gp.CloseFigure();
            popup.Region = new Region(gp);
        }
    }

    void PaintBg(object s, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Simple frosted fill — no stretched texture, no edge artifacts
        Color fill = Theme.IsLight
            ? Color.FromArgb(235, 242, 240, 236)
            : Color.FromArgb(220, 28, 32, 48);

        using (var gp = new GraphicsPath())
        {
            int r = radius;
            gp.AddArc(0, 0, r * 2, r * 2, 180, 90);
            gp.AddArc(w - 1 - r * 2, 0, r * 2, r * 2, 270, 90);
            gp.AddArc(0, popup.Height - 1 - r * 2, r * 2, r * 2, 90, 90);
            gp.AddArc(w - 1 - r * 2, popup.Height - 1 - r * 2, r * 2, r * 2, 0, 90);
            gp.CloseFigure();

            using (var br = new SolidBrush(fill))
                g.FillPath(br, gp);

            // Hairline border — barely visible
            var borderCol = Theme.IsLight
                ? Color.FromArgb(45, 0, 0, 0)
                : Color.FromArgb(40, 255, 255, 255);
            using (var p = new Pen(borderCol, 1f))
                g.DrawPath(p, gp);
        }
    }

    public void Show(Point screenPos)
    {
        var scr = Screen.FromPoint(screenPos).WorkingArea;
        int x = screenPos.X, y = screenPos.Y;
        if (x + popup.Width > scr.Right) x = scr.Right - popup.Width;
        if (y + popup.Height > scr.Bottom) y = scr.Bottom - popup.Height;
        popup.Location = new Point(x, y);
        popup.Show();
    }

    public void Dispose()
    {
        if (filter != null) { try { Application.RemoveMessageFilter(filter); } catch { } filter = null; }
        if (popup != null) { if (!popup.IsDisposed) popup.Close(); popup.Dispose(); }
    }
}

// ============================================================
// MessageFilter — closes popup on any mouse click outside it
// ============================================================
class MessageFilter : IMessageFilter
{
    Form popup;
    public MessageFilter(Form form) { popup = form; }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg == 0x201 || m.Msg == 0x204 || m.Msg == 0x207 || m.Msg == 0x20B ||
            m.Msg == 0x00A1 || m.Msg == 0x00A4)
        {
            if (popup == null || popup.IsDisposed) return false;
            Point pt = Cursor.Position;
            if (!popup.Bounds.Contains(pt))
            {
                popup.Close();
            }
        }
        return false;
    }
}
