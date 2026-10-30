Add-Type @"
using System;using System.Runtime.InteropServices;
public class Wz {
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
$t = [string]([char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6)
$p = Get-Process | Where-Object { $_.MainWindowTitle -eq $t } | Select-Object -First 1
if (-not $p) { 'no window'; exit 1 }
[Wz]::ShowWindow($p.MainWindowHandle, 3) | Out-Null
[Wz]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
'maximized'
