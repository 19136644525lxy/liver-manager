using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OrderManager.Models;
using OrderManager.Mvvm;
using OrderManager.Services;

namespace OrderManager.ViewModels;

/// <summary>日期快捷筛选项（本月 / 上月 / 近7天 / 全部）。</summary>
public class DatePreset : ObservableObject
{
    private bool _isSelected;

    public DatePreset(string name, bool isSelected = false)
    {
        Name = name;
        _isSelected = isSelected;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>订单管理页的视图模型：筛选、列表、汇总、导出与增删改入口。</summary>
public class OrdersViewModel : ObservableObject
{
    private readonly AppState _state = AppState.I;
    private readonly DispatcherTimer _searchTimer;
    private bool _loading;

    private string _keyword = "";
    private string _selectedStatus = "全部";
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private Order? _selectedOrder;
    private Summary _summary = new();
    private string _headerHint = "";
    private string _emptyTitle = "";
    private string _emptyHint = "";

    public OrdersViewModel()
    {
        StatusOptions.Add("全部");
        foreach (var status in OrderStatus.All) StatusOptions.Add(status);

        // 默认选中「全部」：首次打开要能直接看到已有订单，避免误以为数据丢失
        Presets = new List<DatePreset>
        {
            new("本月"),
            new("上月"),
            new("近7天"),
            new("全部", true),
        };
        _selectedPreset = Presets.First(p => p.Name == "全部");

        AddCommand = new RelayCommand(() => EditRequested?.Invoke(this, null));
        EditCommand = new RelayCommand(EditSelected, () => SelectedOrder is not null);
        DeleteCommand = new RelayCommand(
            () => { if (SelectedOrder is not null) DeleteRequested?.Invoke(this, SelectedOrder); },
            () => SelectedOrder is not null);
        ExportCsvCommand = new RelayCommand(() => ExportRequested?.Invoke(this, BuildCsv()));
        RefreshCommand = new RelayCommand(() => { Refresh(); Notifier.Info("已刷新"); });
        ResetCommand = new RelayCommand(ResetFilters);
        ApplyPresetCommand = new RelayCommand(p => ApplyPreset(p as string ?? "全部"));
        SetStatusCommand = new RelayCommand(
            p => SetStatus(p as string ?? ""),
            _ => SelectedOrder is not null);
        RenewCommand = new RelayCommand(RenewSelected, () => CanRenew);

        // 搜索输入做 260ms 防抖，避免每次按键都查库
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            Refresh();
        };

        ApplyDateRange(_selectedPreset.Name);
        _state.DataChanged += OnDataChanged;
        Refresh();
    }

    /// <summary>请求打开新增 / 编辑窗口（null 表示新增）。</summary>
    public event EventHandler<Order?>? EditRequested;

    /// <summary>请求删除订单（由视图弹确认框后调用 <see cref="DeleteOrder"/>）。</summary>
    public event EventHandler<Order>? DeleteRequested;

    /// <summary>请求导出 CSV（参数为已生成好的 CSV 文本）。</summary>
    public event EventHandler<string>? ExportRequested;

    /// <summary>请求给托管订单续单。</summary>
    public event EventHandler<Order>? RenewRequested;

    public ObservableCollection<Order> Orders { get; } = new();

    public ObservableCollection<string> StatusOptions { get; } = new();

    public List<DatePreset> Presets { get; }

    private DatePreset _selectedPreset;

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand ApplyPresetCommand { get; }
    public ICommand SetStatusCommand { get; }
    public ICommand RenewCommand { get; }

    /// <summary>当前选中的订单是不是托管类（有起止日期）——只有这种才谈得上续单。</summary>
    public bool CanRenew => _selectedOrder is not null && IsCustody(_selectedOrder);

    public string Keyword
    {
        get => _keyword;
        set
        {
            if (!SetProperty(ref _keyword, value)) return;
            if (_loading) return;
            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (!SetProperty(ref _selectedStatus, value)) return;
            if (!_loading) Refresh();
        }
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (!SetProperty(ref _fromDate, value)) return;
            if (_loading) return;
            MarkCustomRange();
            Refresh();
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (!SetProperty(ref _toDate, value)) return;
            if (_loading) return;
            MarkCustomRange();
            Refresh();
        }
    }

    /// <summary>DataGrid 当前选中行。</summary>
    public Order? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (!SetProperty(ref _selectedOrder, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanRenew));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>是否有选中行（供行内操作按钮控制可用状态）。</summary>
    public bool HasSelection => _selectedOrder is not null;

    public Summary Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>标题右侧的提示文案，例如「共 12 笔订单 · 待处理 5 笔」。</summary>
    public string HeaderHint
    {
        get => _headerHint;
        private set => SetProperty(ref _headerHint, value);
    }

    public string EmptyTitle
    {
        get => _emptyTitle;
        private set => SetProperty(ref _emptyTitle, value);
    }

    public string EmptyHint
    {
        get => _emptyHint;
        private set => SetProperty(ref _emptyHint, value);
    }

    public bool IsEmpty => Orders.Count == 0;

