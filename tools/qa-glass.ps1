Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class CapB {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
 public struct RECT { public int Left, Top, Right, Bottom; } }
"@
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'shots'
$title = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$p = Get-Process | Where-Object { $_.MainWindowTitle -eq $title } | Select-Object -First 1
if (-not $p) { Write-Output 'no window'; exit 1 }
$h = $p.MainWindowHandle
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
$navs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
Write-Output ('navs=' + $navs.Count)
function Shot($name) {
  [CapB]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
  Start-Sleep -Milliseconds 250
  $r = New-Object CapB+RECT
  [CapB]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hh))
  $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output ('saved ' + $name)
}
function ClickNav($i) {
  $el = $navs.Item($i)
  $r = $el.Current.BoundingRectangle
  $x = [int]($r.Left + $r.Width / 2); $y = [int]($r.Top + $r.Height / 2)
  [CapB]::SetCursorPos($x, $y) | Out-Null
  Start-Sleep -Milliseconds 60
  [CapB]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  [CapB]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 600
}
ClickNav 0
Shot 'glass-01-dashboard.png'
ClickNav 1
Shot 'glass-02-orders.png'
ClickNav 3
Shot 'glass-03-reports.png'
$null = [CapB]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x03)
