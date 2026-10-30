using System.Windows;
using System.Windows.Input;
using OrderManager.Models;
using OrderManager.ViewModels;

namespace OrderManager.Views;

/// <summary>订单新增 / 编辑窗口。</summary>
public partial class OrderEditWindow : Window
{
    private readonly OrderEditViewModel _vm;

    /// <summary>没有传订单时按「新增订单」处理。</summary>
    public OrderEditWindow(Order? order = null)
    {
        InitializeComponent();

        _vm = new OrderEditViewModel(order);
        _vm.SaveCompleted += OnSaveCompleted;
        _vm.CancelRequested += OnCancelRequested;
        _vm.DeleteRequested += OnDeleteRequested;
        _vm.Deleted += OnDeleted;

        DataContext = _vm;
    }

    private void OnSaveCompleted(object? sender, EventArgs e) => DialogResult = true;

    private void OnCancelRequested(object? sender, EventArgs e) => DialogResult = false;

    private void OnDeleted(object? sender, EventArgs e) => DialogResult = true;

    private void OnDeleteRequested(object? sender, EventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(_vm.BossName) ? "该订单" : $"「{_vm.BossName.Trim()}」的订单";
        var result = MessageBox.Show(this,
            $"确定要删除{name}吗？删除后不可恢复。",
            "删除订单", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _vm.Delete();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标已提前释放时忽略
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
            return;
        }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (e.Key == Key.S && ctrl)
        {
            e.Handled = true;
            _vm.Save();
            return;
        }

        if (e.Key == Key.Enter && ctrl)
        {
            e.Handled = true;
            _vm.Save();
        }
    }
}

