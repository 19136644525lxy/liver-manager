Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class CapT {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
 public struct RECT { public int Left, Top, Right, Bottom; } }
"@
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'shots'
$logPath = Join-Path $out 'qa-renew2.log'
Remove-Item $logPath -ErrorAction SilentlyContinue
function L($m) { Add-Content -Path $logPath -Value ("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $m) -Encoding UTF8 }
function Shot($hwnd, $name) {
  $r = New-Object CapT+RECT
  [CapT]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
  if ($w -le 0 -or $hh -le 0) { L ("bad rect " + $w + "x" + $hh); return }
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  [CapT]::PrintWindow($hwnd, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc)
  $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + $name + " " + $w + "x" + $hh)
}
function InvokeNamed($rootEl, $cond, $name) {
  $items = $rootEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  foreach ($i in $items) {
    if ($i.Current.Name -ne $name) { continue }
    $pat = $null
    if ($i.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) { $pat.Invoke(); L ("invoked " + $name); return $true }
    $b = $i.Current.BoundingRectangle
    ClickAt ($b.Left + $b.Width / 2) ($b.Top + $b.Height / 2)
    [CapT]::SetCursorPos(20, 20) | Out-Null
    L ("clicked " + $name)
    return $true
  }
  L ("NOT FOUND " + $name)
  return $false
}
function ClickAt($x, $y) {
  [CapT]::SetCursorPos([int]$x, [int]$y) | Out-Null
  Start-Sleep -Milliseconds 90
  [CapT]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  [CapT]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 400
}
$title = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$p = Get-Process | Where-Object { $_.MainWindowTitle -eq $title } | Select-Object -First 1
if (-not $p) { L 'no window'; exit 1 }
$h = $p.MainWindowHandle
[CapT]::ShowWindow($h, 9) | Out-Null
[CapT]::SetForegroundWindow($h) | Out-Null
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$radio = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
$btn = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Button)
$ordersName = [string]([char]0x8BA2 + [char]0x5355 + [char]0x7BA1 + [char]0x7406)
$renewName = [string]([char]0x7EED + [char]0x5355)
$confirmName = [string]([char]0x786E + [char]0x8BA4 + [char]0x7EED + [char]0x5355)
InvokeNamed $ae $radio $ordersName
Start-Sleep -Milliseconds 900
$gridCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::DataGrid)
$grid = $ae.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $gridCond)
$g = $grid.Current.BoundingRectangle
L ("grid=" + $g.Left + "," + $g.Top + " " + $g.Width + "x" + $g.Height)
ClickAt ($g.Left + 150) ($g.Top + 70)
[CapT]::SetCursorPos(20, 20) | Out-Null
Start-Sleep -Milliseconds 800
InvokeNamed $ae $btn $renewName
Start-Sleep -Milliseconds 1400
# ?? UIA ???? processo ?????????
$rootEl = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $p.Id
$all = $rootEl.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)
L ("root children=" + $all.Count)
$renewEl = $null
foreach ($w in $all) { L ("  win=" + $w.Current.Name); if ($w.Current.Name -eq $renewName) { $renewEl = $w } }
if (-not $renewEl) { L 'renew window not in UIA root'; exit 0 }
$hwnd = [IntPtr]$renewEl.Current.NativeWindowHandle
L ("renew hwnd=" + $hwnd)
Shot $hwnd 'renew-02-dialog.png'
InvokeNamed $renewEl $btn $confirmName
Start-Sleep -Milliseconds 1200
Shot $h 'renew-03-after.png'
L 'done'

