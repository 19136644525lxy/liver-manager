using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using OrderManager.Models;
using OrderManager.Mvvm;
using OrderManager.Services;

namespace OrderManager.ViewModels;

/// <summary>
/// 托管订单续单：在原有托管期之后顺延若干个月，可同时追加一笔金额。
/// 旧托管期已经过期的（比如上个月就该续了），从今天算起，避免续出过去的日期。
/// </summary>
public sealed class RenewOrderViewModel : ObservableObject
{
    private readonly AppState _state = AppState.I;
    private readonly Order _order;

    private int _months = 1;
    private string _extraAmount = "0";
    private string _note = "";
    private string _error = "";

    public RenewOrderViewModel(Order order)
    {
        _order = order;

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? Saved;

    public string BossName => string.IsNullOrWhiteSpace(_order.BossName) ? "（未填客户）" : _order.BossName;

    public string ItemText => string.IsNullOrWhiteSpace(_order.Item) ? "代肝服务" : _order.Item;

    public string OriginalRangeText
    {
        get
        {
            var start = ParseDate(_order.StartDate);
            var end = ParseDate(_order.EndDate);
            if (start is null && end is null) return "原订单没有托管期";
            var left = start?.ToString("yyyy-MM-dd") ?? "未填";
            var right = end?.ToString("yyyy-MM-dd") ?? "未填";
            return $"原托管期：{left} 至 {right}";
        }
    }

    public string OriginalAmountText => $"原订单金额：¥{Money(_order.Amount)}";

    /// <summary>可选的续单月数。</summary>
    public ObservableCollection<int> MonthOptions { get; } = new() { 1, 2, 3, 6, 12 };

    public int Months
    {
        get => _months;
        set
        {
            var safe = value <= 0 ? 1 : value;
            if (!SetProperty(ref _months, safe)) return;
            Fail("");
            RaisePreviewChanged();
        }
    }

    public string ExtraAmount
    {
        get => _extraAmount;
        set
        {
            if (!SetProperty(ref _extraAmount, value)) return;
            Fail("");
            RaisePreviewChanged();
        }
    }

    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }

    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_error);

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>续单后的托管结束日期。</summary>
    public DateTime NewEndDate => BaseEnd.AddMonths(_months);

    public string PreviewPeriodText
    {
        get
        {
            var start = ParseDate(_order.StartDate) ?? DateTime.Today;
            return $"{start:yyyy-MM-dd} 至 {NewEndDate:yyyy-MM-dd}（多 {_months} 个月）";
        }
    }

    public string PreviewAmountText => $"¥{Money(_order.Amount + ExtraAmountValue)}";

    private DateTime BaseEnd
    {
        get
        {
            var end = ParseDate(_order.EndDate) ?? DateTime.Today;
            var start = ParseDate(_order.StartDate);
            var floor = start is not null && start.Value > DateTime.Today ? start.Value : DateTime.Today;
            return end < floor ? floor : end;
        }
    }

    private double ExtraAmountValue
    {
        get
        {
            if (double.TryParse(_extraAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && value > 0)
            {
                return Round2(value);
            }
            return 0;
        }
    }

    private void RaisePreviewChanged()
    {
        OnPropertyChanged(nameof(NewEndDate));
        OnPropertyChanged(nameof(PreviewPeriodText));
        OnPropertyChanged(nameof(PreviewAmountText));
    }

    private void Save()
    {
        double extra = 0;
        var amountText = (_extraAmount ?? "").Trim();
        if (amountText.Length > 0)
        {
            if (!double.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out extra))
            {
                Fail("追加金额要填数字，比如 30 或 30.5");
                return;
            }
            if (extra < 0)
            {
                Fail("追加金额不能是负数");
                return;
            }
        }

        if (_months <= 0)
        {
            Fail("续单月数至少 1 个月");
            return;
        }

        var newEnd = NewEndDate;

        if (ParseDate(_order.StartDate) is null)
        {
            _order.StartDate = DateTime.Today.ToString("yyyy-MM-dd");
        }

        _order.EndDate = newEnd.ToString("yyyy-MM-dd");
        if (extra > 0) _order.Amount = Round2(_order.Amount + extra);

        var piece = $"续单 +{_months} 个月（至 {newEnd:yyyy-MM-dd}）";
        if (!string.IsNullOrWhiteSpace(Note))
        {
            piece += " " + Note.Trim();
        }
        _order.Note = string.IsNullOrWhiteSpace(_order.Note) ? piece : _order.Note + "；" + piece;

        try
        {
            _state.Db.UpdateOrder(_order);
        }
        catch (Exception ex)
        {
            Fail("保存失败：" + ex.Message);
            return;
        }

        _state.NotifyChanged();
        Notifier.Info($"{BossName} 已续单到 {newEnd:yyyy-MM-dd}");
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void Fail(string message)
    {
        Error = message ?? "";
        OnPropertyChanged(nameof(HasError));
    }


    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value.Date
            : null;
    }

    private static double Round2(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Money(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}

