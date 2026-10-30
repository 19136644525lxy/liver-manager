$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root ("src\OrderManager\bin\Debug\net10.0-windows\" + [char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6 + ".exe")
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)
$logf = Join-Path (Join-Path $root "shots") "drag.log"

function L($m) { Add-Content -Path $logf -Value ("[" + (Get-Date).ToString("HH:mm:ss") + "] " + $m) -Encoding UTF8 }
Remove-Item $logf -Force -ErrorAction SilentlyContinue

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class DW {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int c);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public static void Down() { mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); }
  public static void Up() { mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); }
}
"@

function WinList($pidOf) {
  $list = New-Object System.Collections.ArrayList
  $cb = [DW+EnumProc]{
    param($h, $l)
    $p = 0
    [void][DW]::GetWindowThreadProcessId($h, [ref]$p)
    if ($p -eq $pidOf -and [DW]::IsWindowVisible($h)) {
      $sb = New-Object System.Text.StringBuilder 256
      [void][DW]::GetWindowText($h, $sb, 256)
      $cn = New-Object System.Text.StringBuilder 256
      [void][DW]::GetClassName($h, $cn, 256)
      $r = New-Object DW+RECT
      [void][DW]::GetWindowRect($h, [ref]$r)
      [void]$list.Add([pscustomobject]@{ H = $h; Cls = $cn.ToString(); Title = $sb.ToString(); L = $r.Left; T = $r.Top; W = ($r.Right - $r.Left); Hh = ($r.Bottom - $r.Top) })
    }
    return $true
  }
  [void][DW]::EnumWindows($cb, [IntPtr]::Zero)
  return $list
}

function MainWin($pidOf, $mainH) {
  foreach ($w in (WinList $pidOf)) { if ($w.H -eq $mainH) { return $w } }
  return $null
}

Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
Start-Sleep -Milliseconds 900
cmd /c start "" "$exe"
Start-Sleep -Seconds 6
$app = Get-Process | Where-Object { $_.ProcessName -eq $pname } | Select-Object -First 1
if (-not $app) { L "app not started"; exit 1 }
$main = $null
for ($i = 0; $i -lt 20 -and -not $main; $i++) {
  foreach ($w in (WinList $app.Id)) { if ($w.Cls -like "HwndWrapper*" -and $w.W -gt 800) { $main = $w } }
  if (-not $main) { Start-Sleep -Milliseconds 500 }
}
if (-not $main) { L "main not found"; exit 1 }
[void][DW]::SetForegroundWindow($main.H)
Start-Sleep -Milliseconds 600
L ("start " + $main.L + "," + $main.T + " " + $main.W + "x" + $main.Hh)

$px = $main.L + 600
$py = $main.T + 28
[void][DW]::SetCursorPos($px, $py)
Start-Sleep -Milliseconds 200
L "double click title bar"
[DW]::Down(); Start-Sleep -Milliseconds 40; [DW]::Up()
Start-Sleep -Milliseconds 60
[DW]::Down(); Start-Sleep -Milliseconds 40; [DW]::Up()
Start-Sleep -Milliseconds 1200
$m1 = MainWin $app.Id $main.H
L ("after dblclick " + $m1.L + "," + $m1.T + " " + $m1.W + "x" + $m1.Hh)

# single click on title bar of maximized window should NOT restore
[void][DW]::SetCursorPos(($m1.L + 600), ($m1.T + 28))
Start-Sleep -Milliseconds 300
[DW]::Down(); Start-Sleep -Milliseconds 50; [DW]::Up()
Start-Sleep -Milliseconds 1000
$s1 = MainWin $app.Id $main.H
L ("after single click " + $s1.L + "," + $s1.T + " " + $s1.W + "x" + $s1.Hh)

# double click on title bar should restore
[DW]::Down(); Start-Sleep -Milliseconds 40; [DW]::Up()
Start-Sleep -Milliseconds 70
[DW]::Down(); Start-Sleep -Milliseconds 40; [DW]::Up()
Start-Sleep -Milliseconds 1200
$m2 = MainWin $app.Id $main.H
L ("after dblclick2 " + $m2.L + "," + $m2.T + " " + $m2.W + "x" + $m2.Hh)

$dx = $m2.L + 600
$dy = $m2.T + 28
[void][DW]::SetCursorPos($dx, $dy)
Start-Sleep -Milliseconds 300
L "press and move"
[DW]::Down()
Start-Sleep -Milliseconds 300
for ($k = 1; $k -le 12; $k++) {
  [void][DW]::SetCursorPos($dx - (15 * $k), $dy + (5 * $k))
  Start-Sleep -Milliseconds 60
  if ($k -eq 6 -or $k -eq 12) {
    $mm = MainWin $app.Id $main.H
    L ("  moving " + $mm.L + "," + $mm.T)
  }
}
[DW]::Up()
Start-Sleep -Milliseconds 500
$m3 = MainWin $app.Id $main.H
L ("after drag " + $m3.L + "," + $m3.T)
if ($m3.L -ne $m2.L -or $m3.T -ne $m2.T) { L "drag OK" } else { L "drag FAILED" }

$errf = Join-Path (Split-Path -Parent $exe) "错误日志.txt"
if (Test-Path $errf) { L ("error log found: " + (Get-Item $errf).LastWriteTime) }

Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
L "done"
