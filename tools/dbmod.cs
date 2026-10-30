#:package Microsoft.Data.Sqlite@9.0.0

using Microsoft.Data.Sqlite;

if (args.Length < 2)
{
    Console.WriteLine("usage: dbmod <db> <sql>");
    return 1;
}
using var conn = new SqliteConnection($"Data Source={args[0]}");
conn.Open();
using var cmd = conn.CreateCommand();
cmd.CommandText = args[1];
var affected = cmd.ExecuteNonQuery();
Console.WriteLine($"ok affected={affected}");
return 0;
