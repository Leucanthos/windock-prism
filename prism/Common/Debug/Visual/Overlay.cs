using System;
using System.Drawing;
using System.Windows.Forms;

// ============================================================
// DebugOverlay — keyboard-precise move/resize + control borders
// ============================================================
static class DebugOverlay
{
    static Color[] pal={Color.Red,Color.Lime,Color.Cyan,Color.Yellow,Color.Magenta,Color.Orange,Color.Pink,Color.LightBlue};
    static Control editTarget; static bool resizeMode; static Form activeForm;
    static bool filterAdded=false;

    public static void Attach(Form f){
        if(!DebugMode.On)return;
        if(!filterAdded){Application.AddMessageFilter(new F5Filter());filterAdded=true;}
        LogAll(f);
        f.KeyPreview=true;
        f.KeyDown+=OnKeyDown;
        MakeEditable(f);
    }

    class F5Filter : IMessageFilter {
        public bool PreFilterMessage(ref Message m){
            if(m.Msg==0x100&&(int)m.WParam==0x74){SnapAll();return true;} // WM_KEYDOWN + VK_F5
            return false;
        }
    }

    static void OnKeyDown(object s, KeyEventArgs e){
        // F5: snapshot ALL open forms
        if(e.KeyCode==Keys.F5){SnapAll();e.Handled=true;return;}
        if(editTarget==null)return;
        if(resizeMode){
            switch(e.KeyCode){
                case Keys.Left:  editTarget.Width=Math.Max(4,editTarget.Width-1); break;
                case Keys.Right: editTarget.Width+=1; break;
                case Keys.Up:    editTarget.Height=Math.Max(4,editTarget.Height-1); break;
                case Keys.Down:  editTarget.Height+=1; break;
            }
        }else{
            switch(e.KeyCode){
                case Keys.Left:  editTarget.Left-=1; break;
                case Keys.Right: editTarget.Left+=1; break;
                case Keys.Up:    editTarget.Top-=1; break;
                case Keys.Down:  editTarget.Top+=1; break;
            }
        }
        LogAll(editTarget.FindForm());
        e.Handled=true;
    }

    static void ExitEdit(){
        if(activeForm!=null)activeForm.Text=activeForm.Text.Replace(" [EDIT]","").Replace(" [SIZE]","");
        editTarget=null;resizeMode=false;activeForm=null;
    }

    static void MakeEditable(Control parent){
        // Form-level: right-click or Ctrl+click to select a control
        var form=parent as Form;
        if(form!=null){
            MouseEventHandler selectHandler=(s,e)=>{
                bool isRight=e.Button==MouseButtons.Right;
                bool isCtrlLeft=e.Button==MouseButtons.Left&&(Control.ModifierKeys&Keys.Control)!=0;
                if(!isRight&&!isCtrlLeft)return;
                var hit=FindHit(form,form.PointToClient(Cursor.Position));
                if(hit==null||hit==form)return;
                if(hit.Tag!=null&&hit.Tag.ToString()=="grip")return;
                form.Activate();
                if(editTarget==hit){ExitEdit();form.Text=form.Text.Replace(" [EDIT]","");return;}
                editTarget=hit;resizeMode=false;activeForm=form;
                form.Text=form.Text+" [EDIT]";
            };
            form.MouseUp+=selectHandler;
        }

        foreach(Control c in parent.Controls){
            if(!c.Visible)continue;

            // Border
            int myCi=Math.Abs(c.GetHashCode())%pal.Length;
            c.Paint+=(s2,e2)=>{ControlPaint.DrawBorder(e2.Graphics,c.ClientRectangle,pal[myCi],ButtonBorderStyle.Solid);};

            // Resize grip (≥16x16 only)
            if(c.Width>=16&&c.Height>=16){
                var g=new Label{Text="",Size=new Size(10,10),BackColor=Color.Orange,Cursor=Cursors.SizeNWSE,Tag="grip"};
                g.Location=new Point(c.ClientSize.Width-10,c.ClientSize.Height-10);
                g.BringToFront();c.Controls.Add(g);

                g.MouseUp+=(s2,e2)=>{
                    bool isRight=e2.Button==MouseButtons.Right;
                    bool isCtrlLeft=e2.Button==MouseButtons.Left&&(Control.ModifierKeys&Keys.Control)!=0;
                    if(!isRight&&!isCtrlLeft)return;
                    var frm=c.FindForm(); if(frm!=null)frm.Activate();
                    if(editTarget==c&&resizeMode){ExitEdit();frm.Text=frm.Text.Replace(" [SIZE]","");return;}
                    editTarget=c;resizeMode=true;activeForm=frm;
                    if(frm!=null)frm.Text=frm.Text+" [SIZE]";
                };
            }

            if(c.Controls.Count>0)MakeEditable(c);
        }
    }

    static Control FindHit(Control parent, Point pt){
        // Find deepest child at point (reverse order = topmost first)
        for(int i=parent.Controls.Count-1;i>=0;i--){
            var c=parent.Controls[i];
            if(!c.Visible)continue;
            if(c.Bounds.Contains(pt)){
                var deeper=FindHit(c,new Point(pt.X-c.Left,pt.Y-c.Top));
                return deeper??c;
            }
        }
        return null;
    }

    static void SnapAll(){
        System.IO.File.WriteAllText(@"C:\temp\_debug_overlay.txt","");
        foreach(Form f in Application.OpenForms) LogAll(f);
    }

    static void LogAll(Form f){
        System.IO.File.WriteAllText(@"C:\temp\_debug_overlay.txt","");
        System.IO.File.AppendAllText(@"C:\temp\_debug_overlay.txt",
            string.Format("=== {0} x={1} y={2} w={3} h={4} ===\n",f.Text,f.Left,f.Top,f.Width,f.Height));
        Walk(f,0,0);
    }

    static void Walk(Control parent, int ox, int oy){
        foreach(Control c in parent.Controls){
            if(!c.Visible)continue;
            int x=ox+c.Left,y=oy+c.Top;
            string name=c is Label?((Label)c).Text.Replace('\n',' '):c.GetType().Name;
            if(string.IsNullOrEmpty(name))name=c.GetType().Name;
            if(name.Length>16)name=name.Substring(0,14)+"..";
            System.IO.File.AppendAllText(@"C:\temp\_debug_overlay.txt",
                string.Format("{0,-24} x={1,4} y={2,4} w={3,4} h={4,4} r={5,4} b={6,4}\n",
                name,x,y,c.Width,c.Height,x+c.Width,y+c.Height));
            if(c.Controls.Count>0)Walk(c,x,y);
        }
    }
}
