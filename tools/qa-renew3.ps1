Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Text;using System.Collections.Generic;using System.Runtime.InteropServices;
public class CapU {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int max);
 public delegate bool EnumProc(IntPtr h, IntPtr p);
 public struct RECT { public int Left, Top, Right, Bottom; }
 public static string TitleOf(IntPtr h) { var sb = new StringBuilder(256); GetWindowTextW(h, sb, 256); return sb.ToString(); }
 public static List<IntPtr> WindowsOf(int pid) {
   var list = new List<IntPtr>();
   EnumWindows(delegate(IntPtr h, IntPtr p) {
     uint wp; GetWindowThreadProcessId(h, out wp);
     if (wp == (uint)pid && IsWindowVisible(h)) list.Add(h);
     return true;
   }, IntPtr.Zero);
   return list;
 } }
"@
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'shots'
$logPath = Join-Path $out 'qa-renew3.log'
Remove-Item $logPath -ErrorAction SilentlyContinue
function L($m) { Add-Content -Path $logPath -Value ("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $m) -Encoding UTF8 }
function Shot($hwnd, $name) {
  $r = New-Object CapU+RECT
  [CapU]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
  if ($w -le 0 -or $hh -le 0) { L ("bad rect " + $w + "x" + $hh); return }
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  [CapU]::PrintWindow($hwnd, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc)
  $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + $name + " " + $w + "x" + $hh)
}
function ClickAt($x, $y) {
  [CapU]::SetCursorPos([int]$x, [int]$y) | Out-Null
  Start-Sleep -Milliseconds 90
  [CapU]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  [CapU]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 400
}
function FindEl($rootEl, $type, $name) {
  $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), $type
  $items = $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  foreach ($i in $items) { if ($i.Current.Name -eq $name) { return $i } }
  return $null
}
$title = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$p = Get-Process | Where-Object { $_.MainWindowTitle -eq $title } | Select-Object -First 1
if (-not $p) { L 'no window'; exit 1 }
$h = $p.MainWindowHandle
[CapU]::ShowWindow($h, 9) | Out-Null
[CapU]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 700
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$ordersName = [string]([char]0x8BA2 + [char]0x5355 + [char]0x7BA1 + [char]0x7406)
$renewName = [string]([char]0x7EED + [char]0x5355)
$confirmName = [string]([char]0x786E + [char]0x8BA4 + [char]0x7EED + [char]0x5355)
$rb = FindEl $ae ([System.Windows.Automation.ControlType]::RadioButton) $ordersName
$b = $rb.Current.BoundingRectangle
ClickAt ($b.Left + $b.Width / 2) ($b.Top + $b.Height / 2)
[CapU]::SetCursorPos(20, 20) | Out-Null
Start-Sleep -Milliseconds 900
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$grid = FindEl $ae ([System.Windows.Automation.ControlType]::DataGrid) ''
if (-not $grid) {
  $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::DataGrid)
  $grid = $ae.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
$g = $grid.Current.BoundingRectangle
L ("grid=" + $g.Left + "," + $g.Top + " " + $g.Width + "x" + $g.Height)
ClickAt ($g.Left + 150) ($g.Top + 70)
[CapU]::SetCursorPos(20, 20) | Out-Null
Start-Sleep -Milliseconds 800
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$btn = FindEl $ae ([System.Windows.Automation.ControlType]::Button) $renewName
if (-not $btn) { L 'renew button not found'; exit 1 }
$ip = $null
if ($btn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$ip)) { $ip.Invoke(); L 'invoked renew' } else { L 'no invoke pattern' }
Start-Sleep -Milliseconds 1500
foreach ($w in [CapU]::WindowsOf($p.Id)) {
  $r2 = New-Object CapU+RECT
  [CapU]::GetWindowRect($w, [ref]$r2) | Out-Null
  L ("win hwnd=" + $w + " title=" + [CapU]::TitleOf($w) + " " + ($r2.Right - $r2.Left) + "x" + ($r2.Bottom - $r2.Top))
}
$renew = $null
foreach ($w in [CapU]::WindowsOf($p.Id)) {
  if ([CapU]::TitleOf($w) -eq $renewName) { $renew = $w }
}
if (-not $renew) { Shot $h 'renew-04-main.png'; L 'renew window not found'; exit 0 }
Shot $renew 'renew-05-dialog.png'
$re = [System.Windows.Automation.AutomationElement]::FromHandle($renew)
$cbtn = FindEl $re ([System.Windows.Automation.ControlType]::Button) $confirmName
if ($cbtn) {
  $ip2 = $null
  if ($cbtn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$ip2)) { $ip2.Invoke(); L 'invoked confirm' }
} else { L 'confirm button not found' }
Start-Sleep -Milliseconds 1500
Shot $h 'renew-06-after.png'
L 'done'
