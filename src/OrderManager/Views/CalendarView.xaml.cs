using System.Windows.Controls;
using OrderManager.Services;
using OrderManager.Ui;
using OrderManager.ViewModels;

namespace OrderManager.Views;

public partial class CalendarView : UserControl, IRefreshable
{
    private readonly CalendarViewModel _viewModel = new();
    private int _seenVersion = -1;

    public CalendarView()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    /// <summary>切到本页时按数据版本判断要不要重新读库，没变就直接复用已有界面。</summary>
    public void OnShown()
    {
        if (_seenVersion == AppState.I.DataVersion) return;
        _seenVersion = AppState.I.DataVersion;
        _viewModel.Refresh();
    }
}
