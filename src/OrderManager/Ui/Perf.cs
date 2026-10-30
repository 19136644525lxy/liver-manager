using System.Diagnostics;
using System.IO;

namespace OrderManager.Ui;

/// <summary>
/// 轻量性能打点。只有启动前设置了环境变量 DG_PERF=1 才会写日志，
/// 正常使用时不产生任何文件、几乎没有开销。
/// </summary>
public static class Perf
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("DG_PERF") == "1";

    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "perf.log");

    public static bool IsEnabled => Enabled;

    public static Stopwatch Start() => Stopwatch.StartNew();

    public static void Mark(string message)
    {
        if (!Enabled) return;
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // 打点失败不影响功能
        }
    }
}
