using System.Windows;
using System.Windows.Input;
using OrderManager.Models;
using OrderManager.ViewModels;

namespace OrderManager.Views;

public partial class RenewWindow : Window
{
    private readonly RenewOrderViewModel _vm;

    public RenewWindow(Order order)
    {
        InitializeComponent();

        _vm = new RenewOrderViewModel(order);
        _vm.CloseRequested += (_, _) => Close();
        _vm.Saved += (_, _) => DialogResult = true;

        DataContext = _vm;
    }

    /// <summary>续单成功返回 true。</summary>
    public bool Succeeded { get; private set; }

    protected override void OnClosed(EventArgs e)
    {
        Succeeded = _vm != null && DialogResult == true;
        base.OnClosed(e);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_vm.SaveCommand.CanExecute(null)) _vm.SaveCommand.Execute(null);
        }
    }
}
