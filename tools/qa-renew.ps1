Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public class CapR {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
 public struct RECT { public int Left, Top, Right, Bottom; }
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int max);
 delegate bool EnumProc(IntPtr h, IntPtr p);
 public static string TitleOf(IntPtr h) { var sb = new StringBuilder(256); GetWindowTextW(h, sb, 256); return sb.ToString(); }
 public static System.Collections.Generic.List<IntPtr> FindWindows(int pid) {
   var result = new System.Collections.Generic.List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr p) {
     uint wpid; GetWindowThreadProcessId(h, out wpid);
     if (wpid == (uint)pid && IsWindowVisible(h)) result.Add(h);
     return true;
   }, IntPtr.Zero);
   return result;
 } }
"@
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'shots'
$logPath = Join-Path $out 'qa-renew.log'
Remove-Item $logPath -ErrorAction SilentlyContinue
function L($m) { Add-Content -Path $logPath -Value ("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $m) -Encoding UTF8 }

$title = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$p = Get-Process | Where-Object { $_.MainWindowTitle -eq $title } | Select-Object -First 1
if (-not $p) { L 'no window'; exit 1 }
$h = $p.MainWindowHandle
[CapR]::ShowWindow($h, 9) | Out-Null
[CapR]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
[CapR]::SetForegroundWindow($h) | Out-Null
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$radio = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
$btn = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Button)

function ClickAt($x, $y) {
  [CapR]::SetCursorPos([int]$x, [int]$y) | Out-Null
  Start-Sleep -Milliseconds 90
  [CapR]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  [CapR]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 450
}

function ClickNamed($cond, $name) {
  $items = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  foreach ($i in $items) {
    if ($i.Current.Name -ne $name) { continue }
    $b = $i.Current.BoundingRectangle
    ClickAt ($b.Left + $b.Width / 2) ($b.Top + $b.Height / 2)
    L ("clicked " + $name)
    return $true
  }
  L ("NOT FOUND " + $name)
  return $false
}

$ordersName = [string]([char]0x8BA2 + [char]0x5355 + [char]0x7BA1 + [char]0x7406)
$renewName = [string]([char]0x7EED + [char]0x5355)
ClickNamed $radio $ordersName
Start-Sleep -Milliseconds 900

$gridCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::DataGrid)
$grid = $ae.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $gridCond)
if ($grid) {
  $g = $grid.Current.BoundingRectangle
  L ("grid=" + $g.Left + "," + $g.Top + " " + $g.Width + "x" + $g.Height)
  ClickAt ($g.Left + 130) ($g.Top + 90)
  Start-Sleep -Milliseconds 400
} else { L 'no grid' }

ClickNamed $btn $renewName
Start-Sleep -Milliseconds 1000

$found = $null
$list = [CapR]::FindWindows($p.Id)
foreach ($hh in $list) {
  if ($hh -eq $h) { continue }
  $r0 = New-Object CapR+RECT
  [CapR]::GetWindowRect($hh, [ref]$r0) | Out-Null
  $w0 = $r0.Right - $r0.Left
  $t0 = [CapR]::TitleOf($hh)
  L ("child hwnd=" + $hh + " title=" + $t0 + " " + $w0 + "x" + ($r0.Bottom - $r0.Top))
  if ($w0 -ge 300 -and $w0 -le 900) { $found = $hh; break }
}
if (-not $found) { L 'no candidate window'; exit 0 }

$r = New-Object CapR+RECT
[CapR]::GetWindowRect($found, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $hh2 = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hh2
$g2 = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g2.GetHdc()
[CapR]::PrintWindow($found, $hdc, 2) | Out-Null
$g2.ReleaseHdc($hdc)
$bmp.Save((Join-Path $out 'renew-01-dialog.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g2.Dispose(); $bmp.Dispose()
L 'saved renew-01-dialog.png'

