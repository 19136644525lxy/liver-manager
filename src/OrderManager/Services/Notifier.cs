namespace OrderManager.Services;

/// <summary>
/// 轻量提示条（Toast）：任何页面调用 <see cref="Info"/> 即可在窗口底部弹一句提示，
/// 由 MainWindow 订阅 <see cref="Show"/> 负责显示与淡出。
/// </summary>
public static class Notifier
{
    public static event Action<string>? Show;

    public static void Info(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Show?.Invoke(message);
    }
}
