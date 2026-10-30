using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using OrderManager.Models;
using OrderManager.Mvvm;
using OrderManager.Services;

namespace OrderManager.ViewModels;

/// <summary>概览页 / 报表页通用的 KPI 展示项。</summary>
public class KpiItem
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string Hint { get; init; } = "";
}

/// <summary>近 7 天营业额趋势中的单日柱子。</summary>
public class DayBar
{
    public string DayLabel { get; init; } = "";
    public string AmountText { get; init; } = "";
    public double Height { get; init; }
    public bool IsToday { get; init; }

    /// <summary>当天没有营业额：显示为贴底的浅灰基准线，且不显示金额文案。</summary>
    public bool IsEmpty { get; init; }
}

/// <summary>「概览」首页的视图模型：今日 / 本月 KPI、今日订单、近 7 天趋势、待办提醒。</summary>
public class DashboardViewModel : ObservableObject
{
    /// <summary>柱状图最高柱子的像素高度。</summary>
    private const double MaxBarHeight = 94;

    private string _todayText = "";
    private string _shopText = "";
    private string _updatedText = "";
    private string _todayOrdersSubText = "共 0 单";
    private string _trendTotalText = "";
    private string _pendingTotalText = "暂无待办";
    private string _pendingPaymentText = "0";
    private string _pendingSettleText = "0";
    private string _inProgressText = "0";
    private bool _hasNoTodayOrders = true;
    private bool _hasNoTrend = true;

    public DashboardViewModel()
    {
        RefreshCommand = new RelayCommand(() => { Reload(); Notifier.Info("已刷新"); });
        AppState.I.DataChanged += OnDataChanged;
        Reload();
    }

    public ObservableCollection<KpiItem> TodayKpis { get; } = new();
    public ObservableCollection<KpiItem> MonthKpis { get; } = new();
    public ObservableCollection<DayBar> Trend { get; } = new();
    public ObservableCollection<Order> TodayOrders { get; } = new();

    public ICommand RefreshCommand { get; }

    public string TodayText { get => _todayText; private set => SetProperty(ref _todayText, value); }
    public string ShopText { get => _shopText; private set => SetProperty(ref _shopText, value); }
    public string UpdatedText { get => _updatedText; private set => SetProperty(ref _updatedText, value); }
    public string TodayOrdersSubText { get => _todayOrdersSubText; private set => SetProperty(ref _todayOrdersSubText, value); }
    public string TrendTotalText { get => _trendTotalText; private set => SetProperty(ref _trendTotalText, value); }
    public string PendingTotalText { get => _pendingTotalText; private set => SetProperty(ref _pendingTotalText, value); }
    public string PendingPaymentText { get => _pendingPaymentText; private set => SetProperty(ref _pendingPaymentText, value); }
    public string PendingSettleText { get => _pendingSettleText; private set => SetProperty(ref _pendingSettleText, value); }
    public string InProgressText { get => _inProgressText; private set => SetProperty(ref _inProgressText, value); }
    public bool HasNoTodayOrders { get => _hasNoTodayOrders; private set => SetProperty(ref _hasNoTodayOrders, value); }

    /// <summary>近 7 天没有任何营业额，界面改为显示空态提示。</summary>
    public bool HasNoTrend
    {
        get => _hasNoTrend;
        private set
        {
            if (SetProperty(ref _hasNoTrend, value)) OnPropertyChanged(nameof(HasTrend));
        }
    }

    public bool HasTrend => !_hasNoTrend;

