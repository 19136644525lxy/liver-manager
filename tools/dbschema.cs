#:package Microsoft.Data.Sqlite@9.0.0
using Microsoft.Data.Sqlite;
using var c = new SqliteConnection($"Data Source={args[0]};Mode=ReadOnly");
c.Open();
using var cmd = c.CreateCommand();
cmd.CommandText = "select sql from sqlite_master where name='orders'";
Console.WriteLine(cmd.ExecuteScalar());
