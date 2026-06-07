using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

// ============================================================
// Theme — centralized appearance (fonts, colors, glass, templates)
// ============================================================
public static class Theme
{
    // ===== State =====
    public static bool IsLight;

    // ===== Fonts =====
    public static bool SmallFontMode;
    public static Font TitleFont, BodyFont, SmallFont, SmallRegFont, ItalicFont;
    public static Font BodyRegFont, SmallBoldFont9, ModelFont;

    // ===== Base colors =====
    public static Color Fg, Dim, Accent;
    public static Color FormBg, PanelBg, BarBg, SepColor;

    // ===== Bin colors (blue palette) =====
    public static Color BinFormBg, BinPanelBg, BinFg, BinDim, BinAccent;

    // ===== Glass bitmaps =====
    public static Bitmap GlassBmp, PanelGlassBmp, BinGlassBmp, BinPanelGlassBmp;

    // ===== Layout =====
    public const int BarH = 2;
    public const int StartY = 6;
    public static int TextH(Font f){return TextRenderer.MeasureText("X",f).Height+(f.Size>=10?2:1);}
    public static int Gap(Font f){return Math.Max(2,(int)(f.Size*0.25f));}
    public static int Sp(Font f){return Gap(f)*2;}

    // ===== Init =====
    public static void Init(){
        IsLight=(int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0)??0)==1;
        CreateFonts();
        Apply();
        CreateGlass();
    }
    public static void Toggle(bool light){IsLight=light;Apply();SwapGlass();}

    static void CreateFonts(){
        if(TitleFont!=null){TitleFont.Dispose();BodyFont.Dispose();SmallFont.Dispose();SmallRegFont.Dispose();ItalicFont.Dispose();BodyRegFont.Dispose();SmallBoldFont9.Dispose();ModelFont.Dispose();}
        int d = SmallFontMode ? 2 : 0;
        TitleFont   =new Font("Trebuchet MS",Math.Max(8,12-d),FontStyle.Bold);
        BodyFont    =new Font("Trebuchet MS",Math.Max(8,10-d),FontStyle.Bold);
        BodyRegFont =new Font("Trebuchet MS",Math.Max(8,10-d),FontStyle.Regular);
        SmallFont   =new Font("Trebuchet MS",Math.Max(8,8-d),FontStyle.Bold);
        SmallRegFont=new Font("Trebuchet MS",Math.Max(8,8-d),FontStyle.Regular);
        ItalicFont  =new Font("Trebuchet MS",Math.Max(8,8-d),FontStyle.Italic);
        SmallBoldFont9=new Font("Trebuchet MS",Math.Max(8,9-d),FontStyle.Bold);
        ModelFont   =new Font("Trebuchet MS",6,FontStyle.Italic); // hardcoded 6
    }

    static void Apply(){
        if(IsLight){
            Fg=Color.FromArgb(0,0,0); Dim=Color.FromArgb(110,110,110); Accent=Color.FromArgb(0,55,150);
            FormBg=Color.White; PanelBg=Color.FromArgb(220,220,230); BarBg=Color.FromArgb(200,200,210);
            SepColor=Color.FromArgb(180,180,190);
        }else{
            Fg=Color.FromArgb(255,255,255); Dim=Color.FromArgb(160,255,255,255); Accent=Color.FromArgb(255,185,0);
            FormBg=Color.Black; PanelBg=Color.FromArgb(35,35,45); BarBg=Color.FromArgb(38,38,38);
            SepColor=Color.FromArgb(60,255,255,255);
        }
        if(IsLight){
            BinFormBg=Color.FromArgb(210,220,240); BinPanelBg=Color.FromArgb(190,200,225);
            BinFg=Color.FromArgb(0,0,0); BinDim=Color.FromArgb(80,80,80); BinAccent=Color.FromArgb(0,50,135);
        }else{
            BinFormBg=Color.FromArgb(20,25,45); BinPanelBg=Color.FromArgb(30,36,58);
            BinFg=Color.FromArgb(255,255,255); BinDim=Color.FromArgb(120,255,255,255); BinAccent=Color.FromArgb(255,185,0);
        }
    }

    // ===== Glass rendering (lazy + reduced resolution) =====
    const int GW = 120, GH = 120;
    static Bitmap LightGlassBmp, DarkGlassBmp, LightPanelBmp, DarkPanelBmp, LightBinBmp, DarkBinBmp, LightBinPanelBmp, DarkBinPanelBmp;
    static bool glassLightReady, glassDarkReady;

    static void CreateGlass(){
        if(IsLight) RenderLight(); else RenderDark();
        SwapGlass();
    }
    static void RenderLight(){
        if(glassLightReady)return;
        LightGlassBmp = RenderGlass(244,241,236, 1.0f,1.0f);
        LightPanelBmp = RenderGlass(238,235,230, 0.3f,0.3f);
        LightBinBmp   = RenderGlass(236,226,210, 1.0f,1.0f);
        LightBinPanelBmp = RenderGlass(230,220,205, 0.3f,0.3f);
        glassLightReady=true;
    }
    static void RenderDark(){
        if(glassDarkReady)return;
        DarkGlassBmp  = RenderGlass(36,33,28,   1.0f,1.0f);
        DarkPanelBmp  = RenderGlass(30,27,23,   0.3f,0.3f);
        DarkBinBmp       = RenderGlass(28,32,48,   1.0f,1.0f);
        DarkBinPanelBmp  = RenderGlass(22,26,38,   0.3f,0.3f);
        glassDarkReady=true;
    }
    static void SwapGlass(){
        if(IsLight){if(!glassLightReady)RenderLight();}else{if(!glassDarkReady)RenderDark();}
        GlassBmp = IsLight ? LightGlassBmp : DarkGlassBmp;
        PanelGlassBmp = IsLight ? LightPanelBmp : DarkPanelBmp;
        BinGlassBmp = IsLight ? LightBinBmp : DarkBinBmp;
        BinPanelGlassBmp = IsLight ? LightBinPanelBmp : DarkBinPanelBmp;
    }

    static int Hash(int px, int py){unchecked{int h=px*374761393+py*668265263;h=(h^(h>>13))*1274126177;return h^(h>>16);}}

    static Bitmap RenderGlass(int baseR, int baseG, int baseB, float edgeMul, float specMul){
        var bmp = new Bitmap(GW, GH);
        for(int y=0;y<GH;y++){
            for(int x=0;x<GW;x++){
                int n1=Hash(x,y), n2=Hash(y+999,x+777);
                float nf1=(float)(n1&0x3FF)/1024f, nf2=(float)(n2&0x3FF)/1024f;

                int r=baseR,g=baseG,b=baseB;

                // Brush strokes
                int stroke=(int)((nf1*14f-7f)*edgeMul);
                if(nf2<0.12f)stroke+=(int)(4*edgeMul);else if(nf2>0.88f)stroke-=(int)(3*edgeMul);
                r+=stroke;g+=stroke;b+=stroke;

                // Grain
                int grain=(int)((((n1>>4)&0xFF)/32f-4f)*edgeMul);
                r+=grain;g+=grain;b+=grain;

                // Edge shading (dark glass: glow; light glass: shadow)
                int dT=y,dB=GH-y,dL=x,dR=GW-x;
                int edgeDir=(baseR+baseG+baseB)<300?1:-1; // dark base → glow, light base → shadow
                if(dT<10){int v=(int)((10-dT)*3*edgeMul); r+=edgeDir*v;g+=edgeDir*v;b+=edgeDir*v;}
                if(dL<10){int v=(int)((10-dL)*2*edgeMul); r+=edgeDir*v;g+=edgeDir*v;b+=edgeDir*v;}
                if(dR<10){int v=(int)((10-dR)*2*edgeMul); r+=edgeDir*v;g+=edgeDir*v;b+=edgeDir*v;}
                if(dB<12){int v=(int)((12-dB)*3*edgeMul); r+=edgeDir*v;g+=edgeDir*v;b+=edgeDir*v;}

                // Specular
                int spec=0;
                if(nf1>0.975f)spec=(int)(14f*specMul);
                if(nf2>0.985f&&x<GW/3&&y<GH/2)spec=Math.Max(spec,(int)(20f*specMul));
                if(nf1>0.992f&&x>GW/3&&y<GH/3)spec=Math.Max(spec,(int)(26f*specMul));
                r=Math.Min(255,r+spec);g=Math.Min(255,g+spec);b=Math.Min(255,b+spec);

                // Subtle gradient
                float grad=(float)x/GW,grad2=(float)y/GH;
                int shift=(int)((grad-0.5f)*3f*edgeMul+(grad2-0.5f)*2f*edgeMul);
                r+=shift;b-=shift;

                r=Math.Max(0,Math.Min(255,r));
                g=Math.Max(0,Math.Min(255,g));
                b=Math.Max(0,Math.Min(255,b));

                bmp.SetPixel(x,y,Color.FromArgb(255,r,g,b));
            }
        }
        return bmp;
    }

    static Icon widgetIcon;
    public static Icon GetWidgetIcon(){ if(widgetIcon==null) try{widgetIcon=new Icon(AppDomain.CurrentDomain.BaseDirectory+"Prism-icon.ico");}catch{} return widgetIcon; }

    // ===== Template: Monitor =====
    public static Form NewMonitorForm(string title, Point loc, float opacity, bool binStyle=false){
        var bg=binStyle?BinGlassBmp:GlassBmp;
        var bc=binStyle?BinFormBg:FormBg;
        var f=new Form{Text=title,Location=loc,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,TopMost=false,ShowInTaskbar=false,BackColor=bc,BackgroundImage=bg,BackgroundImageLayout=ImageLayout.Stretch,Opacity=opacity,ShowIcon=true,Icon=GetWidgetIcon()};
        f.Shown+=(s,e)=>{W.Round(f);DebugOverlay.Attach(f);};
        return f;
    }

    // ===== Template: TopBar =====
    public static Form NewTopBarForm(Point loc, float opacity, bool binStyle=false){
        var bg=binStyle?BinGlassBmp:GlassBmp;
        var bc=binStyle?BinFormBg:FormBg;
        var f=new Form{Text="TopBar",Size=new Size(260,0),Location=loc,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,TopMost=false,ShowInTaskbar=false,BackColor=bc,BackgroundImage=bg,BackgroundImageLayout=ImageLayout.Stretch,Opacity=opacity,ShowIcon=true,Icon=GetWidgetIcon()};
        f.Shown+=(s,e)=>{W.Round(f);DebugOverlay.Attach(f);};
        f.FormClosed+=(s,e)=>Application.Exit();
        return f;
    }

    // ===== Template: Bin =====
    public static Form NewBinForm(Point loc, float opacity){
        var f=new Form{Text="Recycle Bin",Size=new Size(200,150),Location=loc,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,TopMost=false,ShowInTaskbar=false,BackColor=BinFormBg,BackgroundImage=BinGlassBmp,BackgroundImageLayout=ImageLayout.Stretch,Opacity=opacity,AllowDrop=false};
        f.Shown+=(s,e)=>{W.Round(f);DebugOverlay.Attach(f);};
        return f;
    }

    // ===== Shared helpers =====
    public static Panel Sep(int w, int h, int x, int y){
        return new Panel{Size=new Size(w,h),Location=new Point(x,y),BackColor=SepColor};
    }

    public static Panel MonitorPanel(int x, int y){
        var p=new Panel{Location=new Point(x,y),BackColor=Color.Transparent};
        p.BackgroundImage=PanelGlassBmp;
        p.BackgroundImageLayout=ImageLayout.Stretch;
        return p;
    }

    // Apply theme + glass to a Monitor-style form
    public static void ApplyMonitor(Form f, Panel content, System.Collections.Generic.List<Panel> seps, bool binStyle=false){
        f.BackColor=binStyle?BinFormBg:FormBg;
        f.BackgroundImage=binStyle?BinGlassBmp:GlassBmp;
        foreach(Control c in f.Controls){
            if(c is Label)((Label)c).BackColor=Color.Transparent;
            if(c is Label)((Label)c).ForeColor=Fg;
        }
        if(content!=null){
            content.BackgroundImage=binStyle?BinPanelGlassBmp:PanelGlassBmp;
            foreach(Control c in content.Controls){
                if(c is Label)((Label)c).BackColor=Color.Transparent;
                if(c is Label)((Label)c).ForeColor=Fg;
            }
        }
        if(seps!=null)foreach(var s in seps)s.BackColor=SepColor;
    }
}
