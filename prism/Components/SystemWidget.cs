using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Management;
using System.Windows.Forms;

// ============================================================
// System Widget — CPU / RAM / GPU (util+VRAM) / NPU (Compute+Mem)
// ============================================================
static class SystemWidget
{
    static Label cv,rv,gv,nv,tl; static Panel cb,rb,gb,nb,panel; static Form form; static List<Panel> seps=new List<Panel>();
    static PerformanceCounter cc,rc,fc;
    static string cpuModel="...",gpuModel="...",npuModel="Intel AI Boost",ramTotal="",gpuTotal="",npuTotal="";static string cachedTemp="...";static int tempTick=99;
    static long ramKB; static int cpuMaxMHz;

    // GPU/NPU LUIDs and memory counters
    static string GpuLuid="", NpuLuid="", IgpuLuid="";
    static PerformanceCounter memIgpu, memArc, memNpu;

    // Engine counter lists (re-scanned periodically)
    static List<PerformanceCounter> gpu3D = new List<PerformanceCounter>();
    static List<PerformanceCounter> npuComp = new List<PerformanceCounter>();
    static int gpuTick=99, npuTick=99;
    const int RESCAN=10;

    // XPU activity tracking via memory delta
    static long lastIgpuMem=0;
    static int xpuAct=0;
    static long igpuBaseline=0;
    static bool baselineSet=false;

