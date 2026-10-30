using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SceneRender;

/// <summary>2D 仿射变换：x' = A*x + B*y + C; y' = D*x + E*y + F</summary>
public readonly struct Aff2
{
    public readonly float A, B, C, D, E, F;
    public Aff2(float a, float b, float c, float d, float e, float f) { A = a; B = b; C = c; D = d; E = e; F = f; }

    public static Aff2 Identity => new(1, 0, 0, 0, 1, 0);
    public static Aff2 Translate(float x, float y) => new(1, 0, x, 0, 1, y);
    public static Aff2 Rotate(float rad)
    {
        float c = MathF.Cos(rad), s = MathF.Sin(rad);
        return new Aff2(c, -s, 0, s, c, 0);
    }
    public static Aff2 Scale(float sx, float sy) => new(sx, 0, 0, 0, sy, 0);

    /// <summary>先应用 child，再应用 parent。</summary>
    public static Aff2 Mul(Aff2 p, Aff2 c) => new(
        p.A * c.A + p.B * c.D,
        p.A * c.B + p.B * c.E,
        p.A * c.C + p.B * c.F + p.C,
        p.D * c.A + p.E * c.D,
        p.D * c.B + p.E * c.E,
        p.D * c.C + p.E * c.F + p.F);

    public (float X, float Y) Apply(float x, float y) => (A * x + B * y + C, D * x + E * y + F);

    public Aff2 Inverse()
    {
        float det = A * E - B * D;
        if (MathF.Abs(det) < 1e-12f) det = det >= 0 ? 1e-12f : -1e-12f;
        float inv = 1f / det;
        float a = E * inv, b = -B * inv, d = -D * inv, e = A * inv;
        return new Aff2(a, b, -(a * C + b * F), d, e, -(d * C + e * F));
    }
}

/// <summary>贴图数据（RGBA8888，行优先）。采样用。</summary>
public sealed class TextureData
{
    public int Width, Height;
    public Rgba32[] Pixels = Array.Empty<Rgba32>();
    public string Source = "";
    public bool IsVideoFrame;

    public static TextureData FromImage(Image<Rgba32> img, string source, bool video = false)
    {
        var t = new TextureData { Width = img.Width, Height = img.Height, Source = source, IsVideoFrame = video };
        t.Pixels = new Rgba32[img.Width * img.Height];
        img.CopyPixelDataTo(t.Pixels);
        return t;
    }
}

/// <summary>场景里一次 draw 所需要的信息。</summary>
public sealed class LayerInfo
{
    public SceneObject Obj = null!;
    public string? TexEntry;
    public string? MaterialPath;
    public string? TextureName;
    public float QuadW, QuadH;
    public string BlendMode = "translucent";
    public float TintR = 1, TintG = 1, TintB = 1;
    public Aff2 World;
    public float EffectiveAlpha;
    public string? SkipReason;
    public TextureData? Tex;
    public bool IsVideoFrame;
}

public sealed class RenderOptions
{
    public int Width = 2560;
    public int Height = 1440;
    public double Time = 1.0;
    public bool Debug;
    public bool IncludeText;
    public bool Verbose;
    public string CacheDir = "";
    public string? FfmpegPath;
    public bool IgnoreZoom;

    /// <summary>是否叠加粒子层（实时播放时开，静态截图默认关，避免随机点缀）。</summary>
    public bool Particles;
}

public sealed class Renderer : IDisposable
{
    private readonly ScenePackage _pkg;
    private readonly SceneDoc _scene;
    private readonly RenderOptions _opt;
    private readonly Dictionary<string, TextureData?> _texCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _texNote = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Aff2> _world = new();
    private readonly List<LayerInfo> _layers = new();
    private bool _layersWarm;
    private readonly List<Action<string>> _logSinks = new();
    private readonly List<ParticleEmitter> _emitterData = new();
    private readonly List<(ParticleEmitter Emitter, SceneObject Obj)> _emitters = new();
    private bool _emittersBuilt;

    public SceneDoc Scene => _scene;
    public IReadOnlyList<LayerInfo> Layers => _layers;

    public Renderer(ScenePackage pkg, SceneDoc scene, RenderOptions opt)
    {
        _pkg = pkg;
        _scene = scene;
        _opt = opt;
    }

