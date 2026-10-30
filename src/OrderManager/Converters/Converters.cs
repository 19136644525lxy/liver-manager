using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OrderManager.Models;

namespace OrderManager.Converters;

/// <summary>订单状态 -> 文字颜色</summary>
public class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string status = value as string ?? "";
        return status switch
        {
            OrderStatus.Completed => new SolidColorBrush(Color.FromRgb(0x2F, 0xA9, 0x8C)),
            OrderStatus.InProgress => new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xC9)),
            OrderStatus.PendingPayment => new SolidColorBrush(Color.FromRgb(0xD9, 0x8A, 0x1F)),
            OrderStatus.PendingSettle => new SolidColorBrush(Color.FromRgb(0x8A, 0x6D, 0xD4)),
            OrderStatus.Cancelled => new SolidColorBrush(Color.FromRgb(0x9A, 0xA5, 0xB1)),
            _ => new SolidColorBrush(Color.FromRgb(0x65, 0x71, 0x7F)),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>订单状态 -> 浅色背景</summary>
public class StatusBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string status = value as string ?? "";
        return status switch
        {
            OrderStatus.Completed => new SolidColorBrush(Color.FromRgb(0xE6, 0xF6, 0xF1)),
            OrderStatus.InProgress => new SolidColorBrush(Color.FromRgb(0xE8, 0xF2, 0xFB)),
            OrderStatus.PendingPayment => new SolidColorBrush(Color.FromRgb(0xFD, 0xF3, 0xE2)),
            OrderStatus.PendingSettle => new SolidColorBrush(Color.FromRgb(0xF1, 0xEC, 0xFC)),
            OrderStatus.Cancelled => new SolidColorBrush(Color.FromRgb(0xF0, 0xF3, 0xF6)),
            _ => new SolidColorBrush(Color.FromRgb(0xF0, 0xF3, 0xF6)),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>非空 / 空 -> Visibility（参数传 invert 表示反转）</summary>
public class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasText = !string.IsNullOrWhiteSpace(value as string);
        bool invert = (parameter as string) == "invert";
        return hasText ^ invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
