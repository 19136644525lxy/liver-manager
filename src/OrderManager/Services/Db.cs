using System.IO;
using Microsoft.Data.Sqlite;
using OrderManager.Models;

namespace OrderManager.Services;

/// <summary>SQLite 数据访问层（单文件本地数据库，免安装、可直接备份）。</summary>
public sealed class Db
{
    private readonly string _connStr;

    public string DataDir { get; }
    public string DbPath { get; }

    public Db(string dataDir)
    {
        DataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        DbPath = Path.Combine(dataDir, "app.db");
        _connStr = new SqliteConnectionStringBuilder { DataSource = DbPath, Cache = SqliteCacheMode.Shared }.ToString();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connStr);
        c.Open();
        return c;
    }

    public void EnsureCreated()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS orders (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              boss_name   TEXT NOT NULL,
              phone       TEXT NOT NULL DEFAULT '',
              item        TEXT NOT NULL DEFAULT '',
              quantity    INTEGER NOT NULL DEFAULT 1,
              amount      REAL NOT NULL DEFAULT 0,
              personal    INTEGER NOT NULL DEFAULT 1,
              runner_name TEXT NOT NULL DEFAULT '',
              status      TEXT NOT NULL DEFAULT '进行中',
              note        TEXT NOT NULL DEFAULT '',
              order_date  TEXT NOT NULL,
              start_date  TEXT NOT NULL DEFAULT '',
              end_date    TEXT NOT NULL DEFAULT '',
              created_at  TEXT NOT NULL,
              updated_at  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS runners (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              name TEXT NOT NULL UNIQUE,
              rate REAL NOT NULL DEFAULT 0.8,
              note TEXT NOT NULL DEFAULT '',
              active INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS items (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              name TEXT NOT NULL,
              category TEXT NOT NULL DEFAULT '',
              price REAL NOT NULL DEFAULT 0,
              note TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS checkins (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              order_id INTEGER NOT NULL,
              date TEXT NOT NULL,
              UNIQUE(order_id, date)
            );
            CREATE TABLE IF NOT EXISTS settings (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_orders_date ON orders(order_date);
            CREATE INDEX IF NOT EXISTS idx_orders_status ON orders(status);
            CREATE INDEX IF NOT EXISTS idx_checkins_order ON checkins(order_id);
            """;
        cmd.ExecuteNonQuery();
        DropLegacyItemPointsColumn(c);
    }

    /// <summary>旧版本的价目表带 points（积分）列，新版本不再使用，老库升级时顺手删掉。</summary>
    private static void DropLegacyItemPointsColumn(SqliteConnection c)
    {
        try
        {
            using var check = c.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('items') WHERE name='points'";
            if (Convert.ToInt64(check.ExecuteScalar() ?? 0L) == 0) return;

            using var drop = c.CreateCommand();
            drop.CommandText = "ALTER TABLE items DROP COLUMN points";
            drop.ExecuteNonQuery();
        }
        catch
        {
            // 老版本 SQLite 不支持 DROP COLUMN 时忽略：多一列不影响使用
        }
    }

    // ---------------- 订单 ----------------

    public List<Order> ListOrders(string? from = null, string? to = null, string? status = null, string? keyword = null)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        var sql = "SELECT * FROM orders WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(from)) { sql += " AND order_date >= $from"; cmd.Parameters.AddWithValue("$from", from); }
        if (!string.IsNullOrWhiteSpace(to)) { sql += " AND order_date <= $to"; cmd.Parameters.AddWithValue("$to", to); }
        if (!string.IsNullOrWhiteSpace(status) && status != "全部") { sql += " AND status = $status"; cmd.Parameters.AddWithValue("$status", status); }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            sql += " AND (boss_name LIKE $kw OR item LIKE $kw OR phone LIKE $kw OR runner_name LIKE $kw OR note LIKE $kw)";
            cmd.Parameters.AddWithValue("$kw", $"%{keyword.Trim()}%");
        }
        sql += " ORDER BY order_date DESC, id DESC";
        cmd.CommandText = sql;
        var list = new List<Order>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(MapOrder(r));
        return list;
    }

    private static Order MapOrder(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        BossName = r.GetString(1),
        Phone = r.GetString(2),
        Item = r.GetString(3),
        Quantity = r.GetInt32(4),
        Amount = r.GetDouble(5),
        Personal = r.GetInt32(6) == 1,
        RunnerName = r.GetString(7),
        Status = r.GetString(8),
        Note = r.GetString(9),
        OrderDate = r.GetString(10),
        StartDate = r.GetString(11),
        EndDate = r.GetString(12),
        CreatedAt = r.GetString(13),
        UpdatedAt = r.GetString(14),
    };

    public Order? GetOrder(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM orders WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapOrder(r) : null;
    }

    public long InsertOrder(Order o)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        o.CreatedAt = string.IsNullOrWhiteSpace(o.CreatedAt) ? now : o.CreatedAt;
        o.UpdatedAt = now;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO orders (boss_name, phone, item, quantity, amount, personal, runner_name, status, note,
                                order_date, start_date, end_date, created_at, updated_at)
            VALUES ($b,$p,$i,$q,$a,$pe,$rn,$st,$no,$od,$sd,$ed,$ca,$ua);
            SELECT last_insert_rowid();
            """;
        AddOrderParams(cmd, o);
        cmd.Parameters.AddWithValue("$ca", o.CreatedAt);
        cmd.Parameters.AddWithValue("$ua", o.UpdatedAt);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void UpdateOrder(Order o)
    {
        o.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE orders SET boss_name=$b, phone=$p, item=$i, quantity=$q, amount=$a, personal=$pe,
                runner_name=$rn, status=$st, note=$no, order_date=$od, start_date=$sd, end_date=$ed, updated_at=$ua
            WHERE id=$id
            """;
        AddOrderParams(cmd, o);
        cmd.Parameters.AddWithValue("$ua", o.UpdatedAt);
        cmd.Parameters.AddWithValue("$id", o.Id);
        cmd.ExecuteNonQuery();
    }

    private static void AddOrderParams(SqliteCommand cmd, Order o)
    {
        cmd.Parameters.AddWithValue("$b", o.BossName);
        cmd.Parameters.AddWithValue("$p", o.Phone ?? "");
        cmd.Parameters.AddWithValue("$i", o.Item ?? "");
        cmd.Parameters.AddWithValue("$q", o.Quantity);
        cmd.Parameters.AddWithValue("$a", o.Amount);
        cmd.Parameters.AddWithValue("$pe", o.Personal ? 1 : 0);
        cmd.Parameters.AddWithValue("$rn", o.RunnerName ?? "");
        cmd.Parameters.AddWithValue("$st", o.Status);
        cmd.Parameters.AddWithValue("$no", o.Note ?? "");
        cmd.Parameters.AddWithValue("$od", o.OrderDate);
        cmd.Parameters.AddWithValue("$sd", o.StartDate ?? "");
        cmd.Parameters.AddWithValue("$ed", o.EndDate ?? "");
    }

    public void DeleteOrder(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM checkins WHERE order_id=$id; DELETE FROM orders WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetStatus(long id, string status)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE orders SET status=$s, updated_at=$u WHERE id=$id";
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$u", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ---------------- 跑手 ----------------

    public List<Runner> ListRunners()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,rate,note,active FROM runners ORDER BY active DESC, id ASC";
        var list = new List<Runner>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Runner { Id = r.GetInt64(0), Name = r.GetString(1), Rate = r.GetDouble(2), Note = r.GetString(3), Active = r.GetInt32(4) == 1 });
        return list;
    }

    public Runner InsertRunner(Runner x)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO runners(name,rate,note,active) VALUES($n,$r,$o,$a); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", x.Name);
        cmd.Parameters.AddWithValue("$r", x.Rate);
        cmd.Parameters.AddWithValue("$o", x.Note ?? "");
        cmd.Parameters.AddWithValue("$a", x.Active ? 1 : 0);
        x.Id = (long)(cmd.ExecuteScalar() ?? 0L);
        return x;
    }

    public void UpdateRunner(Runner x)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE runners SET name=$n, rate=$r, note=$o, active=$a WHERE id=$id";
        cmd.Parameters.AddWithValue("$n", x.Name);
        cmd.Parameters.AddWithValue("$r", x.Rate);
        cmd.Parameters.AddWithValue("$o", x.Note ?? "");
        cmd.Parameters.AddWithValue("$a", x.Active ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", x.Id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteRunner(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM runners WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ---------------- 价目表 ----------------

    public List<Item> ListItems()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,category,price,note FROM items ORDER BY category, id";
        var list = new List<Item>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Item { Id = r.GetInt64(0), Name = r.GetString(1), Category = r.GetString(2), Price = r.GetDouble(3), Note = r.GetString(4) });
        return list;
    }

    public Item InsertItem(Item x)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO items(name,category,price,note) VALUES($n,$c,$p,$o); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", x.Name);
        cmd.Parameters.AddWithValue("$c", x.Category ?? "");
        cmd.Parameters.AddWithValue("$p", x.Price);
        cmd.Parameters.AddWithValue("$o", x.Note ?? "");
        x.Id = (long)(cmd.ExecuteScalar() ?? 0L);
        return x;
    }

    public void UpdateItem(Item x)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE items SET name=$n, category=$c, price=$p, note=$o WHERE id=$id";
        cmd.Parameters.AddWithValue("$n", x.Name);
        cmd.Parameters.AddWithValue("$c", x.Category ?? "");
        cmd.Parameters.AddWithValue("$p", x.Price);
        cmd.Parameters.AddWithValue("$o", x.Note ?? "");
        cmd.Parameters.AddWithValue("$id", x.Id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteItem(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM items WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public int CountItems()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM items";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ---------------- 托管打卡 ----------------

    public HashSet<string> ListCheckinDates(long orderId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT date FROM checkins WHERE order_id=$id";
        cmd.Parameters.AddWithValue("$id", orderId);
        var set = new HashSet<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    /// <summary>切换打卡状态，返回切换后是否已打卡。</summary>
    public bool ToggleCheckin(long orderId, string date)
    {
        using var c = Open();
        using var del = c.CreateCommand();
        del.CommandText = "DELETE FROM checkins WHERE order_id=$id AND date=$d";
        del.Parameters.AddWithValue("$id", orderId);
        del.Parameters.AddWithValue("$d", date);
        int removed = del.ExecuteNonQuery();
        if (removed > 0) return false;
        using var ins = c.CreateCommand();
        ins.CommandText = "INSERT INTO checkins(order_id,date) VALUES($id,$d)";
        ins.Parameters.AddWithValue("$id", orderId);
        ins.Parameters.AddWithValue("$d", date);
        ins.ExecuteNonQuery();
        return true;
    }

    /// <summary>添加打卡记录（已存在则不重复添加），返回是否新增成功。</summary>
    public bool AddCheckin(long orderId, string date)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO checkins(order_id,date) VALUES($id,$d)";
        cmd.Parameters.AddWithValue("$id", orderId);
        cmd.Parameters.AddWithValue("$d", date);
        return cmd.ExecuteNonQuery() > 0;
    }

    public int CountOrders()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM orders";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public Dictionary<long, HashSet<string>> AllCheckins()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT order_id, date FROM checkins";
        var dict = new Dictionary<long, HashSet<string>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long id = r.GetInt64(0);
            if (!dict.TryGetValue(id, out var set)) dict[id] = set = new HashSet<string>();
            set.Add(r.GetString(1));
        }
        return dict;
    }

    // ---------------- 设置 ----------------

    public string? GetSetting(string key)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }
}