    public void Log(string msg)
    {
        if (_opt.Verbose || _opt.Debug) Console.Error.WriteLine(msg);
        foreach (var s in _logSinks) s(msg);
    }

    public void AddLogSink(Action<string> sink) => _logSinks.Add(sink);

    // ------------------------------------------------------------------ 变换

    private Aff2 WorldOf(SceneObject o, HashSet<int> visiting)
    {
        if (_world.TryGetValue(o.Id, out var w)) return w;
        if (o.Parent is int pid && !visiting.Contains(o.Id))
        {
            var parent = _scene.Objects.FirstOrDefault(x => x.Id == pid);
            if (parent != null && parent != o)
            {
                visiting.Add(o.Id);
                var pw = WorldOf(parent, visiting);
                visiting.Remove(o.Id);
                w = Aff2.Mul(pw, LocalOf(o));
                _world[o.Id] = w;
                return w;
            }
        }
        w = LocalOf(o);
        _world[o.Id] = w;
        return w;
    }

    private static Aff2 LocalOf(SceneObject o)
    {
        var t = Aff2.Translate(o.Origin.X, o.Origin.Y);
        var r = Aff2.Rotate(o.Angles.Z);
        var s = Aff2.Scale(o.Scale.X, o.Scale.Y);
        return Aff2.Mul(Aff2.Mul(t, r), s);
    }

    // ------------------------------------------------------------------ 模型/材质解析

    /// <summary>把 scene object 的 image 链（models/*.json → materials/*.json → *.tex）解开。</summary>
    public LayerInfo ResolveLayer(SceneObject o)
    {
        var info = new LayerInfo { Obj = o, World = WorldOf(o, new HashSet<int>()) };
        if (o.Image == null) { info.SkipReason = "非图片对象"; return info; }

        string modelPath = o.Image;
        var modelJson = _pkg.TryReadText(modelPath);
        if (modelJson == null)
        {
            info.SkipReason = $"包内缺少 {modelPath}（引擎内置模型，如 layercolor/composelayer）";
            return info;
        }

        float modelW = 0, modelH = 0;
        bool autosize = false;
        Vec2 cropOffset = default;
        string? material = null;
        try
        {
            using var doc = JsonDocument.Parse(modelJson, new JsonDocumentOptions { AllowTrailingCommas = true, MaxDepth = 128 });
            var root = doc.RootElement;
            modelW = SceneDoc.GetFloat(root, "width", 0);
            modelH = SceneDoc.GetFloat(root, "height", 0);
            autosize = SceneDoc.GetBool(root, "autosize", false);
            material = SceneDoc.GetStringOrNull(root, "material");
            if (SceneDoc.TryGet(root, "cropoffset", out var co) && co.ValueKind == JsonValueKind.String)
                cropOffset = SceneDoc.ParseVec2String(co.GetString()!, default);
        }
        catch (Exception ex) { info.SkipReason = $"模型 JSON 解析失败: {ex.Message}"; return info; }

        if (material == null) { info.SkipReason = "模型没有 material 字段"; return info; }
        info.MaterialPath = material;
        var matJson = _pkg.TryReadText(material);
        if (matJson == null) { info.SkipReason = $"包内缺少材质 {material}"; return info; }

        string? texName = null;
        try
        {
            using var doc = JsonDocument.Parse(matJson, new JsonDocumentOptions { AllowTrailingCommas = true, MaxDepth = 256 });
            if (SceneDoc.TryGet(doc.RootElement, "passes", out var passes) && passes.ValueKind == JsonValueKind.Array)
            {
                foreach (var pass in passes.EnumerateArray())
                {
                    if (SceneDoc.TryGet(pass, "blending", out var bl) && bl.ValueKind == JsonValueKind.String)
                        info.BlendMode = bl.GetString()!;
                    else if (SceneDoc.TryGet(pass, "blending", out var bl2) && bl2.ValueKind == JsonValueKind.Object)
                        info.BlendMode = SceneDoc.GetString(bl2, "value", info.BlendMode);

                    if (SceneDoc.TryGet(pass, "textures", out var texs) && texs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var t in texs.EnumerateArray())
                        {
                            if (t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                            {
                                texName = t.GetString();
                                break;
                            }
                        }
                    }
                    break; // 只看第一个 pass 的贴图
                }
            }
        }
        catch (Exception ex) { info.SkipReason = $"材质 JSON 解析失败: {ex.Message}"; return info; }

        if (texName == null) { info.SkipReason = "材质没有贴图（纯色/组合/后处理层）"; return info; }
        info.TextureName = texName;

        var texEntry = ResolveTexEntry(texName);
        if (texEntry == null) { info.SkipReason = $"包内找不到贴图 {texName}"; return info; }
        info.TexEntry = texEntry.Name;

        var tex = GetTexture(texEntry, out var note);
        if (tex == null) { info.SkipReason = note; return info; }
        info.Tex = tex;
        info.IsVideoFrame = tex.IsVideoFrame;

        float qw = o.HasSize ? o.Size.X : (modelW > 0 ? modelW : tex.Width);
        float qh = o.HasSize ? o.Size.Y : (modelH > 0 ? modelH : tex.Height);
        if (cropOffset.X != 0 || cropOffset.Y != 0)
        {
            // 部分模型（提示框等）用 cropoffset 记录实际画面在填充贴图中的位置。
            // 贴图解码时已按 TEX 头 ImageWidth/ImageHeight 裁掉填充，这里仅在尺寸差异明显时提示。
            Log($"[提示] {o.Name}#{o.Id} 模型 cropoffset={cropOffset}，对象尺寸 {qw}x{qh}，贴图 {tex.Width}x{tex.Height}");
        }
        info.QuadW = qw;
        info.QuadH = qh;
        return info;
    }

