using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using OrderManager.Models;
using OrderManager.Mvvm;
using OrderManager.Services;

namespace OrderManager.ViewModels;

/// <summary>订单新增 / 编辑窗口的视图模型，含实时分成预览与输入校验。</summary>
public class OrderEditViewModel : ObservableObject
{
    private readonly AppState _state = AppState.I;
    private readonly Order _order;
    private readonly bool _isNew;
    private bool _loading = true;

    private string _bossName = "";
    private string _phone = "";
    private string _itemName = "";
    private string _quantityText = "1";
    private string _amountText = "0";
    private bool _personal = true;
    private string _runnerName = "";
    private string _status = OrderStatus.InProgress;
    private string _note = "";
    private DateTime? _orderDate = DateTime.Today;
    private DateTime? _startDate;
    private DateTime? _endDate;
    private Item? _selectedItem;
    private string _errorMessage = "";
    private bool _hasError;
    private string _minePreview = "¥0.00";
    private string _assistantPreview = "¥0.00";
    private string _runnerPreview = "¥0.00";
    private string _kindText = "";

    public OrderEditViewModel(Order? order = null)
    {
        _isNew = order is null;
        _order = order ?? new Order
        {
            Personal = true,
            Quantity = 1,
            Status = OrderStatus.InProgress,
            OrderDate = DateTime.Today.ToString("yyyy-MM-dd"),
        };

        foreach (var status in OrderStatus.All) StatusOptions.Add(status);

        Items = _state.Db.ListItems();
        RunnerNames = _state.Db.ListRunners().Select(r => r.Name).ToList();

        _loading = true;
        BossName = _order.BossName;
        Phone = _order.Phone;
        ItemName = _order.Item;
        QuantityText = _order.Quantity.ToString(CultureInfo.InvariantCulture);
        AmountText = _order.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        Personal = _order.Personal;
        RunnerName = _order.RunnerName;
        Status = string.IsNullOrWhiteSpace(_order.Status) ? OrderStatus.InProgress : _order.Status;
        Note = _order.Note;
        OrderDate = ParseDate(_order.OrderDate) ?? DateTime.Today;
        StartDate = ParseDate(_order.StartDate);
        EndDate = ParseDate(_order.EndDate);
        SelectedItem = Items.FirstOrDefault(i =>
            string.Equals(i.Name, _order.Item, StringComparison.OrdinalIgnoreCase));
        _loading = false;

        SaveCommand = new RelayCommand(() => Save());
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke(this, EventArgs.Empty));
        DeleteCommand = new RelayCommand(
            () => DeleteRequested?.Invoke(this, EventArgs.Empty),
            () => !_isNew);