    /// <summary>重新按当前筛选条件查询数据库。</summary>
    public void Refresh()
    {
        _searchTimer.Stop();

        long? keepId = SelectedOrder?.Id;
        var list = _state.LoadOrders(
            DateText(FromDate),
            DateText(ToDate),
            SelectedStatus,
            string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim());

        Orders.Clear();
        foreach (var order in list) Orders.Add(order);

        Summary = _state.Summarize(list);
        HeaderHint = $"共 {Summary.Orders} 笔订单 · 待处理 {Summary.Open} 笔";

        bool filtered = !string.IsNullOrWhiteSpace(Keyword) || SelectedStatus != "全部" || !IsAllRange();
        if (list.Count == 0)
        {
            EmptyTitle = filtered ? "没有符合条件的订单" : "还没有订单记录";
            EmptyHint = filtered
                ? "试试更换关键词或调整筛选条件，也可以点击「重置筛选」。"
                : "点击右上角「新增订单」，记录第一笔代肝订单。";
        }

        SelectedOrder = keepId is long id ? list.FirstOrDefault(o => o.Id == id) : null;

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>打开当前选中订单的编辑窗口。</summary>
    /// <summary>托管类订单：有开始和结束日期。</summary>
    public static bool IsCustody(Order? order)
    {
        if (order is null) return false;
        return !string.IsNullOrWhiteSpace(order.StartDate) && !string.IsNullOrWhiteSpace(order.EndDate);
    }

    private void RenewSelected()
    {
        if (_selectedOrder is null) return;
        if (!IsCustody(_selectedOrder)) return;
        RenewRequested?.Invoke(this, _selectedOrder);
    }

    public void EditSelected()
    {
        if (SelectedOrder is null) return;
        EditRequested?.Invoke(this, SelectedOrder);
    }

    /// <summary>删除订单并通知其他页面刷新。</summary>
    public void DeleteOrder(Order order)
    {
        _state.Db.DeleteOrder(order.Id);
        _state.NotifyChanged();
        Refresh();
        Notifier.Info("订单已删除");
    }

    /// <summary>快速修改当前选中订单的状态。</summary>
    public void SetStatus(string status)
    {
        if (SelectedOrder is null || string.IsNullOrWhiteSpace(status)) return;
        if (SelectedOrder.Status == status) return;

        _state.Db.SetStatus(SelectedOrder.Id, status);
        _state.NotifyChanged();
        Refresh();
        Notifier.Info($"状态已改为 {status}");
    }

    /// <summary>应用日期快捷区间（本月 / 上月 / 近7天 / 全部）。</summary>
    public void ApplyPreset(string name)
    {
        var preset = Presets.FirstOrDefault(p => p.Name == name) ?? _selectedPreset;
        _selectedPreset = preset;
        foreach (var item in Presets) item.IsSelected = ReferenceEquals(item, preset);

        ApplyDateRange(preset.Name);
        if (!_loading) Refresh();
    }

    private void ApplyDateRange(string name)
    {
        var today = DateTime.Today;
        DateTime? from;
        DateTime? to;
        switch (name)
        {
            case "本月":
                from = new DateTime(today.Year, today.Month, 1);
                to = from.Value.AddMonths(1).AddDays(-1);
                break;
            case "上月":
                from = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                to = new DateTime(today.Year, today.Month, 1).AddDays(-1);
                break;
            case "近7天":
                from = today.AddDays(-6);
                to = today;
                break;
            default:
                from = null;
                to = null;
                break;
        }

        _loading = true;
        FromDate = from;
        ToDate = to;
        _loading = false;
    }

    private void ResetFilters()
    {
        _loading = true;
        Keyword = "";
        SelectedStatus = "全部";
        FromDate = null;
        ToDate = null;
        _loading = false;

        _selectedPreset = Presets.First(p => p.Name == "全部");
        foreach (var item in Presets) item.IsSelected = ReferenceEquals(item, _selectedPreset);
        Refresh();
    }

    /// <summary>用户手改日期时，快捷区间自动切到「自定义」。</summary>
    private void MarkCustomRange()
    {
        foreach (var item in Presets) item.IsSelected = false;
    }

    private bool IsAllRange()
    {
        return FromDate is null && ToDate is null;
    }

    private static string? DateText(DateTime? value) => value?.ToString("yyyy-MM-dd");

    private void OnDataChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(new Action(Refresh));
    }

    private string BuildCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("订单日期,老板,电话,项目,数量,金额,我的分成,助手分成,跑手分成,状态,跑手,托管开始,托管结束,备注");
        foreach (var order in Orders)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(order.OrderDate),
                Csv(order.BossName),
                Csv(order.Phone),
                Csv(order.Item),
                order.Quantity.ToString(CultureInfo.InvariantCulture),
                Money(order.Amount),
                Money(order.MineShare),
                Money(order.AssistantShare),
                Money(order.RunnerShare),
                Csv(order.Status),
                Csv(order.RunnerText),
                Csv(order.StartDate),
                Csv(order.EndDate),
                Csv(order.Note),
            }));
        }
        return sb.ToString();
    }

    private static string Money(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Csv(string? value)
    {
        value ??= "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

