using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// ============================================================
// TopBar Widget — clock, date, day/night theme toggle
// ============================================================
static class TopBarWidget
{
    static Label timeLabel,dateLabel,darkOn,darkOff,lightOn,lightOff,verLabel;
    static Form form; static bool isLight; static List<Panel> seps=new List<Panel>();
    static string[] days={"Sunday","Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"};
    static Font btnFont, btnSelFont; // cached to avoid allocation on theme toggle

    [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern int SystemParametersInfo(int uAction,int uParam,string lpvParam,int fuWinIni);

    public static Form Create(Settings cfg){
        isLight=Theme.IsLight;
        form=Theme.NewTopBarForm(new Point(cfg.TopX,cfg.TopY),cfg.TopOpacity,true);
        Font tf=Theme.TitleFont, bf=Theme.BodyFont, sf=Theme.SmallFont;

        int px=2,gw=24,gh=32; var mp=new Panel{Size=new Size(gw*px,gh*px),Location=new Point(6,6),BackColor=Color.Transparent};
        mp.Paint+=PaintJelly; form.Controls.Add(mp);

        int lm=64,tw=188,y=Theme.StartY;
        var titleLbl=W.Lbl("Now is:",tf,Theme.Fg,tw,20,lm,y);titleLbl.Cursor=Cursors.Hand;
        titleLbl.MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Right)AboutPanel.Show();};
        form.Controls.Add(titleLbl);y+=20;
        seps.Add(AddSep(tw,1,lm,y));y+=4;
        timeLabel=W.Lbl("00:00",bf,Theme.Fg,tw,16,lm,y);form.Controls.Add(timeLabel);y+=16;
        seps.Add(AddSep(tw,1,lm,y));y+=4;
        dateLabel=W.Lbl("Sunday, 2026-06-07",Theme.BodyRegFont,Theme.Dim,tw,16,lm,y);form.Controls.Add(dateLabel);y+=16;
        seps.Add(AddSep(tw,1,lm,y));y+=12;
        form.Controls.Add(W.Lbl("Mode:",tf,Theme.Fg,tw,20,lm,y));y+=20;
        seps.Add(AddSep(tw,1,lm,y));y+=4;

        btnFont=Theme.BodyFont;
        btnSelFont=new Font("Trebuchet MS",10,FontStyle.Bold|FontStyle.Underline);
        darkOn=MakeBtn("[Dark]",isLight?Theme.Dim:Theme.Accent,btnFont,55,16,lm+14,y);darkOn.Visible=!isLight;
        darkOff=MakeBtn("[Dark]",isLight?Theme.Accent:Theme.Dim,btnFont,55,16,lm+21,y);darkOff.Visible=isLight;
        var div=W.Lbl("|",Theme.SmallBoldFont9,Theme.SepColor,10,16,lm+76,y);
        lightOn=MakeBtn("[Light]",isLight?Theme.Accent:Theme.Dim,btnFont,55,16,lm+86,y);lightOn.Visible=isLight;
        lightOff=MakeBtn("[Light]",isLight?Theme.Dim:Theme.Accent,btnFont,55,16,lm+93,y);lightOff.Visible=!isLight;
        form.Controls.Add(darkOn);form.Controls.Add(darkOff);form.Controls.Add(div);form.Controls.Add(lightOn);form.Controls.Add(lightOff);
        darkOn.Click+=(s,e)=>SetTheme(false);darkOff.Click+=(s,e)=>SetTheme(false);
        lightOn.Click+=(s,e)=>SetTheme(true);lightOff.Click+=(s,e)=>SetTheme(true);

        // Font size toggle
        y+=18;
        seps.Add(AddSep(tw,1,lm,y));y+=4;
        string fontLabel = Theme.SmallFontMode ? "[larg-font]" : "[small-font]";
        var fontBtn = MakeBtn(fontLabel, Theme.Accent, btnFont, 90, 16, lm, y);
        fontBtn.Click += (s, e) => {
            fontBtn.Enabled = false;
            Theme.SmallFontMode = !Theme.SmallFontMode;
            cfg.SmallFont = Theme.SmallFontMode;
            Settings.Save(cfg);
            W.Unlock(); // release mutex so new instance can start
            try { System.Diagnostics.Process.Start(Application.ExecutablePath); } catch { }
            Application.Exit();
        };
        form.Controls.Add(fontBtn); y += 16;

