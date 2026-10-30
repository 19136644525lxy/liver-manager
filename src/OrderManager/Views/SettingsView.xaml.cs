using System.Windows.Controls;
using OrderManager.Services;
using OrderManager.Ui;
using OrderManager.ViewModels;

namespace OrderManager.Views;

public partial class SettingsView : UserControl, IRefreshable
{
    private readonly SettingsViewModel _viewModel = new();
    private int _seenVersion = -1;

    public SettingsView()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.Load();
    }

    /// <summary>切到本页时按数据版本判断要不要重新读设置，没变就直接复用已有界面。</summary>
    public void OnShown()
    {
        if (_seenVersion == AppState.I.DataVersion) return;
        _seenVersion = AppState.I.DataVersion;
        _viewModel.Load();
    }
}
