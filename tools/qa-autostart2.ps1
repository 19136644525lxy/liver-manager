Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;
public class AT {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
 public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$root = Split-Path $PSScriptRoot -Parent
$t = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$p = Get-Process | Where-Object { $_.MainWindowTitle -eq $t } | Select-Object -First 1
$h = $p.MainWindowHandle
[AT]::SetForegroundWindow($h) | Out-Null
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$cbCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::CheckBox)
$cbs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cbCond)
$target = $null
foreach ($c in $cbs) { if ($c.Current.Name -match '^(?=\S*' + [string]([char]0x5F00 + [char]0x673A) + ')') { $target = $c } }
if (-not $target) { foreach ($c in $cbs) { 'cb=' + $c.Current.Name }; exit 1 }
$tp = $target.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
$r = $target.Current.BoundingRectangle
[AT]::SetCursorPos([int]($r.Left + 14), [int]($r.Top + $r.Height / 2)) | Out-Null
Start-Sleep -Milliseconds 200
[AT]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
[AT]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 900
'after click state=' + $tp.Current.ToggleState
$v = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $t -ErrorAction SilentlyContinue).$t
'registry=' + $v
$wr = New-Object AT+RECT
[AT]::GetWindowRect($h, [ref]$wr) | Out-Null
$w = $wr.Right - $wr.Left; $hh = $wr.Bottom - $wr.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[AT]::PrintWindow($h, $dc, 2) | Out-Null
$g.ReleaseHdc($dc)
$bmp.Save((Join-Path (Join-Path $root 'shots') 'autostart-on.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
'saved autostart-on.png'
