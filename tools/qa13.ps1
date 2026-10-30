$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
$log = Join-Path $out "qa13.log"
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
public class QaWin13 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
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
  public static void Click(double x, double y) {
    SetCursorPos((int)x, (int)y);
    Thread.Sleep(120);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
    Thread.Sleep(90);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    Thread.Sleep(250);
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
foreach ($line in [QaWin13]::ListWindows([uint32]$app.Id)) {
  L ("  " + $line)
  $parts = $line.Split("|")
  if (([IntPtr][int64]$parts[0]) -ne $mainH) { $editH = [IntPtr][int64]$parts[0] }
}
if ($editH -eq [IntPtr]0) { L "no dialog"; exit 1 }
[QaWin13]::SetForegroundWindow($editH) | Out-Null
Start-Sleep -Milliseconds 600

$dlg = $uia::FromHandle($editH)
L ("dlg=" + $dlg.Current.Name + " cls=" + $dlg.Current.ClassName)
$ec = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Edit)
$edits = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ec)
L ("edits=" + $edits.Count)
$i = 0
foreach ($e in $edits) {
  L ("  edit[{0}] name='{1}' cls={2} rect={3} val='{4}'" -f $i, $e.Current.Name, $e.Current.ClassName, $e.Current.BoundingRectangle, $e.Current.ValueIfAvailable())
  $i++
}
function Set-Val($el, $text) {
  try { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text); L ("  set -> " + $text) }
  catch { L ("  set fail: " + $_.Exception.Message) }
}
$nameEl = $null; $amountEl = $null
foreach ($e in $edits) {
  $rc = $e.Current.BoundingRectangle
  if ($rc.Top -lt 200) { if (-not $nameEl) { $nameEl = $e } }
}
# amount field: y 716..747, x > 1000
foreach ($e in $edits) {
  $rc = $e.Current.BoundingRectangle
  if ($rc.Top -gt 700 -and $rc.Top -lt 760 -and $rc.Left -gt 1000) { $amountEl = $e }
}
if ($nameEl) { Set-Val $nameEl "TestQA" } else { L "name edit not found" }
if ($amountEl) { Set-Val $amountEl "88" } else { L "amount edit not found" }
Start-Sleep -Milliseconds 500

Add-Type -AssemblyName System.Drawing
$r = New-Object QaWin13+RECT
[QaWin13]::GetWindowRect($editH, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bmp.Save((Join-Path $out "08c-order-edit-uia.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
L "saved 08c"

$saved = $false
$bc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Button)
$saveName = ([char]0x4FDD).ToString() + [char]0x5B58
foreach ($b in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
  L ("  btn '" + $b.Current.Name + "'")
  if ($b.Current.Name -eq $saveName) {
    $brr = $b.Current.BoundingRectangle
    L ("invoke save at " + $brr.X + "," + $brr.Y)
    $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $saved = $true
  }
}
Start-Sleep -Milliseconds 2500
$left = 0
foreach ($line in [QaWin13]::ListWindows([uint32]$app.Id)) {
  $parts = $line.Split("|")
  if (([IntPtr][int64]$parts[0]) -ne $mainH) { $left++; L ("still: " + $line) }
}
L ("save invoked=" + $saved + " extra windows left=" + $left)
L "done"
