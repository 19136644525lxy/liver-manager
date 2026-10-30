using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OrderManager.Models;
using OrderManager.Mvvm;
using OrderManager.Services;

namespace OrderManager.ViewModels;

/// <summary>托管日历左侧列表中的一条托管订单。</summary>
public sealed class CustodyOrder : ObservableObject
{
    private HashSet<string> _checkins = new();

    public Order Order { get; init; } = new();

    public HashSet<string> Checkins
    {
        get => _checkins;
        set
        {
            _checkins = value;
            OnPropertyChanged(nameof(CheckinText));
        }
    }

    public string BossName => string.IsNullOrWhiteSpace(Order.BossName) ? "未填写老板" : Order.BossName;
    public string ItemName => string.IsNullOrWhiteSpace(Order.Item) ? "未填写项目" : Order.Item;
    public string RangeText => $"托管 {Order.StartDate} 至 {Order.EndDate}";
    public string StatusText => string.IsNullOrWhiteSpace(Order.Status) ? "进行中" : Order.Status;
    public string CheckinText => _checkins.Count == 0 ? "尚未打卡" : $"累计打卡 {_checkins.Count} 天";
}

/// <summary>月历里的一天。IsPlaceholder 为 true 表示月初的空白占位格。</summary>
public sealed class CalendarDay
{
    public DateTime Date { get; init; }
    public bool IsPlaceholder { get; init; }
    public bool IsInRange { get; init; }
    public bool HasCheckin { get; init; }
    public bool IsToday { get; init; }

    public bool CanToggle => !IsPlaceholder && IsInRange;
    public string DayText => IsPlaceholder ? "" : Date.Day.ToString(CultureInfo.InvariantCulture);
    public string DateKey => IsPlaceholder ? "" : Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>托管日历页：按月查看托管订单，点击日期切换当天打卡。</summary>
public sealed class CalendarViewModel : ObservableObject
{
    private DateTime _month;
    private CustodyOrder? _selectedOrder;
    private string _todayHint = "点击托管期内的日期即可切换当天打卡";
    private string _missingCustodyHint = "";

    public CalendarViewModel()
    {
        _month = FirstDayOfMonth(DateTime.Today);
        PreviousMonthCommand = new RelayCommand(() => ShiftMonth(-1));
        NextMonthCommand = new RelayCommand(() => ShiftMonth(1));
        TodayCommand = new RelayCommand(GoToday);
        ToggleDayCommand = new RelayCommand(parameter =>
        {
            if (parameter is CalendarDay day) ToggleDay(day);
        });

        AppState.I.DataChanged += OnDataChanged;
    }

    public ObservableCollection<CustodyOrder> Orders { get; } = new();
    public ObservableCollection<CalendarDay> Days { get; } = new();

    public ICommand PreviousMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand ToggleDayCommand { get; }

    public string MonthTitle => $"{_month.Year}年 {_month.Month}月";