    public static Form Create(Settings cfg, Form above=null){
        Font tf=Theme.TitleFont, bf=Theme.BodyFont, sf=Theme.SmallFont;
        int tH=Theme.TextH(tf), bH=Theme.TextH(bf), sH=Theme.TextH(sf);
        int barH=4, gap=Theme.Gap(bf), sp=Theme.Sp(bf);

        int posY=above!=null?above.Bounds.Top+above.Bounds.Height/2:cfg.SysY;
        int FW=240;
        form=Theme.NewMonitorForm("System",new Point(Program.SW-FW,posY),cfg.Opacity);
        try{cc=new PerformanceCounter("Processor","% Processor Time","_Total");}catch{}
        try{rc=new PerformanceCounter("Memory","% Committed Bytes In Use");}catch{}
        try{fc=new PerformanceCounter("Processor Information","% Processor Performance","_Total");}catch{}
        try{using(var s=new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor")){foreach(ManagementObject o in s.Get()){cpuMaxMHz=Convert.ToInt32(o["MaxClockSpeed"]);break;}}}catch{}

        // ── LUID Auto-detect (PerformanceCounterCategory-based) ──
        DetectLuid();

        // ── Build initial engine counters and warm up ──
        WarmupCounters();

        // ── Hardware models ──
        try{using(var s=new ManagementObjectSearcher("SELECT Name FROM Win32_Processor")){foreach(ManagementObject o in s.Get()){cpuModel=(string)o["Name"];break;}}}catch{}
        try{using(var s=new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem")){foreach(ManagementObject o in s.Get()){ramKB=Convert.ToInt64(o["TotalVisibleMemorySize"]);ramTotal=ramKB>1048576?((ramKB/1048576f).ToString("F1")+" GB"):((ramKB/1024f).ToString("F0")+" MB");break;}}}catch{}
        if(ramKB>0){float npuGB=ramKB/2097152f-0.2f; npuTotal=npuGB.ToString("F1")+" GB";}
        try{using(var s=new ManagementObjectSearcher("SELECT Name,AdapterRAM FROM Win32_VideoController")){foreach(ManagementObject o in s.Get()){gpuModel=(string)o["Name"]; var ram=o["AdapterRAM"]; if(ram!=null){long b=Convert.ToInt64(ram);gpuTotal=(b/1073741824f).ToString("F0")+" GB";}break;}}}catch{}

        int y=Theme.StartY;
        var btn=W.Lbl("[>]",tf,Theme.Accent,30,tH,5,y);btn.Cursor=Cursors.Hand;btn.Click+=(s,e)=>Process.Start("taskmgr");form.Controls.Add(btn);
        form.Controls.Add(W.Lbl("System Info",tf,Theme.Fg,198,tH,38,y));y+=tH+2;
        seps.Add(AddSep(232,1,sp,y));y+=2;

        int cw=232-2*sp, labelW=50, valW=132, barW=cw-labelW-valW-6;
        Font modelFont=Theme.ModelFont;
        Font rowFont=Theme.BodyRegFont;
        panel=Theme.MonitorPanel(sp,y);int py=gap;
        int barCY=bH/2-barH/2, valX=sp+labelW+2+barW+4;

        // CPU
        panel.Controls.Add(W.Lbl("CPU",rowFont,Theme.Fg,labelW,bH,sp,py));
        cb=W.Bar(Theme.Accent,barW,barH,sp+labelW+2,py+barCY);panel.Controls.Add(cb);
        cv=W.Lbl("0%",rowFont,Theme.Fg,valW,bH,valX,py);cv.TextAlign=ContentAlignment.MiddleRight;panel.Controls.Add(cv);
        py+=bH-2;
        panel.Controls.Add(W.Lbl("-------- "+cpuModel,modelFont,Theme.Dim,232-2*sp,10,sp,py));py+=10+gap*3;

        // RAM
        panel.Controls.Add(W.Lbl("RAM",sf,Theme.Fg,labelW,sH,sp,py));
        rb=W.Bar(Theme.Accent,barW,barH,sp+labelW+2,py+sH/2-barH/2);panel.Controls.Add(rb);
        rv=W.Lbl("0%",sf,Theme.Fg,valW,sH,valX,py);rv.TextAlign=ContentAlignment.MiddleRight;panel.Controls.Add(rv);
        py+=sH+gap;

        // GPU
        panel.Controls.Add(W.Lbl("GPU",rowFont,Theme.Fg,labelW,bH,sp,py));
        gb=W.Bar(Theme.Accent,barW,barH,sp+labelW+2,py+bH/2-barH/2);panel.Controls.Add(gb);
        gv=W.Lbl("0%",rowFont,Theme.Fg,valW,bH,valX,py);gv.TextAlign=ContentAlignment.MiddleRight;panel.Controls.Add(gv);
        py+=bH-2;
        panel.Controls.Add(W.Lbl("-------- "+gpuModel,modelFont,Theme.Dim,232-2*sp,10,sp,py));py+=10+gap*3;

        // NPU
        panel.Controls.Add(W.Lbl("NPU",rowFont,Theme.Fg,labelW,bH,sp,py));
        nb=W.Bar(Theme.Accent,barW,barH,sp+labelW+2,py+bH/2-barH/2);panel.Controls.Add(nb);
        nv=W.Lbl("0%",rowFont,Theme.Fg,valW,bH,valX,py);nv.TextAlign=ContentAlignment.MiddleRight;panel.Controls.Add(nv);
        py+=bH-2;
        panel.Controls.Add(W.Lbl("-------- "+npuModel,modelFont,Theme.Dim,232-2*sp,10,sp,py));py+=10+gap;

        // Thermal
        panel.Controls.Add(W.Lbl("Thermal",sf,Theme.Dim,labelW,sH,sp,py));
        tl=W.Lbl("...",sf,Theme.Fg,valW,sH,valX,py);tl.TextAlign=ContentAlignment.MiddleRight;panel.Controls.Add(tl);
        py+=sH+gap;

        panel.Size=new Size(232,py);form.Controls.Add(panel);Theme.ApplyMonitor(form,panel,seps);
        form.Size=new Size(FW,y+py+gap);

        var timer=new System.Windows.Forms.Timer{Interval=cfg.SysMs};timer.Tick+=(s,e)=>Refresh();timer.Start();Refresh();
        Program.ThemeChanged+=(light)=>{Theme.ApplyMonitor(form,panel,seps);W.BarColor(cb,Theme.Accent);W.BarColor(rb,Theme.Accent);W.BarColor(gb,Theme.Accent);W.BarColor(nb,Theme.Accent);};
        W.MakeDraggable(form,panel);return form;
    }
    static Panel AddSep(int w,int h,int x,int y){var p=Theme.Sep(w,h,x,y);form.Controls.Add(p);return p;}

    // ─── LUID Auto-detect ──────────────────────────────
    static void DetectLuid(){
        try{
            // Step 1: get mem instance names
            var memCat=new PerformanceCounterCategory("GPU Adapter Memory");
            string[] memNames=memCat.GetInstanceNames();
            if(memNames.Length==0) return;

            // Step 2: get engine instance names
            var engCat=new PerformanceCounterCategory("GPU Engine");
            string[] engNames=engCat.GetInstanceNames();

            // Step 3: classify each LUID
            foreach(string mn in memNames){
                string luid=mn.Split('_')[2];
                bool has3D=false, hasComp=false;
                int c3=0, cc=0;
                foreach(string en in engNames){
                    if(en.IndexOf(luid,StringComparison.OrdinalIgnoreCase)<0) continue;
                    if(en.IndexOf("engtype_3D",StringComparison.OrdinalIgnoreCase)>=0){has3D=true;c3++;}
                    if(en.IndexOf("engtype_Compute",StringComparison.OrdinalIgnoreCase)>=0){hasComp=true;cc++;}
                }

                // Create memory counter
                if(luid==memNames[0].Split('_')[2]/*first*/ || true){
                    try{
                        var pc=new PerformanceCounter("GPU Adapter Memory","Shared Usage",mn);
                        memCounters[luid]=pc;
                    }catch{}
                }

                Console.Error.WriteLine("LUID "+luid+" 3D="+c3+" Comp="+cc);

                // Classify
                if(hasComp&&!has3D&&string.IsNullOrEmpty(NpuLuid)){NpuLuid=luid;Console.Error.WriteLine("NPU LUID: "+luid);}
                if(has3D&&hasComp&&string.IsNullOrEmpty(IgpuLuid)){IgpuLuid=luid;Console.Error.WriteLine("iGPU LUID: "+luid);}
                if(has3D&&!hasComp&&string.IsNullOrEmpty(GpuLuid)){GpuLuid=luid;Console.Error.WriteLine("dGPU LUID: "+luid);}
            }

            // Fallback: if no dGPU, use iGPU
            if(string.IsNullOrEmpty(GpuLuid)&&!string.IsNullOrEmpty(IgpuLuid)){GpuLuid=IgpuLuid;Console.Error.WriteLine("GPU <- iGPU");}
            if(string.IsNullOrEmpty(gpuModel)) gpuModel="Intel Arc GPU";
        }
        catch(Exception ex){Console.Error.WriteLine("DetectLuid error: "+ex.Message);}
    }

    // Dictionary for memory counters
    static Dictionary<string,PerformanceCounter> memCounters = new Dictionary<string,PerformanceCounter>();

    static void WarmupCounters(){
        ScanGpu3D();
        ScanNpuComp();
        foreach(var pc in gpu3D) try{pc.NextValue();}catch{}
        foreach(var pc in npuComp) try{pc.NextValue();}catch{}
    }

    static void ScanGpu3D(){
        gpu3D.Clear();
        string target=!string.IsNullOrEmpty(GpuLuid)?GpuLuid:IgpuLuid;
        if(string.IsNullOrEmpty(target)) return;
        try{
            var cat=new PerformanceCounterCategory("GPU Engine");
            foreach(string n in cat.GetInstanceNames()){
                if(n.IndexOf(target,StringComparison.OrdinalIgnoreCase)>=0&&n.IndexOf("engtype_3D",StringComparison.OrdinalIgnoreCase)>=0){
                    try{gpu3D.Add(new PerformanceCounter("GPU Engine","Utilization Percentage",n));}catch{}
                }
            }
        }catch{}
        gpuTick=0;
        Console.Error.WriteLine("GPU 3D counters: "+gpu3D.Count);
    }

    static void ScanNpuComp(){
        npuComp.Clear();
        if(string.IsNullOrEmpty(NpuLuid)) return;
        try{
            var cat=new PerformanceCounterCategory("GPU Engine");
            foreach(string n in cat.GetInstanceNames()){
                if(n.IndexOf(NpuLuid,StringComparison.OrdinalIgnoreCase)>=0&&n.IndexOf("engtype_Compute",StringComparison.OrdinalIgnoreCase)>=0){
                    try{npuComp.Add(new PerformanceCounter("GPU Engine","Utilization Percentage",n));}catch{}
                }
            }
        }catch{}
        npuTick=0;
        Console.Error.WriteLine("NPU Compute counters: "+npuComp.Count);
    }

    static void Refresh(){
        // CPU
        try{int v=(int)cc.NextValue();string freq="";try{if(fc!=null&&cpuMaxMHz>0){float ghz=cpuMaxMHz*fc.NextValue()/100f/1000f;freq="  "+ghz.ToString("F2")+"GHz";}}catch{} cv.Text=v+"%"+freq;W.BarSet(cb,v);}catch{}
        // RAM
        try{int v=(int)rc.NextValue();rv.Text=v+"% ("+ramTotal+")";W.BarSet(rb,v);}catch{}

        // GPU + NPU
        RefreshGpu();
        RefreshNpu();

        // Temperature
        tempTick++;if(tempTick%4==0)ReadTemps();
        if(tl.Text!=cachedTemp&&cachedTemp!="...")tl.Text=cachedTemp;

        string engInfo="GPUeng:"+gpu3D.Count+" NPUeng:"+npuComp.Count;
        Dumper.Sys(cv.Text,rv.Text,gv.Text,nv.Text,tl.Text,engInfo,"",
            memCounters.ContainsKey(GpuLuid??""),memCounters.ContainsKey(NpuLuid??""));
    }

    static void RefreshGpu(){
        try{
            gpuTick++;
            if(gpuTick>=RESCAN||gpu3D.Count==0) ScanGpu3D();

            int total=0;
            // Use a separate list to avoid modifying while iterating
            var stale=new List<PerformanceCounter>();
            foreach(var pc in gpu3D){
                try{
                    float v=pc.NextValue();
                    if(v>=0) total+=(int)v;
                }catch{
                    stale.Add(pc);
                }
            }
            foreach(var pc in stale){try{pc.Dispose();}catch{} gpu3D.Remove(pc);}

            // Memory string
            string memStr="";
            if(!string.IsNullOrEmpty(IgpuLuid)&&memCounters.ContainsKey(IgpuLuid)){
                try{float mb=memCounters[IgpuLuid].NextValue()/1048576f;memStr+=" iGPU"+(int)mb+"MB";}catch{}
            }
            if(!string.IsNullOrEmpty(GpuLuid)&&GpuLuid!=IgpuLuid&&memCounters.ContainsKey(GpuLuid)){
                try{float mb=memCounters[GpuLuid].NextValue()/1048576f;memStr+=" Arc"+(int)mb+"MB";}catch{}
            }

            // XPU activity: compare against baseline idle memory
            if(!string.IsNullOrEmpty(IgpuLuid)&&memCounters.ContainsKey(IgpuLuid)){
                try{
                    long cur=(long)memCounters[IgpuLuid].NextValue();
                    if(!baselineSet){igpuBaseline=cur;baselineSet=true;}
                    long adjusted=cur>igpuBaseline?cur-igpuBaseline:0;
                    if(adjusted>200*1048576) xpuAct=Math.Min(xpuAct+1,20);
                    else if(adjusted<50*1048576&&xpuAct>0) xpuAct--;
                    lastIgpuMem=cur;
                }catch{}
            }

            string utilStr;
            if(total>0) utilStr=Math.Min(total,100)+"%";
            else if(gpu3D.Count==0) utilStr="N/A";
            else if(xpuAct>3) utilStr="act";
            else utilStr="0%";

            gv.Text=utilStr+(memStr.Length>0?" ("+memStr.TrimStart()+")":"");
            W.BarSet(gb,Math.Min(total,100));
        }catch{gv.Text="N/A";W.BarSet(gb,0);}
    }

    static void RefreshNpu(){
        try{
            npuTick++;
            if(npuTick>=RESCAN||npuComp.Count==0) ScanNpuComp();

            int total=0;
            var stale=new List<PerformanceCounter>();
            foreach(var pc in npuComp){
                try{
                    float v=pc.NextValue();
                    if(v>=0) total+=(int)v;
                }catch{stale.Add(pc);}
            }
            foreach(var pc in stale){try{pc.Dispose();}catch{} npuComp.Remove(pc);}

            string memStr="";
            if(!string.IsNullOrEmpty(NpuLuid)&&memCounters.ContainsKey(NpuLuid)){
                try{float mb=memCounters[NpuLuid].NextValue()/1048576f;memStr=(int)mb+"MB";}catch{}
            }

            string utilStr;
            if(total>0) utilStr=Math.Min(total,100)+"%";
            else if(npuComp.Count==0) utilStr="N/A";
            else utilStr="0%";

            nv.Text=utilStr+(memStr.Length>0?" ("+memStr+")":"");
            W.BarSet(nb,Math.Min(total,100));
        }catch{nv.Text="N/A";W.BarSet(nb,0);}
    }

    static void ReadTemps(){
        try{
            using(var s=new ManagementObjectSearcher(@"root\wmi","SELECT * FROM MSAcpi_ThermalZoneTemperature"))
            {
                float cpuT=float.NaN,gpuT=float.NaN;
                foreach(ManagementObject o in s.Get()){
                    float c=(Convert.ToInt32(o["CurrentTemperature"])/10f)-273.15f;
                    string name=(string)o["InstanceName"]??"";
                    if(name.IndexOf("CPU",StringComparison.OrdinalIgnoreCase)>=0) cpuT=c;
                    else if(name.IndexOf("GPU",StringComparison.OrdinalIgnoreCase)>=0) gpuT=c;
                    else if(float.IsNaN(cpuT)) cpuT=c;
                }
                string txt="";
                if(!float.IsNaN(cpuT)) txt+="CPU "+cpuT.ToString("F0")+" C";
                if(!float.IsNaN(gpuT)) txt+=(txt.Length>0?"  ":"")+"GPU "+gpuT.ToString("F0")+" C";
                if(txt.Length>0) tl.Text=txt;
            }
        }catch{tl.Text="--";}
    }
}
