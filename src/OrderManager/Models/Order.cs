namespace OrderManager.Models;

public static class OrderStatus
{
    public const string PendingPayment = "待付款";
    public const string InProgress = "进行中";
    public const string PendingSettle = "待结算";
    public const string Completed = "已完成";
    public const string Cancelled = "已取消";

    public static readonly string[] All = { PendingPayment, InProgress, PendingSettle, Completed, Cancelled };

    /// <summary>未完成的订单（需要关注）</summary>
    public static bool IsOpen(string status) =>
        status is PendingPayment or InProgress or PendingSettle;
}

public class Order
{
    public long Id { get; set; }
    public string BossName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Item { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public double Amount { get; set; }
    public bool Personal { get; set; } = true;
    public string RunnerName { get; set; } = "";
    public string Status { get; set; } = OrderStatus.InProgress;
    public string Note { get; set; } = "";
    public string OrderDate { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    // 计算后填充（不持久化）
    public double AssistantShare { get; set; }
    public double RunnerShare { get; set; }
    public double MineShare { get; set; }
    public string SplitKind { get; set; } = "";

    public string PersonalText => Personal ? "个人接单" : (string.IsNullOrWhiteSpace(RunnerName) ? "跑手" : RunnerName);
    public string QuantityText => $"x{Quantity}";
    public string AmountText => Amount.ToString("0.00");
    public string MineText => MineShare.ToString("0.00");
    public string AssistantText => AssistantShare.ToString("0.00");
    public string DateShort => OrderDate.Length >= 10 ? OrderDate[5..] : OrderDate;
    public string RunnerText => Personal ? "—" : (string.IsNullOrWhiteSpace(RunnerName) ? "跑手" : RunnerName);
}
