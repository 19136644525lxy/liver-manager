using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OrderManager.Services;

/// <summary>
/// 开机自动启动。按可靠性依次尝试四种方式：
/// 1) 当前用户注册表启动项（HKCU\...\Run，标准做法，不需要管理员权限）
/// 2) 「启动」文件夹快捷方式
/// 3) 「启动」文件夹 .cmd 脚本
/// 4) 计划任务（登录时触发，任务由系统的计划任务服务创建，绕开对注册表和启动目录的写入限制）
/// 有些机器（安全软件、组策略、受限会话）会禁掉前三种，这种情况下第 4 种通常仍然可用。
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>启动项名字（注册表值名 / 计划任务名）。</summary>
    private const string ValueName = "代肝管家";

    private const string ShortcutName = "代肝管家.lnk";

    private const string CmdName = "代肝管家.cmd";

    /// <summary>用户意图（设置表里的开关记录）。这个值写在 data 目录里，不受系统权限限制，作为最终依据。</summary>
    private const string PrefKey = "autostart_enabled";

    private const string AutostartArgument = "--autostart";

    /// <summary>这次启动是不是计划任务（登录时）拉起的。</summary>
    public static bool IsAutostartLaunch(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, AutostartArgument, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string? ReadPref()
    {
        try
        {
            return AppState.I.Db.GetSetting(PrefKey);
        }
        catch
        {
            return null;
        }
    }

    private static void WritePref(bool enabled)
    {
        try
        {
            AppState.I.Db.SetSetting(PrefKey, enabled ? "1" : "0");
        }
        catch (Exception ex)
        {
            App.LogError("开机自启：记录开关状态失败", ex, null);
        }
    }

    /// <summary>当前程序的可执行文件路径（带引号）。</summary>
    public static string CommandLine
    {
        get
        {
            var path = Environment.ProcessPath;
            return string.IsNullOrEmpty(path) ? string.Empty : "\"" + path + "\"";
        }
    }

    private static string StartupFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Start Menu\Programs\Startup");

    private static string StartupShortcutPath => Path.Combine(StartupFolder, ShortcutName);

    private static string StartupCmdPath => Path.Combine(StartupFolder, CmdName);

    /// <summary>是否已设置为开机自动启动（任一方式生效即为是）。</summary>
    public static bool IsEnabled
    {
        get
        {
            var pref = ReadPref();
            if (pref == "0") return false;
            if (pref == "1") return true;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                if (key?.GetValue(ValueName) is string value && value.Length > 0) return true;
            }
            catch
            {
                // 读不到就继续用别的方式判断
            }

            try
            {
                if (File.Exists(StartupShortcutPath) || File.Exists(StartupCmdPath)) return true;
            }
            catch
            {
                // 忽略
            }

            return ScheduledTaskExists();
        }
    }

    /// <summary>开启 / 关闭开机自动启动。返回 false 表示所有方式都失败了。</summary>
    public static bool SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            // 先记录用户意图（这个一定写得进去），再尽量清理系统里的各种痕迹
            WritePref(false);
            RemoveRegistryValue();
            DeleteShortcut();
            DeleteScheduledTask();
            return true;
        }

        WritePref(true);
        if (WriteRegistryValue()) return true;
        if (CreateShortcut()) return true;
        if (CreateCommandFallback()) return true;
        if (CreateScheduledTask()) return true;

        // 四种都失败：不要留下"看起来开了其实没开"的状态
        if (ScheduledTaskExists()) return true;

        WritePref(false);
        return false;
    }

    // ---------- 方式 1：注册表 ----------

    private static bool WriteRegistryValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return false;

            var command = CommandLine;
            if (command.Length == 0) return false;

            key.SetValue(ValueName, command);
            return true;
        }
        catch (Exception ex)
        {
            App.LogError("开机自启：写注册表被拒绝，改用别的方式", ex, null);
            return false;
        }
    }

    private static void RemoveRegistryValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            App.LogError("开机自启：删除注册表启动项失败", ex, null);
        }
    }

    // ---------- 方式 2：启动文件夹快捷方式 ----------

    private static bool CreateShortcut()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;

            Directory.CreateDirectory(StartupFolder);

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return false;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null) return false;

            dynamic shortcut = shell.CreateShortcut(StartupShortcutPath);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;
            shortcut.IconLocation = exe + ",0";
            shortcut.Description = "代肝管家";
            shortcut.Save();

            return File.Exists(StartupShortcutPath);
        }
        catch (Exception ex)
        {
            App.LogError("开机自启：创建启动文件夹快捷方式失败", ex, null);
            return false;
        }
    }

    private static void DeleteShortcut()
    {
        try
        {
            if (File.Exists(StartupShortcutPath)) File.Delete(StartupShortcutPath);
            if (File.Exists(StartupCmdPath)) File.Delete(StartupCmdPath);
        }
        catch (Exception ex)
        {
            App.LogError("开机自启：删除启动文件夹项失败", ex, null);
        }
    }

    // ---------- 方式 3：启动文件夹 .cmd ----------

    private static bool CreateCommandFallback()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            Directory.CreateDirectory(StartupFolder);
            File.WriteAllText(StartupCmdPath, "@echo off\r\nstart \"\" \"" + exe + "\"\r\n");
            return File.Exists(StartupCmdPath);
        }
        catch (Exception ex)
        {
            App.LogError("开机自启：写启动脚本失败", ex, null);
            return false;
        }
    }

    // ---------- 方式 4：计划任务（登录时触发） ----------

    private static bool CreateScheduledTask()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        // schtasks /create /tn 代肝管家 /tr "<exe>" /sc onlogon /rl limited /f
        var args = "/create /tn \"" + ValueName + "\" /tr \"\\\"" + exe + "\\\" --autostart\" /sc onlogon /rl limited /f";
        var code = RunTool("schtasks.exe", args);
        if (code == 0) return true;

        App.LogError("开机自启：创建计划任务失败", null, "schtasks exit=" + code);
        return false;
    }

    private static bool ScheduledTaskExists()
    {
        var args = "/query /tn \"" + ValueName + "\"";
        return RunTool("schtasks.exe", args) == 0;
    }

    private static void DeleteScheduledTask()
    {
        var args = "/delete /tn \"" + ValueName + "\" /f";
        var code = RunTool("schtasks.exe", args);
        if (code != 0) App.LogError("开机自启：删除计划任务失败", null, "schtasks exit=" + code);
    }

    /// <summary>静默执行一个命令行工具，返回退出码（失败返回 -1）。</summary>
    private static int RunTool(string file, string args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null) return -1;
            if (!process.WaitForExit(8000))
            {
                try { process.Kill(); } catch { /* 忽略 */ }
                return -1;
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            App.LogError("开机自启：调用 " + file + " 失败", ex, args);
            return -1;
        }
    }
}