    private PkgEntry? ResolveTexEntry(string texName)
    {
        string n = texName.Replace('\\', '/');
        if (n.EndsWith(".tex", StringComparison.OrdinalIgnoreCase))
        {
            var e = _pkg.Find(n);
            if (e != null) return e;
        }
        else
        {
            foreach (var cand in new[] { n, n + ".tex", "materials/" + n, "materials/" + n + ".tex" })
            {
                var e = _pkg.Find(cand);
                if (e != null) return e;
            }
        }
        return _pkg.Find(n);
    }

    // ------------------------------------------------------------------ 贴图解码

    private TextureData? GetTexture(PkgEntry entry, out string note)
    {
        note = "";
        if (_texCache.TryGetValue(entry.Name, out var cached))
        {
            note = _texNote.TryGetValue(entry.Name, out var n) ? n : "";
            return cached;
        }
        try
        {
            var bytes = _pkg.ReadEntryBytes(entry);
            var tex = TexFile.FromBytes(bytes);
            try
            {
                using var img = tex.DecodeImage();
                var td = TextureData.FromImage(img, $"{entry.Name} ({tex.Format}, {tex.Format switch { TexFormat.RGBA8888 => "RGBA8888", TexFormat.DXT1 => "DXT1", TexFormat.DXT3 => "DXT3", TexFormat.DXT5 => "DXT5", _ => tex.Format.ToString() }})");
                _texCache[entry.Name] = td;
                Log($"[tex] {entry.Name}: {tex.TextureWidth}x{tex.TextureHeight} -> {td.Width}x{td.Height} fmt={tex.Format} flags={tex.Flags} blend-ok");
                return td;
            }
            catch (VideoTextureException vex)
            {
                var frame = ExtractVideoFrame(vex.Mp4Bytes, entry.Name, out note);
                if (frame == null)
                {
                    _texCache[entry.Name] = null;
                    _texNote[entry.Name] = note;
                    return null;
                }
                _texCache[entry.Name] = frame;
                Log($"[tex] {entry.Name}: 内嵌 MP4 视频贴图，已用 ffmpeg 抽帧 {frame.Width}x{frame.Height} ({frame.Source})");
                return frame;
            }
        }
        catch (Exception ex)
        {
            note = $"贴图解码失败 {entry.Name}: {ex.Message}";
            _texCache[entry.Name] = null;
            _texNote[entry.Name] = note;
            return null;
        }
    }