    /// <summary>重新读取全部订单并刷新页面上的所有统计。</summary>
    public void Reload()
    {
        var state = AppState.I;
        var all = state.LoadAllOrders();
        var today = DateTime.Now.Date;

        TodayText = $"{today.Year} 年 {today.Month} 月 {today.Day} 日 · {WeekName(today)}";
        ShopText = string.IsNullOrWhiteSpace(state.Profile.ShopName) ? "代肝管家" : state.Profile.ShopName.Trim();

        string todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string monthKey = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var todayOrders = all.Where(o => string.Equals(o.OrderDate, todayKey, StringComparison.Ordinal))
                             .OrderByDescending(o => o.Id)
                             .ToList();
        var monthOrders = all.Where(o => o.OrderDate.StartsWith(monthKey, StringComparison.Ordinal)).ToList();

        var todaySum = state.Summarize(todayOrders);
        var monthSum = state.Summarize(monthOrders);

        // ---------------- 今日 KPI ----------------
        TodayKpis.Clear();
        TodayKpis.Add(new KpiItem
        {
            Label = "今日订单",
            Value = Count(todaySum.Orders),
            Hint = $"已完成 {todaySum.Completed} 单",
        });
        TodayKpis.Add(new KpiItem
        {
            Label = "今日营业额",
            Value = Money(todaySum.Amount),
            Hint = $"未完成 {todaySum.Open} 单",
        });
        TodayKpis.Add(new KpiItem
        {
            Label = "我赚",
            Value = Money(todaySum.Mine),
            Hint = "我的分成",
        });
        TodayKpis.Add(new KpiItem
        {
            Label = "助手分成",
            Value = Money(todaySum.Assistant),
            Hint = "需结算给助手",
        });
        TodayKpis.Add(new KpiItem
        {
            Label = "跑手分成",
            Value = Money(todaySum.Runner),
            Hint = "需结算给跑手",
        });

        // ---------------- 本月 KPI ----------------
        int monthInProgress = monthOrders.Count(o => o.Status == OrderStatus.InProgress);
        MonthKpis.Clear();
        MonthKpis.Add(new KpiItem { Label = "本月订单", Value = Count(monthSum.Orders) });
        MonthKpis.Add(new KpiItem { Label = "本月营业额", Value = Money(monthSum.Amount) });
        MonthKpis.Add(new KpiItem { Label = "我的分成", Value = Money(monthSum.Mine) });
        MonthKpis.Add(new KpiItem { Label = "助手分成", Value = Money(monthSum.Assistant) });
        MonthKpis.Add(new KpiItem { Label = "跑手分成", Value = Money(monthSum.Runner) });
        MonthKpis.Add(new KpiItem { Label = "未完成订单", Value = Count(monthSum.Open) });
        MonthKpis.Add(new KpiItem { Label = "进行中订单", Value = Count(monthInProgress) });

        // ---------------- 近 7 天趋势 ----------------
        var days = new DateTime[7];
        var dayAmounts = new double[7];
        for (int i = 0; i < 7; i++) days[i] = today.AddDays(i - 6);
        for (int i = 0; i < 7; i++)
        {
            string key = days[i].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            dayAmounts[i] = all.Where(o => string.Equals(o.OrderDate, key, StringComparison.Ordinal)).Sum(o => o.Amount);
        }

        double maxAmount = dayAmounts.Max();
        double total7 = dayAmounts.Sum();

        bool noTrend = total7 <= 0;
        Trend.Clear();
        for (int i = 0; i < 7; i++)
        {
            double amount = dayAmounts[i];
            bool isEmpty = amount <= 0;
            double height = isEmpty
                ? 3
                : Math.Max(4, Math.Round(amount / maxAmount * MaxBarHeight, 1));
            Trend.Add(new DayBar
            {
                DayLabel = WeekName(days[i]),
                AmountText = isEmpty ? "" : Money(amount),
                Height = height,
                IsToday = i == 6,
                IsEmpty = isEmpty,
            });
        }
        HasNoTrend = noTrend;
        TrendTotalText = noTrend ? "" : $"7 天合计 {Money(total7)} · 日均 {Money(total7 / 7)}";

        // ---------------- 今日订单列表（最多 6 条） ----------------
        TodayOrders.Clear();
        foreach (var order in todayOrders.Take(6)) TodayOrders.Add(order);
        TodayOrdersSubText = $"共 {todayOrders.Count} 单";
        HasNoTodayOrders = todayOrders.Count == 0;

        // ---------------- 待办提醒 ----------------
        int pendingPay = all.Count(o => o.Status == OrderStatus.PendingPayment);
        int pendingSettle = all.Count(o => o.Status == OrderStatus.PendingSettle);
        int inProgress = all.Count(o => o.Status == OrderStatus.InProgress);
        PendingPaymentText = Count(pendingPay);
        PendingSettleText = Count(pendingSettle);
        InProgressText = Count(inProgress);
        PendingTotalText = pendingPay + pendingSettle + inProgress == 0
            ? "暂无待办"
            : $"共 {pendingPay + pendingSettle + inProgress} 项待处理";

        UpdatedText = "更新于 " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private void OnDataChanged(object? sender, EventArgs e) => Reload();

    private static string WeekName(DateTime day) => day.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        DayOfWeek.Sunday => "周日",
        _ => "",
    };

    private static string Money(double value) => "¥" + value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
