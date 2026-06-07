using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

// ============================================================
// Theme — dock-focused (Bin glass style)
// ============================================================
public static class Theme
{
    public static bool IsLight;
    public static Font IconFont;
    public static Color Fg, Dim, Accent, FormBg, PanelBg, SepColor;
    public static Bitmap GlassBmp, PanelGlassBmp;

    // Render glass at arbitrary size (for menus, etc.)
    public static Bitmap RenderGlassAtSize(int w, int h)
    {
        float scaleX = (float)w / GW, scaleY = (float)h / GH;
        int baseR, baseG, baseB;
        float edgeMul, specMul;
        if (IsLight) { baseR = 236; baseG = 226; baseB = 210; edgeMul = 1.0f; specMul = 1.0f; }
        else { baseR = 28; baseG = 32; baseB = 48; edgeMul = 1.0f; specMul = 1.0f; }

        var bmp = new Bitmap(w, h);
        for (int y = 0; y < h; y++)
        {
            float gy = y / scaleY; // map back to glass-space Y
            for (int x = 0; x < w; x++)
            {
                float gx = x / scaleX; // map back to glass-space X
                int igx = (int)gx, igy = (int)gy;
                int n1 = Hash(igx, igy), n2 = Hash(igy + 999, igx + 777);
                float nf1 = (float)(n1 & 0x3FF) / 1024f, nf2 = (float)(n2 & 0x3FF) / 1024f;

                int r = baseR, g = baseG, b = baseB;

                int stroke = (int)((nf1 * 14f - 7f) * edgeMul);
                if (nf2 < 0.12f) stroke += (int)(4 * edgeMul);
                else if (nf2 > 0.88f) stroke -= (int)(3 * edgeMul);
                r += stroke; g += stroke; b += stroke;

                int grain = (int)((((n1 >> 4) & 0xFF) / 32f - 4f) * edgeMul);
                r += grain; g += grain; b += grain;

                int dT = igy, dB = GH - igy, dL = igx, dR = GW - igx;
                int edgeDir = (baseR + baseG + baseB) < 300 ? 1 : -1;
                if (dT < 10) { int v = (int)((10 - dT) * 3 * edgeMul); r += edgeDir * v; g += edgeDir * v; b += edgeDir * v; }
                if (dL < 10) { int v = (int)((10 - dL) * 2 * edgeMul); r += edgeDir * v; g += edgeDir * v; b += edgeDir * v; }
                if (dR < 10) { int v = (int)((10 - dR) * 2 * edgeMul); r += edgeDir * v; g += edgeDir * v; b += edgeDir * v; }
                if (dB < 12) { int v = (int)((12 - dB) * 3 * edgeMul); r += edgeDir * v; g += edgeDir * v; b += edgeDir * v; }

                int spec = 0;
                if (nf1 > 0.975f) spec = (int)(14f * specMul);
                if (nf2 > 0.985f && igx < GW / 3 && igy < GH / 2) spec = Math.Max(spec, (int)(20f * specMul));
                if (nf1 > 0.992f && igx > GW / 3 && igy < GH / 3) spec = Math.Max(spec, (int)(26f * specMul));
                r = Math.Min(255, r + spec); g = Math.Min(255, g + spec); b = Math.Min(255, b + spec);

                float grad = (float)igx / GW, grad2 = (float)igy / GH;
                int shift = (int)((grad - 0.5f) * 3f * edgeMul + (grad2 - 0.5f) * 2f * edgeMul);
                r += shift; b -= shift;

                r = Math.Max(0, Math.Min(255, r));
                g = Math.Max(0, Math.Min(255, g));
                b = Math.Max(0, Math.Min(255, b));

                bmp.SetPixel(x, y, Color.FromArgb(255, r, g, b));
            }
        }
        return bmp;
    }

