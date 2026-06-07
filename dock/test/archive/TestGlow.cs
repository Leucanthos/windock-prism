using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

class TestGlow
{
    static void DrawGlow(Graphics g, float x0, float y0, float x1, float y1, Color[] colors, int[] widths){
        for(int i = 0; i < colors.Length; i++)
            using(var p = new Pen(colors[i], widths[i]))
                g.DrawLine(p, x0, y0, x1, y1);
    }

    [STAThread] static void Main()
    {
        Application.EnableVisualStyles();
        // Transparent background test
        var f = new Form {
            Size = new Size(600, 130), StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.None, TopMost = true,
            BackColor = Color.Black, TransparencyKey = Color.Black,
        };
        f.Paint += (s, e) => {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int my = 30, gy = 85;

            // Dark glow on transparent bg
            DrawGlow(g, 30, my, 250, my,
                new[]{Color.FromArgb(30,25,40,120), Color.FromArgb(50,35,55,155),
                      Color.FromArgb(80,50,80,195), Color.FromArgb(120,70,110,220),
                      Color.FromArgb(180,100,150,240), Color.FromArgb(230,150,195,255),
                      Color.FromArgb(255,210,235,255)},
                new[]{15,12,9,7,5,3,1});

            // Light glow on transparent bg
            DrawGlow(g, 30, gy, 250, gy,
                new[]{Color.FromArgb(30,190,150,80), Color.FromArgb(50,210,175,105),
                      Color.FromArgb(80,225,195,125), Color.FromArgb(120,240,215,150),
                      Color.FromArgb(180,250,230,175), Color.FromArgb(230,255,245,210),
                      Color.FromArgb(255,255,250,230)},
                new[]{15,12,9,7,5,3,1});

            // Opaque comparison
            DrawGlow(g, 350, my, 500, my,
                new[]{Color.FromArgb(80,140,255), Color.FromArgb(50,80,200), Color.FromArgb(200,60,110,245)},
                new[]{15,7,3});
        };
        f.Show(); Application.Run();
    }
}
