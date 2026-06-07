using System;
using System.Collections.Generic;
using System.IO;

// ============================================================
// Settings — INI-file configuration
// ============================================================
class Settings
{
    public int TopX=1186,TopY=20, SysY=142, DiskY=310, NetY=432, RbX=20,RbY=50, WiFiY=432, VolY=230, BriY=300, BatY=570;
    public float Opacity=0.78f, BinOpacity=0.78f, TopOpacity=0.62f;
    public int ClockMs=30000, SysMs=2000, DiskMs=5000, NetMs=2000, NetSlowTick=5, NetIpTick=10, WiFiMs=20000, RbMs=5000;
    public bool SmallFont;

    public static Settings Load(){
        var s = new Settings();
        try{
            string path = Path.Combine(Program.BaseDir,"settings.ini");
            if(File.Exists(path)){
                var map = new Dictionary<string,Action<string>>{
                    {"TopX",v=>s.TopX=int.Parse(v)},{"TopY",v=>s.TopY=int.Parse(v)},
                    {"SysY",v=>s.SysY=int.Parse(v)},{"DiskY",v=>s.DiskY=int.Parse(v)},
                    {"NetY",v=>s.NetY=int.Parse(v)},{"RbX",v=>s.RbX=int.Parse(v)},
                    {"RbY",v=>s.RbY=int.Parse(v)},{"WiFiY",v=>s.WiFiY=int.Parse(v)},
                    {"VolY",v=>s.VolY=int.Parse(v)},{"BriY",v=>s.BriY=int.Parse(v)},
                    {"BatY",v=>s.BatY=int.Parse(v)},
                    {"Opacity",v=>s.Opacity=float.Parse(v)},
                    {"BinOpacity",v=>s.BinOpacity=float.Parse(v)},
                    {"TopOpacity",v=>s.TopOpacity=float.Parse(v)},
                    {"ClockMs",v=>s.ClockMs=int.Parse(v)},{"SysMs",v=>s.SysMs=int.Parse(v)},
                    {"DiskMs",v=>s.DiskMs=int.Parse(v)},{"NetMs",v=>s.NetMs=int.Parse(v)},
                    {"NetSlowTick",v=>s.NetSlowTick=int.Parse(v)},{"NetIpTick",v=>s.NetIpTick=int.Parse(v)},
                    {"WiFiMs",v=>s.WiFiMs=int.Parse(v)},{"RbMs",v=>s.RbMs=int.Parse(v)},
                    {"SmallFont",v=>s.SmallFont=v=="1"||v.ToLower()=="true"},
                };
                foreach(var line in File.ReadAllLines(path)){
                    var p = line.Split('='); if(p.Length!=2)continue;
                    var k=p[0].Trim(); var v=p[1].Trim();
                    if(map.ContainsKey(k))map[k](v);
                }
            }
        }catch{}
        return s;
    }

    public static void Save(Settings s){
        try{
            string path = Path.Combine(Program.BaseDir,"settings.ini");
            var lines = new string[]{
                "TopX="+s.TopX, "TopY="+s.TopY, "SysY="+s.SysY,
                "DiskY="+s.DiskY, "NetY="+s.NetY, "RbX="+s.RbX, "RbY="+s.RbY,
                "WiFiY="+s.WiFiY, "VolY="+s.VolY, "BriY="+s.BriY, "BatY="+s.BatY,
                "Opacity="+s.Opacity, "BinOpacity="+s.BinOpacity, "TopOpacity="+s.TopOpacity,
                "ClockMs="+s.ClockMs, "SysMs="+s.SysMs, "DiskMs="+s.DiskMs,
                "NetMs="+s.NetMs, "NetSlowTick="+s.NetSlowTick, "NetIpTick="+s.NetIpTick,
                "WiFiMs="+s.WiFiMs, "RbMs="+s.RbMs,
                "SmallFont="+(s.SmallFont?"1":"0"),
            };
            File.WriteAllLines(path, lines);
        }catch{}
    }
}
