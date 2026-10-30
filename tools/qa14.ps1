$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
$log = Join-Path $out "qa14.log"
Remove-Item $log -ErrorAction SilentlyContinue
function L($m) { ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) | Out-File -FilePath $log -Append -Encoding UTF8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
public class QaWin14 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  public struct RECT { public int Left, Top, Right, Bottom; }
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  public static List<string> ListWindows(uint pid) {
    var res = new List<string>();
    EnumWindows((h, p) => {
      uint wp; GetWindowThreadProcessId(h, out wp);
      if (wp != pid) return true;
      if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      var cn = new StringBuilder(256); GetClassName(h, cn, 256);
      RECT r; GetWindowRect(h, out r);
      res.Add(h.ToInt64() + "|" + cn.ToString() + "|" + sb.ToString() + "|" + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom);
      return true;
    }, IntPtr.Zero);
    return res;
  }
}
"@
$uia = [System.Windows.Automation.AutomationElement]
$ctProp = [System.Windows.Automation.AutomationElement]::ControlTypeProperty
$exe = (Get-ChildItem (Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows") -Filter *.exe | Select-Object -First 1).FullName
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)
$app = Get-Process | Where-Object { $_.ProcessName -eq $pname } | Select-Object -First 1
if (-not $app) { L "app not running"; exit 1 }
$mainH = [IntPtr]$app.MainWindowHandle
L ("pid=" + $app.Id)
$editH = [IntPtr]0
foreach ($line in [QaWin14]::ListWindows([uint32]$app.Id)) {
  L ("  " + $line)
  $parts = $line.Split("|")
  if (([IntPtr][int64]$parts[0]) -ne $mainH) { $editH = [IntPtr][int64]$parts[0] }
}
if ($editH -eq [IntPtr]0) { L "no dialog"; exit 1 }
[QaWin14]::SetForegroundWindow($editH) | Out-Null
Start-Sleep -Milliseconds 600
$dlg = $uia::FromHandle($editH)

$ec = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Edit)
$edits = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ec)
L ("edits=" + $edits.Count)
$i = 0
foreach ($e in $edits) {
  $rc = $e.Current.BoundingRectangle
  L ("  edit[{0}] name='{1}' rect={2},{3},{4},{5}" -f $i, $e.Current.Name, [int]$rc.X, [int]$rc.Y, [int]$rc.Width, [int]$rc.Height)
  $i++
}
function Set-Val($el, $text) {
  try { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text); L ("  set -> " + $text); return $true }
  catch { L ("  set fail: " + $_.Exception.Message); return $false }
}
$nameEl = $null; $amountEl = $null
foreach ($e in $edits) {
  $rc = $e.Current.BoundingRectangle
  if ($rc.Y -gt 480 -and $rc.Y -lt 520 -and $rc.X -lt 1300) { if (-not $nameEl) { $nameEl = $e } }
  if ($rc.Y -gt 700 -and $rc.Y -lt 760 -and $rc.X -gt 1000) { $amountEl = $e }
}
if ($nameEl) { Set-Val $nameEl "TestQA" } else { L "name edit not found" }
if ($amountEl) { Set-Val $amountEl "88" } else { L "amount edit not found" }
Start-Sleep -Milliseconds 400

$r = New-Object QaWin14+RECT
[QaWin14]::GetWindowRect($editH, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bmp.Save((Join-Path $out "08c-order-edit-uia.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
L "saved 08c"

$bc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Button)
$saveName = ([char]0x4FDD).ToString() + [char]0x5B58
foreach ($b in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
  if ($b.Current.Name -eq $saveName) {
    $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    L "save invoked"
  }
}
Start-Sleep -Milliseconds 2500
$left = 0
foreach ($line in [QaWin14]::ListWindows([uint32]$app.Id)) {
  $parts = $line.Split("|")
  if (([IntPtr][int64]$parts[0]) -ne $mainH) { $left++; L ("still: " + $line) }
}
L ("extra windows left=" + $left)

$tc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Text)
$main = $uia::FromHandle($mainH)
$texts = @()
foreach ($t in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tc)) {
  if ($t.Current.Name -match "^[0-9]+$" -or $t.Current.Name -match "1016|1104") { $texts += $t.Current.Name }
}
L ("numeric texts: " + ($texts -join ","))
L "done"
