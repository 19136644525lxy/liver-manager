$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shots"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$log = Join-Path $out "qa.log"
Remove-Item $log -ErrorAction SilentlyContinue
function L($m) { ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) | Out-File -FilePath $log -Append -Encoding UTF8 }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class QaWin {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$uia = [System.Windows.Automation.AutomationElement]
L "start"

$exe = (Get-ChildItem (Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows") -Filter *.exe | Select-Object -First 1).FullName
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)
L "exe=$exe"

Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 800

& cmd /c start "" $exe
L "launched"

$app = $null
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 500
  $app = Get-Process | Where-Object { $_.ProcessName -eq $pname } | Select-Object -First 1
  if ($app -and $app.MainWindowHandle -ne 0) { break }
}
if (-not $app) { L "no process"; exit 1 }
$app.Refresh()
L ("pid={0} handle={1} title={2}" -f $app.Id, $app.MainWindowHandle, $app.MainWindowTitle)
Start-Sleep -Seconds 2

function Get-Tops($procId) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($uia::ProcessIdProperty, [int]$procId)
  return $uia::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
}

$tops = Get-Tops $app.Id
L ("tops={0}" -f $tops.Count)
foreach ($t in $tops) { L ("  cls=" + $t.Current.ClassName + " name=" + $t.Current.Name) }

function Save-Window($hwnd, $path) {
  [QaWin]::ShowWindow($hwnd, 3) | Out-Null
  Start-Sleep -Milliseconds 500
  [QaWin]::SetForegroundWindow($hwnd) | Out-Null
  Start-Sleep -Milliseconds 800
  $r = New-Object QaWin+RECT
  [QaWin]::GetWindowRect($hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  if ($w -le 0 -or $h -le 0) { L "bad rect $w x $h"; return }
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  L ("saved " + (Split-Path $path -Leaf) + " ${w}x${h}")
}

# 关闭首次运行导入提示等对话框
$dlgs = @()
foreach ($t in $tops) { if ($t.Current.ClassName -eq "#32770") { $dlgs += $t } }
foreach ($d in $dlgs) {
  $h = [IntPtr]$d.Current.NativeWindowHandle
  Save-Window $h (Join-Path $out "00-dialog.png")
  $bc = New-Object System.Windows.Automation.PropertyCondition($uia::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  $btn = $d.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $bc)
  if ($btn) { $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); L "dialog dismissed" }
  Start-Sleep -Milliseconds 1000
}

$main = $null
for ($k = 0; $k -lt 24 -and -not $main; $k++) {
  Start-Sleep -Milliseconds 500
  $tops = Get-Tops $app.Id
  foreach ($t in $tops) { if ($t.Current.ClassName -ne "#32770") { $main = $t } }
}
if (-not $main) {
  L "main not found; tops:"
  foreach ($t in (Get-Tops $app.Id)) { L ("  cls=" + $t.Current.ClassName + " name=" + $t.Current.Name) }
  exit 1
}
$mainH = [IntPtr]$main.Current.NativeWindowHandle

$rc = New-Object System.Windows.Automation.PropertyCondition($uia::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$navs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
L ("navs={0}" -f $navs.Count)
$names = @("01-dashboard","02-orders","03-calendar","04-reports","05-runners","06-items","07-settings")
for ($i = 0; $i -lt $navs.Count; $i++) {
  $n = $navs[$i]
  $navName = $n.Current.Name
  L ("nav[{0}]='{1}'" -f $i, $navName)
  try {
    $sel = $n.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $sel.Select()
  } catch {
    try { $n.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { L ("nav fail " + $_.Exception.Message) }
  }
  Start-Sleep -Milliseconds 1400
  $file = if ($i -lt $names.Count) { $names[$i] } else { "page-$i" }
  Save-Window $mainH (Join-Path $out ($file + ".png"))
}
L "done"
