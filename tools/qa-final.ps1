$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root ("src\OrderManager\bin\Debug\net10.0-windows\" + [char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6 + ".exe")
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)
$shots = Join-Path $root "shots"
$logf = Join-Path $shots "final.log"
$names = @("01-dashboard", "02-orders", "03-calendar", "04-reports", "05-runners", "06-items", "07-settings")
$btnNew = [string]([char]0x65B0 + [char]0x589E + [char]0x8BA2 + [char]0x5355)
$btnCancel = [string]([char]0x53D6 + [char]0x6D88)

function L($m) { Add-Content -Path $logf -Value ("[" + (Get-Date).ToString("HH:mm:ss") + "] " + $m) -Encoding UTF8 }
Remove-Item $logf -Force -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WF {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int c);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public static void Click(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(120);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
    System.Threading.Thread.Sleep(60);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
  }
}
"@

function WinList($pidOf) {
  $list = New-Object System.Collections.ArrayList
  $cb = [WF+EnumProc]{
    param($h, $l)
    $p = 0
    [void][WF]::GetWindowThreadProcessId($h, [ref]$p)
    if ($p -eq $pidOf -and [WF]::IsWindowVisible($h)) {
      $sb = New-Object System.Text.StringBuilder 256
      [void][WF]::GetWindowText($h, $sb, 256)
      $cn = New-Object System.Text.StringBuilder 256
      [void][WF]::GetClassName($h, $cn, 256)
      $r = New-Object WF+RECT
      [void][WF]::GetWindowRect($h, [ref]$r)
      [void]$list.Add([pscustomobject]@{ H = $h; Cls = $cn.ToString(); Title = $sb.ToString(); L = $r.Left; T = $r.Top; W = ($r.Right - $r.Left); Hh = ($r.Bottom - $r.Top) })
    }
    return $true
  }
  [void][WF]::EnumWindows($cb, [IntPtr]::Zero)
  return $list
}

function Shot($hwnd, $left, $top, $w, $h, $file) {
  [void][WF]::SetForegroundWindow($hwnd)
  Start-Sleep -Milliseconds 500
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($left, $top, 0, 0, $bmp.Size)
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + (Split-Path -Leaf $file) + " " + $w + "x" + $h)
}

Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
Start-Sleep -Milliseconds 900
$startExe = $exe
cmd /c start "" "$startExe"
Start-Sleep -Seconds 6
$app = Get-Process | Where-Object { $_.ProcessName -eq $pname } | Select-Object -First 1
if (-not $app) { L "app not started"; exit 1 }
L ("pid=" + $app.Id)

$main = $null
for ($i = 0; $i -lt 20 -and -not $main; $i++) {
  $wins = WinList $app.Id
  foreach ($w in $wins) { if ($w.Cls -like "HwndWrapper*" -and $w.W -gt 800) { $main = $w } }
  if (-not $main) { Start-Sleep -Milliseconds 500 }
}
if (-not $main) { L "main not found"; exit 1 }
L ("main=" + $main.W + "x" + $main.Hh + " at " + $main.L + "," + $main.T)

$ae = [System.Windows.Automation.AutomationElement]::FromHandle($main.H)
$rc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
$navs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
L ("navs=" + $navs.Count)

for ($i = 0; $i -lt $navs.Count -and $i -lt 7; $i++) {
  $n = $navs.Item($i)
  $r = $n.Current.BoundingRectangle
  [WF]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
  Start-Sleep -Milliseconds 1500
  Shot $main.H $main.L $main.T $main.W $main.Hh (Join-Path $shots ("final-" + $names[$i] + ".png"))
}

# 回到订单页并打开编辑窗口
for ($i = 0; $i -lt $navs.Count -and $i -lt 7; $i++) {
  $r2 = $navs.Item($i).Current.BoundingRectangle
  if ([int]($r2.Y) -gt 0 -and $i -eq 1) {
    [WF]::Click([int]($r2.X + $r2.Width / 2), [int]($r2.Y + $r2.Height / 2))
  }
}
Start-Sleep -Milliseconds 1200
$bc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $btnNew
$btn = $ae.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $bc)
if ($btn) {
  $br = $btn.Current.BoundingRectangle
  [WF]::Click([int]($br.X + $br.Width / 2), [int]($br.Y + $br.Height / 2))
  Start-Sleep -Seconds 2
  $edit = $null
  foreach ($w in (WinList $app.Id)) { if ($w.H -ne $main.H -and $w.W -gt 500) { $edit = $w } }
  if ($edit) {
    L ("edit=" + $edit.W + "x" + $edit.Hh + " title=" + $edit.Title)
    Shot $edit.H $edit.L $edit.T $edit.W $edit.Hh (Join-Path $shots "final-08-order-edit.png")
    $ae2 = [System.Windows.Automation.AutomationElement]::FromHandle($edit.H)
    $cc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $btnCancel
    $cb2 = $ae2.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cc)
    if ($cb2) {
      $cr = $cb2.Current.BoundingRectangle
      [WF]::Click([int]($cr.X + $cr.Width / 2), [int]($cr.Y + $cr.Height / 2))
      Start-Sleep -Milliseconds 800
      L "edit closed"
    }
  } else { L "edit window not found" }
} else { L "new button not found" }

Start-Sleep -Milliseconds 300
Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
L "done"
