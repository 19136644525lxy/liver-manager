using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OrderManager.Models;

namespace OrderManager.Services;

/// <summary>Wallpaper Engine（Steam AppID 431960）里的一件订阅壁纸。</summary>
public sealed class WeWallpaper
{
    private ImageSource? _thumbnail;

    /// <summary>Steam 创意工坊 ID（目录名）。</summary>
    public string Id { get; init; } = "";

    /// <summary>project.json 里的标题。</summary>
    public string Title { get; init; } = "";

    /// <summary>类型：image / video / scene / web。</summary>
    public string Kind { get; init; } = "image";

    /// <summary>类型的中文说明，界面直接显示。</summary>
    public string KindText => Kind switch
    {
        "video" => "视频",
        "scene" => "场景",
        "web" => "网页",
        _ => "图片",
    };

    public string DirPath { get; init; } = "";
    public string ProjectPath { get; init; } = "";

    /// <summary>缩略图路径（preview.jpg / preview.gif …）。</summary>
    public string PreviewPath { get; init; } = "";

    /// <summary>视频壁纸的视频文件；场景/网页类为空。</summary>
    public string MediaPath { get; init; } = "";

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Id : Title;

    /// <summary>缩略图（懒加载，界面绑定用）。</summary>
    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbnail is not null || string.IsNullOrWhiteSpace(PreviewPath) || !File.Exists(PreviewPath))
                return _thumbnail;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 320;
                image.UriSource = new Uri(PreviewPath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                _thumbnail = image;
            }
            catch
            {
                _thumbnail = null;
            }

            return _thumbnail;
        }
    }

    /// <summary>视频壁纸才有的动图，其它为 null。</summary>
    public bool HasVideo => !string.IsNullOrWhiteSpace(MediaPath) && File.Exists(MediaPath);
}

/// <summary>
/// 读取本机 Wallpaper Engine 订阅的壁纸（Steam 创意工坊 431960 目录），
/// 并支持通过 `wallpaper64.exe -control getWallpaper` 拿到当前正在播放的壁纸。
/// Wallpaper Engine 没有公开的"查账号订阅"接口，订阅关系只能从本机文件读出来。
/// </summary>
public static class WallpaperEngine
{
    private const string AppId = "431960";

