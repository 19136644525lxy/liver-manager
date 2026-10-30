using OrderManager.Models;

namespace OrderManager.Services;

/// <summary>分成计算：与旧版规则保持一致的纯函数，方便测试。</summary>
public static class CalcService
{
    public static SplitResult Split(Order order, AppRules rules)
    {
        double amount = order.Amount;

        if (rules.DisableAssistantShare)
            return new SplitResult(0, 0, Round2(amount), "personal");

        if (order.Personal)
        {
            bool high = amount > rules.PersonalHighThreshold;
            bool isNew = IsNewRule(order, rules);
            double rate = isNew
                ? (high ? rules.PersonalHighRateNew : rules.PersonalLowRateNew)
                : (high ? rules.PersonalHighRateOld : rules.PersonalLowRateOld);
            double assistant = Round2(amount * rate);
            return new SplitResult(assistant, 0, Round2(amount - assistant), high ? "personal-high" : "personal-low");
        }

        if (!string.IsNullOrWhiteSpace(order.RunnerName) &&
            order.RunnerName.Trim() == rules.XiaoyuName.Trim())
        {
            double assistant = Round2(amount * rules.RunnerXiaoyuRate);
            return new SplitResult(assistant, 0, Round2(amount - assistant), "runner-xiaoyu");
        }

        double runner = Round2(amount * rules.RunnerRate);
        double assistantShare = Round2(amount * rules.RunnerAssistantRate);
        double mine = Round2(amount - runner - assistantShare);
        return new SplitResult(assistantShare, runner, mine, "runner");
    }

    public static bool IsNewRule(Order order, AppRules rules)
    {
        string date = string.IsNullOrWhiteSpace(order.OrderDate) ? order.CreatedAt : order.OrderDate;
        if (date.Length > 10) date = date[..10];
        return !string.IsNullOrEmpty(date) && string.CompareOrdinal(date, rules.NewRulesStartDate) >= 0;
    }

    public static void Apply(Order order, AppRules rules)
    {
        var s = Split(order, rules);
        order.AssistantShare = s.Assistant;
        order.RunnerShare = s.Runner;
        order.MineShare = s.Mine;
        order.SplitKind = s.Kind;
    }

    public static Summary Summarize(IEnumerable<Order> orders, AppRules rules)
    {
        var s = new Summary();
        foreach (var o in orders)
        {
            var r = Split(o, rules);
            s.Orders++;
            s.Amount += o.Amount;
            s.Assistant += r.Assistant;
            s.Runner += r.Runner;
            s.Mine += r.Mine;
            if (o.Personal) s.PersonalOrders++; else s.RunnerOrders++;
            if (o.Status == OrderStatus.Completed) s.Completed++;
            if (OrderStatus.IsOpen(o.Status)) s.Open++;
        }
        s.Amount = Round2(s.Amount);
        s.Assistant = Round2(s.Assistant);
        s.Runner = Round2(s.Runner);
        s.Mine = Round2(s.Mine);
        return s;
    }

    public static double Round2(double v) => Math.Round(v + 1e-9, 2, MidpointRounding.AwayFromZero);
}
