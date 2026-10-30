$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$appName = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$bin = Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows"
$exe = Join-Path $bin ($appName + ".exe")
$pname = $appName
$shots = Join-Path $root "shots"
$logf = Join-Path $shots "perf.log"
$perfFile = Join-Path $bin "perf.log"
function L($m) { Add-Content -Path $logf -Value ("[" + (Get-Date).ToString("HH:mm:ss.fff") + "] " + $m) -Encoding UTF8 }
Remove-Item $logf -Force -ErrorAction SilentlyContinue
Remove-Item $perfFile -Force -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class CW4 {
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
  public static void Click(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(90);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(50);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
  }
}
"@

function WinList($pidOf) {
  $list = New-Object System.Collections.ArrayList
  $cb = [CW4+EnumProc]{
    param($h, $l)
    $p = 0
    [void][CW4]::GetWindowThreadProcessId($h, [ref]$p)
    if ($p -eq $pidOf -and [CW4]::IsWindowVisible($h)) {
      $sb = New-Object System.Text.StringBuilder 256
      [void][CW4]::GetWindowText($h, $sb, 256)
      $cn = New-Object System.Text.StringBuilder 256
      [void][CW4]::GetClassName($h, $cn, 256)
      $r = New-Object CW4+RECT
      [void][CW4]::GetWindowRect($h, [ref]$r)
      [void]$list.Add([pscustomobject]@{ H = $h; Cls = $cn.ToString(); Title = $sb.ToString(); L = $r.Left; T = $r.Top; W = ($r.Right - $r.Left); Hh = ($r.Bottom - $r.Top) })
    }
    return $true
  }
  [void][CW4]::EnumWindows($cb, [IntPtr]::Zero)
  return $list
}

function Shot($main, $name) {
  [void][CW4]::SetForegroundWindow($main.H)
  Start-Sleep -Milliseconds 260
  $bmp = New-Object System.Drawing.Bitmap $main.W, $main.Hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($main.L, $main.T, 0, 0, $bmp.Size)
  $bmp.Save((Join-Path $shots $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + $name)
}

Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
Start-Sleep -Milliseconds 700
$env:DG_PERF = "1"
cmd /c start "" "$exe"
Start-Sleep -Seconds 5
$app = Get-Process | Where-Object { $_.ProcessName -eq $pname } | Select-Object -First 1
if (-not $app) { L "app not started"; exit 1 }
$main = $null
for ($i = 0; $i -lt 20 -and -not $main; $i++) {
  foreach ($w in (WinList $app.Id)) { if ($w.Cls -like "HwndWrapper*" -and $w.W -gt 600) { $main = $w } }
  if (-not $main) { Start-Sleep -Milliseconds 500 }
}
if (-not $main) { L "main not found"; exit 1 }
L ("main " + $main.L + "," + $main.T + " " + $main.W + "x" + $main.Hh)

$ae = [System.Windows.Automation.AutomationElement]::FromHandle($main.H)
$radioCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
$navs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
L ("navs=" + $navs.Count)

# round 1: click each nav once (cold first switch)
for ($i = 0; $i -lt $navs.Count; $i++) {
  $r = $navs.Item($i).Current.BoundingRectangle
  [CW4]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
  Start-Sleep -Milliseconds 420
}
L "round 1 done"

# round 2/3: repeat switching (warm)
for ($k = 0; $k -lt 2; $k++) {
  for ($i = 0; $i -lt $navs.Count; $i++) {
    $r = $navs.Item($i).Current.BoundingRectangle
    [CW4]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 320
  }
}
L "round 3 done"

Shot $main "perf-orders.png"

Start-Sleep -Milliseconds 300
if (Test-Path $perfFile) {
  Get-Content $perfFile -Encoding UTF8 | ForEach-Object { L ("P " + $_) }
} else {
  L "perf.log missing"
}

Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
L "done"


