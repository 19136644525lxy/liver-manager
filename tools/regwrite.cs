using System;
using System.Security.Principal;
using Microsoft.Win32;

var id = WindowsIdentity.GetCurrent();
Console.WriteLine("user=" + id.Name + " admin=" + new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator));
const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
try
{
    using var key = Registry.CurrentUser.OpenSubKey(run, writable: true);
    Console.WriteLine("key=" + (key is null ? "null" : "ok"));
    key?.SetValue("regtest_dg", "\"C:\\x.exe\"");
    Console.WriteLine("value=" + key?.GetValue("regtest_dg"));
    key?.DeleteValue("regtest_dg", false);
    Console.WriteLine("result=OK");
}
catch (Exception ex)
{
    Console.WriteLine("result=FAIL " + ex.GetType().Name + ": " + ex.Message);
}
