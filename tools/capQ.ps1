Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;
public class CQ {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
 public struct RECT { public int Left, Top, Right, Bottom; } }
"@
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'shots'
$t = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$p = Get-Process | Where-Object { $_.MainWindowTitle -eq $t } | Select-Object -First 1
if (-not $p) { 'no window'; exit 1 }
$h = $p.MainWindowHandle
$r = New-Object CQ+RECT
[CQ]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[CQ]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)
$name = if ($args.Count -gt 0) { $args[0] } else { 'shot.png' }
$bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output ('ok ' + $w + 'x' + $hh)
