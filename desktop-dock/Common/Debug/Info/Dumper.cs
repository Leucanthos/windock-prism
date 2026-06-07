using System;
using System.IO;

// ============================================================
// Dumper — centralized debug-data logging for dock
// ============================================================
static class Dumper
{
    public static void Icons(string[] names, string file){
        if(!DebugMode.On)return;
        try{
            var sb=new System.Text.StringBuilder();
            sb.AppendLine(DateTime.Now.ToString("HH:mm:ss")+" Icons:");
            for(int i=0;i<names.Length;i++) sb.AppendLine("  ["+i+"] "+names[i]);
            File.WriteAllText(file,sb.ToString());
        }catch{}
    }
}
