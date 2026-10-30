$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
$log = Join-Path $out "qa9.log"
Remove-Item $log -ErrorAction SilentlyContinue
function L($m) { ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) | Out-File -FilePath $log -Append -Encoding UTF8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public class QaWin9 {
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

function Shot-Full($path) {
  $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp = New-Object System.Drawing.Bitmap $vs.Width, $vs.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($vs.X, $vs.Y, 0, 0, (New-Object System.Drawing.Size $vs.Width, $vs.Height))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved full " + (Split-Path $path -Leaf) + " " + $vs.Width + "x" + $vs.Height)
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

$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$app.Id)
$wins = $uia::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)
L ("uia wins=" + $wins.Count)
$main = $null
foreach ($w in $wins) { if ($w.Current.ClassName -ne "#32770") { $main = $w } }
if (-not $main) { L "main missing"; Shot-Full (Join-Path $out "diag-main.png"); exit 1 }
$mainH = [IntPtr]$main.Current.NativeWindowHandle
[QaWin9]::ShowWindow($mainH, 3) | Out-Null
Start-Sleep -Milliseconds 600
[QaWin9]::SetForegroundWindow($mainH) | Out-Null
Start-Sleep -Milliseconds 1200

$rc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
$r = $navs[1].Current.BoundingRectangle
[QaWin9]::Click($r.X + $r.Width / 2, $r.Y + $r.Height / 2)
Start-Sleep -Milliseconds 2000

$bc = New-Object System.Windows.Automation.PropertyCondition($ctProp, [System.Windows.Automation.ControlType]::Button)
$newBtn = $null
foreach ($b in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
  if ($b.Current.Name -like ("*" + ([char]0x65B0 + [char]0x589E) + "*")) { $newBtn = $b }
}
if (-not $newBtn) { L "new btn missing"; Shot-Full (Join-Path $out "diag-main.png"); exit 1 }
$br = $newBtn.Current.BoundingRectangle
L ("click new at " + [int]($br.X + $br.Width / 2) + "," + [int]($br.Y + $br.Height / 2))
[QaWin9]::Click($br.X + $br.Width / 2, $br.Y + $br.Height / 2)
Start-Sleep -Milliseconds 3000
Shot-Full (Join-Path $out "diag-new.png")

$wins2 = $uia::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)
L ("after click uia wins=" + $wins2.Count)
foreach ($w in $wins2) { L ("  pid=" + $w.Current.ProcessId + " cls=" + $w.Current.ClassName + " name='" + $w.Current.Name + "'") }
L "done"
