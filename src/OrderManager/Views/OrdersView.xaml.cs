using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OrderManager.Models;
using OrderManager.Services;
using OrderManager.Ui;
using OrderManager.ViewModels;

namespace OrderManager.Views;

public partial class OrdersView : UserControl, IRefreshable
{
    private readonly OrdersViewModel _vm;
    private int _seenVersion = -1;

    public OrdersView()
    {
        InitializeComponent();

        _vm = new OrdersViewModel();
        _vm.EditRequested += OnEditRequested;
        _vm.DeleteRequested += OnDeleteRequested;
        _vm.ExportRequested += OnExportRequested;
        _vm.RenewRequested += OnRenewRequested;

        DataContext = _vm;
    }

    /// <summary>切到本页时按数据版本判断要不要重新读库，没变就直接复用已有界面。</summary>
    public void OnShown()
    {
        if (_seenVersion == AppState.I.DataVersion) return;
        _seenVersion = AppState.I.DataVersion;
        _vm.Refresh();
    }

    private void OnEditRequested(object? sender, Order? order)
    {
        var owner = Window.GetWindow(this);
        var window = new OrderEditWindow(order);
        if (owner is not null) window.Owner = owner;
        window.ShowDialog();
        _vm.Refresh();
    }

    private void OnRenewRequested(object? sender, Order order)
    {
        var owner = Window.GetWindow(this);
        var window = new RenewWindow(order);
        if (owner is not null) window.Owner = owner;

        bool ok = window.ShowDialog() == true;
        if (ok)
        {
            _vm.Refresh();
        }
    }

    private void OnDeleteRequested(object? sender, Order order)
    {
        var owner = Window.GetWindow(this);
        var result = Confirm(owner,
            $"确定删除「{order.BossName}」的这笔订单吗？\n删除后无法恢复。",
            "删除订单");
        if (result != MessageBoxResult.Yes) return;

        // 交给 ViewModel 去做，顺带触发"订单已删除"的底部提示
        _vm.DeleteOrder(order);
    }

    /// <summary>快捷键 Ctrl+N：直接打开发新订单窗口。</summary>
    public void BeginNewOrder()
    {
        if (_vm.AddCommand.CanExecute(null)) _vm.AddCommand.Execute(null);
    }

    /// <summary>快捷键 Ctrl+F：把光标放进搜索框。</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnExportRequested(object? sender, string csv)
    {
        var owner = Window.GetWindow(this);
        var dialog = new SaveFileDialog
        {
            Title = "导出订单",
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"订单_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            AddExtension = true,
            DefaultExt = ".csv",
        };

        bool? picked = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (picked != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(true));
            Notifier.Info("已导出：" + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            Error(owner, "导出失败：" + ex.Message, "导出失败");
        }
    }

    /// <summary>右键时先选中鼠标所在行，保证右键菜单操作的是这一行。</summary>
    private void OrdersGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not DataGridRow row) return;
        if (!row.IsSelected) grid.SelectedItem = row.Item;
    }

    private void OrdersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 只在真正双击到数据行时打开编辑窗口，避免点表头误触发
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is null) return;
        _vm.EditSelected();
    }

    /// <summary>点击「改状态」按钮时在按钮下方展开状态菜单。</summary>
    private void StatusButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private static MessageBoxResult Confirm(Window? owner, string text, string caption)
    {
        return owner is null
            ? MessageBox.Show(text, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(owner, text, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);
    }

    private static void Info(Window? owner, string text, string caption)
    {
        if (owner is null) MessageBox.Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void Error(Window? owner, string text, string caption)
    {
        if (owner is null) MessageBox.Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Error);
        else MessageBox.Show(owner, text, caption, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null && current is not T)
            current = VisualTreeHelper.GetParent(current);
        return current as T;
    }
}

