using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// ============================================================
// Disk Widget — C:/D: usage
// ============================================================
static class DiskWidget
{
    static Label ca,da; static Panel cb,db,panel; static Form form; static List<Panel> seps=new List<Panel>();

    public static Form Create(Settings cfg, Form above=null){
        Font tf=Theme.TitleFont, bf=Theme.BodyFont;
        int tH=Theme.TextH(tf), bH=Theme.TextH(bf), barH=4, gap=Theme.Gap(bf), sp=Theme.Sp(bf);

        int FW=240, posY=above!=null?above.Bounds.Bottom+20:cfg.DiskY;
        form=Theme.NewMonitorForm("Disk",new Point(Program.SW-FW,posY),cfg.Opacity);

        int y=Theme.StartY;
        var diskBtn=W.Lbl("[>]",tf,Theme.Accent,30,tH,5,y);diskBtn.Cursor=Cursors.Hand;
        diskBtn.Click+=(s,e)=>Process.Start("explorer.exe");
        form.Controls.Add(diskBtn);
        form.Controls.Add(W.Lbl("Disk",tf,Theme.Fg,198,tH,38,y));y+=tH+2;
        seps.Add(AddSep(232,1,sp,y));y+=2;

        // [>] C: | bar | %
        int btnW=55, pctW=50, bw=232-2*sp-btnW-pctW-10, bx=sp+btnW+4, vx=bx+bw+4;
        panel=Theme.MonitorPanel(sp,y);int py=gap;
        int barY=py+bH/2-barH/2;

        var cBtn=W.Lbl("[>] C:",bf,Theme.Accent,btnW,bH,sp,py);cBtn.Cursor=Cursors.Hand;cBtn.Click+=(s,e)=>Process.Start("C:\\");panel.Controls.Add(cBtn);
        cb=W.Bar(Theme.Accent,bw,barH,bx,barY);panel.Controls.Add(cb);
        ca=W.Lbl("0%",bf,Theme.Fg,pctW,bH,vx,py);ca.TextAlign=ContentAlignment.MiddleRight;panel.Controls.Add(ca);
        py+=bH+gap;

        var dBtn=W.Lbl("[>] D:",bf,Theme.Accent,btnW,bH,sp,py);dBtn.Cursor=Cursors.Hand;dBtn.Click+=(s,e)=>Process.Start("D:\\");panel.Controls.Add(dBtn);
        db=W.Bar(Theme.Accent,bw,barH,bx,py+bH/2-barH/2);panel.Controls.Add(db);
        da=W.Lbl("0%",bf,Theme.Fg,pctW,bH,vx,py);da.TextAlign=ContentAlignment.MiddleRight;panel.Controls.Add(da);
        py+=bH+gap;

        panel.Size=new Size(232,py);form.Controls.Add(panel);Theme.ApplyMonitor(form,panel,seps);
        form.Size=new Size(FW,y+py+gap);

        var timer=new System.Windows.Forms.Timer{Interval=cfg.DiskMs};timer.Tick+=(s,e)=>Refresh();timer.Start();Refresh();
        Program.ThemeChanged+=(light)=>{Theme.ApplyMonitor(form,panel,seps);W.BarColor(cb,Theme.Accent);W.BarColor(db,Theme.Accent);};
        W.MakeDraggable(form,panel);return form;
    }
    static Panel AddSep(int w,int h,int x,int y){var p=Theme.Sep(w,h,x,y);form.Controls.Add(p);return p;}
    static void SD(string p,Label l,Panel b){try{var di=new DriveInfo(p);if(!di.IsReady){l.Text="N/A";W.BarSet(b,0);return;}long u=di.TotalSize-di.TotalFreeSpace;int pct=(int)(u*100/di.TotalSize);l.Text=pct+"%";W.BarSet(b,pct);}catch{l.Text="N/A";W.BarSet(b,0);}}
    static void Refresh(){SD("C:\\",ca,cb);SD("D:\\",da,db);}
}
