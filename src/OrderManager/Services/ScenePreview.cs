using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SceneRender;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace OrderManager.Services;

/// <summary>
/// 把 Wallpaper Engine 的场景壁纸（scene.pkg）离线渲染成一张高清静态图，当作窗口背景。
/// 创意工坊只提供 254~1079px 的封面图，直接用会糊；这里按窗口实际像素尺寸真渲染一次并缓存，
/// 之后切回来就是秒开；窗口变大了再按新尺寸重渲染（也就是"自适应"）。
/// </summary>
public static class ScenePreview
{
    /// <summary>缓存目录：&lt;数据目录&gt;\images\scene。</summary>
    public static string CacheRoot => Path.Combine(AppState.I.DataDir, "images", "scene");

    /// <summary>渲染上限 4K：高 DPI 缩放时窗口像素宽度可能超过 3000，这里给到 3840 保证清晰。</summary>
    public const int MaxWidth = 3840;
    public const int MaxHeight = 2160;

    /// <summary>同一张壁纸最多保留几个尺寸的缓存（大屏切小屏时免得重复渲染，又不会无限堆）。</summary>
    private const int MaxCacheFilesPerWallpaper = 3;

    /// <summary>缓存超过这个天数没用过就删除。</summary>
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(30);

    private static bool _cleanupStarted;

    /// <summary>低于这个宽度就没必要渲染了（还不如封面清楚）。</summary>
    public const int MinWidth = 960;

    private static readonly Regex CacheName = new(@"^(?<id>\d+)_(?<w>\d+)x(?<h>\d+)\.jpg$", RegexOptions.Compiled);

    /// <summary>ffmpeg 路径（视频贴图抽帧用，找不到就用 PATH 里的 ffmpeg）。</summary>
    public static string? FfmpegPath() => ResolveFfmpeg();

