$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
$log = Join-Path $out "qa8.log"
Remove-Item $log -ErrorAction SilentlyContinue
function L($m) { ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) | Out-File -FilePath $log -Append -Encoding UTF8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public class QaWin8 {
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
}
"@
$uia = [System.Windows.Automation.AutomationElement]
$ctProp = [System.Windows.Automation.AutomationElement]::ControlTypeProperty
$exe = (Get-ChildItem (Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows") -Filter *.exe | Select-Object -First 1).FullName
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)

function Dump-AllWins($tag) {
  $all = $uia::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
  L ("  [{0}] desktop windows={1}" -f $tag, $all.Count)
  $i = 0
  foreach ($w in $all) {
    L ("    [{0}] w[{1}] pid={2} cls={3} name='{4}' rect={5}" -f $tag, $i, $w.Current.ProcessId, $w.Current.ClassName, $w.Current.Name, $w.Current.BoundingRectangle)
    $i++
  }
}
function Dump-Shot($hwnd, $path) {
  $r = New-Object QaWin8+RECT
  [QaWin8]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + (Split-Path $path -Leaf) + " ${w}x${h}")
}

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
$mainH = [IntPtr]$app.MainWindowHandle
[QaWin8]::ShowWindow($mainH, 3) | Out-Null
Start-Sleep -Milliseconds 600
[QaWin8]::SetForegroundWindow($mainH) | Out-Null
Start-Sleep -Milliseconds 1000

# 用 Win32 消息找主窗口自动化元素（不用 pid 过滤）
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$app.Id)
$main = $uia::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
if (-not $main) { L "main uia not found"; exit 1 }
L ("main name=" + $main.Current.Name + " cls=" + $main.Current.ClassName)

$rc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
$r = $navs[1].Current.BoundingRectangle
[QaWin8]::Click($r.X + $r.Width / 2, $r.Y + $r.Height / 2)
Start-Sleep -Milliseconds 2000

$bc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Button)
$newBtn = $null
foreach ($b in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
  if ($b.Current.Name -like ("*" + ([char]0x65B0 + [char]0x589E) + "*")) { $newBtn = $b }
}
if (-not $newBtn) { L "new btn missing"; exit 1 }
$br = $newBtn.Current.BoundingRectangle
L ("click new btn at " + [int]($br.X + $br.Width / 2) + "," + [int]($br.Y + $br.Height / 2))
[QaWin8]::Click($br.X + $br.Width / 2, $br.Y + $br.Height / 2)
Start-Sleep -Milliseconds 2500
Dump-AllWins "after-new"

$editH = [IntPtr]0
foreach ($w in ($uia::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition))) {
  if (($w.Current.ProcessId -eq $app.Id) -and (([IntPtr]$w.Current.NativeWindowHandle) -ne $mainH) -and ($w.Current.ClassName -ne "#32770")) {
    $editH = [IntPtr]$w.Current.NativeWindowHandle
    L ("EDIT candidate pid={0} cls={1} name='{2}'" -f $w.Current.ProcessId, $w.Current.ClassName, $w.Current.Name)
  }
}
if ($editH -ne [IntPtr]0) {
  [QaWin8]::SetForegroundWindow($editH) | Out-Null
  Start-Sleep -Milliseconds 1200
  Dump-Shot $editH (Join-Path $out "08-order-edit.png")
  [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
} else {
  L "no edit window; full screen shot"
  Dump-Shot $mainH (Join-Path $out "diag-new.png")
  [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
}
L "done"
