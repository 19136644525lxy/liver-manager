using System.IO;
using System.Windows;
using System.Windows.Threading;
using OrderManager.Services;

namespace OrderManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常兜底：任何未处理异常都记录日志并弹提示，避免程序直接消失
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            LogError("AppDomain", ex, args.ExceptionObject?.ToString());
            MessageBox.Show("程序遇到未处理的错误：\n" + args.ExceptionObject, "代肝管家",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };

        // 提前初始化数据库（异常在这里就能被看到）
        _ = AppState.I;

        // 由「登录时启动」的计划任务拉起、但用户在设置里关掉了自启 → 直接退出，不再占屏幕
        if (StartupManager.IsAutostartLaunch(e.Args) && !StartupManager.IsEnabled)
        {
            Shutdown();
            return;
        }

        // 首次启动自动导入旧数据时给出提示
        if (AppState.I.FirstImport is { } report)
        {
            MessageBox.Show("已从旧版工具自动导入历史数据：\n\n" + report.ToText(),
                "代肝管家", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("Dispatcher", e.Exception, e.Exception.ToString());
        MessageBox.Show("操作出错了：\n" + e.Exception.Message, "代肝管家",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    /// <summary>把异常写入安装目录下的 错误日志.txt，方便排查（写日志本身失败则忽略）。</summary>
    internal static void LogError(string tag, Exception? ex, string? raw)
    {
        try
        {
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}\r\n{raw}\r\n{(ex is null ? "" : ex.ToString())}\r\n\r\n";
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "错误日志.txt"), text);
        }
        catch
        {
            // 忽略日志写入失败
        }
    }
}
