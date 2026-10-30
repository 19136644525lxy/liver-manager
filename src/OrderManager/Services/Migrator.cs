using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OrderManager.Models;

namespace OrderManager.Services;

/// <summary>从旧版工具导入历史数据：F:\鸣潮\bills\*.json 与 F:\鸣潮\tools\日期记载\data.db。</summary>
public static class Migrator
{
    public const string LegacyBillsDir = @"F:\鸣潮\bills";
    public const string LegacyDateDb = @"F:\鸣潮\tools\日期记载\data.db";

    public sealed class Report
    {
        public int BillOrders;
        public int HostOrders;
        public int Checkins;
        public int Runners;
        public int Skipped;
        public readonly List<string> Errors = new();

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"导入旧账单订单：{BillOrders} 条");
            sb.AppendLine($"导入旧托管订单：{HostOrders} 条（含 {Checkins} 次打卡）");
            if (Runners > 0) sb.AppendLine($"补录跑手：{Runners} 位");
            if (Skipped > 0) sb.AppendLine($"跳过重复：{Skipped} 条");
            if (Errors.Count > 0) sb.AppendLine("异常：" + string.Join("；", Errors));
            return sb.ToString().TrimEnd();
        }

        public bool Any => BillOrders + HostOrders > 0;
    }

    /// <summary>首次启动时自动尝试导入（仅在没有任何订单时执行）。</summary>
    public static Report? TryImportOnFirstRun(Db db)
    {
        if (db.GetSetting("legacy_imported") == "1") return null;
        if (db.CountOrders() > 0)
        {
            db.SetSetting("legacy_imported", "1");
            return null;
        }

        var report = new Report();
        try
        {
            if (Directory.Exists(LegacyBillsDir)) Merge(report, ImportBills(db, LegacyBillsDir));
            if (File.Exists(LegacyDateDb)) Merge(report, ImportHostOrders(db, LegacyDateDb));
        }
        catch (Exception ex)
        {
            report.Errors.Add(ex.Message);
        }

        db.SetSetting("legacy_imported", "1");

        try
        {
            var logPath = Path.Combine(db.DataDir, "导入记录.txt");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{report.ToText()}\n\n", Encoding.UTF8);
        }
        catch { /* 日志失败不影响主流程 */ }

        return report.Any ? report : null;
    }

    private static void Merge(Report target, Report src)
    {
        target.BillOrders += src.BillOrders;
        target.HostOrders += src.HostOrders;
        target.Checkins += src.Checkins;
        target.Runners += src.Runners;
        target.Skipped += src.Skipped;
        target.Errors.AddRange(src.Errors);
    }

    /// <summary>导入按日保存的旧账单 JSON。</summary>
    public static Report ImportBills(Db db, string billsDir)
    {
        var report = new Report();
        if (!Directory.Exists(billsDir)) return report;

        var existing = db.ListOrders()
            .Select(o => o.BossName + "|" + o.OrderDate + "|" + o.Amount.ToString("0.00"))
            .ToHashSet();

        foreach (var file in Directory.GetFiles(billsDir, "*_bill.json").OrderBy(x => x))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
                var root = doc.RootElement;
                string date = root.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(date))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    date = name.Length >= 10 ? name[..10] : DateTime.Now.ToString("yyyy-MM-dd");
                }

                if (!root.TryGetProperty("orders", out var orders) || orders.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var o in orders.EnumerateArray())
                {
                    string boss = GetStr(o, "name");
                    if (string.IsNullOrWhiteSpace(boss)) continue;
                    double amount = GetDouble(o, "amount");
                    string key = boss + "|" + date + "|" + amount.ToString("0.00");
                    if (!existing.Add(key)) { report.Skipped++; continue; }

                    var order = new Order
                    {
                        BossName = boss,
                        Phone = GetStr(o, "phone"),
                        Item = "代肝服务",
                        Quantity = Math.Max(1, GetInt(o, "quantity", 1)),
                        Amount = amount,
                        Personal = GetBool(o, "personal"),
                        RunnerName = GetStr(o, "runner_name"),
                        Status = NormalizeStatus(GetStr(o, "status")),
                        Note = "旧账单导入",
                        OrderDate = date,
                        CreatedAt = GetStr(o, "created_at"),
                    };
                    db.InsertOrder(order);
                    report.BillOrders++;
                }
            }
            catch (Exception ex)
            {
                report.Errors.Add(Path.GetFileName(file) + "：" + ex.Message);
            }
        }
        return report;
    }

    /// <summary>导入旧「日期记载」托管订单与打卡记录。</summary>
    public static Report ImportHostOrders(Db db, string legacyDbPath)
    {
        var report = new Report();
        if (!File.Exists(legacyDbPath)) return report;

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = legacyDbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        };

        try
        {
            using var conn = new SqliteConnection(builder.ToString());
            conn.Open();

            var oldOrders = new List<(long Id, string Name, string Start, string End, int Months)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, name, startDate, endDate, monthCount FROM orders ORDER BY id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    oldOrders.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4)));
            }

            var idMap = new Dictionary<long, long>();
            var existing = db.ListOrders().Select(o => o.BossName + "|" + o.OrderDate).ToHashSet();

            foreach (var (oldId, name, start, end, months) in oldOrders)
            {
                if (!existing.Add(name + "|" + start)) { report.Skipped++; continue; }

                var order = new Order
                {
                    BossName = name,
                    Item = "托管服务",
                    Quantity = 1,
                    Amount = 0,
                    Personal = true,
                    Status = OrderStatus.InProgress,
                    Note = $"旧托管记录：{months} 个月（原始金额未记录）",
                    OrderDate = start,
                    StartDate = start,
                    EndDate = end,
                };
                long newId = db.InsertOrder(order);
                idMap[oldId] = newId;
                report.HostOrders++;
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT orderId, checkDate FROM checkins ORDER BY orderId, checkDate";
                using var r = cmd.ExecuteReader();
                var pairs = new List<(long, string)>();
                while (r.Read()) pairs.Add((r.GetInt64(0), r.GetString(1)));

                foreach (var (oldOrderId, date) in pairs)
                {
                    if (!idMap.TryGetValue(oldOrderId, out long newOrderId)) continue;
                    if (db.AddCheckin(newOrderId, date)) report.Checkins++;
                }
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add("旧托管库：" + ex.Message);
        }
        return report;
    }

    // ---------- JSON 取值小工具（旧文件字段可能缺失） ----------

    private static string GetStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double GetDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static int GetInt(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string NormalizeStatus(string status) =>
        OrderStatus.All.Contains(status) ? status : OrderStatus.Completed;
}
