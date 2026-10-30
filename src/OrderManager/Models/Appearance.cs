using System.IO;

namespace OrderManager.Models;

/// <summary>背景来源：静态图片，或 Wallpaper Engine 里订阅的壁纸。</summary>
public static class BackgroundSource
{
    public const string Image = "image";
    public const string WallpaperEngine = "we";
}

/// <summary>外观偏好：自定义背景图 / Wallpaper Engine 壁纸、蒙层与模糊，存于 settings 表的 appearance 键。</summary>
public sealed class Appearance
{
    /// <summary>背景来源，见 <see cref="BackgroundSource"/>。</summary>
    public string Source { get; set; } = BackgroundSource.Image;

    /// <summary>背景图绝对路径，空字符串表示不使用背景图。</summary>
    public string BackgroundPath { get; set; } = "";

    /// <summary>背景图之上的白色蒙层浓度（0-100），越高文字越清晰、图越淡。</summary>
    public int OverlayOpacity { get; set; } = 78;

    /// <summary>背景图铺法：UniformToFill / Uniform / Fill / Tile。</summary>
    public string Stretch { get; set; } = "UniformToFill";

    /// <summary>背景图模糊半径（0-30），0 表示不模糊；视频壁纸不支持模糊。</summary>
    public int BlurRadius { get; set; }

    // ===== Wallpaper Engine 壁纸 =====

    /// <summary>订阅壁纸的标题（仅用于设置页显示）。</summary>
    public string WeTitle { get; set; } = "";

    /// <summary>壁纸的 project.json 绝对路径，是这台机器上定位壁纸的唯一标识。</summary>
    public string WeProjectPath { get; set; } = "";

    /// <summary>壁纸缩略图路径（preview.jpg / preview.gif 等）。</summary>
    public string WePreviewPath { get; set; } = "";

    /// <summary>视频壁纸的视频文件路径；场景/网页类壁纸为空。</summary>
    public string WeMediaPath { get; set; } = "";

    /// <summary>壁纸类型：image / video / scene / web。</summary>
    public string WeKind { get; set; } = "";

    /// <summary>跟随 Wallpaper Engine 当前正在播放的壁纸（每次切回窗口时重新取一次）。</summary>
    public bool WeFollowCurrent { get; set; }

    /// <summary>从 Steam 创意工坊抓到的高清预览图缓存路径（场景类壁纸用，比本地 254px 缩略图清晰）。</summary>
    public string WeHiResPath { get; set; } = "";

    /// <summary>通透模式：卡片/侧栏/输入框变成近乎透明，只留文字和 1px 轮廓，让壁纸整块透出来。</summary>
    public bool GlassMode { get; set; }

    /// <summary>动态壁纸（实验）：场景类壁纸按帧渲染，让图层动画和粒子动起来。</summary>
    public bool LiveWallpaper { get; set; }

    /// <summary>动态壁纸渲染比例（相对窗口像素），100 = 1:1 最清晰，70 = 更流畅。</summary>
    public int LiveQuality { get; set; } = 85;

    /// <summary>是否使用 Wallpaper Engine 壁纸作为背景。</summary>
    public bool FromWallpaperEngine => string.Equals(Source, BackgroundSource.WallpaperEngine, StringComparison.OrdinalIgnoreCase);

    /// <summary>视频壁纸是否可以播放。</summary>
    public bool HasVideo => !string.IsNullOrWhiteSpace(WeMediaPath)
                            && File.Exists(WeMediaPath)
                            && !string.Equals(WeKind, "scene", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(WeKind, "web", StringComparison.OrdinalIgnoreCase);

    /// <summary>实际用来铺背景的图片路径（图片壁纸本身，或 WE 壁纸的高清预览/缩略图）。</summary>
    public string ResolvedImagePath
    {
        get
        {
            if (!FromWallpaperEngine) return BackgroundPath;
            if (!string.IsNullOrWhiteSpace(WeHiResPath) && File.Exists(WeHiResPath)) return WeHiResPath;
            return WePreviewPath;
        }
    }

    /// <summary>是否有可用的背景（图片存在，或视频壁纸存在）。</summary>
    public bool HasBackground
    {
        get
        {
            if (FromWallpaperEngine)
            {
                if (HasVideo) return true;
                if (!string.IsNullOrWhiteSpace(WeHiResPath) && File.Exists(WeHiResPath)) return true;
                return !string.IsNullOrWhiteSpace(WePreviewPath) && File.Exists(WePreviewPath);
            }

            return !string.IsNullOrWhiteSpace(BackgroundPath) && File.Exists(BackgroundPath);
        }
    }

    public Appearance Clone() => new()
    {
        Source = Source,
        BackgroundPath = BackgroundPath,
        OverlayOpacity = OverlayOpacity,
        Stretch = Stretch,
        BlurRadius = BlurRadius,
        WeTitle = WeTitle,
        WeProjectPath = WeProjectPath,
        WePreviewPath = WePreviewPath,
        WeMediaPath = WeMediaPath,
        WeKind = WeKind,
        WeFollowCurrent = WeFollowCurrent,
        WeHiResPath = WeHiResPath,
        GlassMode = GlassMode,
        LiveWallpaper = LiveWallpaper,
    };
}