    /// <summary>找一张已经渲染好、且不小于所需尺寸的缓存图（在够大的里面挑面积最小的）。</summary>
    public static string? FindCached(string id, int needWidth, int needHeight)
    {
        try
        {
            if (!Directory.Exists(CacheRoot)) return null;
            string? best = null;
            var bestArea = int.MaxValue;
            foreach (var file in Directory.EnumerateFiles(CacheRoot, id + "_*.jpg"))
            {
                var match = CacheName.Match(Path.GetFileName(file));
                if (!match.Success) continue;
                if (!int.TryParse(match.Groups["w"].Value, out var w)) continue;
                if (!int.TryParse(match.Groups["h"].Value, out var h)) continue;
                if (w < needWidth || h < needHeight) continue;
                var area = w * h;
                if (area >= bestArea) continue;
                best = file;
                bestArea = area;
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    public static string CachePath(string id, int width, int height) =>
        Path.Combine(CacheRoot, $"{id}_{width}x{height}.jpg");

    /// <summary>把窗口客户区尺寸换算成要渲染的像素尺寸（对齐到 8 的倍数，限制上下限）。</summary>
    public static (int Width, int Height) TargetSize(int pixelWidth, int pixelHeight)
    {
        var w = Math.Clamp(pixelWidth, MinWidth, MaxWidth);
        var h = Math.Max(1, (int)Math.Round(w * (double)pixelHeight / Math.Max(1, pixelWidth)));
        if (h > MaxHeight)
        {
            h = MaxHeight;
            w = Math.Max(1, (int)Math.Round(h * (double)pixelWidth / Math.Max(1, pixelHeight)));
        }
        w = Math.Max(64, w / 8 * 8);
        h = Math.Max(64, h / 8 * 8);
        return (w, h);
    }

    /// <summary>
    /// 渲染（或复用缓存），返回 JPEG 路径；失败返回 null（调用方继续用封面图）。
    /// 必须在后台线程调用：内部是同步的重活，单个 1~3 秒、峰值内存几十到几百 MB。
    /// </summary>
    public static string? Render(string id, string scenePkgPath, int width, int height)
    {
        var target = CachePath(id, width, height);
        if (File.Exists(target) && new FileInfo(target).Length > 1024) return target;

        Directory.CreateDirectory(CacheRoot);
        var tempRoot = Path.Combine(Path.GetTempPath(), "dg-scene");

        try
        {
            using var package = ScenePackage.Open(scenePkgPath);
            var json = package.TryReadText("scene.json");
            if (string.IsNullOrWhiteSpace(json)) return null;
            var scene = SceneDoc.Parse(json);

            var options = new SceneRender.RenderOptions
            {
                Width = width,
                Height = height,
                FfmpegPath = ResolveFfmpeg(),
                // 视频贴图抽的帧放在数据目录里，别污染程序目录
                CacheDir = Path.Combine(AppState.I.DataDir, "images", "frames"),
            };
            using var renderer = new Renderer(package, scene, options);
            using var image = renderer.Render();

            var partial = target + ".part";
            image.SaveAsJpeg(partial, new JpegEncoder { Quality = 92 });
            if (File.Exists(target)) File.Delete(target);
            File.Move(partial, target);
            return target;
        }
        catch
        {
            return null;
        }
        finally
        {
            // 视频贴图抽帧会临时写 mp4/png，单个包可能几百 MB，渲染完立刻清掉
            TryCleanTemp(tempRoot);
        }
    }

    /// <summary>找 ffmpeg：程序目录 → 常见安装位置 → PATH。只有含内嵌视频贴图的场景需要它。</summary>
    private static string? ResolveFfmpeg()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;

        string[] candidates =
        {
            @"F:\ffmpeg\ffmpeg-8.0.1-essentials_build\bin\ffmpeg.exe",
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return "ffmpeg";
    }

    private static void TryCleanTemp(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromMinutes(10))
                        File.Delete(file);
                }
                catch
                {
                    // 忽略：删不掉就留着
                }
            }
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>后台清理一次缓存（每次启动只跑一次）：删掉 30 天没用过的图，同一张壁纸只留最大的 3 个尺寸。</summary>
    public static void CleanupOnce()
    {
        if (_cleanupStarted) return;
        _cleanupStarted = true;
        try
        {
            CleanupCache();
        }
        catch
        {
            // 清理失败不影响使用
        }
    }

    /// <summary>执行缓存清理，返回删掉的文件数。</summary>
    public static int CleanupCache()
    {
        if (!Directory.Exists(CacheRoot)) return 0;

        var removed = 0;
        var now = DateTime.UtcNow;
        var byWallpaper = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(CacheRoot, "*.jpg"))
        {
            FileInfo info;
            try { info = new FileInfo(path); }
            catch { continue; }

            var match = CacheName.Match(info.Name);
            if (!match.Success || info.Length < 1024)
            {
                // 渲染中断留下的半成品直接清掉
                TryDelete(info, ref removed);
                continue;
            }

            if (now - info.LastWriteTimeUtc > CacheMaxAge)
            {
                TryDelete(info, ref removed);
                continue;
            }

            var id = match.Groups["id"].Value;
            if (!byWallpaper.TryGetValue(id, out var list))
            {
                list = new List<FileInfo>();
                byWallpaper[id] = list;
            }
            list.Add(info);
        }

        foreach (var files in byWallpaper.Values)
        {
            if (files.Count <= MaxCacheFilesPerWallpaper) continue;
            foreach (var info in files
                         .OrderByDescending(f => f.Length)
                         .ThenByDescending(f => f.LastWriteTimeUtc)
                         .Skip(MaxCacheFilesPerWallpaper))
            {
                TryDelete(info, ref removed);
            }
        }

        return removed;
    }

    private static void TryDelete(FileInfo info, ref int removed)
    {
        try
        {
            info.Delete();
            removed++;
        }
        catch
        {
            // 正在被占用就下回再删
        }
    }

    /// <summary>把 JPEG 读成冻结的 BitmapImage（可跨线程使用）。</summary>
    public static BitmapImage? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
