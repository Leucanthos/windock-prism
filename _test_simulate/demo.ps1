# demo.ps1 — visual demo of real mouse simulation
# Watch the mouse move in a spiral pattern, click screen corners,
# and perform a drag rectangle. A magenta target form shows
# where clicks land.
#
# IMPORTANT: Keep your hands off the mouse while this runs!
# The demo takes ~8 seconds.
$ErrorActionPreference = 'Stop'
$base = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).FullName

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

Write-Host @"
============================================
  SimMouse — Real Mouse Simulation Demo
============================================

This demo will:
  1. Move the mouse in a spiral pattern
  2. Click at 4 corners of the screen
  3. Drag a small rectangle
  4. Click on a visible magenta target form

HANDS OFF the mouse while this runs!
Starting in 3 seconds...
"@ -ForegroundColor Cyan

Start-Sleep -Seconds 3

# Generate a temporary demo C# file
$demoCs = @"
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

class Demo
{
    [STAThread] static void Main()
    {
        int sw = Screen.PrimaryScreen.WorkingArea.Width;
        int sh = Screen.PrimaryScreen.WorkingArea.Height;

        // Show a target form so we can see it
        var target = new Form {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(sw / 2 - 60, sh / 2 - 60),
            Size = new Size(120, 120),
            BackColor = Color.FromArgb(255, 0, 255),
            TopMost = true,
            ShowInTaskbar = false
        };
        target.Show();
        Application.DoEvents();
        Thread.Sleep(500);

        // ---- 1: Spiral movement ----
        int cx = sw / 2, cy = sh / 2;
        SimMouse.MoveTo(cx, cy);
        Thread.Sleep(200);

        // Draw an expanding spiral
        for (int r = 0; r < 360 * 3; r += 15)
        {
            double rad = r * Math.PI / 180.0;
            int radius = 30 + r / 10;
            int sx = cx + (int)(radius * Math.Cos(rad));
            int sy = cy + (int)(radius * Math.Sin(rad) * 0.7); // ellipse
            // Clamp to screen
            sx = Math.Max(10, Math.Min(sw - 10, sx));
            sy = Math.Max(10, Math.Min(sh - 10, sy));
            SimMouse.MoveTo(sx, sy);
            Thread.Sleep(8);
        }
        Thread.Sleep(300);

        // ---- 2: Click at 4 corners ----
        int margin = 30;
        Point[] corners = {
            new Point(margin, margin),
            new Point(sw - margin, margin),
            new Point(sw - margin, sh - margin),
            new Point(margin, sh - margin)
        };
        foreach (var corner in corners)
        {
            SimMouse.MoveSmooth(corner.X, corner.Y, 500);
            Thread.Sleep(150);
            SimMouse.LeftClick();
            Thread.Sleep(300);
        }

        // ---- 3: Drag a rectangle ----
        int rx = sw / 2 - 100, ry = sh / 2 + 150;
        SimMouse.LeftDrag(rx, ry, rx + 200, ry + 100, 600);
        Thread.Sleep(300);

        // ---- 4: Click on target form ----
        var center = target.PointToScreen(new Point(60, 60));
        SimMouse.MoveSmooth(center.X, center.Y, 400);
        Thread.Sleep(150);
        SimMouse.LeftClick();
        Thread.Sleep(300);

        // Move back to center and finish
        SimMouse.MoveSmooth(sw / 2, sh / 2, 400);

        target.Close();
        target.Dispose();
        Application.Exit();
    }
}
"@

$demoCsFile = Join-Path $env:TEMP "_sim_mouse_demo.cs"
$demoExeFile = Join-Path $env:TEMP "_sim_mouse_demo.exe"
Set-Content -Path $demoCsFile -Value $demoCs

Write-Host "Compiling demo..." -ForegroundColor Gray
$refs = "/reference:System.Windows.Forms.dll /reference:System.Drawing.dll"
$compile = & "$csc" /target:winexe /out:"$demoExeFile" $refs "$base\MouseSimulator.cs" "$demoCsFile" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Compile failed: $compile" -ForegroundColor Red
    exit 1
}

Write-Host "Running demo... (do NOT touch mouse!)" -ForegroundColor Yellow
& $demoExeFile

Write-Host ""
Write-Host "Demo complete!" -ForegroundColor Green

# Cleanup
Remove-Item $demoCsFile -Force -ErrorAction SilentlyContinue
Remove-Item $demoExeFile -Force -ErrorAction SilentlyContinue
