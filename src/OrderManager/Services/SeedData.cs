namespace OrderManager.Services;

/// <summary>首次运行时写入的示例价目表（可在「价目」页随意增删改）。</summary>
public static class SeedData
{
    public static List<Models.Item> Items() => new()
    {
        // 一、托管服务
        new() { Name = "日常包月", Category = "托管服务", Price = 30, Note = "每日活跃+清体力" },
        new() { Name = "标准包月", Category = "托管服务", Price = 50, Note = "每日活跃+清体力+周度游历" },

        // 二、任务 & 活动
        new() { Name = "主线（每幕）", Category = "任务活动", Price = 30, Note = "10-30 块/幕" },
        new() { Name = "危行", Category = "任务活动", Price = 15, Note = "10-15 块/个" },
        new() { Name = "伴星", Category = "任务活动", Price = 15, Note = "" },
        new() { Name = "纪闻", Category = "任务活动", Price = 10, Note = "3-10 块/个" },

        // 三、地图满探索
        new() { Name = "煌陇（全探索）", Category = "地图满探索", Price = 350, Note = "前置危行打包+55" },
        new() { Name = "云陵谷", Category = "地图满探索", Price = 10, Note = "煌陇" },
        new() { Name = "今州城", Category = "地图满探索", Price = 20, Note = "煌陇" },
        new() { Name = "无明湾", Category = "地图满探索", Price = 20, Note = "煌陇" },
        new() { Name = "北落野", Category = "地图满探索", Price = 20, Note = "煌陇" },
        new() { Name = "怨鸟泽", Category = "地图满探索", Price = 45, Note = "煌陇" },
        new() { Name = "乘霄山", Category = "地图满探索", Price = 50, Note = "煌陇" },
        new() { Name = "中曲台地", Category = "地图满探索", Price = 50, Note = "煌陇" },
        new() { Name = "荒石高地", Category = "地图满探索", Price = 35, Note = "煌陇" },
        new() { Name = "虎口山脉", Category = "地图满探索", Price = 15, Note = "煌陇" },
        new() { Name = "归墟港市", Category = "地图满探索", Price = 45, Note = "煌陇" },
        new() { Name = "无光之森", Category = "地图满探索", Price = 40, Note = "煌陇" },

        new() { Name = "黑海岸群岛", Category = "地图满探索", Price = 15, Note = "黑海岸两图全包45" },
        new() { Name = "泰提斯之底", Category = "地图满探索", Price = 30, Note = "黑海岸两图全包45" },

        new() { Name = "黎那汐塔（全探索）", Category = "地图满探索", Price = 355, Note = "前置危行打包+80" },
        new() { Name = "拉古那城", Category = "地图满探索", Price = 0, Note = "黎那汐塔" },
        new() { Name = "拂风水畔", Category = "地图满探索", Price = 20, Note = "黎那汐塔" },
        new() { Name = "狄萨莱海脊", Category = "地图满探索", Price = 30, Note = "黎那汐塔" },
        new() { Name = "悲叹墓岛", Category = "地图满探索", Price = 25, Note = "黎那汐塔" },
        new() { Name = "赞悼圣迹", Category = "地图满探索", Price = 15, Note = "黎那汐塔" },
        new() { Name = "氯柔水境", Category = "地图满探索", Price = 20, Note = "黎那汐塔" },
        new() { Name = "槲生半岛", Category = "地图满探索", Price = 35, Note = "黎那汐塔" },
        new() { Name = "埃弗拉德金库", Category = "地图满探索", Price = 25, Note = "黎那汐塔" },
        new() { Name = "下层金库", Category = "地图满探索", Price = 20, Note = "黎那汐塔" },
        new() { Name = "黎乔利群岛", Category = "地图满探索", Price = 20, Note = "黎那汐塔" },
        new() { Name = "贝奥海域", Category = "地图满探索", Price = 20, Note = "黎那汐塔" },
        new() { Name = "阿维纽林", Category = "地图满探索", Price = 40, Note = "黎那汐塔" },
        new() { Name = "隐海试验场", Category = "地图满探索", Price = 20, Note = "黎那汐塔" },
        new() { Name = "七丘", Category = "地图满探索", Price = 60, Note = "黎那汐塔" },
        new() { Name = "桑古伊斯狩原", Category = "地图满探索", Price = 45, Note = "黎那汐塔" },

        new() { Name = "罗伊冰原（全探索）", Category = "地图满探索", Price = 360, Note = "前置危行打包+80" },
        new() { Name = "联运椎骨", Category = "地图满探索", Price = 15, Note = "罗伊冰原" },
        new() { Name = "牙列石壑", Category = "地图满探索", Price = 20, Note = "罗伊冰原" },
        new() { Name = "复生丘原", Category = "地图满探索", Price = 30, Note = "罗伊冰原" },
        new() { Name = "星炬学院", Category = "地图满探索", Price = 10, Note = "罗伊冰原" },
        new() { Name = "蚀刻平原", Category = "地图满探索", Price = 20, Note = "罗伊冰原" },
        new() { Name = "巨目远野", Category = "地图满探索", Price = 20, Note = "罗伊冰原" },
        new() { Name = "隐喙深腹", Category = "地图满探索", Price = 10, Note = "罗伊冰原" },
        new() { Name = "陷足流川", Category = "地图满探索", Price = 20, Note = "罗伊冰原" },
        new() { Name = "浮光林", Category = "地图满探索", Price = 30, Note = "罗伊冰原" },
        new() { Name = "冰原运输港", Category = "地图满探索", Price = 10, Note = "罗伊冰原" },
        new() { Name = "盲望之塌", Category = "地图满探索", Price = 30, Note = "罗伊冰原" },
        new() { Name = "加拉尔冠阶", Category = "地图满探索", Price = 20, Note = "罗伊冰原" },
        new() { Name = "元林遗址", Category = "地图满探索", Price = 30, Note = "罗伊冰原" },
        new() { Name = "覆海原", Category = "地图满探索", Price = 20, Note = "罗伊冰原" },
        new() { Name = "落日堤屿", Category = "地图满探索", Price = 30, Note = "罗伊冰原" },
        new() { Name = "恒黯之原", Category = "地图满探索", Price = 15, Note = "罗伊冰原" },
        new() { Name = "寂静断崖", Category = "地图满探索", Price = 15, Note = "罗伊冰原" },
        new() { Name = "封存地", Category = "地图满探索", Price = 15, Note = "罗伊冰原" },
    };

    public static List<Models.Runner> Runners() => new()
    {
        new() { Name = "小予", Rate = 0.8, Note = "跑手分成 85%（助手 15%）", Active = true },
    };
}
