Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class CapQ {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
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
$rc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)

function Shot($name) {
  Start-Sleep -Milliseconds 500
  $pp = Get-Process | Where-Object { $_.MainWindowTitle -eq $title } | Select-Object -First 1
  $script:h = $pp.MainWindowHandle
  $r = New-Object CapQ+RECT
  [CapQ]::GetWindowRect($h, [ref]$r) | Out-Null
  Write-Output ('handle=' + $h + ' rect=' + $r.Left + ',' + $r.Top + ' ' + ($r.Right - $r.Left) + 'x' + ($r.Bottom - $r.Top))
  $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  [CapQ]::PrintWindow($h, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc)
  $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output ('saved ' + $name)
}

function SelectNav($name) {
  $pp = Get-Process | Where-Object { $_.MainWindowTitle -eq $title } | Select-Object -First 1
  $script:h = $pp.MainWindowHandle
  [CapQ]::ShowWindow($h, 9) | Out-Null
  [CapQ]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
  [CapQ]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 400
  $navs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
  foreach ($n in $navs) {
    if ($n.Current.Name -ne $name) { continue }
    $b = $n.Current.BoundingRectangle
    [CapQ]::SetCursorPos([int]($b.Left + $b.Width / 2), [int]($b.Top + $b.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 80
    [CapQ]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [CapQ]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
    Write-Output ('clicked ' + $name)
    return
  }
  Write-Output ('nav not found: ' + $name)
}

SelectNav ([string]([char]0x6982 + [char]0x89C8))
Shot 'g2-01-dashboard.png'
SelectNav ([string]([char]0x8BA2 + [char]0x5355 + [char]0x7BA1 + [char]0x7406))
Shot 'g2-02-orders.png'
SelectNav ([string]([char]0x7EDF + [char]0x8BA1 + [char]0x62A5 + [char]0x8868))
Shot 'g2-03-reports.png'
SelectNav ([string]([char]0x8BBE + [char]0x7F6E))
$svc = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty), $true))
Write-Output ('scrollable=' + $svc.Count)
$best = $null; $bestArea = 0
foreach ($c in $svc) {
  $b = $c.Current.BoundingRectangle
  $area = $b.Width * $b.Height
  if ($area -gt $bestArea) { $bestArea = $area; $best = $c }
}
if ($best) {
  $pat = $best.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
  $pat.SetScrollPercent(-1, 30)
  Write-Output 'scrolled 30%'
}
Shot 'g2-04-settings-appearance.png'