    private TextureData? ExtractVideoFrame(byte[] mp4, string entryName, out string note)
    {
        note = "";
        string cacheDir = string.IsNullOrEmpty(_opt.CacheDir) ? Path.Combine(AppContext.BaseDirectory, "_cache") : _opt.CacheDir;
        string framesDir = Path.Combine(cacheDir, "frames");
        Directory.CreateDirectory(framesDir);
        string safe = Sanitize(entryName, mp4.Length) + ".mp4";
        string mp4Path = Path.Combine(framesDir, safe);
        string frameStem = Path.GetFileNameWithoutExtension(safe)
            + "_t" + _opt.Time.ToString("0.##", CultureInfo.InvariantCulture);
        string pngPath = Path.Combine(framesDir, frameStem + ".png");

        try
        {
            if (!File.Exists(pngPath))
            {
                if (!File.Exists(mp4Path) || new FileInfo(mp4Path).Length != mp4.Length)
                    File.WriteAllBytes(mp4Path, mp4);

                string ffmpeg = _opt.FfmpegPath ?? "ffmpeg";
                bool ok = RunFfmpeg(ffmpeg, mp4Path, pngPath, _opt.Time);
                if (!ok) ok = RunFfmpeg(ffmpeg, mp4Path, pngPath, 0.0);
                if (!ok)
                {
                    note = $"ffmpeg 抽帧失败: {entryName}";
                    return null;
                }
            }
            using var img = Image.Load<Rgba32>(pngPath);
            return TextureData.FromImage(img, $"视频帧 {Path.GetFileName(pngPath)} @ {_opt.Time:0.##}s", true);
        }
        catch (Exception ex)
        {
            note = $"视频贴图抽帧异常 {entryName}: {ex.Message}";
            return null;
        }
    }

