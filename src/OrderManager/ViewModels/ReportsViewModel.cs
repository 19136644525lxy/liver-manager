using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using OrderManager.Models;
using OrderManager.Mvvm;
using OrderManager.Services;

namespace OrderManager.ViewModels;

/// <summary>按月统计表格中的一行。</summary>
public class MonthRow
{
    public string MonthLabel { get; init; } = "";
    public int Orders { get; init; }
    public double Amount { get; init; }
    public double Mine { get; init; }
    public double Assistant { get; init; }
    public double Runner { get; init; }

    public string OrdersText => Orders.ToString(CultureInfo.InvariantCulture);
    public string AmountText => Money(Amount);
    public string MineText => Money(Mine);
    public string AssistantText => Money(Assistant);
    public string RunnerText => Money(Runner);

    public static string Money(double value) => "¥" + value.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>分类 / 跑手统计的横向条形条目。</summary>
public class BarRow
{
    public string Name { get; init; } = "";
    public int Orders { get; init; }
    public double Amount { get; init; }
    public double Mine { get; init; }
    public double Assistant { get; init; }
    public double Runner { get; init; }
    public double BarWidth { get; init; }

    public string OrdersText => Orders.ToString(CultureInfo.InvariantCulture) + " 单";
    public string AmountText => MonthRow.Money(Amount);
}

/// <summary>「统计报表」页的视图模型：年份切换、按月、按分类、按跑手、导出 CSV。</summary>
public class ReportsViewModel : ObservableObject
{
    /// <summary>横向条形最大像素长度。</summary>
    private const double MaxBarWidth = 168;

    private int _selectedYear;
    private int _yearOrders;
    private double _yearAmount;
    private double _yearMine;
    private double _yearAssistant;
    private double _yearRunner;
    private string _yearTotalText = "";
    private string _monthTotalText = "";
    private string _exportHint = "CSV 会导出到桌面，Excel 可直接打开";
    private bool _hasNoCategory = true;
    private bool _hasNoRunner = true;
    private bool _hasNoMonths = true;

    public ReportsViewModel()
    {
        _selectedYear = DateTime.Now.Year;
        ExportCommand = new RelayCommand(ExportCsv);
        AppState.I.DataChanged += OnDataChanged;
        Reload();
    }

    public ObservableCollection<int> Years { get; } = new();
    public ObservableCollection<KpiItem> YearKpis { get; } = new();
    public ObservableCollection<MonthRow> MonthRows { get; } = new();
    public ObservableCollection<BarRow> CategoryRows { get; } = new();
    public ObservableCollection<BarRow> RunnerRows { get; } = new();

    public ICommand ExportCommand { get; }

