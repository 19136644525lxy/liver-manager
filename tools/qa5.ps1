$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$log = Join-Path $out "qa5.log"
Remove-Item $log -ErrorAction SilentlyContinue
function L($m) { ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) | Out-File -FilePath $log -Append -Encoding UTF8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public class QaWin5 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  public struct RECT { public int Left, Top, Right, Bottom; }
  public static void Click(double x, double y) {
    SetCursorPos((int)x, (int)y);
    Thread.Sleep(120);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
    Thread.Sleep(90);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    Thread.Sleep(300);
  }
}
"@
$uia = [System.Windows.Automation.AutomationElement]
$ctProp = [System.Windows.Automation.AutomationElement]::ControlTypeProperty
L "start"

$exe = (Get-ChildItem (Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows") -Filter *.exe | Select-Object -First 1).FullName
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)
Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 900

& cmd /c start "" $exe
$app = $null
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 500
  $app = Get-Process | Where-Object { $_.ProcessName -eq $pname } | Select-Object -First 1
  if ($app -and $app.MainWindowHandle -ne 0) { break }
}
if (-not $app) { L "no process"; exit 1 }
$app.Refresh()
L ("pid={0}" -f $app.Id)
Start-Sleep -Seconds 3

$pidCond = New-Object System.Windows.Automation.PropertyCondition($ctProp, $null)
$pidOk = $false
try { $pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$app.Id); $pidOk = $true } catch { L ("pidcond fail: " + $_.Exception.Message) }
L ("pidcond ok=" + $pidOk)

function Get-Tops($procId) {
  $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$procId)
  return $uia::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $c)
}
$tops = Get-Tops $app.Id
$main = $null
foreach ($t in $tops) { if ($t.Current.ClassName -ne "#32770") { $main = $t } }
if (-not $main) { L "main not found"; exit 1 }
$mainH = [IntPtr]$main.Current.NativeWindowHandle
[QaWin5]::ShowWindow($mainH, 3) | Out-Null
Start-Sleep -Milliseconds 700
[QaWin5]::SetForegroundWindow($mainH) | Out-Null
Start-Sleep -Milliseconds 1200

function Save-Window($hwnd, $path) {
  $r = New-Object QaWin5+RECT
  [QaWin5]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + (Split-Path $path -Leaf))
}

$tc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Text)
$bc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Button)
function Clear-Dialogs($tag) {
  $n = 0
  while ($n -lt 4) {
    $d = $null
    foreach ($t in (Get-Tops $app.Id)) { if ($t.Current.ClassName -eq "#32770") { $d = $t } }
    if (-not $d) { return }
    $texts = @()
    foreach ($x in $d.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tc)) { if ($x.Current.Name) { $texts += $x.Current.Name } }
    L ("DIALOG {0}: {1}" -f $tag, ($texts -join " | "))
    $h = [IntPtr]$d.Current.NativeWindowHandle
    Save-Window $h (Join-Path $out ("err-" + $tag + "-" + $n + ".png"))
    $btn = $d.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $bc)
    if ($btn) { $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Start-Sleep -Milliseconds 900
    $n++
  }
}

$rc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
$names = @("01-dashboard","02-orders","03-calendar","04-reports","05-runners","06-items","07-settings")
L ("navs=" + $navs.Count)
for ($i = 0; $i -lt $navs.Count; $i++) {
  $n = $navs[$i]
  $r = $n.Current.BoundingRectangle
  [QaWin5]::Click($r.X + $r.Width / 2, $r.Y + $r.Height / 2)
  Start-Sleep -Milliseconds 1500
  Clear-Dialogs ("nav" + $i)
  Start-Sleep -Milliseconds 500
  $file = if ($i -lt $names.Count) { $names[$i] } else { "page-$i" }
  Save-Window $mainH (Join-Path $out ($file + ".png"))
}

# 回到订单管理页，打开新增订单窗口
$r = $navs[1].Current.BoundingRectangle
[QaWin5]::Click($r.X + $r.Width / 2, $r.Y + $r.Height / 2)
Start-Sleep -Milliseconds 1800
$newName = ([char]0x65B0).ToString() + [char]0x589E + [char]0x8BA2 + [char]0x5355
$newBtn = $null
foreach ($b in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
  if ($b.Current.Name) { L ("btn: " + $b.Current.Name) }
  if ($b.Current.Name -like ("*" + ([char]0x65B0 + [char]0x589E) + "*")) { $newBtn = $b }
}
if ($newBtn) {
  $br = $newBtn.Current.BoundingRectangle
  L "click new order"
  [QaWin5]::Click($br.X + $br.Width / 2, $br.Y + $br.Height / 2)
  Start-Sleep -Milliseconds 2500
  $wins2 = Get-Tops $app.Id
  $edit = $null
  foreach ($w in $wins2) {
    L ("  win cls=" + $w.Current.ClassName + " name=" + $w.Current.Name)
    if ((([IntPtr]$w.Current.NativeWindowHandle) -ne $mainH) -and ($w.Current.ClassName -ne "#32770")) { $edit = $w }
  }
  if ($edit) {
    $eh = [IntPtr]$edit.Current.NativeWindowHandle
    [QaWin5]::SetForegroundWindow($eh) | Out-Null
    Start-Sleep -Milliseconds 1000
    Save-Window $eh (Join-Path $out "08-order-edit.png")
  } else {
    L "edit window not found"
  }
} else {
  L "new button not found"
}
L "done"
