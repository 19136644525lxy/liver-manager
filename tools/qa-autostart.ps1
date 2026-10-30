Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class AS {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
$t = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$p = Get-Process | Where-Object { $_.MainWindowTitle -eq $t } | Select-Object -First 1
if (-not $p) { 'no window'; exit 1 }
$h = $p.MainWindowHandle
[AS]::SetForegroundWindow($h) | Out-Null
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)

function ClickEl($el) {
  $r = $el.Current.BoundingRectangle
  [AS]::SetCursorPos([int]($r.Left + $r.Width / 2), [int]($r.Top + $r.Height / 2)) | Out-Null
  Start-Sleep -Milliseconds 150
  [AS]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  [AS]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 400
}

$radioCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
$nav = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
if ($nav.Count -ge 7) { ClickEl $nav[6] }
Start-Sleep -Milliseconds 600

$spCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty), $true
$sps = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $spCond)
$big = $null; $area = 0
foreach ($e in $sps) { $r = $e.Current.BoundingRectangle; $a = $r.Width * $r.Height; if ($a -gt $area) { $area = $a; $big = $e } }
if ($big) {
  $sp = $big.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
  $sp.SetScrollPercent(-1, 100)
  Start-Sleep -Milliseconds 600
}

$cbCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::CheckBox)
$cbs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cbCond)
$target = $null
foreach ($c in $cbs) { if ($c.Current.Name -match [string]([char]0x5F00 + [char]0x673A)) { $target = $c } }
if (-not $target) { 'checkbox not found'; foreach ($c in $cbs) { 'cb=' + $c.Current.Name }; exit 1 }
'found checkbox: ' + $target.Current.Name
$tp = $target.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
'before=' + $tp.Current.ToggleState
$tp.Toggle()
Start-Sleep -Milliseconds 700
$after = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $t -ErrorAction SilentlyContinue).$t
'registry after ON = ' + $after
$tp.Toggle()
Start-Sleep -Milliseconds 700
$after2 = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $t -ErrorAction SilentlyContinue).$t
'registry after OFF = ' + $after2
'final state=' + $tp.Current.ToggleState
