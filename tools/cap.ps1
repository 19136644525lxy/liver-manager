Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;
public class Cap3 { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 public struct RECT { public int Left, Top, Right, Bottom; } }
"@
$out = Join-Path (Split-Path $PSScriptRoot -Parent) "shots"
$p = Get-Process | Where-Object { $_.Path -like "*OrderManager*" } | Select-Object -First 1
if (-not $p) { Write-Output "not running"; exit 1 }
$h = $p.MainWindowHandle
[Cap3]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 900
$r = New-Object Cap3+RECT
[Cap3]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hh))
$dest = Join-Path $out "10-orders-current.png"
$bmp.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output ("ok " + $w + "x" + $hh + " -> " + $dest)
