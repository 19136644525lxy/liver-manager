#:package Microsoft.Data.Sqlite@9.0.0

using Microsoft.Data.Sqlite;

var path = args.Length > 0 ? args[0] : @"F:\订单管理\backup-data-201809\app.db";
using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
conn.Open();
using var cmd = conn.CreateCommand();
cmd.CommandText = "select id, boss_name, phone, item, quantity, amount, personal, runner_name, status, note, order_date, start_date, end_date, created_at, updated_at from orders order by id desc limit 6";
using var r = cmd.ExecuteReader();
while (r.Read())
{
    var parts = new List<string>();
    for (int i = 0; i < r.FieldCount; i++)
    {
        var v = r.IsDBNull(i) ? "<NULL>" : r.GetValue(i)?.ToString() ?? "<null-str>";
        parts.Add($"{r.GetName(i)}={v}");
    }
    Console.WriteLine(string.Join(" | ", parts));
}
Console.WriteLine("--- count ---");
using var c2 = conn.CreateCommand();
c2.CommandText = "select count(*) from orders";
Console.WriteLine(c2.ExecuteScalar());
