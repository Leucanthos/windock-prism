using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Management;
using System.Windows.Forms;

// ============================================================
// System Widget — CPU / RAM / GPU0 (dGPU) / GPU1 (iGPU) / NPU*
// * NPU section auto-hides when no NPU hardware detected
// ============================================================
static class SystemWidget
{
    static Label cv, rv, gv0, gv1, nv, tl;
    static Panel cb, rb, gb0, gb1, nb, panel;
    static Form form;
    static List<Panel> seps = new List<Panel>();
    static PerformanceCounter cc, rc, fc;
    static string cpuModel = "...", gpu0Model = "...", gpu1Model = "...", npuModel = "", ramTotal = "";
    static string cachedTemp = "...";
    static int tempTick = 99;
    static long ramKB;
    static int cpuMaxMHz;
    static bool hasNpu;

    // LUIDs and memory counters
    static string GpuLuid = "", NpuLuid = "", IgpuLuid = "";
    static Dictionary<string, PerformanceCounter> memCounters = new Dictionary<string, PerformanceCounter>();

    // dGPU (GPU0) 3D engine counters
    static List<PerformanceCounter> gpu0_3D = new List<PerformanceCounter>();
    static int gpu0Tick = 99;

    // iGPU (GPU1) 3D engine counters
    static List<PerformanceCounter> gpu1_3D = new List<PerformanceCounter>();
    static int gpu1Tick = 99;

    // NPU compute counters
    static List<PerformanceCounter> npuComp = new List<PerformanceCounter>();
    static int npuTick = 99;
    const int RESCAN = 10;

    // XPU activity tracking via iGPU memory delta
    static long lastIgpuMem = 0;
    static int xpuAct = 0;
    static long igpuBaseline = 0;
    static bool baselineSet = false;

