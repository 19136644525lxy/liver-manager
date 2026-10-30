param([string]$Out = "")
$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root ("src\OrderManager\bin\Debug\net10.0-windows\" + [char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6 + ".exe")
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)
if ($Out -eq "") { $Out = Join-Path $root "shots\diag-crash.png" }
Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
Start-Sleep -Milliseconds 900

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W2 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int c);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int c);
}
"@

$proc = Start-Process -FilePath $exe -PassThru
Write-Output ("started pid=" + $proc.Id)
Start-Sleep -Seconds 7
if ($proc.HasExited) { Write-Output "process exited" } else { Write-Output "process alive" }

$found = New-Object System.Collections.ArrayList
$pidOf = $proc.Id
$cb = [W2+EnumProc]{
  param($h, $l)
  $p2 = 0
  [void][W2]::GetWindowThreadProcessId($h, [ref]$p2)
  if ($p2 -eq $pidOf -and [W2]::IsWindowVisible($h)) {
    $sb = New-Object System.Text.StringBuilder 512
    [void][W2]::GetWindowText($h, $sb, 512)
    $cn = New-Object System.Text.StringBuilder 512
    [void][W2]::GetClassName($h, $cn, 512)
    $txt = $sb.ToString()
    if ($txt.Length -gt 600) { $txt = $txt.Substring(0, 600) }
    [void]$found.Add(([string]$h) + " | " + $cn.ToString() + " | " + $txt)
  }
  return $true
}
[void][W2]::EnumWindows($cb, [IntPtr]::Zero)
Write-Output ("windows=" + $found.Count)
foreach ($f in $found) { Write-Output ("  " + $f) }

$vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bmp = New-Object System.Drawing.Bitmap $vs.Width, $vs.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($vs.X, $vs.Y, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output ("saved " + $Out)