    private static bool RunFfmpeg(string ffmpeg, string input, string output, double time)
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
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(time.ToString("0.###", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(input);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add(output);
            using var p = Process.Start(psi)!;
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(120_000);
            if (p.ExitCode == 0 && File.Exists(output)) return true;
            if (err.Length > 0) Console.Error.WriteLine($"[ffmpeg] {err.Trim()}");
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ffmpeg] 启动失败: {ex.Message}");
            return false;
        }
    }

    private static string Sanitize(string name, int size)
    {
        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) && ch < 128) sb.Append(ch);
            else if (ch == '.' || ch == '-' || ch == '_') sb.Append(ch);
            else sb.Append('_');
        }
        var s = sb.ToString().Trim('_');
        if (s.Length > 80) s = s[^80..];
        // 用稳定哈希（FNV-1a），避免 string.GetHashCode 每次进程随机会导致缓存失效/重复落盘
        byte[] bytes = Encoding.UTF8.GetBytes(name + "#" + size);
        uint h = 2166136261u;
        foreach (byte b in bytes)
        {
            h ^= b;
            h *= 16777619u;
        }
        return s + "_" + h.ToString("x8");
    }

    // ------------------------------------------------------------------ 渲染

    public Image<Rgba32> Render() => RenderInternal(true);

    private Image<Rgba32> RenderInternal(bool withParticles)
    {
        var canvas = new Image<Rgba32>(_opt.Width, _opt.Height);
        var clear = _scene.ClearColor;
        Rgba32 bg = new(
            (byte)Math.Clamp(clear.X * 255f, 0, 255),
            (byte)Math.Clamp(clear.Y * 255f, 0, 255),
            (byte)Math.Clamp(clear.Z * 255f, 0, 255), 255);
        canvas.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < canvas.Height; y++) acc.GetRowSpan(y).Fill(bg);
        });

        BuildLayers();

        float designW = _scene.BaseWidth, designH = _scene.BaseHeight;
        float zoom = _opt.IgnoreZoom ? 1f : Math.Max(0.0001f, _scene.Zoom);
        if (!_opt.IgnoreZoom && Math.Abs(zoom - 1f) > 0.001f)
            Log($"[提示] 场景 general.zoom={zoom}，当前按 zoom 缩放绘制（--nozoom 可关闭）");

        float scale = MathF.Max(_opt.Width / designW, _opt.Height / designH) * zoom;
        float offX = (_opt.Width - designW * scale) / 2f;
        float offY = (_opt.Height - designH * scale) / 2f;
        Aff2 canvasToOut = new(scale, 0, offX, 0, scale, offY);

        int drawn = 0;
        foreach (var layer in _layers)
        {
            if (layer.Tex == null || layer.SkipReason != null) continue;
            if (layer.EffectiveAlpha <= 0.001f) continue;
            DrawLayer(canvas, layer, canvasToOut, ref drawn);
        }

        // 粒子层最后叠上去（Wallpaper Engine 的粒子是覆盖在图层之上的）
        if (withParticles && _opt.Particles)
        {
            UpdateEmitters();
            ParticleRenderer.Draw(canvas, _emitterData, canvasToOut, _opt.Time);
        }

        Log($"[render] 设计画布 {designW}x{designH}, zoom={zoom}, cover 比例 {scale:0.####}, 绘制 {drawn} 层 / 共 {_layers.Count} 层");
        DrawnCount = drawn;
        return canvas;
    }

    /// <summary>最近一次 Render() 实际绘制（非跳过）的层数。</summary>
    public int DrawnCount { get; private set; }

    private void BuildLayers()
    {
        if (_layersWarm)
        {
            RefreshLayers();
            return;
        }

        _layers.Clear();
        foreach (var o in _scene.Objects)
        {
            float alpha = o.Alpha;
            if (o.AlphaAnim != null) alpha = o.AlphaAnim.Eval(_opt.Time);
            if (!o.Visible)
            {
                Log($"[跳过] #{o.Id} {o.Name}: visible=false" + (o.VisibleHasScript ? " (script)" : ""));
                continue;
            }
            if (o.Image == null)
            {
                if (o.Text != null) Log($"[跳过] #{o.Id} {o.Name}: 文字层 \"{Trim(o.Text)}\"（暂不渲染文字）");
                else if (o.Particle != null) Log($"[跳过] #{o.Id} {o.Name}: 粒子层 {o.Particle}");
                continue;
            }
            var layer = ResolveLayer(o);
            layer.EffectiveAlpha = alpha;
            layer.TintR = o.Color.X; layer.TintG = o.Color.Y; layer.TintB = o.Color.Z;
            if (layer.SkipReason != null)
            {
                Log($"[跳过] #{o.Id} {o.Name}: {layer.SkipReason}");
                _layers.Add(layer);
                continue;
            }
            _layers.Add(layer);
        }

        _layersWarm = true;
    }

    /// <summary>暖机后的每帧刷新：只更新可见性、透明度和世界变换，不再读包里的 JSON。</summary>
    private void RefreshLayers()
    {
        var visiting = new HashSet<int>();
        foreach (var layer in _layers)
        {
            var o = layer.Obj;
            layer.EffectiveAlpha = o.Visible ? AlphaOf(o) : 0f;
            if (layer.Tex != null && layer.SkipReason == null)
            {
                visiting.Clear();
                layer.World = WorldOf(o, visiting);
            }
        }
    }

    private float AlphaOf(SceneObject o) => o.AlphaAnim != null ? o.AlphaAnim.Eval(_opt.Time) : o.Alpha;

    /// <summary>按时间推进各图层的位移 / 缩放 / 旋转动画。</summary>
    public void ApplyAnimations(double time)
    {
        foreach (var o in _scene.Objects)
        {
            if (o.OriginXAnim != null || o.OriginYAnim != null)
            {
                float x = o.OriginXAnim?.Eval(time) ?? o.Origin.X;
                float y = o.OriginYAnim?.Eval(time) ?? o.Origin.Y;
                o.Origin = new Vec3(x, y, o.Origin.Z);
            }
            if (o.ScaleXAnim != null || o.ScaleYAnim != null)
            {
                float sx = o.ScaleXAnim?.Eval(time) ?? o.Scale.X;
                float sy = o.ScaleYAnim?.Eval(time) ?? o.Scale.Y;
                o.Scale = new Vec3(sx, sy, o.Scale.Z);
            }
            if (o.RotationAnim != null)
            {
                o.Angles = new Vec3(o.Angles.X, o.Angles.Y, o.RotationAnim.Eval(time));
            }
        }
    }

    /// <summary>
    /// 渲染某一时刻的一帧（实时播放用）：推进动画 → 清世界矩阵缓存 → 复用已解析图层重新合成。
    /// 首帧解析全部图层（慢），之后每帧只做变换与采样。
    /// 没有关键帧动画的场景（48 个订阅壁纸里只有 5 个有）会把静态图层合成为缓存底图，
    /// 之后每帧只克隆底图 + 画粒子，省掉整层重采样。
    /// </summary>
    public Image<Rgba32> RenderFrame(double time)
    {
        _opt.Time = time;
        ApplyAnimations(time);
        EnsureAnimationInfo();

        if (!_hasAnimatedLayers)
        {
            _baseFrame ??= RenderInternal(false);
            var canvas = _baseFrame.Clone();
            if (_opt.Particles)
            {
                UpdateEmitters();
                ParticleRenderer.Draw(canvas, _emitterData, CanvasToOut(), time);
            }
            return canvas;
        }

        _world.Clear();
        return Render();
    }

    private Image<Rgba32>? _baseFrame;
    private bool _hasAnimatedLayers;
    private bool _animationChecked;

    private void EnsureAnimationInfo()
    {
        if (_animationChecked) return;
        _animationChecked = true;
        foreach (var o in _scene.Objects)
        {
            if (o.OriginXAnim != null || o.OriginYAnim != null || o.ScaleXAnim != null ||
                o.ScaleYAnim != null || o.RotationAnim != null || o.AlphaAnim != null)
            {
                _hasAnimatedLayers = true;
                break;
            }
        }
    }

    private Aff2 CanvasToOut()
    {
        float designW = _scene.BaseWidth, designH = _scene.BaseHeight;
        float zoom = _opt.IgnoreZoom ? 1f : Math.Max(0.0001f, _scene.Zoom);
        float scale = MathF.Max(_opt.Width / designW, _opt.Height / designH) * zoom;
        float offX = (_opt.Width - designW * scale) / 2f;
        float offY = (_opt.Height - designH * scale) / 2f;
        return new Aff2(scale, 0, offX, 0, scale, offY);
    }

    /// <summary>首帧图层解析是否已完成。</summary>
    public bool LayersReady => _layersWarm;

    /// <summary>收集所有粒子发射器（按 scene.json 里带 particle 的对象）。</summary>
    private void BuildEmitters()
    {
        _emitterData.Clear();
        _emitters.Clear();
        foreach (var o in _scene.Objects)
        {
            if (o.Particle == null) continue;
            var preset = ParticleRenderer.NormalizePreset(o.Particle);
            if (preset == null)
            {
                Log($"[粒子] 跳过无法识别的自定义粒子 #{o.Id} {o.Name}: {o.Particle}");
                continue;
            }
            var emitter = new ParticleEmitter
            {
                Preset = preset,
                Seed = unchecked((int)(o.Id * 2654435761u + 17u)),
            };
            _emitters.Add((emitter, o));
            _emitterData.Add(emitter);
        }
        _emittersBuilt = true;
    }

    /// <summary>每帧更新发射器的位置与范围（跟随对象的动画变换）。</summary>
    private void UpdateEmitters()
    {
        if (!_emittersBuilt) BuildEmitters();
        var visiting = new HashSet<int>();
        foreach (var (emitter, o) in _emitters)
        {
            visiting.Clear();
            var world = WorldOf(o, visiting);
            var (cx, cy) = world.Apply(0f, 0f);
            emitter.CenterX = cx;
            emitter.CenterY = cy;

            var halfW = o.HasSize ? o.Size.X / 2f : _scene.BaseWidth / 2f;
            var halfH = o.HasSize ? o.Size.Y / 2f : _scene.BaseHeight / 2f;
            emitter.HalfW = halfW * MathF.Abs(o.Scale.X);
            emitter.HalfH = halfH * MathF.Abs(o.Scale.Y);
            emitter.Scale = Math.Clamp(MathF.Abs(o.Scale.X) * (o.HasSize ? 1f : 1.6f), 0.4f, 4f);
        }
    }

    /// <summary>当前场景的粒子发射器（调试/统计用）。</summary>
    public IReadOnlyList<ParticleEmitter> Emitters => _emitterData;

    private static string Trim(string s) => s.Length <= 24 ? s : s[..24] + "…";

    private void DrawLayer(Image<Rgba32> canvas, LayerInfo layer, Aff2 canvasToOut, ref int drawn)
    {
        var tex = layer.Tex!;
        var quad = new Aff2(layer.QuadW, 0, -layer.QuadW / 2f, 0, layer.QuadH, -layer.QuadH / 2f); // 纹理 UV(0..1)->局部坐标
        Aff2 texToOut = Aff2.Mul(canvasToOut, Aff2.Mul(layer.World, quad));
        // 反向映射：输出像素 -> 纹理 UV
        Aff2 outToUv = texToOut.Inverse();

        // 输出包围盒
        var (x0, y0) = texToOut.Apply(0, 0);
        var (x1, y1) = texToOut.Apply(1, 0);
        var (x2, y2) = texToOut.Apply(0, 1);
        var (x3, y3) = texToOut.Apply(1, 1);
        float minX = MathF.Min(MathF.Min(x0, x1), MathF.Min(x2, x3));
        float maxX = MathF.Max(MathF.Max(x0, x1), MathF.Max(x2, x3));
        float minY = MathF.Min(MathF.Min(y0, y1), MathF.Min(y2, y3));
        float maxY = MathF.Max(MathF.Max(y0, y1), MathF.Max(y2, y3));

        int ix0 = Math.Max(0, (int)MathF.Floor(minX));
        int ix1 = Math.Min(canvas.Width - 1, (int)MathF.Ceiling(maxX));
        int iy0 = Math.Max(0, (int)MathF.Floor(minY));
        int iy1 = Math.Min(canvas.Height - 1, (int)MathF.Ceiling(maxY));
        if (ix1 < ix0 || iy1 < iy0) return;

        float alpha = Math.Clamp(layer.EffectiveAlpha, 0f, 1f);
        bool additive = string.Equals(layer.BlendMode, "additive", StringComparison.OrdinalIgnoreCase);
        bool masked = string.Equals(layer.BlendMode, "masked", StringComparison.OrdinalIgnoreCase);
        float tr = layer.TintR, tg = layer.TintG, tb = layer.TintB;

        int tw = tex.Width, th = tex.Height;
        var px = tex.Pixels;

        // 行并行：每个输出像素只读写自己的 row[x]，行间无共享状态，安全可并行。
        // 注意不能用 canvas.ProcessPixelRows（它的 PixelAccessor 是 ref struct，无法被 lambda 捕获，
        // 直接拿底层连续内存按行切片即可）。
        if (!canvas.DangerousTryGetSinglePixelMemory(out var canvasMemory))
        {
            throw new InvalidOperationException("输出画布不是连续内存，无法并行绘制");
        }

        System.Threading.Tasks.Parallel.For(iy0, iy1 + 1, y =>
        {
            var row = canvasMemory.Span.Slice(y * canvas.Width, canvas.Width);
            for (int x = ix0; x <= ix1; x++)
                {
                    var (u, v) = outToUv.Apply(x + 0.5f, y + 0.5f);
                    if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
                    float fx = u * (tw - 1);
                    float fy = v * (th - 1);
                    int x0i = (int)fx, y0i = (int)fy;
                    int x1i = Math.Min(x0i + 1, tw - 1), y1i = Math.Min(y0i + 1, th - 1);
                    float wx = fx - x0i, wy = fy - y0i;
                    Rgba32 c00 = px[y0i * tw + x0i], c10 = px[y0i * tw + x1i];
                    Rgba32 c01 = px[y1i * tw + x0i], c11 = px[y1i * tw + x1i];
                    float r = ((c00.R * (1 - wx) + c10.R * wx) * (1 - wy) + (c01.R * (1 - wx) + c11.R * wx) * wy) * tr;
                    float g = ((c00.G * (1 - wx) + c10.G * wx) * (1 - wy) + (c01.G * (1 - wx) + c11.G * wx) * wy) * tg;
                    float b = ((c00.B * (1 - wx) + c10.B * wx) * (1 - wy) + (c01.B * (1 - wx) + c11.B * wx) * wy) * tb;
                    float a = ((c00.A * (1 - wx) + c10.A * wx) * (1 - wy) + (c01.A * (1 - wx) + c11.A * wx) * wy) / 255f * alpha;
                    if (a <= 0.0001f) continue;
                    if (masked) continue;

                    Rgba32 d = row[x];
                    if (additive)
                    {
                        row[x] = new Rgba32(
                            (byte)Math.Clamp(d.R + r * a, 0, 255),
                            (byte)Math.Clamp(d.G + g * a, 0, 255),
                            (byte)Math.Clamp(d.B + b * a, 0, 255),
                            255);
                    }
                    else
                    {
                        row[x] = new Rgba32(
                            (byte)Math.Clamp(d.R + (r - d.R) * a, 0, 255),
                            (byte)Math.Clamp(d.G + (g - d.G) * a, 0, 255),
                            (byte)Math.Clamp(d.B + (b - d.B) * a, 0, 255),
                            (byte)Math.Clamp(d.A + (255 - d.A) * a, 0, 255));
                    }
                }
            });
        drawn++;
    }

    // ------------------------------------------------------------------ 诊断/主体导出

    public void PrintSceneInfo(TextWriter w)
    {
        w.WriteLine($"PKG: {_pkg.FilePath}  magic={_pkg.Magic} entries={_pkg.Entries.Count} dataStart={_pkg.DataStart} size={_pkg.FileSize:N0}");
        w.WriteLine($"场景画布: {_scene.BaseWidth}x{_scene.BaseHeight}  zoom={_scene.Zoom} clear={_scene.ClearColor} (clearEnabled={_scene.ClearEnabled})");
        w.WriteLine($"对象数: {_scene.Objects.Count}");
        int images = 0, particles = 0, texts = 0, solids = 0, other = 0;
        foreach (var o in _scene.Objects)
        {
            if (o.Image != null) images++;
            else if (o.Particle != null) particles++;
            else if (o.Text != null) texts++;
            else if (o.Solid) solids++;
            else other++;
        }
        w.WriteLine($"  image={images} particle={particles} text={texts} solid={solids} other={other}");
        w.WriteLine();
        foreach (var o in _scene.Objects)
        {
            w.WriteLine(o.ToString());
            if (o.Image == null)
            {
                if (o.Text != null) w.WriteLine($"    text=\"{o.Text}\" font={o.Font} size={o.PointSize:0.##}");
                if (o.Particle != null) w.WriteLine($"    particle={o.Particle}");
                continue;
            }
            var layer = ResolveLayer(o);
            w.Write($"    world=({layer.World.A:0.###},{layer.World.B:0.###},{layer.World.C:0.###};{layer.World.D:0.###},{layer.World.E:0.###},{layer.World.F:0.###})");
            if (layer.TexEntry != null) w.Write($"  tex={layer.TexEntry}");
            if (layer.QuadW > 0) w.Write($"  quad={layer.QuadW:0.#}x{layer.QuadH:0.#}");
            if (layer.BlendMode != "translucent") w.Write($"  blend={layer.BlendMode}");
            w.WriteLine();
            if (layer.SkipReason != null) w.WriteLine($"    ! {layer.SkipReason}");
            else w.WriteLine($"    material={layer.MaterialPath} texture={layer.TextureName} tex={layer.Tex?.Width}x{layer.Tex?.Height}");
        }
    }

    /// <summary>退路：挑视觉主体（可见、面积最大、有贴图）导出原始分辨率。</summary>
    public (LayerInfo? Layer, Image<Rgba32>? Image) BestSubject()
    {
        LayerInfo? best = null;
        double bestScore = double.MinValue;
        foreach (var o in _scene.Objects)
        {
            if (o.Image == null || !o.Visible) continue;
            var layer = ResolveLayer(o);
            if (layer.Tex == null || layer.SkipReason != null) continue;
            double area = (double)layer.QuadW * layer.QuadH * Math.Abs(o.Scale.X * o.Scale.Y);
            if (o.HasSize) area *= 1.0;
            if (area > bestScore) { bestScore = area; best = layer; }
        }
        if (best?.Tex == null) return (null, null);
        var t = best.Tex;
        var img = new Image<Rgba32>(t.Width, t.Height);
        img.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < t.Height; y++)
                t.Pixels.AsSpan(y * t.Width, t.Width).CopyTo(acc.GetRowSpan(y));
        });
        return (best, img);
    }

    public void Dispose() { }
}
