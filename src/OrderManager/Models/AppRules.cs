namespace OrderManager.Models;

/// <summary>分成规则（与旧版 order_statistics.py 的 config.json 对齐，可自由调整）。</summary>
public class AppRules
{
    public string NewRulesStartDate { get; set; } = "2026-06-24";
    public double PersonalHighThreshold { get; set; } = 100;
    public double PersonalHighRateOld { get; set; } = 0.4;
    public double PersonalHighRateNew { get; set; } = 0.2;
    public double PersonalLowRateOld { get; set; } = 0.2;
    public double PersonalLowRateNew { get; set; } = 0.1;
    public double RunnerAssistantRate { get; set; } = 0.15;
    public double RunnerXiaoyuRate { get; set; } = 0.85;
    public double RunnerRate { get; set; } = 0.8;
    public double MyRate { get; set; } = 0.05;
    public bool DisableAssistantShare { get; set; }
    public string XiaoyuName { get; set; } = "小予";

    public AppRules Clone() => (AppRules)MemberwiseClone();
}

public class AppProfile
{
    public string ShopName { get; set; } = "代肝管家";
    public string OwnerName { get; set; } = "店长";
}

/// <summary>单笔订单的分成结果。</summary>
public readonly record struct SplitResult(double Assistant, double Runner, double Mine, string Kind);

/// <summary>一组订单的汇总。</summary>
public class Summary
{
    public int Orders { get; set; }
    public int PersonalOrders { get; set; }
    public int RunnerOrders { get; set; }
    public double Amount { get; set; }
    public double Assistant { get; set; }
    public double Runner { get; set; }
    public double Mine { get; set; }
    public int Completed { get; set; }
    public int Open { get; set; }

    public string AmountText => $"¥{Amount:0.00}";
    public string AssistantText => $"¥{Assistant:0.00}";
    public string RunnerText => $"¥{Runner:0.00}";
    public string MineText => $"¥{Mine:0.00}";
}
