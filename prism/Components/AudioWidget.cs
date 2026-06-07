using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// ============================================================
// Audio Widget — Volume + Brightness combined
// ============================================================

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorCoClass { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator {
    int EnumAudioEndpoints(int d, int m, out IntPtr p);
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    int GetDevice(string id, out IMMDevice d2);
    int RegisterEndpointNotificationCallback(IntPtr c);
    int UnregisterEndpointNotificationCallback(IntPtr c);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice {
    int Activate(ref Guid id, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    int OpenPropertyStore(int a, out IntPtr p);
    int GetId(out IntPtr p);
    int GetState(out int s);
}
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume {
    int RegisterControlChangeNotify(IntPtr n);
    int UnregisterControlChangeNotify(IntPtr n);
    int GetChannelCount(out int c);
    int SetMasterVolumeLevel(float l, ref Guid g);
    int SetMasterVolumeLevelScalar(float l, ref Guid g);
    int GetMasterVolumeLevel(out float l);
    int GetMasterVolumeLevelScalar(out float l);
    int SetChannelVolumeLevel(uint c, float l, ref Guid g);
    int SetChannelVolumeLevelScalar(uint c, float l, ref Guid g);
    int GetChannelVolumeLevel(uint c, out float l);
    int GetChannelVolumeLevelScalar(uint c, out float l);
    int SetMute(bool m, ref Guid g);
    int GetMute(out bool m);
}

static class AudioWidget
{
    static Form form; static Panel panel, volBar, briBar; static Label volVal, briVal; static List<Panel> seps=new List<Panel>();
    static IAudioEndpointVolume audioVol;

    // --- Volume ---
    static float GetVol(){if(audioVol==null)return 0;float v;audioVol.GetMasterVolumeLevelScalar(out v);return v;}
    static void SetVol(float v){if(audioVol!=null){var g=Guid.Empty;audioVol.SetMasterVolumeLevelScalar(Math.Max(0,Math.Min(1,v)),ref g);}}

    // --- Brightness ---
    static int GetBri(){
        try{using(var s=new ManagementObjectSearcher(@"root\wmi","SELECT CurrentBrightness FROM WmiMonitorBrightness")){foreach(ManagementObject o in s.Get())return (byte)o["CurrentBrightness"];}}catch{}return 50;
    }
    static void SetBri(int v){
        v=Math.Max(0,Math.Min(100,v));
        try{using(var s=new ManagementObjectSearcher(@"root\wmi","SELECT * FROM WmiMonitorBrightnessMethods")){foreach(ManagementObject o in s.Get()){o.InvokeMethod("WmiSetBrightness",new object[]{uint.MaxValue,v});return;}}}catch{}
    }

    public static Form Create(Settings cfg, Form above=null){
        try{
            var enu=new MMDeviceEnumeratorCoClass() as IMMDeviceEnumerator;
            IMMDevice dev;enu.GetDefaultAudioEndpoint(0,0,out dev);
            Guid iid=new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
            object obj;dev.Activate(ref iid,0x17,IntPtr.Zero,out obj);
            audioVol=obj as IAudioEndpointVolume;
        }catch{}

        Font bf=Theme.BodyFont, sf=Theme.SmallFont;
        int bH=Theme.TextH(bf), sH=Theme.TextH(sf), barH=4, gap=Theme.Gap(bf), sp=Theme.Sp(bf);

        int FW=260, y=Theme.StartY;
        int lw=70, vw=50, bx=sp+lw+2, bw=252-2*sp-lw-vw-10, vx=bx+bw+4;

        int posY=above!=null?above.Bounds.Bottom+20:cfg.VolY;
        form=Theme.NewMonitorForm("Audio",new Point(cfg.TopX,posY),cfg.TopOpacity,true);
        panel=Theme.MonitorPanel(sp,y);int py=gap;

        // === Volume section ===
        panel.Controls.Add(W.Lbl("Volume",bf,Theme.Fg,lw,bH,sp,py));
        volBar=W.Bar(Theme.Accent,bw,barH,bx,py+4);panel.Controls.Add(volBar);
        volBar.Cursor=Cursors.Hand;
        volBar.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){SetVol((float)e.X/bw);RefreshVol();}};
        volBar.MouseMove+=(s,e)=>{if(e.Button==MouseButtons.Left){SetVol((float)e.X/bw);RefreshVol();}};
        volVal=W.Lbl("50%",bf,Theme.Accent,vw,bH,vx,py);volVal.TextAlign=ContentAlignment.MiddleRight;panel.Controls.Add(volVal);
        py+=bH;

        // Volume: [Min]-[-5]-[50%]-[+5]-[Max]
        AccentRow(panel,sf,sH,sp,py,
            new[]{"[Min]","[-5]","[50%]","[+5]","[Max]"},
            new[]{40,32,40,32,40},
            new Action[]{()=>{SetVol(0);RefreshVol();},()=>{SetVol(GetVol()-0.05f);RefreshVol();},()=>{SetVol(0.5f);RefreshVol();},()=>{SetVol(GetVol()+0.05f);RefreshVol();},()=>{SetVol(1);RefreshVol();}});
        py+=sH+2;

        // === Brightness section ===
        panel.Controls.Add(W.Lbl("Bright",bf,Theme.Fg,lw,bH,sp,py));
        briBar=W.Bar(Theme.Accent,bw,barH,bx,py+4);panel.Controls.Add(briBar);
        briBar.Cursor=Cursors.Hand;
        briBar.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){SetBri((int)((float)e.X/bw*100));RefreshBri();}};
        briBar.MouseMove+=(s,e)=>{if(e.Button==MouseButtons.Left){SetBri((int)((float)e.X/bw*100));RefreshBri();}};
        briVal=W.Lbl("50%",bf,Theme.Accent,vw,bH,vx,py);briVal.TextAlign=ContentAlignment.MiddleRight;panel.Controls.Add(briVal);
        py+=bH;

        // Brightness: [Min]-[-5]-[50%]-[+5]-[Max]
        AccentRow(panel,sf,sH,sp,py,
            new[]{"[Min]","[-5]","[50%]","[+5]","[Max]"},
            new[]{40,32,40,32,40},
            new Action[]{()=>{SetBri(0);RefreshBri();},()=>{SetBri(GetBri()-5);RefreshBri();},()=>{SetBri(50);RefreshBri();},()=>{SetBri(GetBri()+5);RefreshBri();},()=>{SetBri(100);RefreshBri();}});

        py+=sH+2;
        panel.Size=new Size(252,py);form.Controls.Add(panel);Theme.ApplyMonitor(form,panel,seps,true);
        form.Size=new Size(FW,y+py+gap);

        var volTimer=new System.Windows.Forms.Timer{Interval=2000};volTimer.Tick+=(s,e)=>RefreshVol();volTimer.Start();
        var briTimer=new System.Windows.Forms.Timer{Interval=8000};briTimer.Tick+=(s,e)=>RefreshBri();briTimer.Start();
        RefreshVol();RefreshBri();
        Program.ThemeChanged+=(light)=>{Theme.ApplyMonitor(form,panel,seps,true);W.BarColor(volBar,Theme.Accent);W.BarColor(briBar,Theme.Accent);};
        W.MakeDraggable(form,panel);
        return form;
    }

    static Label MakeBtn(string t,Font f,Color c,int w,int h,int x,int y){
        return new Label{Text=t,ForeColor=c,Font=f,AutoSize=false,TextAlign=ContentAlignment.MiddleCenter,Size=new Size(w,h),Location=new Point(x,y),Cursor=Cursors.Hand,BackColor=Color.Transparent};
    }

    static void AccentRow(Panel p, Font f, int h, int sp, int y, string[] labels, int[] widths, Action[] clicks){
        int x=sp;
        for(int i=0;i<labels.Length;i++){
            var btn=MakeBtn(labels[i],f,Theme.Accent,widths[i],h,x,y);
            var idx=i; btn.Click+=(s,e)=>clicks[idx]();
            p.Controls.Add(btn);
            x+=widths[i]+1;
            if(i<labels.Length-1){
                var dash=W.Lbl("-",f,Theme.Dim,10,h,x,y);p.Controls.Add(dash);x+=11;
            }
        }
    }

    static void RefreshVol(){float v=GetVol();volVal.Text=(int)(v*100)+"%";W.BarSet(volBar,(int)(v*100));}
    static void RefreshBri(){int b=GetBri();briVal.Text=b+"%";W.BarSet(briBar,b);}
}