        UpdatePreview();
    }

    /// <summary>保存成功后触发，视图据此关闭窗口并返回 true。</summary>
    public event EventHandler? SaveCompleted;

    /// <summary>取消 / 关闭。</summary>
    public event EventHandler? CancelRequested;

    /// <summary>请求删除（视图弹确认框，确认后调用 <see cref="Delete"/>）。</summary>
    public event EventHandler? DeleteRequested;

    public bool IsNew => _isNew;

    public string Title => _isNew ? "新增订单" : "编辑订单";

    public string SubTitle => _isNew ? "填写订单信息，保存后自动计算分成" : "修改后保存，分成会按最新规则重新计算";

    public List<Item> Items { get; }

    public List<string> RunnerNames { get; }

    public ObservableCollection<string> StatusOptions { get; } = new();

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }

    public string BossName
    {
        get => _bossName;
        set
        {
            if (!SetProperty(ref _bossName, value)) return;
            if (HasError && !string.IsNullOrWhiteSpace(value)) ClearError();
        }
    }

    public string Phone
    {
        get => _phone;
        set => SetProperty(ref _phone, value);
    }

    public string ItemName
    {
        get => _itemName;
        set
        {
            if (!SetProperty(ref _itemName, value)) return;
            AutoFillCustodyDates();
            OnPropertyChanged(nameof(CustodyHint));
        }
    }

    /// <summary>托管类订单（包月 / 托管服务）的日期提示文案。</summary>
    public string CustodyHint => IsCustodyName(ItemName)
        ? "托管类订单填好这两个日期才会出现在「托管日历」里，留空则日历不显示。"
        : "填了开始 / 结束日期就会出现在「托管日历」里，可以按天打卡。";

    /// <summary>判断项目名是否属于托管类（名字含「托管」「包月」，或价目表分类为「托管服务」）。</summary>
    private bool IsCustodyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Contains("托管") || name.Contains("包月")) return true;
        var item = Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        return item is not null && string.Equals(item.Category, "托管服务", StringComparison.Ordinal);
    }

    /// <summary>选中托管类项目时，若两个日期都还没填，就按「订单日期 ~ 一个月后」带出默认值。</summary>
    private void AutoFillCustodyDates()
    {
        if (_loading) return;
        if (!IsCustodyName(ItemName)) return;
        if (StartDate is not null || EndDate is not null) return;
        var start = (OrderDate ?? DateTime.Today).Date;
        StartDate = start;
        EndDate = start.AddMonths(1);
    }

    /// <summary>价目表下拉选中项，选中后自动填名称与金额。</summary>
    public Item? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            if (_loading || value is null) return;
            ItemName = value.Name;
            if (value.Price > 0)
                AmountText = value.Price.ToString("0.##", CultureInfo.InvariantCulture);
            AutoFillCustodyDates();
            UpdatePreview();
        }
    }

    public string QuantityText
    {
        get => _quantityText;
        set
        {
            if (!SetProperty(ref _quantityText, value)) return;
            UpdatePreview();
        }
    }

    public string AmountText
    {
        get => _amountText;
        set
        {
            if (!SetProperty(ref _amountText, value)) return;
            UpdatePreview();
        }
    }

    /// <summary>是否个人接单（不分配跑手）。</summary>
    public bool Personal
    {
        get => _personal;
        set
        {
            if (!SetProperty(ref _personal, value)) return;
            OnPropertyChanged(nameof(IsRunnerEnabled));
            UpdatePreview();
        }
    }

    public bool IsRunnerEnabled => !Personal;

    public string RunnerName
    {
        get => _runnerName;
        set
        {
            if (!SetProperty(ref _runnerName, value)) return;
            UpdatePreview();
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }

    public DateTime? OrderDate
    {
        get => _orderDate;
        set
        {
            if (!SetProperty(ref _orderDate, value)) return;
            UpdatePreview();
        }
    }

    public DateTime? StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value);
    }

    public DateTime? EndDate
    {
        get => _endDate;
        set => SetProperty(ref _endDate, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string MinePreview
    {
        get => _minePreview;
        private set => SetProperty(ref _minePreview, value);
    }

    public string AssistantPreview
    {
        get => _assistantPreview;
        private set => SetProperty(ref _assistantPreview, value);
    }

    public string RunnerPreview
    {
        get => _runnerPreview;
        private set => SetProperty(ref _runnerPreview, value);
    }

    /// <summary>分成规则说明文案。</summary>
    public string KindText
    {
        get => _kindText;
        private set => SetProperty(ref _kindText, value);
    }

    /// <summary>校验并保存，返回是否保存成功。</summary>
    public bool Save()
    {
        if (string.IsNullOrWhiteSpace(BossName))
        {
            ShowError("请填写老板网名");
            return false;
        }

        if (!TryParseAmount(out var amount))
        {
            ShowError("金额需要填写不小于 0 的数字");
            return false;
        }

        if (!TryParseQuantity(out var quantity))
        {
            ShowError("数量需要填写不小于 1 的整数");
            return false;
        }

        // 托管类订单如果还没填日期，保存前补上默认托管期，避免"存了却不出现在托管日历里"
        AutoFillCustodyDates();

        if (StartDate is not null && EndDate is not null && EndDate.Value.Date < StartDate.Value.Date)
        {
            ShowError("托管结束日期不能早于开始日期");
            return false;
        }

        ClearError();

        _order.BossName = BossName.Trim();
        _order.Phone = (Phone ?? "").Trim();
        _order.Item = (ItemName ?? "").Trim();
        _order.Quantity = quantity;
        _order.Amount = amount;
        _order.Personal = Personal;
        _order.RunnerName = Personal ? "" : (RunnerName ?? "").Trim();
        _order.Status = string.IsNullOrWhiteSpace(Status) ? OrderStatus.InProgress : Status;
        _order.Note = (Note ?? "").Trim();
        _order.OrderDate = (OrderDate ?? DateTime.Today).ToString("yyyy-MM-dd");
        _order.StartDate = StartDate?.ToString("yyyy-MM-dd") ?? "";
        _order.EndDate = EndDate?.ToString("yyyy-MM-dd") ?? "";

        if (_isNew) _state.Db.InsertOrder(_order);
        else _state.Db.UpdateOrder(_order);

        _state.NotifyChanged();
        Notifier.Info("订单已保存");
        SaveCompleted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>删除完成后触发，视图据此关闭窗口。</summary>
    public event EventHandler? Deleted;

    /// <summary>删除当前订单（仅编辑模式，视图确认后调用）。</summary>
    public void Delete()
    {
        if (_isNew) return;
        _state.Db.DeleteOrder(_order.Id);
        _state.NotifyChanged();
        Notifier.Info("订单已删除");
        Deleted?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePreview()
    {
        if (_loading) return;

        var temp = new Order
        {
            Amount = TryParseAmount(out var amount) ? amount : 0,
            Quantity = TryParseQuantity(out var quantity) ? quantity : 1,
            Personal = Personal,
            RunnerName = Personal ? "" : (RunnerName ?? "").Trim(),
            OrderDate = (OrderDate ?? DateTime.Today).ToString("yyyy-MM-dd"),
        };

        var split = CalcService.Split(temp, _state.Rules);
        MinePreview = "¥" + split.Mine.ToString("0.00", CultureInfo.InvariantCulture);
        AssistantPreview = "¥" + split.Assistant.ToString("0.00", CultureInfo.InvariantCulture);
        RunnerPreview = "¥" + split.Runner.ToString("0.00", CultureInfo.InvariantCulture);
        KindText = split.Kind switch
        {
            "personal-high" => "个人接单 · 大额订单规则",
            "personal-low" => "个人接单 · 标准规则",
            "runner-xiaoyu" => "跑手接单 · 小予专属规则",
            "runner" => "跑手接单 · 标准规则",
            "personal" => "个人接单 · 助手分成已关闭",
            _ => "按当前分成规则计算",
        };
    }

    private bool TryParseAmount(out double value)
    {
        value = 0;
        var text = (AmountText ?? "").Trim();
        if (text.Length == 0) return true;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value >= 0;
    }

    private bool TryParseQuantity(out int value)
    {
        value = 1;
        var text = (QuantityText ?? "").Trim();
        if (text.Length == 0) return false;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) && value >= 1;
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private void ClearError()
    {
        ErrorMessage = "";
        HasError = false;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)) return date;
        return DateTime.TryParse(value, out var parsed) ? parsed : null;
    }
}