    public static Form Create(Settings cfg, Form above = null)
    {
        Font tf = Theme.TitleFont, bf = Theme.BodyFont, sf = Theme.SmallFont;
        int tH = Theme.TextH(tf), bH = Theme.TextH(bf), sH = Theme.TextH(sf);
        int barH = 4, gap = Theme.Gap(bf), sp = Theme.Sp(bf);

        int posY = above != null ? above.Bounds.Top + above.Bounds.Height / 2 : cfg.SysY;
        int FW = 240;
        form = Theme.NewMonitorForm("System", new Point(Program.SW - FW, posY), cfg.Opacity);
        try { cc = new PerformanceCounter("Processor", "% Processor Time", "_Total"); } catch { }
        try { rc = new PerformanceCounter("Memory", "% Committed Bytes In Use"); } catch { }
        try { fc = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total"); } catch { }
        try { using (var s = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor")) { foreach (ManagementObject o in s.Get()) { cpuMaxMHz = Convert.ToInt32(o["MaxClockSpeed"]); break; } } } catch { }

        // ── LUID Auto-detect ──
        DetectLuid();
        hasNpu = !string.IsNullOrEmpty(NpuLuid);

        // ── Build initial engine counters and warm up ──
        WarmupCounters();

        // ── Hardware models ──
        try { using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor")) { foreach (ManagementObject o in s.Get()) { cpuModel = (string)o["Name"]; break; } } } catch { }
        try { using (var s = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem")) { foreach (ManagementObject o in s.Get()) { ramKB = Convert.ToInt64(o["TotalVisibleMemorySize"]); ramTotal = ramKB > 1048576 ? ((ramKB / 1048576f).ToString("F1") + " GB") : ((ramKB / 1024f).ToString("F0") + " MB"); break; } } } catch { }

        // GPU models from WMI (up to 2)
        try {
            int gi = 0;
            using (var s = new ManagementObjectSearcher("SELECT Name,AdapterRAM FROM Win32_VideoController")) {
                foreach (ManagementObject o in s.Get()) {
                    string n = (string)o["Name"];
                    if (gi == 0) gpu0Model = n; else if (gi == 1) gpu1Model = n;
                    gi++; if (gi >= 2) break;
                }
            }
        } catch { }

        int y = Theme.StartY;
        var btn = W.Lbl("[>]", tf, Theme.Accent, 30, tH, 5, y); btn.Cursor = Cursors.Hand; btn.Click += (s, e) => Process.Start("taskmgr"); form.Controls.Add(btn);
        form.Controls.Add(W.Lbl("System Info", tf, Theme.Fg, 198, tH, 38, y)); y += tH + 2;
        seps.Add(AddSep(232, 1, sp, y)); y += 2;

        int cw = 232 - 2 * sp, labelW = 50, valW = 132, barW = cw - labelW - valW - 6;
        Font modelFont = Theme.ModelFont;
        Font rowFont = Theme.BodyRegFont;
        panel = Theme.MonitorPanel(sp, y); int py = gap;
        int barCY = bH / 2 - barH / 2, valX = sp + labelW + 2 + barW + 4;

        // CPU
        panel.Controls.Add(W.Lbl("CPU", rowFont, Theme.Fg, labelW, bH, sp, py));
        cb = W.Bar(Theme.Accent, barW, barH, sp + labelW + 2, py + barCY); panel.Controls.Add(cb);
        cv = W.Lbl("0%", rowFont, Theme.Fg, valW, bH, valX, py); cv.TextAlign = ContentAlignment.MiddleRight; panel.Controls.Add(cv);
        py += bH - 2;
        panel.Controls.Add(W.Lbl("-------- " + cpuModel, modelFont, Theme.Dim, 232 - 2 * sp, 10, sp, py)); py += 10 + gap * 3;

        // RAM
        panel.Controls.Add(W.Lbl("RAM", sf, Theme.Fg, labelW, sH, sp, py));
        rb = W.Bar(Theme.Accent, barW, barH, sp + labelW + 2, py + sH / 2 - barH / 2); panel.Controls.Add(rb);
        rv = W.Lbl("0%", sf, Theme.Fg, valW, sH, valX, py); rv.TextAlign = ContentAlignment.MiddleRight; panel.Controls.Add(rv);
        py += sH + gap;

        // GPU0 (dGPU — primary graphics)
        panel.Controls.Add(W.Lbl("GPU0", rowFont, Theme.Fg, labelW, bH, sp, py));
        gb0 = W.Bar(Theme.Accent, barW, barH, sp + labelW + 2, py + bH / 2 - barH / 2); panel.Controls.Add(gb0);
        gv0 = W.Lbl("0%", rowFont, Theme.Fg, valW, bH, valX, py); gv0.TextAlign = ContentAlignment.MiddleRight; panel.Controls.Add(gv0);
        py += bH - 2;
        panel.Controls.Add(W.Lbl("-------- " + gpu0Model, modelFont, Theme.Dim, 232 - 2 * sp, 10, sp, py)); py += 10 + gap * 3;

        // GPU1 (iGPU — integrated graphics)
        panel.Controls.Add(W.Lbl("GPU1", rowFont, Theme.Fg, labelW, bH, sp, py));
        gb1 = W.Bar(Theme.Accent, barW, barH, sp + labelW + 2, py + bH / 2 - barH / 2); panel.Controls.Add(gb1);
        gv1 = W.Lbl("0%", rowFont, Theme.Fg, valW, bH, valX, py); gv1.TextAlign = ContentAlignment.MiddleRight; panel.Controls.Add(gv1);
        py += bH - 2;
        panel.Controls.Add(W.Lbl("-------- " + gpu1Model, modelFont, Theme.Dim, 232 - 2 * sp, 10, sp, py)); py += 10 + gap * 3;

        // NPU (conditional — only shown when NPU hardware is detected)
        if (hasNpu) {
            panel.Controls.Add(W.Lbl("NPU", rowFont, Theme.Fg, labelW, bH, sp, py));
            nb = W.Bar(Theme.Accent, barW, barH, sp + labelW + 2, py + bH / 2 - barH / 2); panel.Controls.Add(nb);
            nv = W.Lbl("0%", rowFont, Theme.Fg, valW, bH, valX, py); nv.TextAlign = ContentAlignment.MiddleRight; panel.Controls.Add(nv);
            py += bH - 2;
            panel.Controls.Add(W.Lbl("-------- " + (npuModel.Length > 0 ? npuModel : "N/A"), modelFont, Theme.Dim, 232 - 2 * sp, 10, sp, py)); py += 10 + gap;
        }

        // Thermal
        panel.Controls.Add(W.Lbl("Thermal", sf, Theme.Dim, labelW, sH, sp, py));
        tl = W.Lbl("...", sf, Theme.Fg, valW, sH, valX, py); tl.TextAlign = ContentAlignment.MiddleRight; panel.Controls.Add(tl);
        py += sH + gap;

        // Auto-size panel height
        panel.Size = new Size(232, py); form.Controls.Add(panel); Theme.ApplyMonitor(form, panel, seps);
        form.Size = new Size(FW, y + py + gap);

        var timer = new System.Windows.Forms.Timer { Interval = cfg.SysMs }; timer.Tick += (s, e) => Refresh(); timer.Start(); Refresh();
        Program.ThemeChanged += (light) => {
            Theme.ApplyMonitor(form, panel, seps);
            W.BarColor(cb, Theme.Accent); W.BarColor(rb, Theme.Accent);
            W.BarColor(gb0, Theme.Accent); W.BarColor(gb1, Theme.Accent);
            if (hasNpu) W.BarColor(nb, Theme.Accent);
        };
        W.MakeDraggable(form, panel); return form;
    }
    static Panel AddSep(int w, int h, int x, int y) { var p = Theme.Sep(w, h, x, y); form.Controls.Add(p); return p; }

    // ─── LUID Auto-detect ──────────────────────────────
    // Heuristic: sort 3D-capable LUIDs by 3D engine instance count DESC.
    // The GPU driving the display (iGPU) has the most active 3D contexts.
    // The dGPU (on-demand gaming) typically has fewer, and may have Compute.
    // This works across NVIDIA+AMD, Intel Arc+Intel iGPU, and single-GPU configs.
    static void DetectLuid()
    {
        try {
            var memCat = new PerformanceCounterCategory("GPU Adapter Memory");
            string[] memNames = memCat.GetInstanceNames();
            if (memNames.Length == 0) return;

            var engCat = new PerformanceCounterCategory("GPU Engine");
            string[] engNames = engCat.GetInstanceNames();

            // Parallel lists for 3D-capable LUIDs
            var gpuLuid = new List<string>();
            var gpuCount3D = new List<int>();
            var gpuHasComp = new List<bool>();

            foreach (string mn in memNames) {
                string luid = mn.Split('_')[2];
                bool has3D = false, hasComp = false;
                int c3 = 0, cc = 0;
                foreach (string en in engNames) {
                    if (en.IndexOf(luid, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (en.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) >= 0) { has3D = true; c3++; }
                    if (en.IndexOf("engtype_Compute", StringComparison.OrdinalIgnoreCase) >= 0) { hasComp = true; cc++; }
                }

                // Create shared memory counter for this LUID
                try { memCounters[luid] = new PerformanceCounter("GPU Adapter Memory", "Shared Usage", mn); } catch { }

                Console.Error.WriteLine("LUID " + luid + " 3D=" + c3 + " Comp=" + cc);

                // NPU: Compute-only (exposed as GPU engine with Compute, no 3D)
                if (hasComp && !has3D && string.IsNullOrEmpty(NpuLuid)) { NpuLuid = luid; Console.Error.WriteLine("NPU LUID: " + luid); }

                // Collect 3D-capable LUIDs for GPU classification
                if (has3D) { gpuLuid.Add(luid); gpuCount3D.Add(c3); gpuHasComp.Add(hasComp); }
            }

            // Sort 3D GPUs by 3D engine count DESC (iGPU driving display has most)
            for (int i = 0; i < gpuLuid.Count - 1; i++) {
                for (int j = i + 1; j < gpuLuid.Count; j++) {
                    if (gpuCount3D[j] > gpuCount3D[i]) {
                        string tL = gpuLuid[i]; gpuLuid[i] = gpuLuid[j]; gpuLuid[j] = tL;
                        int tC = gpuCount3D[i]; gpuCount3D[i] = gpuCount3D[j]; gpuCount3D[j] = tC;
                        bool tH = gpuHasComp[i]; gpuHasComp[i] = gpuHasComp[j]; gpuHasComp[j] = tH;
                    }
                }
            }

            // Classify: most 3D engines → iGPU (display), remaining → dGPU (prefer with Compute)
            if (gpuLuid.Count > 0) {
                IgpuLuid = gpuLuid[0];
                Console.Error.WriteLine("iGPU LUID: " + IgpuLuid + " (3D=" + gpuCount3D[0] + ")");
            }
            if (gpuLuid.Count > 1) {
                bool found = false;
                for (int i = 1; i < gpuLuid.Count; i++) {
                    if (gpuHasComp[i]) { GpuLuid = gpuLuid[i]; Console.Error.WriteLine("dGPU LUID: " + GpuLuid + " (Compute)"); found = true; break; }
                }
                if (!found) { GpuLuid = gpuLuid[1]; Console.Error.WriteLine("dGPU LUID: " + GpuLuid); }
            }

            // Fallback: if no dGPU, use iGPU as primary
            if (string.IsNullOrEmpty(GpuLuid) && !string.IsNullOrEmpty(IgpuLuid)) { GpuLuid = IgpuLuid; Console.Error.WriteLine("GPU <- iGPU"); }
        }
        catch (Exception ex) { Console.Error.WriteLine("DetectLuid error: " + ex.Message); }
    }

    static void WarmupCounters()
    {
        ScanGpu3D(GpuLuid, gpu0_3D, ref gpu0Tick);
        ScanGpu3D(IgpuLuid, gpu1_3D, ref gpu1Tick);
        if (hasNpu) ScanNpuComp();

        foreach (var pc in gpu0_3D) try { pc.NextValue(); } catch { }
        foreach (var pc in gpu1_3D) try { pc.NextValue(); } catch { }
        if (hasNpu) foreach (var pc in npuComp) try { pc.NextValue(); } catch { }
    }

    static void ScanGpu3D(string target, List<PerformanceCounter> list, ref int tick)
    {
        list.Clear();
        if (string.IsNullOrEmpty(target)) return;
        try {
            var cat = new PerformanceCounterCategory("GPU Engine");
            foreach (string n in cat.GetInstanceNames()) {
                if (n.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    n.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) >= 0) {
                    try { list.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", n)); } catch { }
                }
            }
        }
        catch { }
        tick = 0;
        Console.Error.WriteLine("GPU 3D counters for " + target + ": " + list.Count);
    }

    static void ScanNpuComp()
    {
        npuComp.Clear();
        if (string.IsNullOrEmpty(NpuLuid)) return;
        try {
            var cat = new PerformanceCounterCategory("GPU Engine");
            foreach (string n in cat.GetInstanceNames()) {
                if (n.IndexOf(NpuLuid, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    n.IndexOf("engtype_Compute", StringComparison.OrdinalIgnoreCase) >= 0) {
                    try { npuComp.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", n)); } catch { }
                }
            }
        }
        catch { }
        npuTick = 0;
        Console.Error.WriteLine("NPU Compute counters: " + npuComp.Count);
    }

    static void Refresh()
    {
        // CPU
        try { int v = (int)cc.NextValue(); string freq = ""; try { if (fc != null && cpuMaxMHz > 0) { float ghz = cpuMaxMHz * fc.NextValue() / 100f / 1000f; freq = "  " + ghz.ToString("F2") + "GHz"; } } catch { } cv.Text = v + "%" + freq; W.BarSet(cb, v); } catch { }
        // RAM
        try { int v = (int)rc.NextValue(); rv.Text = v + "% (" + ramTotal + ")"; W.BarSet(rb, v); } catch { }

        // GPU0 (dGPU)
        RefreshGpu(GpuLuid, gv0, gb0, gpu0_3D, ref gpu0Tick, false);
        // GPU1 (iGPU)
        RefreshGpu(IgpuLuid, gv1, gb1, gpu1_3D, ref gpu1Tick, true);

        // NPU (conditional)
        if (hasNpu) RefreshNpu();

        // Temperature
        tempTick++; if (tempTick % 4 == 0) ReadTemps();
        if (tl.Text != cachedTemp && cachedTemp != "...") tl.Text = cachedTemp;

        string engInfo = "GPU0eng:" + gpu0_3D.Count + " GPU1eng:" + gpu1_3D.Count;
        if (hasNpu) engInfo += " NPUeng:" + npuComp.Count;
        Dumper.Sys(cv.Text, rv.Text,
            "0:" + (gv0 != null ? gv0.Text : "N/A") + " 1:" + (gv1 != null ? gv1.Text : "N/A"),
            nv != null ? nv.Text : "N/A",
            tl.Text, engInfo, "",
            memCounters.ContainsKey(GpuLuid ?? ""),
            hasNpu && memCounters.ContainsKey(NpuLuid ?? ""));
    }

    static void RefreshGpu(string target, Label valLabel, Panel bar, List<PerformanceCounter> counterList, ref int tick, bool isIgpu)
    {
        try {
            tick++;
            if (tick >= RESCAN || counterList.Count == 0) ScanGpu3D(target, counterList, ref tick);

            int total = 0;
            var stale = new List<PerformanceCounter>();
            foreach (var pc in counterList) {
                try { float v = pc.NextValue(); if (v >= 0) total += (int)v; }
                catch { stale.Add(pc); }
            }
            foreach (var pc in stale) { try { pc.Dispose(); } catch { } counterList.Remove(pc); }

            // Memory usage string
            string memStr = "";
            if (!string.IsNullOrEmpty(target) && memCounters.ContainsKey(target)) {
                try { float mb = memCounters[target].NextValue() / 1048576f; memStr = (int)mb + "MB"; } catch { }
            }

            // iGPU-specific: XPU activity via memory delta (Intel NPU-like activity detection)
            if (isIgpu && !string.IsNullOrEmpty(target) && memCounters.ContainsKey(target)) {
                try {
                    long cur = (long)memCounters[target].NextValue();
                    if (!baselineSet) { igpuBaseline = cur; baselineSet = true; }
                    long adjusted = cur > igpuBaseline ? cur - igpuBaseline : 0;
                    if (adjusted > 200 * 1048576) xpuAct = Math.Min(xpuAct + 1, 20);
                    else if (adjusted < 50 * 1048576 && xpuAct > 0) xpuAct--;
                    lastIgpuMem = cur;
                }
                catch { }
            }

            string utilStr;
            if (total > 0) utilStr = Math.Min(total, 100) + "%";
            else if (counterList.Count == 0) utilStr = "N/A";
            else if (isIgpu && xpuAct > 3) utilStr = "act";
            else utilStr = "0%";

            valLabel.Text = utilStr + (memStr.Length > 0 ? " (" + memStr + ")" : "");
            W.BarSet(bar, Math.Min(total, 100));
        }
        catch { valLabel.Text = "N/A"; W.BarSet(bar, 0); }
    }

    static void RefreshNpu()
    {
        try {
            npuTick++;
            if (npuTick >= RESCAN || npuComp.Count == 0) ScanNpuComp();

            int total = 0;
            var stale = new List<PerformanceCounter>();
            foreach (var pc in npuComp) {
                try { float v = pc.NextValue(); if (v >= 0) total += (int)v; }
                catch { stale.Add(pc); }
            }
            foreach (var pc in stale) { try { pc.Dispose(); } catch { } npuComp.Remove(pc); }

            string memStr = "";
            if (memCounters.ContainsKey(NpuLuid ?? "")) {
                try { float mb = memCounters[NpuLuid].NextValue() / 1048576f; memStr = (int)mb + "MB"; } catch { }
            }

            string utilStr;
            if (total > 0) utilStr = Math.Min(total, 100) + "%";
            else if (npuComp.Count == 0) utilStr = "N/A";
            else utilStr = "0%";

            nv.Text = utilStr + (memStr.Length > 0 ? " (" + memStr + ")" : "");
            W.BarSet(nb, Math.Min(total, 100));
        }
        catch { nv.Text = "N/A"; W.BarSet(nb, 0); }
    }

    static void ReadTemps()
    {
        try {
            using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSAcpi_ThermalZoneTemperature")) {
                float cpuT = float.NaN, gpuT = float.NaN;
                foreach (ManagementObject o in s.Get()) {
                    float c = (Convert.ToInt32(o["CurrentTemperature"]) / 10f) - 273.15f;
                    string name = (string)o["InstanceName"] ?? "";
                    if (name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0) cpuT = c;
                    else if (name.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0) gpuT = c;
                    else if (float.IsNaN(cpuT)) cpuT = c;
                }
                string txt = "";
                if (!float.IsNaN(cpuT)) txt += "CPU " + cpuT.ToString("F0") + " C";
                if (!float.IsNaN(gpuT)) txt += (txt.Length > 0 ? "  " : "") + "GPU " + gpuT.ToString("F0") + " C";
                if (txt.Length > 0) tl.Text = txt;
            }
        }
        catch { if (tl.Text == "..." || tl.Text == "--") tl.Text = "N/A"; }
    }
}
