Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;
public class AU {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
 public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$root = Split-Path $PSScriptRoot -Parent
$shots = Join-Path $root 'shots'
$t = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$p = Get-Process | Where-Object { $_.MainWindowTitle -eq $t } | Select-Object -First 1
if (-not $p) { 'no window'; exit 1 }
$h = $p.MainWindowHandle
[AU]::ShowWindow($h, 9) | Out-Null
[AU]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
[AU]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 1200
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)

function ClickEl($el) {
  $r = $el.Current.BoundingRectangle
  [AU]::SetCursorPos([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2)) | Out-Null
  Start-Sleep -Milliseconds 150
  [AU]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  [AU]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 500
}

$radioCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
$nav = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
if ($nav.Count -ge 7) { ClickEl $nav[6] }
Start-Sleep -Milliseconds 800

$spCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty), $true
$sps = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $spCond)
$big = $null; $area = 0
foreach ($e in $sps) { $r = $e.Current.BoundingRectangle; $a = $r.Width * $r.Height; if ($a -gt $area) { $area = $a; $big = $e } }
if ($big) { $sp = $big.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern); $sp.SetScrollPercent(-1, 100); Start-Sleep -Milliseconds 700 }

$cbCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::CheckBox)
$cbs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cbCond)
$target = $null
foreach ($c in $cbs) { if ($c.Current.Name -like ([string]([char]0x5F00 + [char]0x673A) + '*')) { $target = $c } }
if (-not $target) { foreach ($c in $cbs) { 'cb=' + $c.Current.Name }; exit 1 }
'checkbox=' + $target.Current.Name
$tp = $target.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
'before=' + $tp.Current.ToggleState
ClickEl $target
Start-Sleep -Milliseconds 800
'after=' + $tp.Current.ToggleState
$v = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $t -ErrorAction SilentlyContinue).$t
'registry=' + $v
$wr = New-Object AU+RECT
[AU]::GetWindowRect($h, [ref]$wr) | Out-Null
$w = $wr.Right - $wr.Left; $hh = $wr.Bottom - $wr.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[AU]::PrintWindow($h, $dc, 2) | Out-Null
$g.ReleaseHdc($dc)
$bmp.Save((Join-Path $shots 'autostart-on.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
'saved ' + $w + 'x' + $hh

