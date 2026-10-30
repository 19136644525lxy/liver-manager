$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$appName = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$exe = Join-Path (Join-Path (Join-Path (Join-Path $root "src") "OrderManager") "bin\Debug\net10.0-windows") ($appName + ".exe")
$pname = $appName
$shots = Join-Path $root "shots"
$logf = Join-Path $shots "autofill.log"
function L($m) { Add-Content -Path $logf -Value ("[" + (Get-Date).ToString("HH:mm:ss") + "] " + $m) -Encoding UTF8 }
Remove-Item $logf -Force -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class BW {
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
}
"@

function WinList($pidOf) {
  $list = New-Object System.Collections.ArrayList
  $cb = [BW+EnumProc]{
    param($h, $l)
    $p = 0
    [void][BW]::GetWindowThreadProcessId($h, [ref]$p)
    if ($p -eq $pidOf -and [BW]::IsWindowVisible($h)) {
      $sb = New-Object System.Text.StringBuilder 256
      [void][BW]::GetWindowText($h, $sb, 256)
      $cn = New-Object System.Text.StringBuilder 256
      [void][BW]::GetClassName($h, $cn, 256)
      $r = New-Object BW+RECT
      [void][BW]::GetWindowRect($h, [ref]$r)
      [void]$list.Add([pscustomobject]@{ H = $h; Cls = $cn.ToString(); Title = $sb.ToString(); L = $r.Left; T = $r.Top; W = ($r.Right - $r.Left); Hh = ($r.Bottom - $r.Top) })
    }
    return $true
  }
  [void][BW]::EnumWindows($cb, [IntPtr]::Zero)
  return $list
}

function Shot($hwnd, $left, $top, $w, $h, $file) {
  [void][BW]::SetForegroundWindow($hwnd)
  Start-Sleep -Milliseconds 500
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($left, $top, 0, 0, $bmp.Size)
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + (Split-Path -Leaf $file))
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

$ae = [System.Windows.Automation.AutomationElement]::FromHandle($main.H)
$radioCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)
$navs = $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
$r1 = $navs.Item(1).Current.BoundingRectangle
[BW]::Click([int]($r1.X + $r1.Width / 2), [int]($r1.Y + $r1.Height / 2))
Start-Sleep -Milliseconds 1200

$btnNew = [string]([char]0x65B0 + [char]0x589E + [char]0x8BA2 + [char]0x5355)
$bc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $btnNew
$btn = $ae.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $bc)
$br = $btn.Current.BoundingRectangle
[BW]::Click([int]($br.X + $br.Width / 2), [int]($br.Y + $br.Height / 2))
Start-Sleep -Seconds 2

$edit = $null
foreach ($w in (WinList $app.Id)) { if ($w.H -ne $main.H -and $w.W -gt 500) { $edit = $w } }
if (-not $edit) { L "edit window not found"; exit 1 }
L ("edit " + $edit.W + "x" + $edit.Hh)

$ea = [System.Windows.Automation.AutomationElement]::FromHandle($edit.H)
$editCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Edit)
$edits = $ea.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)
L ("edits=" + $edits.Count)

function Val($el) {
  try {
    $vp = $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    return $vp.Current.Value
  } catch { return "<no value pattern>" }
}
function SetVal($el, $text) {
  $vp = $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
  $vp.SetValue($text)
}

$itemName = [string]([char]0x6807 + [char]0x51C6 + [char]0x5305 + [char]0x6708)
$bossName = [string]([char]0x81EA + [char]0x52A8 + [char]0x6D4B + [char]0x8BD5)

L ("before: e2=" + (Val $edits.Item(2)) + " | e6=" + (Val $edits.Item(6)) + " | e7=" + (Val $edits.Item(7)))
SetVal $edits.Item(0) $bossName
SetVal $edits.Item(2) $itemName
Start-Sleep -Milliseconds 900
L ("after:  e2=" + (Val $edits.Item(2)) + " | e6=" + (Val $edits.Item(6)) + " | e7=" + (Val $edits.Item(7)))
Shot $edit.H $edit.L $edit.T $edit.W $edit.Hh (Join-Path $shots "autofill-01-form.png")

$btnCancel = [string]([char]0x53D6 + [char]0x6D88)
$cc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $btnCancel
$cbtn = $ea.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cc)
if ($cbtn) {
  $cr = $cbtn.Current.BoundingRectangle
  [BW]::Click([int]($cr.X + $cr.Width / 2), [int]($cr.Y + $cr.Height / 2))
  Start-Sleep -Milliseconds 800
}
$still = $false
foreach ($w in (WinList $app.Id)) { if ($w.H -ne $main.H -and $w.W -gt 500) { $still = $true } }
L ("dialog still open=" + $still)

Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
L "done"
