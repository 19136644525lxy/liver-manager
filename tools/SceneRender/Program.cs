using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace SceneRender;

internal static class Program
{
    private const string ExeName = "SceneRender";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length == 0) return Usage(null);
            switch (args[0].ToLowerInvariant())
            {
                case "list": return CmdList(args);
                case "tex": return CmdTex(args);
                case "info": return CmdInfo(args);
                case "render": return CmdRender(args);
                case "main": return CmdMain(args);
                case "dump": return CmdDump(args);
                case "bench": return CmdBench(args);
                case "stats": return CmdStats(args);
                case "cat": return CmdCat(args);
                case "live": return CmdLive(args);
                case "-h":
                case "--help":
                case "help": return Usage(null);
                default:
                    Console.Error.WriteLine($"未知命令: {args[0]}");
                    return Usage(args[0]);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[错误] {ex.GetType().Name}: {ex.Message}");
            if (ex.StackTrace != null && Environment.GetEnvironmentVariable("SCENERENDER_TRACE") == "1")
                Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static int Usage(string? bad)
    {
        if (bad != null) Console.Error.WriteLine();
        Console.Error.WriteLine($"SceneRender — Wallpaper Engine scene.pkg 渲染工具");
        Console.Error.WriteLine();
        Console.Error.WriteLine("用法:");
        Console.Error.WriteLine("  dotnet run -- list  <scene.pkg> [--all]");
        Console.Error.WriteLine("  dotnet run -- tex   <scene.pkg> <包内条目名> <out.png> [--time=1.0] [--ffmpeg=路径]");
        Console.Error.WriteLine("  dotnet run -- tex   <单文件.tex> <out.png> [--time=1.0] [--ffmpeg=路径]");
        Console.Error.WriteLine("  dotnet run -- info  <scene.pkg>");
        Console.Error.WriteLine("  dotnet run -- render <scene.pkg> <宽> <高> <out.png|out.jpg> [--time=1.0] [--debug] [--verbose] [--nozoom] [--text] [--ffmpeg=路径] [--cache=目录]");
        Console.Error.WriteLine("  dotnet run -- main  <scene.pkg> <out.png|out.jpg> [--time=1.0] [--ffmpeg=路径]   # 退路：导出视觉主体贴图原始分辨率");
        Console.Error.WriteLine("  dotnet run -- dump  <scene.pkg> <包内条目名> <输出文件>");
        Console.Error.WriteLine();
        Console.Error.WriteLine("通用选项: --time=秒（动画/视频取帧时间）, --debug, --verbose, --ffmpeg=可执行文件路径, --cache=缓存目录");
        return 2;
    }

    // ---------------------------------------------------------------- 参数

    private sealed class CliOpts
    {
        public double Time = 1.0;
        public bool Debug;
        public bool Verbose;
        public bool NoZoom;
        public bool IncludeText;
        public bool All;
        public string? Ffmpeg;
        public string? Cache;
        public List<string> Positional = new();

        public static CliOpts Parse(IEnumerable<string> args, int start)
        {
            var o = new CliOpts();
            foreach (var raw in args.Skip(start))
            {
                if (raw.StartsWith("--"))
                {
                    string a = raw[2..];
                    int eq = a.IndexOf('=');
                    string key = eq >= 0 ? a[..eq] : a;
                    string? val = eq >= 0 ? a[(eq + 1)..] : null;
                    switch (key.ToLowerInvariant())
                    {
                        case "time":
                            if (val != null) o.Time = double.Parse(val, CultureInfo.InvariantCulture);
                            break;
                        case "debug": o.Debug = true; break;
                        case "verbose": o.Verbose = true; break;
                        case "nozoom": o.NoZoom = true; break;
                        case "text": o.IncludeText = true; break;
                        case "all": o.All = true; break;
                        case "ffmpeg": o.Ffmpeg = val; break;
                        case "cache": o.Cache = val; break;
                        default: Console.Error.WriteLine($"[警告] 未知选项 --{key}"); break;
                    }
                }
                else o.Positional.Add(raw);
            }
            return o;
        }
    }

    private static string ResolveFfmpeg(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return explicitPath!;
        string[] candidates =
        {
            @"F:\ffmpeg\ffmpeg-8.0.1-essentials_build\bin\ffmpeg.exe",
        };
        foreach (var c in candidates) if (File.Exists(c)) return c;
        return "ffmpeg";
    }

    private static RenderOptions MakeOptions(CliOpts cli) => new()
    {
        Time = cli.Time,
        Debug = cli.Debug,
        Verbose = cli.Verbose,
        IncludeText = cli.IncludeText,
        IgnoreZoom = cli.NoZoom,
        FfmpegPath = ResolveFfmpeg(cli.Ffmpeg),
        CacheDir = cli.Cache ?? Path.Combine(AppContext.BaseDirectory, "_cache"),
    };

    // ---------------------------------------------------------------- list

    private static int CmdList(string[] args)
    {
        var cli = CliOpts.Parse(args, 1);
        if (cli.Positional.Count < 1) return Usage("list 缺少参数");
        using var pkg = ScenePackage.Open(cli.Positional[0]);
        Console.WriteLine($"PKG: {pkg.FilePath}");
        Console.WriteLine($"magic={pkg.Magic}  entries={pkg.Entries.Count}  dataStart={pkg.DataStart}  fileSize={pkg.FileSize:N0}");
        Console.WriteLine();
        Console.WriteLine($"{"#",4}  {"name",-58} {"offset",12} {"length",12}");
        long align = 0;
        foreach (var e in pkg.Entries)
        {
            if (align != e.Offset) Console.Error.WriteLine($"[警告] offset 不连续: 期望 {align} 实际 {e.Offset} ({e.Name})");
            align = e.Offset + e.Length;
        }
        if (align != pkg.FileSize - pkg.DataStart)
            Console.Error.WriteLine($"[警告] 条目总长度 {align} != 文件剩余 {pkg.FileSize - pkg.DataStart}");
        int show = cli.All ? pkg.Entries.Count : Math.Min(20, pkg.Entries.Count);
        for (int i = 0; i < show; i++)
        {
            var e = pkg.Entries[i];
            Console.WriteLine($"{i,4}  {e.Name,-58} {e.Offset,12} {e.Length,12}");
        }
        if (show < pkg.Entries.Count) Console.WriteLine($"... 其余 {pkg.Entries.Count - show} 条，用 --all 查看");
        return 0;
    }

    // ---------------------------------------------------------------- tex

    private static int CmdTex(string[] args)
    {
        var cli = CliOpts.Parse(args, 1);
        string input, outPath, entry = "";
        if (cli.Positional.Count == 3 && cli.Positional[0].EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
        {
            input = cli.Positional[0];
            entry = cli.Positional[1];
            outPath = cli.Positional[2];
        }
        else if (cli.Positional.Count == 2)
        {
            input = cli.Positional[0];
            outPath = cli.Positional[1];
        }
        else return Usage("tex 参数错误");

        byte[] texBytes;
        string displayName;
        if (entry.Length > 0)
        {
            using var pkg = ScenePackage.Open(input);
            var e = pkg.Find(entry) ?? throw new FileNotFoundException($"包内找不到条目 {entry}");
            texBytes = pkg.ReadEntryBytes(e);
            displayName = e.Name;
        }
        else
        {
            texBytes = File.ReadAllBytes(input);
            displayName = Path.GetFileName(input);
        }

        var tex = TexFile.FromBytes(texBytes);
        Console.WriteLine($"TEX: {displayName}");
        Console.WriteLine($"  version={tex.Version}/{tex.ImageVersion} container={tex.ContainerMagic} imageFormat={tex.ContainerImageFormat} videoFlag={tex.VideoFlag}");
        Console.WriteLine($"  format={tex.Format} flags={tex.Flags} texture={tex.TextureWidth}x{tex.TextureHeight} image={tex.ImageWidth}x{tex.ImageHeight} mipmaps={tex.Images[0].Mipmaps.Count}");
        for (int i = 0; i < Math.Min(4, tex.Images[0].Mipmaps.Count); i++)
            Console.WriteLine($"  mip{i}: {tex.Images[0].Mipmaps[i]}");

        Image<Rgba32> image;
        try
        {
            image = tex.DecodeImage();
        }
        catch (VideoTextureException vex)
        {
            string dir = string.IsNullOrEmpty(cli.Cache) ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outPath))!, "_cache") : cli.Cache!;
            Directory.CreateDirectory(dir);
            string mp4 = Path.Combine(dir, "tex_" + SafeName(displayName) + ".mp4");
            string png = Path.Combine(dir, "tex_" + SafeName(displayName) + $"_t{cli.Time:0.##}.png");
            File.WriteAllBytes(mp4, vex.Mp4Bytes);
            string ffmpeg = ResolveFfmpeg(cli.Ffmpeg);
            Console.WriteLine($"  内嵌 MP4 视频贴图，大小 {vex.Mp4Bytes.Length:N0} 字节，ffmpeg 抽帧 t={cli.Time:0.##}s ...");
            if (!RenderFfmpeg(ffmpeg, mp4, png, cli.Time) && !RenderFfmpeg(ffmpeg, mp4, png, 0))
                throw new InvalidOperationException("ffmpeg 抽帧失败");
            image = Image.Load<Rgba32>(png);
            Console.WriteLine($"  视频帧: {image.Width}x{image.Height} -> {png}");
        }

        using (image)
        {
            var outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
            SaveImage(image, outPath);
            Console.WriteLine($"已输出: {Path.GetFullPath(outPath)} ({image.Width}x{image.Height})");
        }
        return 0;
    }

    private static string SafeName(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) && ch < 128) sb.Append(ch);
            else if (ch == '.' || ch == '-' || ch == '_') sb.Append(ch);
            else sb.Append('_');
        }
        var r = sb.ToString().Trim('_');
        if (r.Length > 60) r = r[^60..];
        return r + "_" + Math.Abs(s.GetHashCode()).ToString("x8");
    }

    private static bool RenderFfmpeg(string ffmpeg, string input, string output, double time)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y", "-ss", time.ToString("0.###", CultureInfo.InvariantCulture), "-i", input, "-frames:v", "1", output })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(180_000);
            if (p.ExitCode == 0 && File.Exists(output)) return true;
            if (!string.IsNullOrWhiteSpace(err)) Console.Error.WriteLine($"[ffmpeg] {err.Trim()}");
            try { if (File.Exists(output)) File.Delete(output); } catch { }
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ffmpeg] 无法启动 ({ffmpeg}): {ex.Message}");
            return false;
        }
    }

    // ---------------------------------------------------------------- info

    private static int CmdInfo(string[] args)
    {
        var cli = CliOpts.Parse(args, 1);
        if (cli.Positional.Count < 1) return Usage("info 缺少参数");
        using var pkg = ScenePackage.Open(cli.Positional[0]);
        var scene = LoadScene(pkg);
        var opt = MakeOptions(cli);
        using var renderer = new Renderer(pkg, scene, opt);
        renderer.AddLogSink(s => { if (opt.Verbose) Console.Error.WriteLine(s); });
        renderer.PrintSceneInfo(Console.Out);

        var skipped = renderer.Layers.Where(l => l.SkipReason != null).Select(l => l.SkipReason!).ToList();
        if (skipped.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"跳过层原因统计（{skipped.Count}）:");
            foreach (var g in skipped.GroupBy(s => s).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Count(),3}  {g.Key}");
        }
        return 0;
    }

    // ---------------------------------------------------------------- render

    private static int CmdRender(string[] args)
    {
        var cli = CliOpts.Parse(args, 1);
        if (cli.Positional.Count < 4) return Usage("render 参数错误");
        string pkgPath = cli.Positional[0];
        int w = int.Parse(cli.Positional[1], CultureInfo.InvariantCulture);
        int h = int.Parse(cli.Positional[2], CultureInfo.InvariantCulture);
        string outPath = cli.Positional[3];
        if (w < 16 || h < 16 || (long)w * h > 120_000_000) throw new ArgumentOutOfRangeException(nameof(w), "尺寸不合法");

        var swTotal = Stopwatch.StartNew();
        using var pkg = ScenePackage.Open(pkgPath);
        var scene = LoadScene(pkg);
        var opt = MakeOptions(cli);
        opt.Width = w;
        opt.Height = h;
        using var renderer = new Renderer(pkg, scene, opt);
        var verboseConsole = cli.Debug || cli.Verbose;
        renderer.AddLogSink(s => { if (verboseConsole) Console.Error.WriteLine(s); });

        var sw = Stopwatch.StartNew();
        using var image = renderer.Render();
        sw.Stop();

        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var swSave = Stopwatch.StartNew();
        SaveImage(image, outPath);
        swSave.Stop();
        swTotal.Stop();

        long peak = Process.GetCurrentProcess().PeakWorkingSet64;
        Console.WriteLine($"已输出: {Path.GetFullPath(outPath)} ({image.Width}x{image.Height})  文件 {new FileInfo(outPath).Length / 1024.0:0.0} KB");
        Console.WriteLine($"耗时: 合成 {sw.ElapsedMilliseconds} ms, 编码 {swSave.ElapsedMilliseconds} ms, 总计 {swTotal.ElapsedMilliseconds} ms; 峰值内存 {peak / 1048576.0:0} MB");

        var problems = renderer.Layers.Where(l => l.SkipReason != null).ToList();
        if (problems.Count > 0)
        {
            Console.WriteLine($"跳过 {problems.Count} 层:");
            foreach (var g in problems.GroupBy(l => l.SkipReason!).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Count(),3}  {g.Key}");
        }
        var videos = renderer.Layers.Count(l => l.IsVideoFrame);
        if (videos > 0) Console.WriteLine($"其中 {videos} 层使用视频贴图抽帧（t={opt.Time:0.##}s，静态效果与动态壁纸略有差异）");
        // 机器可读统计行（批量脚本用）
        Console.WriteLine($"STATS drew={renderer.DrawnCount} skipped={problems.Count} video={videos} out={image.Width}x{image.Height} ms={swTotal.ElapsedMilliseconds} peakmb={peak / 1048576}");
        return 0;
    }


    // ---------------------------------------------------------------- cat（打印包内文本条目）

    /// <summary>cat：打印包内某个文本条目（用来查看 particles/*.json、scene.json 等）。</summary>
    private static int CmdCat(string[] args)
    {
        if (args.Length < 3) return Usage("cat");
        using var pkg = ScenePackage.Open(args[1]);
        var text = pkg.TryReadText(args[2]);
        if (text == null) { Console.Error.WriteLine("找不到条目: " + args[2]); return 1; }
        Console.WriteLine(text);
        return 0;
    }

    // ---------------------------------------------------------------- live（渲染一帧带粒子的实时画面）

    /// <summary>live：渲染某一时刻的一帧（含粒子层），用来肉眼检查实时效果。</summary>
    private static int CmdLive(string[] args)
    {
        if (args.Length < 6) return Usage("live");
        var pkg = args[1];
        int.TryParse(args[2], out var w);
        int.TryParse(args[3], out var h);
        double.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var time);
        var outPath = args[5];
        if (w <= 0) w = 960;
        if (h <= 0) h = 600;

        using var player = new ScenePlayer(pkg, w, h);
        player.Warm(time);
        using var frame = player.Frame(time);
        frame.SaveAsPng(outPath);
        Console.WriteLine($"saved {outPath} ({w}x{h}, t={time}s, 粒子发射器={player.DrawnCount})");
        return 0;
    }
    // ---------------------------------------------------------------- stats（只看结构，不渲染）

    /// <summary>统计一面壁纸的结构：对象数、带动画数、粒子/文字层数、脚本文件数、可绘制图片层数。</summary>
    private static int CmdStats(string[] args)
    {
        if (args.Length < 2) return Usage("stats");
        var pkgPath = args[1];
        using var pkg = ScenePackage.Open(pkgPath);
        var json = pkg.TryReadText("scene.json");
        if (string.IsNullOrWhiteSpace(json)) { Console.Error.WriteLine("没有 scene.json"); return 1; }
        var scene = SceneDoc.Parse(json);

        int animated = 0, particles = 0, texts = 0, images = 0;
        foreach (var o in scene.Objects)
        {
            if (o.OriginXAnim != null || o.OriginYAnim != null || o.ScaleXAnim != null ||
                o.ScaleYAnim != null || o.RotationAnim != null || o.AlphaAnim != null) animated++;
            if (o.Particle != null) particles++;
            if (o.Text != null) texts++;
            if (o.Image != null) images++;
        }

        var scriptEntries = pkg.Entries.Count(e =>
            e.Name.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) ||
            e.Name.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
        var particleEntries = pkg.Entries.Count(e =>
            e.Name.StartsWith("particles/", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            id = Path.GetFileName(Path.GetDirectoryName(pkgPath)),
            objects = scene.Objects.Count,
            animated,
            particles,
            texts,
            images,
            scriptEntries,
            particleEntries,
        }));
        return 0;
    }

    // ---------------------------------------------------------------- bench（实时帧率测试）

    /// <summary>
    /// bench：测实时播放性能。先暖机（解析图层/贴图），再连续渲染 N 帧并按 30fps 时间轴推进动画。
    /// 用法：SceneRender bench <scene.pkg> [--width 960] [--height 600] [--frames 60] [--json out.json]
    /// </summary>
    private static int CmdBench(string[] args)
    {
        string? pkgPath = null;
        int width = 960, height = 600, frames = 60;
        string? jsonOut = null;

        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i].ToLowerInvariant();
            switch (a)
            {
                case "--width": if (i + 1 < args.Length) int.TryParse(args[++i], out width); break;
                case "--height": if (i + 1 < args.Length) int.TryParse(args[++i], out height); break;
                case "--frames": if (i + 1 < args.Length) int.TryParse(args[++i], out frames); break;
                case "--json": if (i + 1 < args.Length) jsonOut = args[++i]; break;
                default:
                    if (!a.StartsWith("-")) pkgPath ??= args[i];
                    break;
            }
        }
        if (pkgPath == null || !File.Exists(pkgPath)) return Usage("bench");

        var sw = Stopwatch.StartNew();
        using var player = new ScenePlayer(pkgPath, width, height);
        player.Warm(0);
        var warmMs = sw.Elapsed.TotalMilliseconds;

        var n = Math.Max(1, frames);
        double total = 0, max = 0;
        for (int i = 0; i < n; i++)
        {
            var s2 = Stopwatch.StartNew();
            using var frame = player.Frame(i / 30.0);
            s2.Stop();
            total += s2.Elapsed.TotalMilliseconds;
            if (s2.Elapsed.TotalMilliseconds > max) max = s2.Elapsed.TotalMilliseconds;
        }
        var avg = total / n;
        var fps = 1000.0 / Math.Max(0.001, avg);

        Console.WriteLine($"pkg={pkgPath}");
        Console.WriteLine($"size={width}x{height} 对象={player.ObjectCount} 带动画={player.AnimatedObjectCount} 绘制层={player.DrawnCount}");
        Console.WriteLine($"warm={warmMs:0}ms frames={n} avg={avg:0.0}ms max={max:0.0}ms fps≈{fps:0.0}");

        if (jsonOut != null)
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                pkg = pkgPath,
                width,
                height,
                objects = player.ObjectCount,
                animated = player.AnimatedObjectCount,
                drawn = player.DrawnCount,
                warmMs = Math.Round(warmMs, 1),
                avgMs = Math.Round(avg, 2),
                maxMs = Math.Round(max, 2),
                fps = Math.Round(fps, 1),
            });
            File.WriteAllText(jsonOut, payload);
        }
        return 0;
    }

    // ---------------------------------------------------------------- main (退路)

    private static int CmdMain(string[] args)
    {
        var cli = CliOpts.Parse(args, 1);
        if (cli.Positional.Count < 2) return Usage("main 参数错误");
        using var pkg = ScenePackage.Open(cli.Positional[0]);
        var scene = LoadScene(pkg);
        var opt = MakeOptions(cli);
        using var renderer = new Renderer(pkg, scene, opt);
        renderer.AddLogSink(s => { if (cli.Verbose || cli.Debug) Console.Error.WriteLine(s); });
        var (layer, image) = renderer.BestSubject();
        if (layer == null || image == null) { Console.Error.WriteLine("[错误] 没找到可导出的图片主体"); return 1; }
        using (image)
        {
            SaveImage(image, cli.Positional[1]);
            Console.WriteLine($"主体: #{layer.Obj.Id} {layer.Obj.Name} 贴图 {layer.TexEntry} ({image.Width}x{image.Height})");
            Console.WriteLine($"已输出: {Path.GetFullPath(cli.Positional[1])}");
        }
        return 0;
    }

    // ---------------------------------------------------------------- dump

    private static int CmdDump(string[] args)
    {
        if (args.Length < 4) return Usage("dump 参数错误");
        using var pkg = ScenePackage.Open(args[1]);
        var e = pkg.Find(args[2]) ?? throw new FileNotFoundException($"包内找不到条目 {args[2]}");
        File.WriteAllBytes(args[3], pkg.ReadEntryBytes(e));
        Console.WriteLine($"已导出 {e.Name} ({e.Length:N0} 字节) -> {Path.GetFullPath(args[3])}");
        return 0;
    }

    // ---------------------------------------------------------------- 工具

    private static SceneDoc LoadScene(ScenePackage pkg)
    {
        var json = pkg.TryReadText("scene.json") ?? throw new InvalidDataException("包内没有 scene.json（可能是视频/网页壁纸）");
        return SceneDoc.Parse(json);
    }

    private static void SaveImage(Image image, string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg")
            image.Save(path, new JpegEncoder { Quality = 92 });
        else
            image.Save(path);
    }
}



