using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OrderManager.Ui;

/// <summary>
/// 滚轮顺滑滚动：把鼠标滚轮一格跳一大段的默认行为，换成一小段带缓动的动画，
/// 手感更细腻。只对窗口内可滚动的区域生效，输入框、下拉框等不受影响。
/// </summary>
public static class SmoothScroller
{
    /// <summary>滚轮一格的滚动距离（像素）。</summary>
    private const double StepPerNotch = 84;

    /// <summary>动画时长（毫秒）。越短越"跟手"，越长越"顺滑"。</summary>
    private const int DurationMs = 150;

    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(SmoothScroller), new PropertyMetadata(0.0, OnOffsetChanged));

    /// <summary>在窗口上启用顺滑滚动（只需调用一次）。</summary>
    public static void Attach(Window window)
    {
        // Preview：抢在 DataGrid / ScrollViewer 自己处理之前接管
        window.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewWheel), true);
    }

    private static void OnPreviewWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        var viewer = FindScrollViewer(e.OriginalSource as DependencyObject);
        if (viewer is null || viewer.ScrollableHeight <= 0.5) return;

        e.Handled = true;

        double from = viewer.VerticalOffset;
        double target = from - e.Delta / 120.0 * StepPerNotch;
        target = Math.Clamp(target, 0, viewer.ScrollableHeight);

        // 忽略过小的位移，避免触控板连续小滚动时排队动画
        if (Math.Abs(target - from) < 0.5) return;

        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(DurationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };

        viewer.BeginAnimation(OffsetProperty, animation);
    }

    private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer viewer && e.NewValue is double offset)
            viewer.ScrollToVerticalOffset(offset);
    }

    /// <summary>从事件源向上找最近的可滚动 ScrollViewer。</summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is ScrollViewer viewer && viewer.ScrollableHeight > 0.5) return viewer;
            node = GetParent(node);
        }
        return null;
    }

    private static DependencyObject? GetParent(DependencyObject node)
    {
        if (node is FrameworkElement element && element.Parent is not null) return element.Parent;
        return node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);
    }
}
