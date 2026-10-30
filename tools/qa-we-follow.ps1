Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class W7 {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
$title = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$log = Join-Path $out "qa-we.log"
function L($m) { Add-Content -Path $log -Value ("[" + (Get-Date -Format "HH:mm:ss") + "] " + $m) -Encoding UTF8 }
$p = Get-Process | Where-Object { $_.MainWindowTitle -eq $title } | Select-Object -First 1
if (-not $p) { L "no window"; exit 1 }
$h = $p.MainWindowHandle
[W7]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
[W7]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$cCb = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::CheckBox)
$cbs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cCb)
L ("checkboxes=" + $cbs.Count)
$target = $null
foreach ($c in $cbs) { if ($c.Current.Name -like ("*Wallpaper Engine*")) { $target = $c } }
if (-not $target) { L "no follow checkbox"; exit 1 }
$tp = $target.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
L ("before=" + $tp.Current.ToggleState)
$tp.Toggle()
Start-Sleep -Milliseconds 2000
L ("after=" + $tp.Current.ToggleState)
$cLb = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::List)
$lbs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cLb)
if ($lbs.Count -gt 0) {
  $sp = $lbs[0].GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
  $sel = $sp.Current.GetSelection()
  L ("selection count=" + $sel.Count)
  foreach ($s2 in $sel) { L ("selected=" + $s2.Current.Name) }
} else { L "no listbox" }
$r2 = New-Object W7+RECT
[W7]::GetWindowRect($h, [ref]$r2) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r2.Right - $r2.Left), ($r2.Bottom - $r2.Top)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r2.Left, $r2.Top, 0, 0, (New-Object System.Drawing.Size ($r2.Right - $r2.Left), ($r2.Bottom - $r2.Top)))
$bmp.Save((Join-Path $out "we-06-follow.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
[W7]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x43) | Out-Null
L "done"
