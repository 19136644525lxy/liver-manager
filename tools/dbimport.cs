#:package Microsoft.Data.Sqlite@9.0.0
using Microsoft.Data.Sqlite;

// 把某个 JSON 文件的内容写进 settings 表的指定键：dotnet run dbimport.cs -- <db> <key> <json文件>
// 用文件传值，避免 PowerShell 传参时双引号被吃掉的问题。
if (args.Length < 3)
{
    Console.WriteLine("usage: dbimport.cs <db> <settingsKey> <jsonFile>");
    return;
}

var dbPath = args[0];
var key = args[1];
var jsonPath = args[2];
var value = File.ReadAllText(jsonPath).Trim();

using var conn = new SqliteConnection($"Data Source={dbPath};Cache=Shared");
conn.Open();

using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "INSERT OR REPLACE INTO settings(key,value) VALUES($k,$v)";
    cmd.Parameters.AddWithValue("$k", key);
    cmd.Parameters.AddWithValue("$v", value);
    var affected = cmd.ExecuteNonQuery();
    Console.WriteLine($"{key} 已写入（{affected} 行），{value.Length} 字符");
}

using (var check = conn.CreateCommand())
{
    check.CommandText = "SELECT value FROM settings WHERE key=$k";
    check.Parameters.AddWithValue("$k", key);
    Console.WriteLine("回读：" + check.ExecuteScalar());
}
