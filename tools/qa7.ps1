$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
$log = Join-Path $out "qa7.log"
Remove-Item $log -ErrorAction SilentlyContinue
function L($m) { ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) | Out-File -FilePath $log -Append -Encoding UTF8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public class QaWin7 {
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
    Thread.Sleep(250);
  }
  public static void DblClick(double x, double y) {
    SetCursorPos((int)x, (int)y);
    Thread.Sleep(150);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
    Thread.Sleep(60);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    Thread.Sleep(80);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
    Thread.Sleep(60);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    Thread.Sleep(250);
  }
}
"@
$uia = [System.Windows.Automation.AutomationElement]
$ctProp = [System.Windows.Automation.AutomationElement]::ControlTypeProperty
$exe = (Get-ChildItem (Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows") -Filter *.exe | Select-Object -First 1).FullName
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)

function Get-Tops($procId) {
  $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$procId)
  return $uia::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $c)
}
function Dump-Shot($hwnd, $path) {
  $r = New-Object QaWin7+RECT
  [QaWin7]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + (Split-Path $path -Leaf) + " ${w}x${h}")
}
function Dump-Wins($tag) {
  $i = 0
  foreach ($w in (Get-Tops $app.Id)) { L ("  [$tag] win[$i] cls=" + $w.Current.ClassName + " name=" + $w.Current.Name + " rect=" + $w.Current.BoundingRectangle); $i++ }
  L ("  [$tag] count=$i")
}

# 启动
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
Start-Sleep -Seconds 3
L ("pid=" + $app.Id)
$main = $null
foreach ($t in (Get-Tops $app.Id)) { if ($t.Current.ClassName -ne "#32770") { $main = $t } }
if (-not $main) { L "main not found"; exit 1 }
$mainH = [IntPtr]$main.Current.NativeWindowHandle
[QaWin7]::ShowWindow($mainH, 3) | Out-Null
Start-Sleep -Milliseconds 600
[QaWin7]::SetForegroundWindow($mainH) | Out-Null
Start-Sleep -Milliseconds 1000

# 切到订单管理
$rc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
$r = $navs[1].Current.BoundingRectangle
[QaWin7]::Click($r.X + $r.Width / 2, $r.Y + $r.Height / 2)
Start-Sleep -Milliseconds 2000
L "--- after nav orders"
Dump-Wins "nav"

# 试验 A：点「导出 CSV」，看是否能捕获到文件对话框
$bc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Button)
$csvName = ([char]0x5BFC).ToString() + [char]0x51FA + " CSV"
$csvBtn = $null
foreach ($b in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) { if ($b.Current.Name -eq $csvName) { $csvBtn = $b } }
if ($csvBtn) {
  $br = $csvBtn.Current.BoundingRectangle
  L ("A: click export at " + $br.X + "," + $br.Y)
  [QaWin7]::Click($br.X + $br.Width / 2, $br.Y + $br.Height / 2)
  Start-Sleep -Milliseconds 2500
  Dump-Wins "export"
  Dump-Shot $mainH (Join-Path $out "diag-export.png")
} else { L "A: export btn not found" }

# 关掉可能出现的对话框：ESC
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 1200
Dump-Wins "after-esc"

# 试验 B：双击第一行数据
$dc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::DataItem)
$rows = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $dc)
L ("B: rows=" + $rows.Count)
if ($rows.Count -gt 0) {
  $rr = $rows[0].Current.BoundingRectangle
  L ("B: dblclick row at " + $rr.X + "," + $rr.Y)
  [QaWin7]::SetForegroundWindow($mainH) | Out-Null
  [QaWin7]::DblClick($rr.X + 60, $rr.Y + 10)
  for ($k = 0; $k -lt 8; $k++) {
    Start-Sleep -Milliseconds 400
    $wins = Get-Tops $app.Id
    if ($wins.Count -gt 1) { L ("B: window appeared at k=" + $k); break }
  }
  Dump-Wins "dblclick"
  $edit = $null
  foreach ($w in (Get-Tops $app.Id)) { if (([IntPtr]$w.Current.NativeWindowHandle) -ne $mainH) { $edit = $w } }
  if ($edit) {
    $eh = [IntPtr]$edit.Current.NativeWindowHandle
    [QaWin7]::SetForegroundWindow($eh) | Out-Null
    Start-Sleep -Milliseconds 1200
    Dump-Shot $eh (Join-Path $out "08-order-edit.png")
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
  } else { L "B: no edit window"; Dump-Shot $mainH (Join-Path $out "diag-dblclick.png") }
}
L "done"
