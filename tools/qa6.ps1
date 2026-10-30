$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
$log = Join-Path $out "qa6.log"
Remove-Item $log -ErrorAction SilentlyContinue
function L($m) { ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) | Out-File -FilePath $log -Append -Encoding UTF8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public class QaWin6 {
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

$exe = (Get-ChildItem (Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows") -Filter *.exe | Select-Object -First 1).FullName
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)
$app = Get-Process | Where-Object { $_.ProcessName -eq $pname } | Select-Object -First 1
if (-not $app) { L "app not running"; exit 1 }
L ("pid=" + $app.Id)

function Get-Tops($procId) {
  $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$procId)
  return $uia::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $c)
}
function Save-Window($hwnd, $path) {
  $r = New-Object QaWin6+RECT
  [QaWin6]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + (Split-Path $path -Leaf) + " ${w}x${h}")
}

$main = $null
foreach ($t in (Get-Tops $app.Id)) { if ($t.Current.ClassName -ne "#32770") { $main = $t } }
if (-not $main) { L "main not found"; exit 1 }
$mainH = [IntPtr]$main.Current.NativeWindowHandle
[QaWin6]::ShowWindow($mainH, 3) | Out-Null
Start-Sleep -Milliseconds 500
[QaWin6]::SetForegroundWindow($mainH) | Out-Null
Start-Sleep -Milliseconds 1200

# 切到订单管理
$rc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
$r = $navs[1].Current.BoundingRectangle
[QaWin6]::Click($r.X + $r.Width / 2, $r.Y + $r.Height / 2)
Start-Sleep -Milliseconds 1800

$bc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Button)
$newBtn = $null
foreach ($b in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
  if ($b.Current.Name -like ("*" + ([char]0x65B0 + [char]0x589E) + "*")) { $newBtn = $b }
}
if (-not $newBtn) { L "new btn missing"; exit 1 }
L ("invoke new btn '" + $newBtn.Current.Name + "'")
$newBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 3000

$edit = $null
foreach ($w in (Get-Tops $app.Id)) {
  L ("  win cls=" + $w.Current.ClassName + " name=" + $w.Current.Name)
  if ((([IntPtr]$w.Current.NativeWindowHandle) -ne $mainH) -and ($w.Current.ClassName -ne "#32770")) { $edit = $w }
}
if (-not $edit) { L "no edit window"; exit 1 }
$eh = [IntPtr]$edit.Current.NativeWindowHandle
[QaWin6]::SetForegroundWindow($eh) | Out-Null
Start-Sleep -Milliseconds 1200
Save-Window $eh (Join-Path $out "08-order-edit.png")

# 关闭：点取消
$cancel = ([char]0x53D6).ToString() + [char]0x6D88
foreach ($b in $edit.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
  if ($b.Current.Name -eq $cancel) {
    $br = $b.Current.BoundingRectangle
    L "click cancel"
    [QaWin6]::Click($br.X + $br.Width / 2, $br.Y + $br.Height / 2)
    break
  }
}
L "done"
