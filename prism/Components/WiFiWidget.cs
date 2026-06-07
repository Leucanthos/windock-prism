using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

// ============================================================
// WiFi Widget — scan / connect / password
// ============================================================
static class WiFiWidget
{
    static Form form; static Panel listPanel,pwdPanel; static Label pwdLabel; static TextBox pwdBox; static string pwdSSID,curSSID="";
    static List<Label[]> rows=new List<Label[]>(); static int maxRows=8; static bool canClose=false;

    public static void Show(Settings cfg){
        if(form!=null&&!form.IsDisposed){form.Show();form.BringToFront();Refresh();return;}
        Font tf=Theme.TitleFont, bf=Theme.SmallFont; // 8pt Bold for row entries
        int tH=Theme.TextH(tf), rowH=22, sp=Theme.Sp(bf);

        form=Theme.NewMonitorForm("WiFi Panel",new Point(Program.SW-440,cfg.WiFiY),cfg.Opacity);
        form.FormClosing+=(s,e)=>{e.Cancel=true;form.Hide();};
        int y=Theme.StartY;form.Controls.Add(W.Lbl("WiFi Panel",tf,Theme.Fg,200,tH,5,y));y+=tH+4;
        form.Controls.Add(Theme.Sep(210,1,sp,y));y+=2;

        listPanel=new Panel{Size=new Size(210,maxRows*rowH),Location=new Point(sp,y),BackColor=Theme.PanelBg};form.Controls.Add(listPanel);
        for(int i=0;i<maxRows;i++){int ry=i*rowH;var sl=W.Lbl("",bf,Theme.Fg,100,rowH,4,ry);sl.TextAlign=ContentAlignment.MiddleLeft;var gl=W.Lbl("",bf,Theme.Fg,40,rowH,108,ry);gl.TextAlign=ContentAlignment.MiddleRight;var bl=W.Lbl("",bf,Theme.Accent,58,rowH,152,ry);bl.TextAlign=ContentAlignment.MiddleCenter;bl.Cursor=Cursors.Hand;int idx=i;bl.Click+=(s,e)=>{if(idx<rows.Count)Connect(idx);};listPanel.Controls.Add(sl);listPanel.Controls.Add(gl);listPanel.Controls.Add(bl);rows.Add(new[]{sl,gl,bl});}
        int pwY=y+maxRows*rowH;pwdPanel=new Panel{Size=new Size(210,28),Location=new Point(sp,pwY),BackColor=Theme.PanelBg,Visible=false};pwdLabel=W.Lbl("",bf,Theme.Dim,140,14,4,2);pwdPanel.Controls.Add(pwdLabel);pwdBox=new TextBox{Size=new Size(100,20),Location=new Point(4,16),Font=bf,BackColor=Theme.IsLight?Color.White:Color.FromArgb(60,60,60),ForeColor=Theme.Fg,BorderStyle=BorderStyle.FixedSingle};pwdPanel.Controls.Add(pwdBox);var pbtn=W.Lbl("[Connect]",bf,Theme.Accent,58,14,110,18);pbtn.Cursor=Cursors.Hand;pbtn.Click+=(s,e)=>{ConnectPwd();};pwdPanel.Controls.Add(pbtn);var cbtn=W.Lbl("[Cancel]",bf,Theme.Dim,50,14,160,18);cbtn.Cursor=Cursors.Hand;cbtn.Click+=(s,e)=>{pwdPanel.Visible=false;pwdSSID=null;};pwdPanel.Controls.Add(cbtn);form.Controls.Add(pwdPanel);
        foreach(Control c in form.Controls)if(c is Label)c.BackColor=Color.Transparent;foreach(Control c in listPanel.Controls)if(c is Label)c.BackColor=Color.Transparent;

        var timer=new System.Windows.Forms.Timer{Interval=100};timer.Tick+=(s,e)=>{Refresh();timer.Interval=cfg.WiFiMs;};form.Shown+=(s2,e2)=>{timer.Start();};var ct=new System.Windows.Forms.Timer{Interval=500};ct.Tick+=(s3,e3)=>{canClose=true;ct.Stop();};ct.Start();
        form.Deactivate+=(s,e)=>{if(canClose&&pwdPanel!=null&&!pwdPanel.Visible)form.Hide();};
        W.MakeDraggable(form,listPanel);

        Program.ThemeChanged+=(light)=>{ApplyTheme();Refresh();};

        form.Show();
    }

