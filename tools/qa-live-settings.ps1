Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;
public class LS {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 public struct RECT { public int Left, Top, Right, Bottom; } }
"@
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'shots'
$t = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$p = Get-Process | Where-Object { $_.MainWindowTitle -eq $t } | Select-Object -First 1
if (-not $p) { 'no window'; exit 1 }
$h = $p.MainWindowHandle
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
$nav = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
Add-Type @"
using System;using System.Runtime.InteropServices;
public class MS {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
}
"@
if ($nav.Count -ge 7) {
  $rr = $nav[6].Current.BoundingRectangle
  [MS]::SetCursorPos([int]($rr.Left + $rr.Width / 2), [int]($rr.Top + $rr.Height / 2)) | Out-Null
  Start-Sleep -Milliseconds 120
  [MS]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  [MS]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
}
Start-Sleep -Milliseconds 800
$spCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty), $true
$sps = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $spCond)
$big = $null; $area = 0
foreach ($e in $sps) { $r = $e.Current.BoundingRectangle; $a = $r.Width * $r.Height; if ($a -gt $area) { $area = $a; $big = $e } }
function Shot($name) {
  $r = New-Object LS+RECT
  [LS]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc()
  [LS]::PrintWindow($h, $dc, 2) | Out-Null
  $g.ReleaseHdc($dc)
  $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output ("saved " + $name + " " + $w + "x" + $hh)
}
if ($big) {
  $sp = $big.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
  $sp.SetScrollPercent(-1, 40); Start-Sleep -Milliseconds 400; Shot 'live-01-appearance.png'
  $sp.SetScrollPercent(-1, 62); Start-Sleep -Milliseconds 400; Shot 'live-02-wallpaper.png'
  $sp.SetScrollPercent(-1, 82); Start-Sleep -Milliseconds 400; Shot 'live-03-startup.png'
}

