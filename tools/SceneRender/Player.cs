using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SceneRender;

/// <summary>
/// 实时播放用的封装：把包、场景、图层一次性解析好，之后按时间只做"变换 + 采样"，
/// 每帧不再读包里的 JSON，所以能跑到每秒十几到几十帧。
/// </summary>
public sealed class ScenePlayer : IDisposable
{
    private readonly ScenePackage _pkg;
    private readonly SceneDoc _scene;
    private readonly Renderer _renderer;

    public int Width { get; }
    public int Height { get; }
    public SceneDoc Scene => _scene;

    public ScenePlayer(string packagePath, int width, int height, string? ffmpegPath = null, bool verbose = false, bool particles = true, string? cacheDir = null)
    {
        _pkg = ScenePackage.Open(packagePath);
        var json = _pkg.TryReadText("scene.json");
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("包内没有 scene.json");
        _scene = SceneDoc.Parse(json);
        Width = Math.Max(64, width);
        Height = Math.Max(64, height);
        _renderer = new Renderer(_pkg, _scene, new RenderOptions
        {
            Width = Width,
            Height = Height,
            FfmpegPath = ffmpegPath,
            Verbose = verbose,
            Particles = particles,
            CacheDir = cacheDir ?? "",
        });
    }

    /// <summary>首帧预热：解析全部图层与贴图（较慢，几百毫秒到几秒），之后每帧才快。</summary>
    public void Warm(double time = 0)
    {
        using var frame = _renderer.RenderFrame(time);
    }

    /// <summary>渲染某一时刻的一帧。调用方负责 Dispose。</summary>
    public Image<Rgba32> Frame(double time) => _renderer.RenderFrame(time);

    /// <summary>最近一帧实际绘制的层数。</summary>
    public int DrawnCount => _renderer.DrawnCount;

    /// <summary>场景里带位移/缩放/旋转/透明度动画的对象数量（用来判断这面壁纸到底会不会动）。</summary>
    public int AnimatedObjectCount => _scene.Objects.Count(o =>
        o.OriginXAnim != null || o.OriginYAnim != null ||
        o.ScaleXAnim != null || o.ScaleYAnim != null ||
        o.RotationAnim != null || o.AlphaAnim != null);

    /// <summary>场景里图层总数。</summary>
    public int ObjectCount => _scene.Objects.Count;

    public void AddLogSink(Action<string> sink) => _renderer.AddLogSink(sink);

    public void Dispose()
    {
        _renderer.Dispose();
        _pkg.Dispose();
    }
}

