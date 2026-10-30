using System.IO;
using System.Text.Json;
using OrderManager.Models;

namespace OrderManager.Services;

/// <summary>全局应用状态：数据库、规则、档案，以及数据变化通知。</summary>
public sealed class AppState
{
    public static AppState I { get; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public Db Db { get; }
    public AppRules Rules { get; private set; } = new();
    public AppProfile Profile { get; private set; } = new();
    public Appearance Appearance { get; private set; } = new();

    /// <summary>订单/跑手/价目/设置发生变化时触发，供各页面刷新。</summary>
    public event EventHandler? DataChanged;

    /// <summary>外观（背景图等）变化时触发，只刷新窗口背景，不重载数据。</summary>
    public event EventHandler? AppearanceChanged;

    private AppState()
    {
        // 数据放在 exe 同目录的 data 文件夹，便携、便于备份
        var dir = Path.Combine(AppContext.BaseDirectory, "data");
        Db = new Db(dir);
        Db.EnsureCreated();
        LoadSettings();

        if (Db.CountItems() == 0)
        {
            foreach (var item in SeedData.Items()) Db.InsertItem(item);
        }
        if (Db.ListRunners().Count == 0)
        {
            foreach (var runner in SeedData.Runners()) Db.InsertRunner(runner);
        }

        // 首次启动：从旧版工具（F:\鸣潮）自动导入历史数据
        FirstImport = Migrator.TryImportOnFirstRun(Db);
    }

    /// <summary>首次启动自动导入旧数据的结果（没有导入则为 null）。</summary>
    public Migrator.Report? FirstImport { get; private set; }

    public string DataDir => Db.DataDir;

    public void LoadSettings()
    {
        var rulesJson = Db.GetSetting("rules");
        if (!string.IsNullOrWhiteSpace(rulesJson))
        {
            try { Rules = JsonSerializer.Deserialize<AppRules>(rulesJson, Json) ?? new AppRules(); }
            catch { Rules = new AppRules(); }
        }

        var profileJson = Db.GetSetting("profile");
        if (!string.IsNullOrWhiteSpace(profileJson))
        {
            try { Profile = JsonSerializer.Deserialize<AppProfile>(profileJson, Json) ?? new AppProfile(); }
            catch { Profile = new AppProfile(); }
        }

        var shop = Db.GetSetting("shop_name");
        if (!string.IsNullOrWhiteSpace(shop)) Profile.ShopName = shop;

        var appearanceJson = Db.GetSetting("appearance");
        if (!string.IsNullOrWhiteSpace(appearanceJson))
        {
            try { Appearance = JsonSerializer.Deserialize<Appearance>(appearanceJson, Json) ?? new Appearance(); }
            catch { Appearance = new Appearance(); }
        }
    }

    public void SaveRules()
    {
        Db.SetSetting("rules", JsonSerializer.Serialize(Rules, Json));
        NotifyChanged();
    }

    public void SaveProfile()
    {
        Db.SetSetting("profile", JsonSerializer.Serialize(Profile, Json));
        Db.SetSetting("shop_name", Profile.ShopName);
        NotifyChanged();
    }

    /// <summary>保存外观设置并通知窗口立即换背景（不重载订单数据）。</summary>
    public void SaveAppearance(Appearance appearance)
    {
        Appearance = appearance.Clone();
        Db.SetSetting("appearance", JsonSerializer.Serialize(Appearance, Json));
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>把用户选的背景图复制进 data\images，返回稳定路径（便于备份/换电脑跟着走）。</summary>
    public string ImportBackgroundImage(string sourcePath)
    {
        var dir = Path.Combine(DataDir, "images");
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var name = "background-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ext;
        var target = Path.Combine(dir, name);
        File.Copy(sourcePath, target, true);
        return target;
    }

    /// <summary>
    /// 数据版本号：每次数据变动 +1。页面用它判断"切回来时数据有没有变"，
    /// 没变就不刷新，避免每次切页都查库重建列表。
    /// </summary>
    public int DataVersion { get; private set; }

    public void NotifyChanged()
    {
        DataVersion++;
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>读取订单并填充分成金额。</summary>
    public List<Order> LoadOrders(string? from = null, string? to = null, string? status = null, string? keyword = null)
    {
        var list = Db.ListOrders(from, to, status, keyword);
        foreach (var o in list) CalcService.Apply(o, Rules);
        return list;
    }

    public List<Order> LoadAllOrders()
    {
        var list = Db.ListOrders();
        foreach (var o in list) CalcService.Apply(o, Rules);
        return list;
    }

    public Summary Summarize(IEnumerable<Order> orders) => CalcService.Summarize(orders, Rules);
}