    public static void Init(){
        IsLight=(int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0)??0)==1;
        IconFont=new Font("Trebuchet MS",8);
        Apply();
        CreateGlass();
    }

    static void Apply(){
        if(IsLight){
            Fg=Color.FromArgb(0,0,0); Dim=Color.FromArgb(110,110,110); Accent=Color.FromArgb(0,55,150);
            FormBg=Color.FromArgb(210,220,240); PanelBg=Color.FromArgb(190,200,225);
            SepColor=Color.FromArgb(180,180,190);
        }else{
            Fg=Color.FromArgb(255,255,255); Dim=Color.FromArgb(160,255,255,255); Accent=Color.FromArgb(255,185,0);
            FormBg=Color.FromArgb(20,25,45); PanelBg=Color.FromArgb(30,36,58);
            SepColor=Color.FromArgb(60,80,120);
        }
    }

    // ===== Glass (Bin style) =====
    const int GW=800, GH=100;
    static Bitmap LightBmp, DarkBmp, LightPanelBmp, DarkPanelBmp;

    static void CreateGlass(){
        LightBmp     = RenderGlass(236,226,210, 1.0f,1.0f);
        DarkBmp      = RenderGlass(28,32,48,   1.0f,1.0f);
        LightPanelBmp= RenderGlass(230,220,205, 0.3f,0.3f);
        DarkPanelBmp = RenderGlass(22,26,38,   0.3f,0.3f);
        GlassBmp = IsLight ? LightBmp : DarkBmp;
        PanelGlassBmp = IsLight ? LightPanelBmp : DarkPanelBmp;
    }

    static int Hash(int x,int y){unchecked{int h=x*374761393+y*668265263;h=(h^(h>>13))*1274126177;return h^(h>>16);}}

    static Bitmap RenderGlass(int baseR, int baseG, int baseB, float edgeMul, float specMul){
        var bmp=new Bitmap(GW,GH);
        for(int y=0;y<GH;y++)for(int x=0;x<GW;x++){
            int n1=Hash(x,y),n2=Hash(y+999,x+777);
            float nf1=(float)(n1&0x3FF)/1024f,nf2=(float)(n2&0x3FF)/1024f;
            int r=baseR,g=baseG,b=baseB;
            int stroke=(int)((nf1*14f-7f)*edgeMul);
            if(nf2<0.12f)stroke+=(int)(4*edgeMul);else if(nf2>0.88f)stroke-=(int)(3*edgeMul);
            r+=stroke;g+=stroke;b+=stroke;
            int grain=(int)((((n1>>4)&0xFF)/32f-4f)*edgeMul);r+=grain;g+=grain;b+=grain;
            int dT=y,dB=GH-y,dL=x,dR=GW-x;
            int edgeDir=(baseR+baseG+baseB)<300?1:-1;
            if(dT<10){int v=(int)((10-dT)*3*edgeMul);r+=edgeDir*v;g+=edgeDir*v;b+=edgeDir*v;}
            if(dL<10){int v=(int)((10-dL)*2*edgeMul);r+=edgeDir*v;g+=edgeDir*v;b+=edgeDir*v;}
            if(dR<10){int v=(int)((10-dR)*2*edgeMul);r+=edgeDir*v;g+=edgeDir*v;b+=edgeDir*v;}
            if(dB<12){int v=(int)((12-dB)*3*edgeMul);r+=edgeDir*v;g+=edgeDir*v;b+=edgeDir*v;}
            int spec=0;
            if(nf1>0.975f)spec=(int)(14f*specMul);
            if(nf2>0.985f&&x<GW/3&&y<GH/2)spec=Math.Max(spec,(int)(20f*specMul));
            if(nf1>0.992f&&x>GW/3&&y<GH/3)spec=Math.Max(spec,(int)(26f*specMul));
            r=Math.Min(255,r+spec);g=Math.Min(255,g+spec);b=Math.Min(255,b+spec);
            float grad=(float)x/GW,grad2=(float)y/GH;
            int shift=(int)((grad-0.5f)*3f*edgeMul+(grad2-0.5f)*2f*edgeMul);
            r+=shift;b-=shift;
            r=Math.Max(0,Math.Min(255,r));g=Math.Max(0,Math.Min(255,g));b=Math.Max(0,Math.Min(255,b));
            bmp.SetPixel(x,y,Color.FromArgb(255,r,g,b));
        }
        return bmp;
    }
}