    /// <summary>当前选中的年份，切换后自动重新统计。</summary>
    public int SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (value <= 0) return;
            if (SetProperty(ref _selectedYear, value)) Reload();
        }
    }

    public string YearTotalText { get => _yearTotalText; private set => SetProperty(ref _yearTotalText, value); }
    public string MonthTotalText { get => _monthTotalText; private set => SetProperty(ref _monthTotalText, value); }
    public string ExportHint { get => _exportHint; private set => SetProperty(ref _exportHint, value); }
    public bool HasNoCategory { get => _hasNoCategory; private set => SetProperty(ref _hasNoCategory, value); }
    public bool HasNoRunner { get => _hasNoRunner; private set => SetProperty(ref _hasNoRunner, value); }
    public bool HasNoMonths { get => _hasNoMonths; private set => SetProperty(ref _hasNoMonths, value); }

    /// <summary>重新统计当前年份的全部数据。</summary>
    public void Reload()
    {
        var state = AppState.I;
        var all = state.LoadAllOrders();

        // ---------------- 年份下拉 ----------------
        var years = all.Select(o => ParseYear(o.OrderDate)).Where(y => y > 0).Distinct().ToList();
        if (!years.Contains(DateTime.Now.Year)) years.Add(DateTime.Now.Year);
        if (!years.Contains(_selectedYear)) years.Add(_selectedYear);
        years.Sort((a, b) => b.CompareTo(a));

        if (!Years.SequenceEqual(years))
        {
            Years.Clear();
            foreach (int year in years) Years.Add(year);
        }
        if (!years.Contains(_selectedYear)) _selectedYear = years[0];
        OnPropertyChanged(nameof(SelectedYear));

        var yearOrders = all.Where(o => ParseYear(o.OrderDate) == _selectedYear).ToList();
        var yearSum = state.Summarize(yearOrders);
        _yearOrders = yearSum.Orders;
        _yearAmount = yearSum.Amount;
        _yearMine = yearSum.Mine;
        _yearAssistant = yearSum.Assistant;
        _yearRunner = yearSum.Runner;

        // ---------------- 年度 KPI ----------------
        YearKpis.Clear();
        YearKpis.Add(new KpiItem { Label = "全年订单", Value = Count(yearSum.Orders), Hint = $"已完成 {yearSum.Completed} 单" });
        YearKpis.Add(new KpiItem { Label = "全年营业额", Value = Money(yearSum.Amount), Hint = $"未完成 {yearSum.Open} 单" });
        YearKpis.Add(new KpiItem { Label = "我的分成", Value = Money(yearSum.Mine), Hint = "全部订单合计" });
        YearKpis.Add(new KpiItem { Label = "助手分成", Value = Money(yearSum.Assistant), Hint = "全部订单合计" });
        YearKpis.Add(new KpiItem { Label = "跑手分成", Value = Money(yearSum.Runner), Hint = "全部订单合计" });
        YearKpis.Add(new KpiItem { Label = "个人 / 跑手", Value = $"{yearSum.PersonalOrders} / {yearSum.RunnerOrders}", Hint = "订单来源占比" });
        YearTotalText = $"全年 {yearSum.Orders} 单 · 营业额 {Money(yearSum.Amount)}";

        // ---------------- 按月统计 ----------------
        MonthRows.Clear();
        for (int month = 1; month <= 12; month++)
        {
            var monthOrders = yearOrders.Where(o => ParseMonth(o.OrderDate) == month).ToList();
            var monthSum = state.Summarize(monthOrders);
            MonthRows.Add(new MonthRow
            {
                MonthLabel = $"{month} 月",
                Orders = monthSum.Orders,
                Amount = monthSum.Amount,
                Mine = monthSum.Mine,
                Assistant = monthSum.Assistant,
                Runner = monthSum.Runner,
            });
        }
        MonthTotalText = $"合计 {yearSum.Orders} 单 · {Money(yearSum.Amount)}";
        HasNoMonths = yearSum.Orders == 0;

        // ---------------- 按项目分类 ----------------
        var itemCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var item in state.Db.ListItems())
            {
                if (string.IsNullOrWhiteSpace(item.Name)) continue;
                itemCategories[item.Name.Trim()] =
                    string.IsNullOrWhiteSpace(item.Category) ? "未分类" : item.Category.Trim();
            }
        }
        catch
        {
            // 价目表读取失败时统一归入「未分类」，不影响报表主体
        }

        var categoryGroups = yearOrders
            .GroupBy(o => CategoryOf(o.Item, itemCategories))
            .Select(g => new { Name = g.Key, Stat = Aggregate(g) })
            .OrderByDescending(g => g.Stat.Amount)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
        double maxCategoryAmount = categoryGroups.Count == 0 ? 0 : categoryGroups.Max(g => g.Stat.Amount);
        CategoryRows.Clear();
        foreach (var group in categoryGroups)
            CategoryRows.Add(MakeBarRow(group.Name, group.Stat, maxCategoryAmount));
        HasNoCategory = CategoryRows.Count == 0;

        // ---------------- 按跑手 ----------------
        var runnerGroups = yearOrders
            .GroupBy(RunnerKey)
            .Select(g => new { Name = g.Key, Stat = Aggregate(g) })
            .OrderByDescending(g => g.Stat.Amount)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
        double maxRunnerAmount = runnerGroups.Count == 0 ? 0 : runnerGroups.Max(g => g.Stat.Amount);
        RunnerRows.Clear();
        foreach (var group in runnerGroups)
            RunnerRows.Add(MakeBarRow(group.Name, group.Stat, maxRunnerAmount));
        HasNoRunner = RunnerRows.Count == 0;
    }

    private void OnDataChanged(object? sender, EventArgs e) => Reload();

    /// <summary>导出当前年份的报表到桌面 CSV（UTF-8 BOM）。</summary>
    private void ExportCsv()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("统计报表," + Csv($"{_selectedYear} 年"));
            sb.AppendLine("导出时间," + Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            sb.AppendLine();

            sb.AppendLine("按月统计");
            sb.AppendLine("月份,单数,营业额,我的分成,助手分成,跑手分成");
            foreach (var row in MonthRows)
            {
                sb.AppendLine(string.Join(",",
                    Csv(row.MonthLabel), row.OrdersText, Num(row.Amount), Num(row.Mine), Num(row.Assistant), Num(row.Runner)));
            }
            sb.AppendLine(string.Join(",", "合计", _yearOrders.ToString(CultureInfo.InvariantCulture),
                Num(_yearAmount), Num(_yearMine), Num(_yearAssistant), Num(_yearRunner)));
            sb.AppendLine();

            sb.AppendLine("按项目分类");
            sb.AppendLine("分类,单数,营业额,我的分成,助手分成,跑手分成");
            foreach (var row in CategoryRows)
            {
                sb.AppendLine(string.Join(",",
                    Csv(row.Name), row.Orders.ToString(CultureInfo.InvariantCulture),
                    Num(row.Amount), Num(row.Mine), Num(row.Assistant), Num(row.Runner)));
            }
            sb.AppendLine();

            sb.AppendLine("按跑手");
            sb.AppendLine("跑手,单数,营业额,我的分成,助手分成,跑手分成");
            foreach (var row in RunnerRows)
            {
                sb.AppendLine(string.Join(",",
                    Csv(row.Name), row.Orders.ToString(CultureInfo.InvariantCulture),
                    Num(row.Amount), Num(row.Mine), Num(row.Assistant), Num(row.Runner)));
            }
            sb.AppendLine();

            string folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(folder)) folder = AppState.I.DataDir;

            string fileName = $"统计报表_{_selectedYear}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string path = Path.Combine(folder, fileName);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));

            ExportHint = "已导出：" + fileName;
            MessageBox.Show($"报表已导出到：\n{path}", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            Notifier.Info("已导出报表 CSV");
        }
        catch (Exception ex)
        {
            ExportHint = "导出失败：" + ex.Message;
            MessageBox.Show("导出失败：" + ex.Message, "统计报表", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static (int Orders, double Amount, double Mine, double Assistant, double Runner) Aggregate(IEnumerable<Order> orders)
    {
        int count = 0;
        double amount = 0, mine = 0, assistant = 0, runner = 0;
        var rules = AppState.I.Rules;
        foreach (var order in orders)
        {
            var split = CalcService.Split(order, rules);
            count++;
            amount += order.Amount;
            mine += split.Mine;
            assistant += split.Assistant;
            runner += split.Runner;
        }
        return (count, CalcService.Round2(amount), CalcService.Round2(mine),
                CalcService.Round2(assistant), CalcService.Round2(runner));
    }

    private static BarRow MakeBarRow(string name,
        (int Orders, double Amount, double Mine, double Assistant, double Runner) stat, double maxAmount)
    {
        double width = 0;
        if (stat.Amount > 0 && maxAmount > 0)
            width = Math.Max(4, Math.Round(stat.Amount / maxAmount * MaxBarWidth, 1));
        return new BarRow
        {
            Name = name,
            Orders = stat.Orders,
            Amount = stat.Amount,
            Mine = stat.Mine,
            Assistant = stat.Assistant,
            Runner = stat.Runner,
            BarWidth = width,
        };
    }

    /// <summary>
    /// 归组名称：先按价目表的「名称 → 分类」映射；价目表里没有该名称时，
    /// 直接用订单里的项目名归组（例如旧数据导入的「代肝服务」），避免全部落到「未分类」。
    /// </summary>
    private static string CategoryOf(string item, Dictionary<string, string> itemCategories)
    {
        if (string.IsNullOrWhiteSpace(item)) return "未分类";
        var name = item.Trim();
        return itemCategories.TryGetValue(name, out var category) ? category : name;
    }

    private static string RunnerKey(Order order)
    {
        if (order.Personal) return "个人自接";
        return string.IsNullOrWhiteSpace(order.RunnerName) ? "未指派跑手" : order.RunnerName.Trim();
    }

    private static int ParseYear(string date)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 4) return 0;
        return int.TryParse(date.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year) ? year : 0;
    }

    private static int ParseMonth(string date)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 7) return 0;
        return int.TryParse(date.Substring(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int month) ? month : 0;
    }

    private static string Csv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string Num(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Money(double value) => "¥" + value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
