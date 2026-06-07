using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

// ============================================================
// AboutPanel — version info + update check
// ============================================================
static class AboutPanel
{
    static Form popup;

    public static void Show(){
        if(popup!=null&&!popup.IsDisposed){popup.BringToFront();return;}

        int w=220,h=130;
        popup=new Form{
            Size=new Size(w,h),FormBorderStyle=FormBorderStyle.None,
            StartPosition=FormStartPosition.CenterScreen,TopMost=true,ShowInTaskbar=false,
            BackColor=Theme.FormBg,BackgroundImage=Theme.GlassBmp,BackgroundImageLayout=ImageLayout.Stretch,
            Opacity=0.85,ShowIcon=true,Icon=Theme.GetWidgetIcon()
        };
        popup.Shown+=(s,e)=>W.Round(popup);
        popup.Deactivate+=(s,e)=>popup.Close();
        popup.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape)popup.Close();};

        var tf=Theme.TitleFont; var sf=Theme.SmallFont; var bf=Theme.BodyFont;
        int y=8;

        var title=W.Lbl("Prism "+VersionInfo.Number,tf,Theme.Fg,200,22,10,y);popup.Controls.Add(title);y+=24;
        var build=W.Lbl("Build: "+RetrieveLinkerTimestamp().ToString("yyyy-MM-dd"),sf,Theme.Dim,200,14,14,y);popup.Controls.Add(build);y+=16;
        var hw=W.Lbl(GetHwSummary(),sf,Theme.Dim,200,14,14,y);popup.Controls.Add(hw);y+=18;

        // Update check button
        var chk=W.Lbl("[Check for Updates]",bf,Theme.Accent,200,20,14,y);chk.Cursor=Cursors.Hand;
        chk.Click+=(s2,e2)=>{try{Process.Start("https://github.com");}catch{}};popup.Controls.Add(chk);
        y+=24;

        var close=W.Lbl("[OK]",bf,Theme.Dim,60,20,80,y);close.Cursor=Cursors.Hand;
        close.Click+=(s2,e2)=>popup.Close();popup.Controls.Add(close);

        foreach(Control c in popup.Controls)if(c is Label)((Label)c).BackColor=Color.Transparent;
        popup.Show();
    }

    static DateTime RetrieveLinkerTimestamp(){
        try{
            string path=System.Reflection.Assembly.GetExecutingAssembly().Location;
            var b=System.IO.File.ReadAllBytes(path);
            int pe=(BitConverter.ToInt32(b,0x3C));
            return new DateTime(1970,1,1).AddSeconds(BitConverter.ToInt32(b,pe+8)).ToLocalTime();
        }catch{return DateTime.Now;}
    }

    static string GetHwSummary(){
        try{
            int ram=(int)(new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory/1073741824);
            return "RAM: "+ram+" GB | Display: "+Screen.PrimaryScreen.Bounds.Width+"x"+Screen.PrimaryScreen.Bounds.Height;
        }catch{return "";}
    }
}
