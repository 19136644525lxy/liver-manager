#:package Microsoft.Data.Sqlite@9.0.0
using Microsoft.Data.Sqlite;

// 简易查询工具：dotnet run dbq.cs -- <db> "<sql>"  —— 打印结果集所有行
if (args.Length < 2)
{
    Console.WriteLine("usage: dbq.cs <db> <sql>");
    return;
}

var path = args[0];
var sql = args[1];

using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Cache=Shared");
conn.Open();
using var cmd = conn.CreateCommand();
cmd.CommandText = sql;
using var reader = cmd.ExecuteReader();

var names = new string[reader.FieldCount];
for (int i = 0; i < reader.FieldCount; i++) names[i] = reader.GetName(i);
Console.WriteLine(string.Join(" | ", names));

int count = 0;
while (reader.Read())
{
    var cells = new string[reader.FieldCount];
    for (int i = 0; i < reader.FieldCount; i++)
        cells[i] = reader.IsDBNull(i) ? "<null>" : reader.GetValue(i)?.ToString() ?? "";
    Console.WriteLine(string.Join(" | ", cells));
    count++;
}
Console.WriteLine($"--- {count} row(s) ---");
