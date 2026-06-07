using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Windows.Forms;

// ============================================================
// Battery Widget — percentage + power status
// ============================================================

static class BatteryWidget
{
    static Form form; static Panel panel, batBar; static Label valLabel, statusLabel, powerLabel; static List<Panel> seps=new List<Panel>();
    static int lastRemaining=0, lastRate=0; static System.Windows.Forms.Timer refreshTimer;

    public static Form Create(Settings cfg, Form above=null){
        Font tf=Theme.TitleFont, bf=Theme.BodyFont, sf=Theme.SmallFont;
        int tH=Theme.TextH(tf), bH=Theme.TextH(bf), sH=Theme.TextH(sf), barH=4, gap=Theme.Gap(bf), sp=Theme.Sp(bf);

        int FW=240, y=Theme.StartY;

        int posY=above!=null?above.Bounds.Bottom+20:cfg.BatY;
        form=Theme.NewMonitorForm("Battery",new Point(Program.SW-FW,posY),cfg.Opacity);

        int ty=y;
        var bBtn=W.Lbl("[>]",tf,Theme.Accent,30,tH,5,ty);bBtn.Cursor=Cursors.Hand;
        bBtn.Click+=(s,e)=>System.Diagnostics.Process.Start("ms-settings:batterysaver");
        form.Controls.Add(bBtn);
        form.Controls.Add(W.Lbl("Battery",tf,Theme.Fg,198,tH,38,ty));ty+=tH+2;
        seps.Add(AddSep(232,1,sp,ty));ty+=2;

        int lw=50, vw=80, bx=sp+lw+2, bw=232-2*sp-lw-vw-8, vx=bx+bw+4;
        panel=Theme.MonitorPanel(sp,ty);int py=gap;

        // Charge
        panel.Controls.Add(W.Lbl("Charge",bf,Theme.Fg,lw,bH,sp,py));
        valLabel=W.Lbl("100%",bf,Theme.Fg,vw,bH,vx,py);valLabel.TextAlign=ContentAlignment.MiddleRight;panel.Controls.Add(valLabel);
        py+=bH+1;
        batBar=W.Bar(Theme.Accent,bw,barH,bx,py);panel.Controls.Add(batBar);
        py+=barH+gap;

        // Status
        panel.Controls.Add(W.Lbl("Status",sf,Theme.Dim,lw,sH,sp,py));
        statusLabel=W.Lbl("AC Power",sf,Theme.Dim,vw+30,sH,vx-30,py);panel.Controls.Add(statusLabel);
        py+=sH+gap;

        // Power
        panel.Controls.Add(W.Lbl("Power",sf,Theme.Dim,lw,sH,sp,py));
        powerLabel=W.Lbl("--",sf,Theme.Dim,vw+30,sH,vx-30,py);panel.Controls.Add(powerLabel);
        py+=sH+gap;

        panel.Size=new Size(232,py);form.Controls.Add(panel);Theme.ApplyMonitor(form,panel,seps);
        form.Size=new Size(FW,ty+py+gap);

        refreshTimer=new System.Windows.Forms.Timer{Interval=5000};refreshTimer.Tick+=(s,e)=>RefreshUI();refreshTimer.Start();RefreshUI();
        Program.ThemeChanged+=(light)=>{Theme.ApplyMonitor(form,panel,seps);W.BarColor(batBar,Theme.Accent);};
        W.MakeDraggable(form,panel);
        return form;
    }

    static Panel AddSep(int w,int h,int x,int y){var p=Theme.Sep(w,h,x,y);form.Controls.Add(p);return p;}

    static void RefreshUI(){
        var ps=SystemInformation.PowerStatus;
        float pct=ps.BatteryLifePercent;
        valLabel.Text=(int)(pct*100)+"%";
        W.BarSet(batBar,(int)(pct*100));

        // Status
        switch(ps.BatteryChargeStatus){
            case BatteryChargeStatus.Charging: statusLabel.Text="Charging"; statusLabel.ForeColor=Theme.Accent; break;
            case BatteryChargeStatus.NoSystemBattery: statusLabel.Text="No Battery"; statusLabel.ForeColor=Theme.Dim; if(refreshTimer!=null)refreshTimer.Stop(); break;
            default: statusLabel.Text=pct>=1?"Full":"Discharging"; statusLabel.ForeColor=Theme.Fg; break;
        }

        // Power rate via WMI + runtime estimate
        try{
            using(var s=new ManagementObjectSearcher(@"root\wmi","SELECT * FROM BatteryStatus")){
                foreach(ManagementObject o in s.Get()){
                    bool charging=false; try{charging=(bool)o["Charging"];}catch{}
                    int rate=0, remaining=0;
                    if(charging) try{rate=Convert.ToInt32(o["ChargeRate"]);}catch{}
                    else try{rate=Convert.ToInt32(o["DischargeRate"]);}catch{}
                    try{remaining=Convert.ToInt32(o["RemainingCapacity"]);}catch{}
                    lastRate=rate; lastRemaining=remaining;

                    string pwStr;
                    if(rate>=1000000) pwStr=(rate/1000000f).ToString("F1")+" W";
                    else if(rate>=1000) pwStr=(rate/1000f).ToString("F1")+" W";
                    else if(rate>0) pwStr=rate+" mW";
                    else pwStr="0 W";

                    // Runtime estimate: remaining (mWh) / rate (mW) = hours
                    if(!charging && rate>0 && remaining>0){
                        float hrs=(float)remaining/rate;
                        if(hrs>=1) pwStr+=" (est. "+hrs.ToString("F1")+"h)";
                        else pwStr+=" (est. "+(int)(hrs*60)+"m)";
                    }
                    powerLabel.Text=pwStr;
                    powerLabel.ForeColor=charging?Theme.Accent:Theme.Fg;
                    return;
                }
            }
        }catch{}
        powerLabel.Text="--";
        powerLabel.ForeColor=Theme.Dim;
    }
}
