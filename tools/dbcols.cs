#:package Microsoft.Data.Sqlite@9.0.0

using Microsoft.Data.Sqlite;

// 打印某张表的列定义，用来确认结构升级（例如价目表的 points 列是否已删除）
var path = args.Length > 0 ? args[0] : @"F:\订单管理\src\OrderManager\bin\Debug\net10.0-windows\data\app.db";
var table = args.Length > 1 ? args[1] : "items";

using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
conn.Open();
using var cmd = conn.CreateCommand();
cmd.CommandText = $"select name, type, \"notnull\", dflt_value from pragma_table_info('{table}')";
using var r = cmd.ExecuteReader();
while (r.Read())
{
    Console.WriteLine($"{r.GetString(0)} | {r.GetString(1)} | notnull={r.GetValue(2)} | default={(r.IsDBNull(3) ? "-" : r.GetValue(3))}");
}
