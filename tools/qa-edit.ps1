$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
$log = Join-Path $out "qa4.log"
Remove-Item $log -ErrorAction SilentlyContinue
function L($m) { ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) | Out-File -FilePath $log -Append -Encoding UTF8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public class QaWin4 {
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

$exe = (Get-ChildItem (Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows") -Filter *.exe | Select-Object -First 1).FullName
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)
$app = Get-Process | Where-Object { $_.ProcessName -eq $pname } | Select-Object -First 1
if (-not $app) { L "app not running"; exit 1 }
L ("pid={0}" -f $app.Id)

function Get-Wins($procId) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($uia::ProcessIdProperty, [int]$procId)
  return $uia::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
}
function Save-Window($hwnd, $path) {
  $r = New-Object QaWin4+RECT
  [QaWin4]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + (Split-Path $path -Leaf) + " ${w}x${h}")
}

$wins = Get-Wins $app.Id
$main = $uia::FromHandle([IntPtr]$app.MainWindowHandle)
if (-not $main) { L "main not found"; exit 1 }
$mainH = [IntPtr]$main.Current.NativeWindowHandle
[QaWin4]::ShowWindow($mainH, 3) | Out-Null
Start-Sleep -Milliseconds 600
[QaWin4]::SetForegroundWindow($mainH) | Out-Null
Start-Sleep -Milliseconds 900

# 点订单管理
$rc = New-Object System.Windows.Automation.PropertyCondition($uia::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
for ($i = 0; $i -lt $navs.Count; $i++) { L ("nav[{0}]='{1}'" -f $i, $navs[$i].Current.Name) }
if ($navs.Count -gt 1) {
  $n = $navs[1]
  $r = $n.Current.BoundingRectangle
  [QaWin4]::Click($r.X + $r.Width / 2, $r.Y + $r.Height / 2)
  L "clicked orders nav"
}
Start-Sleep -Milliseconds 1500

# 找「新增订单」按钮
$bc = New-Object System.Windows.Automation.PropertyCondition($uia::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$newBtn = $null
foreach ($b in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
  $nm = $b.Current.Name
  if ($nm) { L ("btn: '{0}'" -f $nm) }
  if ($nm -like "*新增*") { $newBtn = $b }
}
if (-not $newBtn) { L "new button not found"; exit 1 }
$r = $newBtn.Current.BoundingRectangle
L ("click 新增订单 at {0},{1}" -f [int]$r.X, [int]$r.Y)
[QaWin4]::Click($r.X + $r.Width / 2, $r.Y + $r.Height / 2)
Start-Sleep -Milliseconds 2000

$wins2 = Get-Wins $app.Id
L ("windows now: {0}" -f $wins2.Count)
$edit = $null
foreach ($w in $wins2) {
  L ("  cls=" + $w.Current.ClassName + " name=" + $w.Current.Name)
  if ($w.Current.ClassName -ne "#32770" -and ([IntPtr]$w.Current.NativeWindowHandle) -ne $mainH) { $edit = $w }
}
if ($edit) {
  $eh = [IntPtr]$edit.Current.NativeWindowHandle
  [QaWin4]::SetForegroundWindow($eh) | Out-Null
  Start-Sleep -Milliseconds 900
  Save-Window $eh (Join-Path $out "08-order-edit.png")
  # 关闭编辑窗口：点取消
  foreach ($b in $edit.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
    if ($b.Current.Name -eq ([char]0x53D6 + [char]0x6D88)) {
      $r2 = $b.Current.BoundingRectangle
      [QaWin4]::Click($r2.X + $r2.Width / 2, $r2.Y + $r2.Height / 2)
      L "clicked cancel"
      break
    }
  }
} else {
  L "edit window not found"
}
L "done"
