using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// ============================================================
// W — pure utilities
// ============================================================
public static class W
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    public static void Round(Form f) { int c=3; DwmSetWindowAttribute(f.Handle,33,ref c,4); }

    public static Label Lbl(string t,Font f,Color c,int w,int h,int x,int y){return new Label{Text=t,ForeColor=c,Font=f,AutoSize=false,Size=new Size(w,h),Location=new Point(x,y)};}

    static bool dragging; static Point lastPos;
    public static void MakeDraggable(Form f, params Control[] extras){MouseEventHandler s=(o,e)=>{if(e.Button==MouseButtons.Left){dragging=true;lastPos=Cursor.Position;}};MouseEventHandler m=(o,e)=>{if(dragging){var c=Cursor.Position;f.Location=new Point(f.Location.X+c.X-lastPos.X,f.Location.Y+c.Y-lastPos.Y);lastPos=c;}};MouseEventHandler u=(o,e)=>dragging=false;f.MouseDown+=s;f.MouseMove+=m;f.MouseUp+=u;foreach(var c in extras){c.MouseDown+=s;c.MouseMove+=m;c.MouseUp+=u;}}

    static Mutex _lockMutex;
    public static bool Lock(string name){bool ok;_lockMutex=new Mutex(true,name,out ok);return ok;}
}