        // Version + update (always visible, bottom of TopBar)
        seps.Add(AddSep(tw,1,lm,y));y+=4;
        verLabel=W.Lbl("Prism v"+VersionInfo.Number+"  |  Check for Updates",sf,Theme.Accent,tw,14,lm,y);verLabel.Cursor=Cursors.Hand;
        verLabel.Click+=(s,e)=>{try{System.Diagnostics.Process.Start("https://github.com");}catch{}};
        form.Controls.Add(verLabel);y+=12;

        foreach(Control c in form.Controls)if(c is Label)c.BackColor=Color.Transparent;
        form.Size=new Size(260,y+24);

        int hour=DateTime.Now.Hour; bool sl=(hour>=6&&hour<18);
        if(sl!=isLight){SetTheme(sl);}else{ApplyTopBarTheme();string fallbackWp=System.IO.Path.Combine(Program.BaseDir,isLight?@"assets\wallpaper-day.png":@"assets\wallpaper-night.png");if(System.IO.File.Exists(fallbackWp))SystemParametersInfo(0x0014,0,fallbackWp,0x0002);}

        var timer=new System.Windows.Forms.Timer{Interval=cfg.ClockMs};timer.Tick+=(s,e)=>UpdateClock();timer.Start();UpdateClock();
        Program.ThemeChanged+=(light)=>{isLight=light;ApplyTopBarTheme();};
        W.MakeDraggable(form);
        return form;
    }
    static Panel AddSep(int w,int h,int x,int y){var p=Theme.Sep(w,h,x,y);form.Controls.Add(p);return p;}
    static Label MakeBtn(string t,Color c,Font f,int w,int h,int x,int y){return new Label{Text=t,ForeColor=c,Font=f,AutoSize=false,TextAlign=ContentAlignment.MiddleCenter,Size=new Size(w,h),Location=new Point(x,y),Cursor=Cursors.Hand};}
    static void UpdateClock(){var n=DateTime.Now;timeLabel.Text=n.ToString("HH:mm");dateLabel.Text=string.Format("{0}, {1:yyyy-MM-dd}",days[(int)n.DayOfWeek],n);}

    static void ApplyTopBarTheme(){
        bool light=isLight;
        form.BackColor=Theme.BinFormBg;
        form.BackgroundImage=Theme.BinGlassBmp;
        Color fg=Theme.Fg, dim=Theme.Dim, acc=Theme.Accent;
        foreach(Control c in form.Controls)if(c is Label)((Label)c).ForeColor=fg;
        darkOn.ForeColor=light?dim:acc;darkOff.ForeColor=light?acc:dim;lightOn.ForeColor=light?acc:dim;lightOff.ForeColor=light?dim:acc;
        darkOn.Visible=!light;darkOff.Visible=light;lightOn.Visible=light;lightOff.Visible=!light;
        // Underline only selected: darkOn(!light) or lightOn(light)
        darkOn.Font=light?btnFont:btnSelFont;
        lightOn.Font=light?btnSelFont:btnFont;
        darkOff.Font=btnFont;
        lightOff.Font=btnFont;
        foreach(var s in seps)s.BackColor=Theme.SepColor;
        if(verLabel!=null)verLabel.ForeColor=acc;
    }

    static void SetTheme(bool light){
        int val=light?1:0;
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",val);
        Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",val);
        string wp=light?Path.Combine(Program.BaseDir,@"assets\wallpaper-day.png"):Path.Combine(Program.BaseDir,@"assets\wallpaper-night.png");
        if(File.Exists(wp))SystemParametersInfo(0x0014,0,wp,0x0002);
        try{string vp=Environment.ExpandEnvironmentVariables(@"%APPDATA%\Code\User\settings.json");if(File.Exists(vp)){string js=File.ReadAllText(vp);string th=light?"Light 2026":"Default Dark Modern";js=System.Text.RegularExpressions.Regex.Replace(js,@"(""workbench\.colorTheme""\s*:\s*"")[^""]+","$1"+th);File.WriteAllText(vp,js);}}catch{}
        isLight=light; Theme.Toggle(light);
        ApplyTopBarTheme();
        Program.NotifyTheme(light);
    }

    static void PaintJelly(object s, PaintEventArgs e){
        var g=e.Graphics;g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
        int[,] img={{0,0,0,0,0,1,1,1,1,1,1,1,0,0,0,0,1,1,1,1,1,1,0,0},{0,0,0,0,1,2,2,2,2,2,2,2,1,0,0,1,2,2,2,2,2,2,1,0},{0,0,0,1,2,2,3,2,2,2,2,2,2,1,1,2,2,2,2,2,3,2,2,1},{0,0,1,2,2,3,3,3,2,2,2,2,2,2,2,2,2,2,2,3,3,3,2,1},{0,1,2,2,3,4,4,3,2,2,2,2,2,2,2,2,2,2,3,4,4,3,2,1},{0,1,2,2,3,4,4,3,2,1,5,5,1,2,2,1,5,5,1,3,4,3,2,1},{0,1,2,2,2,3,3,2,2,2,2,2,2,2,2,2,2,2,2,2,3,2,2,1},{1,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,1},{1,2,3,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,3,2,1},{1,2,2,3,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,3,2,2,1},{0,1,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,2,1,0},{0,0,1,2,3,2,2,2,2,3,2,3,2,2,3,2,3,2,2,2,3,2,1,0},{0,0,1,2,2,3,2,3,2,3,2,3,2,2,3,2,3,2,3,2,2,2,1,0},{0,0,0,1,2,2,3,2,3,2,3,2,3,3,2,3,2,3,2,2,2,1,0,0},{0,0,0,0,1,2,2,3,2,3,2,3,2,2,3,2,3,2,3,2,2,1,0,0},{0,0,0,0,1,2,2,2,3,2,3,2,3,3,2,3,2,3,2,2,2,1,0,0},{0,0,0,0,0,1,2,2,2,3,2,3,2,2,3,2,3,2,2,2,1,0,0,0},{0,0,0,0,0,1,2,2,2,2,3,2,3,3,2,2,2,2,2,2,1,0,0,0},{0,0,0,0,0,0,1,1,2,2,2,3,2,2,3,2,2,2,2,1,1,0,0,0},{0,0,0,0,0,0,0,1,1,2,2,2,3,3,2,2,2,2,1,1,0,0,0,0},{0,0,0,0,0,0,0,0,1,1,2,2,2,2,2,2,2,1,1,0,0,0,0,0},{0,0,0,0,0,0,0,0,0,0,1,1,2,2,2,1,1,1,0,0,0,0,0,0},{0,0,0,0,0,0,0,0,0,0,0,0,1,2,1,1,0,0,0,0,0,0,0,0},{0,0,0,0,0,0,0,0,0,0,0,0,0,1,1,0,0,0,0,0,0,0,0,0},{0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0},{0,0,0,0,0,0,0,0,0,0,0,0,1,3,1,0,0,0,0,0,0,0,0,0},{0,0,0,0,0,0,0,0,0,0,0,0,1,2,1,0,0,0,0,0,0,0,0,0},{0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0},{0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0},{0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0},{0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0},{0,0,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0}};
        Color[] pal={Color.Transparent,Color.FromArgb(20,60,140),Color.FromArgb(40,120,220),Color.FromArgb(80,180,255),Color.FromArgb(160,220,255),Color.White};
        for(int r=0;r<32;r++)for(int c=0;c<24;c++){int cl=img[r,c];if(cl>0&&cl<pal.Length)using(var b=new SolidBrush(pal[cl]))g.FillRectangle(b,c*2,r*2,2,2);}
    }
}
