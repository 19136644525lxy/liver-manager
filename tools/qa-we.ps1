Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class W6 {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, int d, UIntPtr e);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 public struct RECT { public int Left, Top, Right, Bottom; }
 public static void Click(int x, int y) { SetCursorPos(x, y); mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }
}
"@
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
$title = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$settingsText = [string]([char]0x8BBE + [char]0x7F6E)
$exe = Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows"
$exe = Join-Path $exe ($title + ".exe")
$log = Join-Path $out "qa-we.log"
function L($m) { Add-Content -Path $log -Value ("[" + (Get-Date -Format "HH:mm:ss") + "] " + $m) -Encoding UTF8 }
Set-Content -Path $log -Value "" -Encoding UTF8
Get-Process | Where-Object { $_.ProcessName -eq $title } | ForEach-Object { try { $_.Kill() } catch {} }
Start-Sleep -Milliseconds 500
L ("exe=" + $exe)
cmd /c start "" "$exe"
$p = $null
for ($i = 0; $i -lt 40 -and -not $p; $i++) { Start-Sleep -Milliseconds 400; $p = Get-Process | Where-Object { $_.MainWindowTitle -eq $title } | Select-Object -First 1 }
if (-not $p) { L "no window"; exit 1 }
$h = $p.MainWindowHandle
[W6]::ShowWindow($h, 9) | Out-Null
Start-Sleep -Milliseconds 1200
function Focus() {
  [W6]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
  [W6]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 400
}
function Shot($name) {
  Focus
  $r2 = New-Object W6+RECT
  [W6]::GetWindowRect($h, [ref]$r2) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap ($r2.Right - $r2.Left), ($r2.Bottom - $r2.Top)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r2.Left, $r2.Top, 0, 0, (New-Object System.Drawing.Size ($r2.Right - $r2.Left), ($r2.Bottom - $r2.Top)))
  $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + $name)
}
function GetNavs() {
  $ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
  $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
  return ,$ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
function OnSettings() {
  $ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
  $c2 = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Text)
  $texts = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c2)
  $r0 = New-Object W6+RECT
  [W6]::GetWindowRect($h, [ref]$r0) | Out-Null
  foreach ($t in $texts) {
    if ($t.Current.Name -eq $settingsText) {
      $rc = $t.Current.BoundingRectangle
      if ($rc.Top -lt ($r0.Top + 130) -and $rc.Left -gt ($r0.Left + 200)) { return $true }
    }
  }
  return $false
}
$ok = $false
for ($try = 0; $try -lt 4 -and -not $ok; $try++) {
  Focus
  $navs = GetNavs
  L ("try=" + $try + " navs=" + $navs.Count)
  if ($navs.Count -ge 7) {
    $rc = $navs[6].Current.BoundingRectangle
    [W6]::Click([int]($rc.X + $rc.Width / 2), [int]($rc.Y + $rc.Height / 2))
    Start-Sleep -Milliseconds 800
    if (OnSettings) { $ok = $true }
  }
}
L ("onSettings=" + $ok)
function PageScroller() {
  $ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
  $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty), $true
  $all = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
  $best = $null; $bestArea = 0
  foreach ($e in $all) {
    try {
      $sp = $e.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
      if ($sp.Current.VerticallyScrollable) {
        $rc = $e.Current.BoundingRectangle
        $area = $rc.Width * $rc.Height
        if ($area -gt $bestArea) { $bestArea = $area; $best = $sp }
      }
    } catch {}
  }
  return $best
}
$sp = PageScroller
if ($sp) { L "scrollable found"; $sp.SetScrollPercent(-1, 8); Start-Sleep -Milliseconds 900; Shot "we-01-wallpaper-card.png"; $sp.SetScrollPercent(-1, 26); Start-Sleep -Milliseconds 900; Shot "we-02-wallpaper-list.png"; $sp.SetScrollPercent(-1, 45); Start-Sleep -Milliseconds 900; Shot "we-03-wallpaper-card2.png"
$sp.SetScrollPercent(-1, 62); Start-Sleep -Milliseconds 900; Shot "we-04-wallpaper-list.png"
$ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$cItem = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::ListItem)
$items = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cItem)
L ("items=" + $items.Count)
if ($items.Count -gt 0) {
  $rc = $items[0].Current.BoundingRectangle
  Focus
  [W6]::Click([int]($rc.X + $rc.Width / 2), [int]($rc.Y + $rc.Height / 2))
  Start-Sleep -Milliseconds 1500
  Shot "we-05-after-select.png"
} } else { L "no scrollable"; Shot "we-01-wallpaper-card.png" }
[W6]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x43) | Out-Null
L "done"

