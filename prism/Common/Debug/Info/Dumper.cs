using System;
using System.IO;

// ============================================================
// Dumper — centralized debug-data logging (widgets call here)
// ============================================================
static class Dumper
{
    public static void Sys(string cpu,string ram,string gpu,string npu,string thermal,string gi,string ni,bool gOK,bool nOK){
        if(!DebugMode.On)return;
        try{File.WriteAllText(@"C:\temp\_sys_debug.txt",
            string.Format("CPU={0}\nRAM={1}\nGPU={2}\nNPU={3}\nThermal={4}\ngi={5}\nni={6}\ngm={7}\nnm={8}\n",
            cpu,ram,gpu,npu,thermal,gi??"(null)",ni??"(null)",gOK?"OK":"null",nOK?"OK":"null"));}catch{}
    }

    public static void Net(int tick,string wifi,string max,string ip,string geo){
        if(!DebugMode.On)return;
        try{File.AppendAllText(@"C:\temp\_net_debug.txt",
            string.Format("t={0} wifi={1} max={2} ip={3} geo={4}\n",tick,wifi,max,ip,geo));}catch{}
    }

    public static void NetErr(string msg){
        if(!DebugMode.On)return;
        try{File.AppendAllText(@"C:\temp\_net_debug.txt",DateTime.Now.ToString("HH:mm:ss")+" "+msg+"\n");}catch{}
    }
}