    private static readonly object Gate = new();
    private static readonly Regex PathLine = new("\"path\"\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".mov", ".m4v", ".wmv"];

    private static bool _probed;
    private static string? _steamRoot;
    private static string? _workshopDir;
    private static string? _engineDir;
    private static List<WeWallpaper> _cache = [];
    private static bool _loaded;

    /// <summary>Steam 安装目录。</summary>
    public static string? SteamRoot
    {
        get
        {
            Probe();
            return _steamRoot;
        }
    }

    /// <summary>Wallpaper Engine 的 workshop 内容目录（...\steamapps\workshop\content\431960）。</summary>
    public static string? WorkshopDir
    {
        get
        {
            Probe();
            return _workshopDir;
        }
    }

    /// <summary>Wallpaper Engine 安装目录（...\steamapps\common\wallpaper_engine）。</summary>
    public static string? EngineDir
    {
        get
        {
            Probe();
            return _engineDir;
        }
    }

    /// <summary>本机是否找得到 Wallpaper Engine 的订阅目录。</summary>
    public static bool IsAvailable => !string.IsNullOrEmpty(WorkshopDir) && Directory.Exists(WorkshopDir);

    /// <summary>设置页顶部那行状态文字。</summary>
    public static string StatusText
    {
        get
        {
            Probe();
            if (!IsAvailable)
                return "没找到 Wallpaper Engine 的订阅目录（需要安装 Steam 版 Wallpaper Engine 并订阅过壁纸）。";

            var count = List().Count;
            var engine = string.IsNullOrEmpty(_engineDir) ? "未找到主程序" : "已安装";
            return $"已找到 {count} 张订阅壁纸 · 来源 {_workshopDir} · 主程序{engine}";
        }
    }

    /// <summary>列出所有订阅壁纸（按标题排序，结果带缓存）。</summary>
    public static IReadOnlyList<WeWallpaper> List(bool forceReload = false)
    {
        lock (Gate)
        {
            if (_loaded && !forceReload) return _cache;

            var list = new List<WeWallpaper>();
            Probe();
            var root = _workshopDir;

            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    try
                    {
                        var item = ReadWallpaper(dir);
                        if (item is not null) list.Add(item);
                    }
                    catch
                    {
                        // 单个壁纸目录读不了就跳过，不影响列表
                    }
                }
            }

            list.Sort((a, b) => string.Compare(a.DisplayTitle, b.DisplayTitle, StringComparison.CurrentCultureIgnoreCase));
            _cache = list;
            _loaded = true;
            return _cache;
        }
    }

    /// <summary>按 project.json 路径找一件壁纸（找不到返回 null）。</summary>
    public static WeWallpaper? Find(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) return null;

        var list = List();
        var hit = list.FirstOrDefault(x => string.Equals(x.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;

        // 设置里存的是旧路径（比如换过盘符），按 workshop id 目录名再找一次
        var id = Path.GetFileName(Path.GetDirectoryName(projectPath));
        if (string.IsNullOrEmpty(id)) return null;
        return list.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 把外观设置里的 WE 壁纸解析成可直接渲染的路径（支持"跟随当前桌面壁纸"）。
    /// 返回新的对象，不会修改传入的实例。
    /// </summary>
    public static Appearance Resolve(Appearance appearance)
        => Resolve(appearance, null);

    /// <summary>解析壁纸信息；传入 cacheDir 时会顺带找一下高清预览图缓存。</summary>
    public static Appearance Resolve(Appearance appearance, string? cacheDir)
    {
        var result = appearance.Clone();
        result.WeHiResPath = "";
        if (!appearance.FromWallpaperEngine) return result;

        WeWallpaper? wallpaper = null;

        if (appearance.WeFollowCurrent)
        {
            var current = CurrentWallpaperPath();
            wallpaper = ResolveFromPath(current);
        }

        wallpaper ??= Find(appearance.WeProjectPath);

        if (wallpaper is null)
        {
            // 兜底：设置里记录的路径若仍然有效，就直接用
            result.WeKind = string.IsNullOrWhiteSpace(result.WeKind) ? "image" : result.WeKind;
            return result;
        }

        result.WeTitle = wallpaper.Title;
        result.WeProjectPath = wallpaper.ProjectPath;
        result.WePreviewPath = wallpaper.PreviewPath;
        result.WeMediaPath = wallpaper.MediaPath;
        result.WeKind = wallpaper.Kind;

        if (!string.IsNullOrEmpty(cacheDir) && !wallpaper.HasVideo)
        {
            var cached = CachedHiResPreview(cacheDir, wallpaper.Id);
            if (cached is not null) result.WeHiResPath = cached;
        }

        return result;
    }

    /// <summary>本地是否已经缓存了这张壁纸的高清预览图。</summary>
    public static string? CachedHiResPreview(string cacheDir, string id)
    {
        if (string.IsNullOrWhiteSpace(cacheDir) || string.IsNullOrWhiteSpace(id)) return null;
        var path = Path.Combine(cacheDir, "images", "we", id + ".jpg");
        return File.Exists(path) && new FileInfo(path).Length > 20000 ? path : null;
    }

    /// <summary>
    /// 场景类壁纸本地只有 190~260px 的方形缩略图，铺满窗口会糊。
    /// 这里按创意工坊 ID 拉一次高清预览图（Steam 的 preview_url，通常 1920 宽）缓存到 data\images\we\&lt;id&gt;.jpg。
    /// </summary>
    public static string? EnsureHiResPreview(string id, string cacheDir)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(cacheDir)) return null;

        var cached = CachedHiResPreview(cacheDir, id);
        if (cached is not null) return cached;

        try
        {
            var url = FetchWorkshopPreviewUrl(id);
            if (string.IsNullOrWhiteSpace(url)) return null;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OrderManager/1.0 (+desktop app)");
            var bytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
            if (bytes.Length < 20000) return null;

            // Steam 上很多创意工坊预览也只有 192px（甚至是 GIF），比本地还糊就别用了
            var size = DecodePixelSize(bytes);
            if (size is null || size.Value.Width < 600) return null;

            var dir = Path.Combine(cacheDir, "images", "we");
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, id + ".jpg");
            File.WriteAllBytes(target, bytes);
            return target;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>不看扩展名，直接从字节流读出图片像素尺寸；解不开返回 null。</summary>
    private static (int Width, int Height)? DecodePixelSize(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0) return null;

            var frame = decoder.Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch
        {
            return null;
        }
    }
    private static string? FetchWorkshopPreviewUrl(string id)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["itemcount"] = "1",
                ["publishedfileids[0]"] = id,
            });
            using var response = client
                .PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", body)
                .GetAwaiter().GetResult();
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("response", out var resp) &&
                resp.TryGetProperty("publishedfiledetails", out var list) &&
                list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0 &&
                list[0].TryGetProperty("preview_url", out var preview))
            {
                return preview.GetString();
            }
        }
        catch
        {
            // 没网 / 接口变动都不影响主流程，回退到本地缩略图
        }

        return null;
    }

    /// <summary>拿到 Wallpaper Engine 当前正在播放的壁纸路径（可能返回 project.json / scene.pkg / 视频文件）。</summary>
    public static string? CurrentWallpaperPath()
    {
        Probe();

        // 1) 先读安装目录的 config.json：selectedwallpapers 里记录着每个显示器当前的壁纸，最快也最稳
        var fromConfig = CurrentFromConfig(_engineDir);
        if (!string.IsNullOrEmpty(fromConfig)) return fromConfig;

        // 2) 退回去问主程序：wallpaper64.exe -control getWallpaper
        var dir = _engineDir;
        if (string.IsNullOrEmpty(dir)) return null;

        foreach (var exe in new[] { "wallpaper64.exe", "wallpaper32.exe" })
        {
            var full = Path.Combine(dir, exe);
            if (!File.Exists(full)) continue;

            try
            {
                var psi = new ProcessStartInfo(full)
                {
                    Arguments = "-control getWallpaper",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi);
                if (process is null) continue;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(4000);

                var line = output
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .FirstOrDefault(x => x.Length > 0 && File.Exists(x));

                if (!string.IsNullOrEmpty(line)) return line;
            }
            catch
            {
                // 主程序没运行或没权限时静默失败，界面会提示未取到
            }
        }

        return null;
    }

    /// <summary>从 wallpaper_engine\config.json 的 selectedwallpapers 里取当前壁纸路径。</summary>
    private static string? CurrentFromConfig(string? engineDir)
    {
        if (string.IsNullOrEmpty(engineDir)) return null;

        var configFile = Path.Combine(engineDir, "config.json");
        if (!File.Exists(configFile)) return null;

        try
        {
            using var stream = File.OpenRead(configFile);
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            return FindSelectedWallpaper(json.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindSelectedWallpaper(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, "selectedwallpapers", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var monitor in property.Value.EnumerateObject())
                        {
                            if (monitor.Value.ValueKind == JsonValueKind.Object
                                && monitor.Value.TryGetProperty("file", out var file)
                                && file.ValueKind == JsonValueKind.String)
                            {
                                var path = file.GetString();
                                if (!string.IsNullOrWhiteSpace(path)) return path;
                            }
                        }
                    }

                    var hit = FindSelectedWallpaper(property.Value);
                    if (hit is not null) return hit;
                }

                break;

            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    var hit = FindSelectedWallpaper(child);
                    if (hit is not null) return hit;
                }

                break;
        }

        return null;
    }

    /// <summary>把任意文件路径（project.json / 视频 / 场景包）解析成订阅壁纸。</summary>
    public static WeWallpaper? ResolveFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var dir = File.Exists(path) ? Path.GetDirectoryName(path) : (Directory.Exists(path) ? path : null);
        if (string.IsNullOrEmpty(dir)) return null;

        // 场景包在子目录里时向上找 project.json
        var current = new DirectoryInfo(dir);
        for (var depth = 0; depth < 3 && current is not null; depth++)
        {
            var project = Path.Combine(current.FullName, "project.json");
            if (File.Exists(project))
            {
                var item = ReadWallpaper(current.FullName);
                if (item is not null) return item;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>重新扫描订阅目录。</summary>
    public static IReadOnlyList<WeWallpaper> Reload()
    {
        lock (Gate)
        {
            _loaded = false;
            _probed = false;
        }

        return List(true);
    }

    private static WeWallpaper? ReadWallpaper(string dir)
    {
        var projectFile = Path.Combine(dir, "project.json");
        if (!File.Exists(projectFile)) return null;

        string title = "";
        string type = "";
        string file = "";
        string preview = "";

        using (var stream = File.OpenRead(projectFile))
        using (var json = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            title = GetString(root, "title");
            type = GetString(root, "type");
            file = GetString(root, "file");
            preview = GetString(root, "preview");
        }

        var kind = type.ToLowerInvariant() switch
        {
            "video" => "video",
            "scene" => "scene",
            "web" => "web",
            "application" => "web",
            _ => "image",
        };

        // 有的壁纸同时带 192px 的 GIF 和 1024px 的 JPG，挑像素最大的那张，铺满窗口才不那么糊
        var previewPath = ResolveFile(dir, preview);
        previewPath = PickLargestImage(dir, previewPath) ?? previewPath;
        previewPath ??= FirstExisting(dir, ".jpg", ".jpeg", ".png", ".gif", ".bmp");

        var mediaPath = "";
        if (kind == "video")
        {
            mediaPath = ResolveFile(dir, file)
                        ?? VideoExtensions
                            .SelectMany(ext => Directory.EnumerateFiles(dir, "*" + ext, SearchOption.TopDirectoryOnly))
                            .FirstOrDefault() ?? "";
        }

        if (previewPath is null && mediaPath.Length == 0) return null;

        return new WeWallpaper
        {
            Id = Path.GetFileName(dir),
            Title = title,
            Kind = kind,
            DirPath = dir,
            ProjectPath = projectFile,
            PreviewPath = previewPath ?? "",
            MediaPath = mediaPath,
        };
    }

    /// <summary>在壁纸目录里挑出像素面积最大的图片文件（缩略图质量参差不齐，有的只有 160px）。</summary>
    private static string? PickLargestImage(string dir, string? declared)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(declared) && File.Exists(declared)) candidates.Add(declared);

        try
        {
            candidates.AddRange(Directory
                .EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            // 目录没权限就只看 project.json 里写的那张
        }

        string? best = null;
        var bestArea = 0;

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count == 0) continue;

                var frame = decoder.Frames[0];
                var area = frame.PixelWidth * frame.PixelHeight;
                if (area <= bestArea) continue;

                bestArea = area;
                best = path;
            }
            catch
            {
                // 解不开的图片直接跳过
            }
        }

        return best;
    }
    private static string GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string? ResolveFile(string dir, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        var combined = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(combined) ? combined : null;
    }

    private static string? FirstExisting(string dir, params string[] extensions)
    {
        foreach (var ext in extensions)
        {
            var hit = Directory.EnumerateFiles(dir, "*" + ext, SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (hit is not null) return hit;
        }

        return null;
    }

    private static void Probe()
    {
        if (_probed) return;
        _probed = true;

        try
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (!string.IsNullOrWhiteSpace(steam))
            {
                steam = steam.Replace('/', Path.DirectorySeparatorChar);
                if (Directory.Exists(steam)) _steamRoot = steam;
            }

            var roots = new List<string>();
            if (!string.IsNullOrEmpty(_steamRoot)) roots.Add(_steamRoot);
            roots.AddRange(ReadLibraryFolders(_steamRoot));

            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var workshop = Path.Combine(root, "steamapps", "workshop", "content", AppId);
                if (Directory.Exists(workshop))
                {
                    _workshopDir ??= workshop;
                }

                var engine = Path.Combine(root, "steamapps", "common", "wallpaper_engine");
                if (Directory.Exists(engine) && File.Exists(Path.Combine(engine, "wallpaper64.exe"))
                    || Directory.Exists(engine) && File.Exists(Path.Combine(engine, "wallpaper32.exe")))
                {
                    _engineDir ??= engine;
                }
            }
        }
        catch
        {
            // 读注册表失败就当作没装 Steam
        }
    }

    private static IEnumerable<string> ReadLibraryFolders(string? steamRoot)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(steamRoot)) return result;

        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return result;

        try
        {
            foreach (var line in File.ReadLines(vdf))
            {
                var match = PathLine.Match(line);
                if (!match.Success) continue;
                var path = match.Groups[1].Value.Replace("\\\\", "\\").Replace('/', Path.DirectorySeparatorChar);
                if (Directory.Exists(path) && !result.Contains(path, StringComparer.OrdinalIgnoreCase))
                    result.Add(path);
            }
        }
        catch
        {
            // 解析失败就当只有默认库
        }

        return result;
    }
}