    public CustodyOrder? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value)) RaiseSelectionChanged();
        }
    }

    public bool HasOrders => Orders.Count > 0;
    public bool ShowNoOrders => Orders.Count == 0;

    /// <summary>标题栏下方的一行提示，点「今天」后会给出口径反馈。</summary>
    public string TodayHint
    {
        get => _todayHint;
        set => SetProperty(ref _todayHint, value);
    }
    public bool HasSelectedOrder => _selectedOrder is not null;
    public string OrderCountText => $"{Orders.Count} 单";

    /// <summary>存在"托管类订单但没填托管期"时的提醒文案，正常时为空。</summary>
    public string MissingCustodyHint
    {
        get => _missingCustodyHint;
        private set
        {
            if (!SetProperty(ref _missingCustodyHint, value)) return;
            OnPropertyChanged(nameof(HasMissingCustody));
        }
    }

    public bool HasMissingCustody => !string.IsNullOrWhiteSpace(_missingCustodyHint);

    public string SelectedOrderTitle => _selectedOrder is null
        ? "未选择托管订单"
        : $"{_selectedOrder.BossName} · {_selectedOrder.ItemName}";

    public string CheckinSummary
    {
        get
        {
            if (_selectedOrder is null) return "先在左侧选择一个托管订单";
            int expected = ExpectedDays();
            if (expected <= 0) return "本月不在该订单的托管期内";
            return $"本月打卡 {CheckedDays()} / {expected} 天";
        }
    }

    public double ProgressPercent
    {
        get
        {
            int expected = ExpectedDays();
            if (expected <= 0) return 0;
            return Math.Round(100.0 * CheckedDays() / expected, 0);
        }
    }

    public string ProgressPercentText => $"{ProgressPercent:0}%";

    /// <summary>重新读取托管订单与打卡数据，尽量保持当前选中的订单。</summary>
    public void Refresh()
    {
        var all = AppState.I.LoadAllOrders();
        var orders = all
            .Where(o => !string.IsNullOrWhiteSpace(o.StartDate) && !string.IsNullOrWhiteSpace(o.EndDate))
            .ToList();
        var checkins = AppState.I.Db.AllCheckins();
        long previousId = _selectedOrder?.Order.Id ?? 0;

        Orders.Clear();
        foreach (var order in orders)
        {
            Orders.Add(new CustodyOrder
            {
                Order = order,
                Checkins = checkins.TryGetValue(order.Id, out var dates) ? dates : new HashSet<string>(),
            });
        }

        OnPropertyChanged(nameof(HasOrders));
        OnPropertyChanged(nameof(ShowNoOrders));
        OnPropertyChanged(nameof(OrderCountText));

        MissingCustodyHint = BuildMissingCustodyHint(all);

        _selectedOrder = Orders.FirstOrDefault(x => x.Order.Id == previousId) ?? Orders.FirstOrDefault();
        RaiseSelectionChanged();
    }

    /// <summary>统计"看起来是托管单、但没填托管日期"的订单，给用户一个可操作的提醒。</summary>
    private static string BuildMissingCustodyHint(List<Order> all)
    {
        var custodyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in AppState.I.Db.ListItems())
        {
            if (string.Equals(item.Category, "托管服务", StringComparison.Ordinal))
                custodyNames.Add(item.Name);
        }

        int missing = all.Count(o =>
            (string.IsNullOrWhiteSpace(o.StartDate) || string.IsNullOrWhiteSpace(o.EndDate))
            && IsCustodyLike(o.Item, custodyNames));

        return missing == 0
            ? ""
            : $"还有 {missing} 单托管类订单没填托管开始 / 结束日期，所以不会显示在这里；到「订单管理」补一下日期即可。";
    }

    private static bool IsCustodyLike(string? item, HashSet<string> custodyNames)
    {
        if (string.IsNullOrWhiteSpace(item)) return false;
        if (item.Contains("托管") || item.Contains("包月")) return true;
        return custodyNames.Contains(item);
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedOrder));
        OnPropertyChanged(nameof(HasSelectedOrder));
        OnPropertyChanged(nameof(SelectedOrderTitle));
        TodayHint = "点击托管期内的日期即可切换当天打卡";
        RebuildDays();
        RaiseStats();
    }

    private void RaiseStats()
    {
        OnPropertyChanged(nameof(CheckinSummary));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressPercentText));
    }

    private void ShiftMonth(int delta)
    {
        _month = _month.AddMonths(delta);
        OnPropertyChanged(nameof(MonthTitle));
        RebuildDays();
        RaiseStats();
    }

    private void GoToday()
    {
        var today = DateTime.Today;

        // 优先选中"托管期包含今天"的订单，这样点一下就落在能打卡的那一单上
        var containing = Orders.FirstOrDefault(o => Contains(o, today));
        if (containing is not null && !ReferenceEquals(containing, _selectedOrder)) SelectedOrder = containing;

        var first = FirstDayOfMonth(today);
        if (first != _month)
        {
            _month = first;
            OnPropertyChanged(nameof(MonthTitle));
        }
        RebuildDays();

        // 今天还没打卡就顺手打上；已经打过则保持原状（想取消直接点日期格）
        if (_selectedOrder is not null && Contains(_selectedOrder, today))
        {
            var key = DateKey(today);
            if (_selectedOrder.Checkins.Contains(key))
            {
                TodayHint = "今天已经打过卡了，点日历格子可以取消";
            }
            else
            {
                AppState.I.Db.ToggleCheckin(_selectedOrder.Order.Id, key);
                _selectedOrder.Checkins = AppState.I.Db.ListCheckinDates(_selectedOrder.Order.Id);
                RebuildDays();
                TodayHint = $"{_selectedOrder.BossName} 今天已打卡";
                Notifier.Info("今天已打卡");
            }
        }
        else
        {
            TodayHint = "今天不在所选托管订单的托管期内";
        }

        RaiseStats();
    }

    /// <summary>某个托管订单的托管期是否包含指定日期。</summary>
    private static bool Contains(CustodyOrder order, DateTime date)
    {
        var start = ParseDate(order.Order.StartDate);
        var end = ParseDate(order.Order.EndDate);
        if (!start.HasValue || !end.HasValue) return false;
        var day = date.Date;
        return day >= start.Value.Date && day <= end.Value.Date;
    }

    private static string DateKey(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void ToggleDay(CalendarDay day)
    {
        if (_selectedOrder is null || !day.CanToggle) return;
        bool added = AppState.I.Db.ToggleCheckin(_selectedOrder.Order.Id, day.DateKey);
        _selectedOrder.Checkins = AppState.I.Db.ListCheckinDates(_selectedOrder.Order.Id);
        RebuildDays();
        RaiseStats();
        Notifier.Info(added
            ? $"已打卡 {day.Date:MM-dd}"
            : $"已取消 {day.Date:MM-dd} 的打卡");
    }

    private void RebuildDays()
    {
        Days.Clear();
        int offset = ((int)_month.DayOfWeek + 6) % 7; // 周一为第一天
        for (int i = 0; i < offset; i++) Days.Add(new CalendarDay { IsPlaceholder = true });

        int count = DateTime.DaysInMonth(_month.Year, _month.Month);
        var range = MonthRange();
        var checkins = _selectedOrder?.Checkins;
        var today = DateTime.Today;

        for (int d = 1; d <= count; d++)
        {
            var date = new DateTime(_month.Year, _month.Month, d);
            bool inRange = range.From.HasValue && range.To.HasValue
                           && date >= range.From.Value.Date && date <= range.To.Value.Date;
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            bool hasCheckin = checkins is not null && checkins.Contains(key);
            Days.Add(new CalendarDay
            {
                Date = date,
                IsInRange = inRange,
                HasCheckin = hasCheckin,
                IsToday = date == today,
            });
        }
    }

    /// <summary>本月与托管期的交集；没有交集时两者均为 null。</summary>
    private (DateTime? From, DateTime? To) MonthRange()
    {
        if (_selectedOrder is null) return (null, null);

        var monthStart = _month;
        var monthEnd = _month.AddMonths(1).AddDays(-1);
        var start = ParseDate(_selectedOrder.Order.StartDate);
        var end = ParseDate(_selectedOrder.Order.EndDate);

        var from = start.HasValue && start.Value.Date > monthStart ? start.Value.Date : monthStart;
        var to = end.HasValue && end.Value.Date < monthEnd ? end.Value.Date : monthEnd;
        return to < from ? (null, null) : (from, to);
    }

    private int ExpectedDays()
    {
        var range = MonthRange();
        if (!range.From.HasValue || !range.To.HasValue) return 0;
        return (range.To.Value - range.From.Value).Days + 1;
    }

    private int CheckedDays()
    {
        if (_selectedOrder is null) return 0;
        var range = MonthRange();
        if (!range.From.HasValue || !range.To.HasValue) return 0;

        int count = 0;
        foreach (var key in _selectedOrder.Checkins)
        {
            var date = ParseDate(key);
            if (date.HasValue && date.Value.Date >= range.From.Value && date.Value.Date <= range.To.Value) count++;
        }
        return count;
    }

    private static DateTime FirstDayOfMonth(DateTime value) => new(value.Year, value.Month, 1);

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var raw = text.Trim();
        if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;
        if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
            return parsed.Date;
        return null;
    }

    private void OnDataChanged(object? sender, EventArgs e) => Refresh();
}
