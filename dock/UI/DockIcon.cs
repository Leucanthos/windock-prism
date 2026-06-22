using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// ============================================================
// DockIcon — single glass-tile icon with hover magnification
// ============================================================

class DockIcon : IDisposable
{
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint fl);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h,int a,ref int v,int s);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc,int idx);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int idx, int val);
    const int LOGPIXELSX=88;

    const uint SWP_NOZORDER=0x4,SWP_NOACTIVATE=0x10,SWP_SHOWWINDOW=0x40;

    static Icon dockFormIcon;
    public static void SetFormIcon(string icoPath)
    {
        try { if (System.IO.File.Exists(icoPath)) dockFormIcon = new Icon(icoPath); } catch { }
    }

    public Form Form;
    public PictureBox pic;
    ToolTip toolTip;
    int baseSize, pad, curSize;
    public int BaseX;
    public IntPtr HWnd;
    public int Pid;
    public string PinPath;
    public bool Pinned;
    Bitmap iconBmp;
    int badgeCount;
    Microsoft.Win32.UserPreferenceChangedEventHandler _themeHandler; // tracked for cleanup

    System.Windows.Forms.Timer magTimer;
    internal float targetScale=1f, curScale=1f; // internal: DockManager uses for elastic lens

    public DockIcon(int logicalSize=48, int padding=10){
        // Scale to physical pixels based on system DPI
        float scale=DpiX/96f;
        baseSize=(int)(logicalSize*scale); pad=(int)(padding*scale); curSize=baseSize;
        int iconBmpSize=(int)(32*scale); // icon bitmap at DPI resolution
        int size=baseSize; // local alias for lambda capture
        Form=new Form{FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,
            TopMost=true,ShowInTaskbar=false,AutoScaleMode=AutoScaleMode.None,Opacity=0.82,
            BackColor=Theme.FormBg,BackgroundImage=Theme.GlassBmp,BackgroundImageLayout=ImageLayout.Stretch,
            ShowIcon=false,Icon=dockFormIcon,Text=""};
        Form.HandleCreated+=(s,e)=>{SetWindowPos(Form.Handle,IntPtr.Zero,0,0,size,size,SWP_NOZORDER|SWP_NOACTIVATE);
            int ex=GetWindowLong(Form.Handle,-20);SetWindowLong(Form.Handle,-20,ex|0x80);}; // WS_EX_TOOLWINDOW
        Form.Shown+=(s,e)=>{int c=2;DwmSetWindowAttribute(Form.Handle,33,ref c,4);Form.BackgroundImage=Theme.GlassBmp;Form.BackColor=Theme.FormBg;};
        // Follow system theme changes
        _themeHandler = (s, e) => {
            Action apply=delegate{
                if (Form.IsDisposed || pic.IsDisposed) return; // guard against disposed icons
                var light=(int)(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0)??0)==1;
                if(light!=Theme.IsLight){Theme.IsLight=light;Theme.Init();Form.BackgroundImage=Theme.GlassBmp;Form.BackColor=Theme.FormBg;pic.Invalidate();}
            };
            if(Form.InvokeRequired)Form.BeginInvoke(apply);else apply();
        };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += _themeHandler;
        pic=new PictureBox{Size=new Size(size-pad*2,size-pad*2),Location=new Point(pad,pad),
            BackColor=Color.Transparent,SizeMode=PictureBoxSizeMode.Zoom,Cursor=Cursors.Hand};
        // MUST be after pic creation: map pic → this for CheckMouseOverAny
        picMap[pic] = this;
        pic.MouseEnter+=(s,e)=>{if(!MenuOpen && !RefreshLock){targetScale=1.35f; if(HoverChanged!=null)HoverChanged(this,true);}};
        pic.MouseLeave+=(s,e)=>{if(!RefreshLock){targetScale=1f; if(HoverChanged!=null)HoverChanged(this,false);}};
        pic.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right)EventLog.Info("RightClick pid="+Pid+" pin="+(PinPath??"-"));};
        Form.Controls.Add(pic);

        // Badge drawn on PictureBox Paint event (always on top)
        pic.Paint+=DrawBadgeOnPic;

        magTimer=new System.Windows.Forms.Timer{Interval=16}; // ~60fps
        magTimer.Tick+=(s,e)=>TickMag();
    }

    void DrawBadgeOnPic(object s, PaintEventArgs e){
        if(badgeCount<1)return;
        var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int r=(int)(10*curScale); // scale with magnification
        int x=pic.Width-r*2-1,y=1;
        var bg=Theme.IsLight?Color.White:Color.Black;
        var fg=Theme.IsLight?Color.Black:Color.White;
        using(var br=new SolidBrush(bg))g.FillEllipse(br,x,y,r*2,r*2);
        float fs=7f*curScale;
        using(var f=new Font("Trebuchet MS",fs,FontStyle.Bold))
        using(var tb=new SolidBrush(fg)){
            var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
            g.DrawString(badgeCount.ToString(),f,tb,new RectangleF(x,y,r*2,r*2),sf);
        }
    }

    public void SetBadge(int count){badgeCount=count;pic.Invalidate();}
    void SetBadgeTheme(){pic.Invalidate();}

    public static Bitmap IconToBmpAtDpi(Icon ico){
        if(ico==null)return null; int sz=(int)(32*DpiX/96f);var b=new Bitmap(sz,sz);
        using(var g=Graphics.FromImage(b)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawIcon(ico,new Rectangle(0,0,sz,sz));}
        return b;
    }
    public void SetIcon(Bitmap bmp){iconBmp=bmp;pic.Image=bmp;}
    Action _onLeftClick;
    Action<Point> _onRightClick;
    bool _clicksBound;
    bool _menuOpen;
    public static bool RefreshLock;
    public static bool MenuOpen;
    static Dictionary<PictureBox, DockIcon> picMap = new Dictionary<PictureBox, DockIcon>();
    static DockIcon FindByPictureBox(PictureBox p) { DockIcon d; picMap.TryGetValue(p, out d); return d; }

    public static event Action<DockIcon, bool> HoverChanged;

    public void SetClick(Action onClick){_onLeftClick=onClick;}
    public void SetRightClick(Action<Point> onRightClick){_onRightClick=onRightClick;}
    public void OnMenuClosed(){MenuOpen=false;magTimer.Start();targetScale=1f; CheckMouseOverAny();}

    static void CheckMouseOverAny()
    {
        try
        {
            var scrPos = Cursor.Position;
            foreach (Form f in Application.OpenForms)
            {
                if (f.Controls.Count == 0) continue;
                var pic = f.Controls[0] as PictureBox;
                if (pic == null || pic.IsDisposed) continue;
                var pt = pic.PointToClient(scrPos);
                if (pic.ClientRectangle.Contains(pt))
                {
                    var di = FindByPictureBox(pic);
                    if (di != null && !MenuOpen) { di.targetScale = 1.35f; if (HoverChanged != null) HoverChanged(di, true); }
                    return;
                }
            }
        }
        catch { }
    }
    public void BindClicks(){
        if(_clicksBound)return;_clicksBound=true;
        pic.Click+=(s,e)=>{
            var me=e as MouseEventArgs;
            if(me!=null&&me.Button==MouseButtons.Right){
                if(_onRightClick!=null){
                    MenuOpen=true;
                    targetScale=1.35f;curScale=1.35f;ApplyScale();magTimer.Stop();
                    _onRightClick(pic.PointToScreen(me.Location));
                }
            }
            else if(_onLeftClick!=null)_onLeftClick();
        };
    }
    public void SetTooltip(string text){
        if(toolTip==null){toolTip=new ToolTip{ShowAlways=true,InitialDelay=200};toolTip.SetToolTip(pic,text);}
        else toolTip.SetToolTip(pic,text);
    }
    public void Show(){
        if (Form == null || Form.IsDisposed) return;
        Form.Show();magTimer.Start();
        int ex=GetWindowLong(Form.Handle,-20);if((ex&0x80)==0)SetWindowLong(Form.Handle,-20,ex|0x80);
    }
    public void Hide(){magTimer.Stop();Form.Hide();SetWindowPos(Form.Handle,IntPtr.Zero,0,0,0,0,SWP_NOZORDER|SWP_NOACTIVATE|0x80);} // SWP_HIDEWINDOW=0x80

    void TickMag(){
        float diff=targetScale-curScale;
        if(Math.Abs(diff)<0.005f){curScale=targetScale;return;}
        curScale+=diff*0.2f;
        ApplyScale();
        if(DebugMode.On) System.IO.File.AppendAllText(@"C:\temp\_dock_mag.txt",
            string.Format("{0:HH:mm:ss.fff} pid={1} target={2:F3} cur={3:F3}\n",DateTime.Now,Pid,targetScale,curScale));
    }

    void ApplyScale(){
        if (!_posSet) return; // skip until SetBasePos has been called
        int ns=(int)(baseSize*curScale);
        if(ns==curSize)return;
        curSize=ns;
        int px=(int)(pad*curScale);
        Form.Size=new Size(ns,ns);
        pic.Size=new Size(ns-px*2,ns-px*2);
        pic.Location=new Point(px,px);
        if(badgeCount>1)pic.Invalidate(); // repaint badge at new scale
        int sx=BaseX-(ns-baseSize)/2;
        int sy=baseY-(ns-baseSize);
        SetWindowPos(Form.Handle,IntPtr.Zero,sx,sy,ns,ns,SWP_NOZORDER|SWP_NOACTIVATE|SWP_SHOWWINDOW);
    }

    int baseY;
    bool _posSet; // guard: ApplyScale skips until SetBasePos called at least once
    public void SetBasePos(int x,int y){
        BaseX=x; baseY=y; curSize=baseSize; _posSet=true;
        int sx=curSize==baseSize?x:x-(curSize-baseSize)/2;
        int sy=curSize==baseSize?y:y-(curSize-baseSize);
        Form.Size=new Size(baseSize,baseSize);
        SetWindowPos(Form.Handle,IntPtr.Zero,sx,sy,baseSize,baseSize,SWP_NOZORDER|SWP_NOACTIVATE);
    }

    // ===== Debug =====
    public static int DpiX; static DockIcon(){var dc=GetDC(IntPtr.Zero);DpiX=GetDeviceCaps(dc,LOGPIXELSX);ReleaseDC(IntPtr.Zero,dc);}
    public int BadgeCount{get{return badgeCount;}}
    public string Dump(){
        return string.Format("DockIcon: baseSize={0} curSize={1} pad={2} scale={3:F2} target={4:F2} Form=({5},{6}) Pic=({7},{8}) badge={9} DPI={10:F0}",
            baseSize,curSize,pad,curScale,targetScale,Form.Width,Form.Height,pic.Width,pic.Height,badgeCount,DpiX);
    }

    public void ResetScale(){
        targetScale=1f; curScale=1f; curSize=baseSize;
        Form.Size=new Size(baseSize,baseSize);
        pic.Size=new Size(baseSize-pad*2,baseSize-pad*2);
        pic.Location=new Point(pad,pad);
    }

    public void UpdateTheme(){
        if(Form.BackgroundImage!=Theme.GlassBmp){Form.BackgroundImage=Theme.GlassBmp;Form.BackColor=Theme.FormBg;}
        SetBadgeTheme();
    }

    // ===== Dispose with theme event cleanup =====
    public void Dispose(){
        magTimer.Stop();
        if (_themeHandler != null) {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= _themeHandler;
            _themeHandler = null;
        }
        if (pic != null) { picMap.Remove(pic); pic.Dispose(); pic = null; }
        if (Form != null && !Form.IsDisposed) { Form.Close(); Form.Dispose(); }
    }
}
