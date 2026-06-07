using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Windows.Forms;

// ============================================================
// Network Widget — WiFi / Max Rate / Up-Down / IP / Geo
// ============================================================
static class NetworkWidget
{
    static Label wv,ml,iv,gl; static Form form; static Panel panel; static List<Panel> seps=new List<Panel>();
    static PerformanceCounter uc,dc; static int tickCount=0, slowTick=5, ipTick=10;
    static string rxRate="",txRate=""; // PHY rates from netsh

    public static Form Create(Settings cfg, Form above=null){
        slowTick=cfg.NetSlowTick; ipTick=cfg.NetIpTick;
        Font tf=Theme.TitleFont, bf=Theme.BodyFont, sf=Theme.SmallFont;
        int tH=Theme.TextH(tf), bH=Theme.TextH(bf), sH=Theme.TextH(sf), gap=Theme.Gap(bf), sp=Theme.Sp(bf);

        int FW=240, posY=above!=null?above.Bounds.Bottom+20:cfg.NetY;
        form=Theme.NewMonitorForm("Network",new Point(Program.SW-FW,posY),cfg.Opacity);
        try{var cat=new PerformanceCounterCategory("Network Interface");string iface=null;foreach(var n in cat.GetInstanceNames()){if(n.IndexOf("Wi-Fi",StringComparison.OrdinalIgnoreCase)>=0&&n.IndexOf("_",StringComparison.Ordinal)<0){iface=n;break;}}if(iface==null)iface=cat.GetInstanceNames()[0];uc=new PerformanceCounter("Network Interface","Bytes Sent/sec",iface);dc=new PerformanceCounter("Network Interface","Bytes Received/sec",iface);}catch{}

        int y=Theme.StartY;
        var btn=W.Lbl("[>]",tf,Theme.Accent,30,tH,5,y);btn.Cursor=Cursors.Hand;
        btn.Click+=(s,e)=>WiFiWidget.Show(cfg);
        form.Controls.Add(btn);
        form.Controls.Add(W.Lbl("Network",tf,Theme.Fg,198,tH,38,y));y+=tH+2;
        seps.Add(AddSep(232,1,sp,y));y+=2;

        int lw=40, vx=sp+lw+2, vw=sp+232-vx; // value width to panel right edge
        Font rf=Theme.BodyRegFont;
        panel=Theme.MonitorPanel(sp,y);
        int py=gap, sg=0, lg=6;

        // Row 1: WiFi + SSID (Quality xx%) — 10pt
        panel.Controls.Add(W.Lbl("WiFi",bf,Theme.Dim,lw,bH,sp,py));
        wv=W.Lbl("...",rf,Theme.Fg,vw,bH,vx,py);panel.Controls.Add(wv);
        py+=bH+sg;

        // Row 2: UP/DN rates — 8pt (full panel width)
        ml=W.Lbl("...",sf,Theme.Fg,232,sH,sp,py);panel.Controls.Add(ml);
        py+=sH+lg;

        // Row 3: IP — 10pt
        panel.Controls.Add(W.Lbl("IP",bf,Theme.Dim,lw,bH,sp,py));
        iv=W.Lbl("...",rf,Theme.Fg,vw,bH,vx,py);panel.Controls.Add(iv);
        py+=bH+sg;

        // Row 4: City | Wall — 8pt (full panel width)
        gl=W.Lbl("...",sf,Theme.Fg,232,sH,sp,py);panel.Controls.Add(gl);
        py+=sH+gap;

        panel.Size=new Size(232,py);form.Controls.Add(panel);Theme.ApplyMonitor(form,panel,seps);
        form.Size=new Size(FW,y+py+gap);

        var timer=new System.Windows.Forms.Timer{Interval=cfg.NetMs};timer.Tick+=(s,e)=>Refresh();timer.Start();Refresh();
        tickCount=0; var t2=new System.Windows.Forms.Timer{Interval=500};t2.Tick+=(s,e)=>{FetchSSID();FetchIP();t2.Stop();};t2.Start();
        Program.ThemeChanged+=(light)=>{Theme.ApplyMonitor(form,panel,seps);};
        W.MakeDraggable(form,panel);return form;
    }
    static Panel AddSep(int w,int h,int x,int y){var p=Theme.Sep(w,h,x,y);form.Controls.Add(p);return p;}

    static string TruncSSID(string s){
        if(s.Length<=12)return s;
        return s.Substring(0,6)+"..."+s.Substring(s.Length-2);
    }