    static void Connect(int idx){if(idx>=rows.Count)return;string ssid=rows[idx][0].Text;if(string.IsNullOrEmpty(ssid))return;if(ssid==curSSID){Process.Start("netsh","wlan disconnect");Refresh();return;}if(HasProfile(ssid)){Process.Start("netsh","wlan connect name=\""+ssid+"\"");Refresh();return;}pwdSSID=ssid;pwdLabel.Text="Password for "+ssid;pwdBox.Text="";pwdPanel.Visible=true;pwdBox.Focus();}
    static void ConnectPwd(){string pwd=pwdBox.Text;if(string.IsNullOrEmpty(pwd)||pwdSSID==null)return;string xml=string.Format(@"<?xml version=""1.0""?><WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1""><name>{0}</name><SSIDConfig><SSID><name>{0}</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security><authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption><sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{1}</keyMaterial></sharedKey></security></MSM></WLANProfile>",pwdSSID,pwd);string tmp=Path.GetTempFileName();File.WriteAllText(tmp,xml,Encoding.UTF8);Process.Start("netsh","wlan add profile filename=\""+tmp+"\"").WaitForExit();File.Delete(tmp);Process.Start("netsh","wlan connect name=\""+pwdSSID+"\"");pwdPanel.Visible=false;pwdSSID=null;var dt=new System.Windows.Forms.Timer{Interval=2000};dt.Tick+=(s,e)=>{Refresh();dt.Stop();};dt.Start();}
    static bool HasProfile(string ssid){try{var p=Process.Start(new ProcessStartInfo("netsh","wlan show profiles"){RedirectStandardOutput=true,CreateNoWindow=true,UseShellExecute=false});string o=p.StandardOutput.ReadToEnd();p.WaitForExit();return o.Contains(": "+ssid)||o.Contains(":\t"+ssid);}catch{return false;}}
    static void Refresh(){try{
        var si=new ProcessStartInfo("cmd","/c chcp 65001 > nul && netsh wlan show networks mode=bssid"){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,StandardOutputEncoding=Encoding.UTF8};
        var p=Process.Start(si);string o=p.StandardOutput.ReadToEnd();p.WaitForExit();
        string[] lines=o.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
        var maxSig=new Dictionary<string,int>(); var sorted=new List<string>(); string cur="";
        foreach(string line in lines){
            var m=Regex.Match(line,@"^\s*SSID\s+\d+\s*:\s*(.+)");
            if(m.Success){cur=m.Groups[1].Value.Trim();continue;}
            m=Regex.Match(line,@"(?:Signal|信号)\s*:\s*(\d+)%");
            if(m.Success&&!string.IsNullOrWhiteSpace(cur)){int s;if(int.TryParse(m.Groups[1].Value,out s)){if(!maxSig.ContainsKey(cur)||s>maxSig[cur]){maxSig[cur]=s;if(!sorted.Contains(cur))sorted.Add(cur);}}}
        }
        sorted.Sort((a,b)=>maxSig[b].CompareTo(maxSig[a]));

        try{var ip=new ProcessStartInfo("netsh","wlan show interfaces"){RedirectStandardOutput=true,CreateNoWindow=true,UseShellExecute=false};var ip2=Process.Start(ip);string o2=ip2.StandardOutput.ReadToEnd();ip2.WaitForExit();var m2=Regex.Match(o2,@"^\s*SSID\s+:\s+(.+)$",RegexOptions.Multiline);curSSID=m2.Success?m2.Groups[1].Value.Trim():"";}catch{}

        bool light=Theme.IsLight;
        Color w=Theme.Fg, g=Theme.Accent, dim=Theme.Dim;
        for(int i=0;i<maxRows;i++){if(i<sorted.Count){string ssid=sorted[i];int sig=maxSig[ssid];rows[i][0].Text=ssid.Length>20?ssid.Substring(0,17)+"...":ssid;rows[i][0].ForeColor=w;rows[i][1].Text=sig+"%";rows[i][1].ForeColor=w;rows[i][2].Text=(ssid==curSSID)?"[Disconnect]":"[Connect]";rows[i][2].ForeColor=(ssid==curSSID)?dim:g;}else{rows[i][0].Text="";rows[i][1].Text="";rows[i][2].Text="";}}
    }catch{}}

    static void ApplyTheme(){
        if(form==null||form.IsDisposed)return;
        form.BackColor=Theme.FormBg;
        form.BackgroundImage=Theme.GlassBmp;
        Color w=Theme.Fg, d=Theme.Dim, g=Theme.Accent;
        foreach(Control c in form.Controls){
            if(c is Label){var l=(Label)c;if(l.Text.StartsWith("[Connect")||l.Text.StartsWith("[Cancel"))l.ForeColor=g;else l.ForeColor=w;}
            if(c is Panel){var p=(Panel)c;if(p!=listPanel&&p!=pwdPanel)p.BackColor=Theme.SepColor;}
        }
        if(listPanel!=null){listPanel.BackColor=Theme.PanelBg;foreach(Control c in listPanel.Controls)if(c is Label){var l=(Label)c;if(l.Text.StartsWith("[Connect")||l.Text.StartsWith("[Disconnect"))l.ForeColor=g;else l.ForeColor=w;}}
        if(pwdPanel!=null){pwdPanel.BackColor=Theme.PanelBg;foreach(Control c in pwdPanel.Controls)if(c is Label){var l=(Label)c;l.ForeColor=l.Text.StartsWith("[Connect")||l.Text.StartsWith("[Cancel")?g:d;}if(pwdBox!=null){pwdBox.BackColor=Theme.IsLight?Color.White:Color.FromArgb(60,60,60);pwdBox.ForeColor=w;}}
    }
}
