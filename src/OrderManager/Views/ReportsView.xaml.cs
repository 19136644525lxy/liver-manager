using System.Windows.Controls;
using OrderManager.ViewModels;

namespace OrderManager.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
        DataContext = new ReportsViewModel();
    }
}