    static void LookupGeoAsync(string ip){
        if(string.IsNullOrEmpty(ip)||ip=="N/A")return;
        try{
            var wc=new WebClient();wc.Encoding=System.Text.Encoding.UTF8;
            wc.Headers.Add("User-Agent","Prism/"+VersionInfo.Number);
            wc.DownloadStringCompleted+=(s2,e2)=>{if(e2.Error==null)ParseGeo(e2.Result);};
            wc.DownloadStringAsync(new Uri("http://ip-api.com/json/"+ip));
        }catch(Exception ex){Dumper.NetErr("Geo: "+ex.Message);}
    }

    static void ParseGeo(string json){
        if(string.IsNullOrEmpty(json))return;
        var cc=JsonVal(json,"countryCode");
        var city=JsonVal(json,"city");
        if(!string.IsNullOrEmpty(cc)){
            bool inCN=cc=="CN";
            string loc=cc+(string.IsNullOrEmpty(city)?"":"-"+city);
            form.BeginInvoke((MethodInvoker)(()=>{
                gl.Text=loc+" | "+(inCN?"In Wall":"No Wall");
                gl.ForeColor=inCN?Theme.Accent:Theme.Fg;
            }));
        }
    }
    static string JsonVal(string json, string key){
        var s="\""+key+"\":\"";
        int i=json.IndexOf(s);if(i<0)return"";
        i+=s.Length;int e=json.IndexOf("\"",i);if(e<0)return"";
        return System.Text.RegularExpressions.Regex.Replace(json.Substring(i,e-i),@"\\u([0-9a-fA-F]{4})",m=>((char)Convert.ToInt32(m.Groups[1].Value,16)).ToString());
    }

    static void Refresh(){
        tickCount++;
        if(tickCount%slowTick==1) FetchSSID();
        if(tickCount%ipTick==1) FetchIP();
        try{ml.Text="UP "+FS((long)uc.NextValue()/1024)+(txRate.Length>0?"("+txRate+")":"")+" | DN "+FS((long)dc.NextValue()/1024)+(rxRate.Length>0?"("+rxRate+")":"");}catch{}
        Dumper.Net(tickCount,wv.Text,ml.Text,iv.Text,gl.Text);
    }

    static void FetchSSID(){try{var p=Process.Start(new ProcessStartInfo("netsh","wlan show interfaces"){RedirectStandardOutput=true,CreateNoWindow=true,UseShellExecute=false,StandardOutputEncoding=System.Text.Encoding.UTF8});string o=p.StandardOutput.ReadToEnd();p.WaitForExit();var sm=System.Text.RegularExpressions.Regex.Match(o,@"^\s*SSID\s+:\s+(.+)$",System.Text.RegularExpressions.RegexOptions.Multiline);string ssid=sm.Success?sm.Groups[1].Value.Trim():"N/A";var sg=System.Text.RegularExpressions.Regex.Match(o,@"(?:Signal|信号)\s*:\s*(\d+)%");string sig=sg.Success?" (Quality "+sg.Groups[1].Value+"%)":"";var rxm=System.Text.RegularExpressions.Regex.Match(o,@"(?:Receive rate|接收速率).*:\s*(\d+)");var txm=System.Text.RegularExpressions.Regex.Match(o,@"(?:Transmit rate|传输速率).*:\s*(\d+)");if(rxm.Success)rxRate=rxm.Groups[1].Value;if(txm.Success)txRate=txm.Groups[1].Value;wv.Text=TruncSSID(ssid)+sig;}catch{wv.Text="N/A";}}
    static void FetchIP(){try{var wc=new WebClient();wc.Encoding=System.Text.Encoding.UTF8;wc.Headers.Add("User-Agent","Prism/"+VersionInfo.Number);wc.DownloadStringCompleted+=(s2,e2)=>{if(e2.Error==null){string ip=e2.Result.Trim();form.BeginInvoke((MethodInvoker)(()=>{iv.Text=ip;}));LookupGeoAsync(ip);}else{form.BeginInvoke((MethodInvoker)(()=>{iv.Text="N/A";}));Dumper.NetErr("IP: "+e2.Error.Message);}};wc.DownloadStringAsync(new Uri("https://checkip.amazonaws.com/"));}catch(Exception ex){iv.Text="N/A";Dumper.NetErr("IP: "+ex.Message);}}
    static string FS(long b){if(b>999999)return string.Format("{0:F1} MB",b/1e6);if(b>999)return string.Format("{0:F1} KB",b/1e3);return b+" B";}
}
