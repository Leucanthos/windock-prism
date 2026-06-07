using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// ============================================================
// Recycle Bin Widget — stats / empty / drop-to-recycle
// Uses Theme.Bin* blue palette
// ============================================================
static class RecycleBinWidget
{
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int wEventId,uint uFlags,IntPtr dwItem1,IntPtr dwItem2);
    static dynamic bin; static Label il,sl,pl,it; static Form form; static Panel dp; static bool isLight;
    static int lastCount=-1; static long lastSize;

    public static Form Create(Settings cfg, Form above=null){
        var shell=Type.GetTypeFromProgID("Shell.Application");dynamic shellApp=Activator.CreateInstance(shell);bin=shellApp.Namespace(0xA);
        isLight=Theme.IsLight;
        int posY=above!=null?above.Bounds.Bottom+20:cfg.RbY;
        int posX=above!=null?cfg.TopX:cfg.RbX;
        form=Theme.NewMonitorForm("Recycle Bin",new Point(posX,posY),cfg.TopOpacity,true);
        // Layout: top buttons(4+14) + items(26+14) + path(42+14) + drop panel(60+64) + icon(128+16) + padding
        int fh = 128 + Theme.TextH(Theme.SmallFont) + 20;
        form.Size=new Size(260,fh);

        int px=8, pw=244, pr=px+pw; // panel left, width, right edge
        Font bf=Theme.BodyFont, sf=Theme.SmallRegFont, tf=Theme.SmallFont;
        var ob=new Label{Text="[Open Bin]",ForeColor=Theme.BinAccent,Font=bf,AutoSize=true,Location=new Point(px,4),Cursor=Cursors.Hand};ob.Click+=(s,e)=>System.Diagnostics.Process.Start("shell:RecycleBinFolder");form.Controls.Add(ob);
        var eb=new Label{Text="[Empty Bin]",ForeColor=Theme.BinAccent,Font=bf,AutoSize=true,Location=new Point(pr-85,4),Cursor=Cursors.Hand};eb.Click+=(s,e)=>{try{bin.Self.InvokeVerb("Empty Recycle &Bin");}catch{}Thread.Sleep(50);RS();};form.Controls.Add(eb);
        int iw=pw/2-10;
        il=new Label{Text="0 items",ForeColor=Theme.BinFg,Font=sf,TextAlign=ContentAlignment.MiddleLeft,Size=new Size(iw,14),Location=new Point(px,26)};form.Controls.Add(il);
        sl=new Label{Text="0 B",ForeColor=Theme.BinFg,Font=sf,TextAlign=ContentAlignment.MiddleRight,Size=new Size(iw,14),Location=new Point(px+pw/2+10,26)};form.Controls.Add(sl);
        pl=new Label{Text=@"C:\$Recycle.Bin",ForeColor=Theme.BinDim,Font=sf,TextAlign=ContentAlignment.MiddleLeft,Size=new Size(pw,14),Location=new Point(px,42)};form.Controls.Add(pl);

        dp=new Panel{Size=new Size(pw,64),Location=new Point(px,60),BorderStyle=BorderStyle.FixedSingle,AllowDrop=true,BackColor=Color.Transparent};
        var dl=new Label{Text="Drop files here to recycle",ForeColor=Theme.BinDim,Font=Theme.SmallBoldFont9,TextAlign=ContentAlignment.MiddleCenter,Dock=DockStyle.Fill,BackColor=Color.Transparent};dp.Controls.Add(dl);form.Controls.Add(dp);
        dp.DragEnter+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};dp.DragOver+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};
        dp.DragDrop+=(s,e)=>{foreach(string f in(string[])e.Data.GetData(DataFormats.FileDrop)){if(!File.Exists(f)&&!Directory.Exists(f))continue;var item=shellApp.Namespace(Path.GetDirectoryName(f)).ParseName(Path.GetFileName(f));if(item!=null)bin.MoveHere(item);}Thread.Sleep(50);RS();};

        string ik=@"{645FF040-5081-101B-9F08-00AA002F954E}";bool hd=(int)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel",ik,0)==1;
        it=new Label{Text=hd?"Icon: Hidden":"Icon: Visible",ForeColor=Theme.BinAccent,Font=tf,TextAlign=ContentAlignment.MiddleCenter,Size=new Size(pw,16),Location=new Point(px,128),Cursor=Cursors.Hand};it.Click+=(s,e)=>{hd=!hd;it.Text=hd?"Icon: Hidden":"Icon: Visible";Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel",ik,hd?1:0);SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);};form.Controls.Add(it);

        foreach(Control c in form.Controls)if(c is Label)c.BackColor=Color.Transparent;
        ApplyBinTheme();
        var timer=new System.Windows.Forms.Timer{Interval=cfg.RbMs};timer.Tick+=(s,e)=>{RS();};timer.Start();RS();
        Program.ThemeChanged+=(light)=>{isLight=light;ApplyBinTheme();};
        W.MakeDraggable(form);return form;
    }
    static void ApplyBinTheme(){
        if(form==null||il==null)return;
        form.BackColor=Theme.BinFormBg;
        form.BackgroundImage=Theme.BinGlassBmp;
        if(dp!=null)dp.BackColor=Color.Transparent;
        il.ForeColor=Theme.BinFg;sl.ForeColor=Theme.BinFg;pl.ForeColor=Theme.BinDim;
        if(it!=null)it.ForeColor=Theme.BinAccent;
        if(dp!=null)foreach(Control c in dp.Controls)if(c is Label)((Label)c).ForeColor=Theme.BinDim;
        foreach(Control c in form.Controls){if(c is Label){var l=(Label)c;if(l.Text.StartsWith("[Open")||l.Text.StartsWith("[Empty"))l.ForeColor=Theme.BinAccent;}}
    }
    static void RS(){try{var items=bin.Items();int count=items.Count;long ts;if(count!=lastCount){ts=0;foreach(dynamic i in items)ts+=(long)i.Size;lastSize=ts;lastCount=count;}else ts=lastSize;string ss;if(ts>1000000000)ss=string.Format("{0:F1} GB",ts/1000000000.0);else if(ts>1000000)ss=string.Format("{0:F1} MB",ts/1000000.0);else if(ts>1000)ss=string.Format("{0:F1} KB",ts/1000.0);else ss=ts+" B";il.Text=count+" items";sl.Text=ss;}catch{}}
}
