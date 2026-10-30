$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$appName = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$exe = Join-Path (Join-Path $root ([string]([char]0x53D1 + [char]0x5E03))) ($appName + ".exe")
$pname = $appName
$shots = Join-Path $root "shots"
$logf = Join-Path $shots "relset.log"
function L($m) { Add-Content -Path $logf -Value ("[" + (Get-Date).ToString("HH:mm:ss") + "] " + $m) -Encoding UTF8 }
Remove-Item $logf -Force -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class CW3 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int c);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public static void Click(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(120);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
  }
  public static void Wheel(int x, int y, int notches) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(80);
    for (int i = 0; i < Math.Abs(notches); i++) {
      mouse_event(0x0800, 0, 0, (uint)(notches > 0 ? -120 : 120), IntPtr.Zero);
      System.Threading.Thread.Sleep(50);
    }
  }
}
"@

function WinList($pidOf) {
  $list = New-Object System.Collections.ArrayList
  $cb = [CW3+EnumProc]{
    param($h, $l)
    $p = 0
    [void][CW3]::GetWindowThreadProcessId($h, [ref]$p)
    if ($p -eq $pidOf -and [CW3]::IsWindowVisible($h)) {
      $sb = New-Object System.Text.StringBuilder 256
      [void][CW3]::GetWindowText($h, $sb, 256)
      $cn = New-Object System.Text.StringBuilder 256
      [void][CW3]::GetClassName($h, $cn, 256)
      $r = New-Object CW3+RECT
      [void][CW3]::GetWindowRect($h, [ref]$r)
      [void]$list.Add([pscustomobject]@{ H = $h; Cls = $cn.ToString(); Title = $sb.ToString(); L = $r.Left; T = $r.Top; W = ($r.Right - $r.Left); Hh = ($r.Bottom - $r.Top) })
    }
    return $true
  }
  [void][CW3]::EnumWindows($cb, [IntPtr]::Zero)
  return $list
}

function Shot($main, $name) {
  [void][CW3]::SetForegroundWindow($main.H)
  Start-Sleep -Milliseconds 320
  $bmp = New-Object System.Drawing.Bitmap $main.W, $main.Hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($main.L, $main.T, 0, 0, $bmp.Size)
  $bmp.Save((Join-Path $shots $name), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + $name)
}

Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
Start-Sleep -Milliseconds 800
cmd /c start "" "$exe"
Start-Sleep -Seconds 6
$app = Get-Process | Where-Object { $_.ProcessName -eq $pname } | Select-Object -First 1
if (-not $app) { L "app not started"; exit 1 }
$main = $null
for ($i = 0; $i -lt 20 -and -not $main; $i++) {
  foreach ($w in (WinList $app.Id)) { if ($w.Cls -like "HwndWrapper*" -and $w.W -gt 600) { $main = $w } }
  if (-not $main) { Start-Sleep -Milliseconds 500 }
}
if (-not $main) { L "main not found"; exit 1 }
L ("main " + $main.L + "," + $main.T + " " + $main.W + "x" + $main.Hh)

$ae = [System.Windows.Automation.AutomationElement]::FromHandle($main.H)
$btnCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Button)
$btns = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
$refreshName = [string]([char]0x5237 + [char]0x65B0)
$hit = $null
foreach ($b in $btns) { if ($b.Current.Name -eq $refreshName) { $hit = $b } }
if ($hit) {
  $inv = $hit.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
  $inv.Invoke()
  L "refresh invoked"
  Start-Sleep -Milliseconds 260
  Shot $main "rel-05-toast.png"
} else { L "refresh not found" }

# 切到设置页并滚到外观卡片
$radioCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
$navs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
$r = $navs.Item(6).Current.BoundingRectangle
[CW3]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
Start-Sleep -Milliseconds 900

[CW3]::Wheel([int]($main.L + $main.W / 2), [int]($main.T + $main.Hh / 2), 6)
Start-Sleep -Milliseconds 900
Shot $main "rel-06-settings-scrolled.png"

[CW3]::Wheel([int]($main.L + $main.W / 2), [int]($main.T + $main.Hh / 2), 10)
Start-Sleep -Milliseconds 900
Shot $main "rel-07-shortcuts.png"

Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
L "done"


