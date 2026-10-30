$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
$log = Join-Path $out "qa10.log"
Remove-Item $log -ErrorAction SilentlyContinue
function L($m) { ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) | Out-File -FilePath $log -Append -Encoding UTF8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
public class QaWin10 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  public struct RECT { public int Left, Top, Right, Bottom; }
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  public static List<string> ListWindows(uint pid) {
    var res = new List<string>();
    EnumWindows((h, p) => {
      uint wp; GetWindowThreadProcessId(h, out wp);
      if (wp != pid) return true;
      if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512);
      GetWindowText(h, sb, 512);
      var cn = new StringBuilder(256);
      GetClassName(h, cn, 256);
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

function Dump-Shot($hwnd, $path) {
  $r = New-Object QaWin10+RECT
  [QaWin10]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + (Split-Path $path -Leaf) + " ${w}x${h} at $($r.Left),$($r.Top)")
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
L ("pid=" + $app.Id + " hwnd=" + $app.MainWindowHandle)
$mainH = [IntPtr]$app.MainWindowHandle
[QaWin10]::ShowWindow($mainH, 3) | Out-Null
Start-Sleep -Milliseconds 600
[QaWin10]::SetForegroundWindow($mainH) | Out-Null
Start-Sleep -Milliseconds 1200

# 切到订单管理
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$app.Id)
$main = $uia::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
if (-not $main) { L "main uia missing"; exit 1 }
$rc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
$r = $navs[1].Current.BoundingRectangle
[QaWin10]::Click($r.X + $r.Width / 2, $r.Y + $r.Height / 2)
Start-Sleep -Milliseconds 2000

$bc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Button)
$newBtn = $null
foreach ($b in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
  if ($b.Current.Name -like ("*" + ([char]0x65B0 + [char]0x589E) + "*")) { $newBtn = $b }
}
if (-not $newBtn) { L "new btn missing"; exit 1 }
$br = $newBtn.Current.BoundingRectangle
[QaWin10]::Click($br.X + $br.Width / 2, $br.Y + $br.Height / 2)
Start-Sleep -Milliseconds 2500

$editH = [IntPtr]0
L "--- windows after new ---"
foreach ($line in [QaWin10]::ListWindows([uint32]$app.Id)) {
  L ("  " + $line)
  $parts = $line.Split("|")
  if (([IntPtr][int64]$parts[0]) -ne $mainH) { $editH = [IntPtr][int64]$parts[0] }
}
if ($editH -eq [IntPtr]0) { L "no extra hwnd"; exit 1 }
Dump-Shot $editH (Join-Path $out "08-order-edit.png")
$er = New-Object QaWin10+RECT
[QaWin10]::GetWindowRect($editH, [ref]$er) | Out-Null
$ew = $er.Right - $er.Left; $eh = $er.Bottom - $er.Top
L ("edit rect ${ew}x${eh}")

# 填表：老板网名 (约 37%,34%)、金额 (约 73%,63%)、保存 (约 88%,90%)
function Pt($fx, $fy) { return @(($er.Left + $ew * $fx), ($er.Top + $eh * $fy)) }
[QaWin10]::SetForegroundWindow($editH) | Out-Null
Start-Sleep -Milliseconds 500
$p = Pt 0.37 0.34
[QaWin10]::Click($p[0], $p[1])
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait("TestQA")
Start-Sleep -Milliseconds 300
$p = Pt 0.73 0.63
[QaWin10]::Click($p[0], $p[1])
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait("^a")
[System.Windows.Forms.SendKeys]::SendWait("88")
Start-Sleep -Milliseconds 300
Dump-Shot $editH (Join-Path $out "08b-order-edit-filled.png")
$p = Pt 0.88 0.90
L ("click save at " + [int]$p[0] + "," + [int]$p[1])
[QaWin10]::Click($p[0], $p[1])
Start-Sleep -Milliseconds 2500

L "--- windows after save ---"
foreach ($line in [QaWin10]::ListWindows([uint32]$app.Id)) { L ("  " + $line) }
[QaWin10]::SetForegroundWindow($mainH) | Out-Null
Start-Sleep -Milliseconds 800
Dump-Shot $mainH (Join-Path $out "09-orders-after-add.png")
L "done"
