$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$relName = "bin\Debug\net10.0-windows"
$appName = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$relDir = Join-Path (Join-Path (Join-Path $root "src") "OrderManager") $relName
$exe = Join-Path $relDir ($appName + ".exe")
$pname = $appName
$shots = Join-Path $root "shots"
$logf = Join-Path $shots "today.log"

function L($m) { Add-Content -Path $logf -Value ("[" + (Get-Date).ToString("HH:mm:ss") + "] " + $m) -Encoding UTF8 }
Remove-Item $logf -Force -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class RW {
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
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(120);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(60);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
  }
}
"@

function WinList($pidOf) {
  $list = New-Object System.Collections.ArrayList
  $cb = [RW+EnumProc]{
    param($h, $l)
    $p = 0
    [void][RW]::GetWindowThreadProcessId($h, [ref]$p)
    if ($p -eq $pidOf -and [RW]::IsWindowVisible($h)) {
      $sb = New-Object System.Text.StringBuilder 256
      [void][RW]::GetWindowText($h, $sb, 256)
      $cn = New-Object System.Text.StringBuilder 256
      [void][RW]::GetClassName($h, $cn, 256)
      $r = New-Object RW+RECT
      [void][RW]::GetWindowRect($h, [ref]$r)
      [void]$list.Add([pscustomobject]@{ H = $h; Cls = $cn.ToString(); Title = $sb.ToString(); L = $r.Left; T = $r.Top; W = ($r.Right - $r.Left); Hh = ($r.Bottom - $r.Top) })
    }
    return $true
  }
  [void][RW]::EnumWindows($cb, [IntPtr]::Zero)
  return $list
}

function Shot($hwnd, $left, $top, $w, $h, $file) {
  [void][RW]::SetForegroundWindow($hwnd)
  Start-Sleep -Milliseconds 600
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($left, $top, 0, 0, $bmp.Size)
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + (Split-Path -Leaf $file))
}

if (-not (Test-Path $exe)) { L "dev exe missing"; exit 1 }
Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
Start-Sleep -Milliseconds 800
cmd /c start "" "$exe"
Start-Sleep -Seconds 7
$app = Get-Process | Where-Object { $_.ProcessName -eq $pname } | Select-Object -First 1
if (-not $app) { L "dev app not started"; exit 1 }
L ("pid=" + $app.Id)
$main = $null
for ($i = 0; $i -lt 20 -and -not $main; $i++) {
  foreach ($w in (WinList $app.Id)) { if ($w.Cls -like "HwndWrapper*" -and $w.W -gt 600) { $main = $w } }
  if (-not $main) { Start-Sleep -Milliseconds 500 }
}
if (-not $main) { L "dev main not found"; exit 1 }
L ("main " + $main.W + "x" + $main.Hh)
Shot $main.H $main.L $main.T $main.W $main.Hh (Join-Path $shots "today-01-calendar-before.png")

$ae = [System.Windows.Automation.AutomationElement]::FromHandle($main.H)
$rc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
$navs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
L ("navs=" + $navs.Count)
# 1) calendar page: screenshot before
if ($navs.Count -ge 3) {
  $r2 = $navs.Item(2).Current.BoundingRectangle
  [RW]::Click([int]($r2.X + $r2.Width / 2), [int]($r2.Y + $r2.Height / 2))
  Start-Sleep -Milliseconds 1500
  Shot $main.H $main.L $main.T $main.W $main.Hh (Join-Path $shots "today-01-calendar-before.png")
}

# 2) click the check-in-today button
$btnName = [string]([char]0x4ECA + [char]0x5929 + [char]0x6253 + [char]0x5361)
$bc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $btnName
$btn = $ae.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $bc)
if ($btn) {
  $br = $btn.Current.BoundingRectangle
  L ("today button at " + [int]$br.X + "," + [int]$br.Y)
  [RW]::Click([int]($br.X + $br.Width / 2), [int]($br.Y + $br.Height / 2))
  Start-Sleep -Milliseconds 1500
  Shot $main.H $main.L $main.T $main.W $main.Hh (Join-Path $shots "today-02-calendar-after.png")

  # 3) second click: should NOT uncheck
  [RW]::Click([int]($br.X + $br.Width / 2), [int]($br.Y + $br.Height / 2))
  Start-Sleep -Milliseconds 1200
  Shot $main.H $main.L $main.T $main.W $main.Hh (Join-Path $shots "today-03-calendar-second-click.png")
} else { L "today button not found" }

# 4) items page (points removed)
if ($navs.Count -ge 6) {
  $r6 = $navs.Item(5).Current.BoundingRectangle
  [RW]::Click([int]($r6.X + $r6.Width / 2), [int]($r6.Y + $r6.Height / 2))
  Start-Sleep -Milliseconds 1500
  Shot $main.H $main.L $main.T $main.W $main.Hh (Join-Path $shots "today-04-items.png")
}
$errf = Join-Path (Split-Path -Parent $exe) ([string]([char]0x9519 + [char]0x8BEF + [char]0x65E5 + [char]0x5FD7) + ".txt")
if (Test-Path $errf) { L "ERROR LOG EXISTS" } else { L "no error log" }
Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
L "done"



