using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SceneRender;

/// <summary>
/// 一个粒子发射器（对应 scene.json 里带 "particle" 的对象）。
/// Wallpaper Engine 的粒子预设是引擎内置的，包内通常没有定义，所以这里按预设名字做"近似还原"：
/// 雪花 / 雨 / 火星 / 萤火 / 花瓣 / 默认漂浮。不追求逐帧一致，追求"看起来是活的"。
/// </summary>
public sealed class ParticleEmitter
{
    public string Preset = "default";
    public float CenterX;
    public float CenterY;
    public float HalfW;
    public float HalfH;
    public int Seed;
    public float Scale = 1f;
}

/// <summary>按时间参数化地模拟粒子（无状态：同一条时间轴永远得到同样的画面，暂停/拖动也不会跳）。</summary>
public static class ParticleRenderer
{
    private readonly record struct Style(
        int Count,
        float MinSize,
        float MaxSize,
        float SpeedY,
        float Sway,
        float Alpha,
        Rgba32 Color,
        bool Additive,
        bool Streak);

    private static Style For(string preset) => preset switch
    {
        "snow" => new Style(120, 1.6f, 4.2f, 42f, 22f, 0.85f, new Rgba32(255, 255, 255), false, false),
        "rain" => new Style(90, 1.1f, 1.8f, 420f, 0f, 0.33f, new Rgba32(214, 232, 255), true, true),
        "ember" => new Style(70, 1.4f, 3.2f, -55f, 16f, 0.75f, new Rgba32(255, 176, 92), true, false),
        "fire" => new Style(90, 2.0f, 5.0f, -90f, 24f, 0.60f, new Rgba32(255, 148, 60), true, false),
        "firefly" => new Style(60, 1.8f, 3.6f, -8f, 30f, 0.70f, new Rgba32(198, 255, 214), true, false),
        "sparkle" => new Style(55, 1.4f, 3.0f, -6f, 18f, 0.65f, new Rgba32(255, 246, 214), true, false),
        "leaf" => new Style(45, 3.0f, 6.5f, 34f, 40f, 0.80f, new Rgba32(255, 214, 224), false, false),
        "petal" => new Style(55, 2.4f, 5.0f, 26f, 36f, 0.80f, new Rgba32(255, 198, 214), false, false),
        "bubble" => new Style(45, 2.0f, 5.5f, -40f, 14f, 0.45f, new Rgba32(206, 236, 255), false, false),
        "fog" => new Style(24, 18f, 42f, -6f, 26f, 0.10f, new Rgba32(236, 242, 255), false, false),
        _ => new Style(45, 1.6f, 3.6f, -14f, 20f, 0.45f, new Rgba32(255, 255, 255), true, false),
    };

    /// <summary>
    /// 把预设名归一化（"particles/presets/snow.json" → "snow"）。
    /// 返回 null 表示"认不出来的自定义粒子"，这种情况宁可不画，也不要随机点缀破坏画面。
    /// </summary>
    public static string? NormalizePreset(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var name = raw.Replace('\\', '/');
        foreach (var suffix in new[] { ".json", ".particle" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^suffix.Length];
        }
        var slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];
        name = name.ToLowerInvariant();

        if (name.Contains("snow")) return "snow";
        if (name.Contains("rain") || name.Contains("drop")) return "rain";
        if (name.Contains("ember") || name.Contains("ash")) return "ember";
        if (name.Contains("fire") || name.Contains("flame")) return "fire";
        if (name.Contains("firefly") || name.Contains("glow")) return "firefly";
        if (name.Contains("fog") || name.Contains("mist") || name.Contains("haze")) return "fog";
        if (name.Contains("leaf") || name.Contains("leaves") || name.Contains("wind")) return "leaf";
        if (name.Contains("petal") || name.Contains("flower") || name.Contains("blossom") || name.Contains("cherry") || name.Contains("sakura")) return "petal";
        if (name.Contains("bubble")) return "bubble";
        if (name.Contains("spark") || name.Contains("star") || name.Contains("dust") || name.Contains("twinkle") ||
            name.Contains("triangle") || name.Contains("shooting")) return "sparkle";
        return null;
    }

    /// <summary>把该帧所有粒子画到画布上（在图片层之后）。</summary>
    public static void Draw(Image<Rgba32> canvas, IReadOnlyList<ParticleEmitter> emitters, Aff2 canvasToOut, double time)
    {
        if (emitters.Count == 0) return;

        var scale = MathF.Sqrt(MathF.Abs(canvasToOut.A * canvasToOut.E - canvasToOut.B * canvasToOut.D));
        if (scale <= 0.0001f) scale = 1f;

        canvas.ProcessPixelRows(acc =>
        {
            var height = acc.Height;
            var width = acc.Width;

            foreach (var emitter in emitters)
            {
                var style = For(emitter.Preset);
                var tail = style.Streak ? 20f : 0f;

                for (var i = 0; i < style.Count; i++)
                {
                    var rnd = Hash(emitter.Seed, i);
                    var life = (float)((time * (0.25 + rnd * 0.55) + rnd) % 1.0);
                    var drift = (float)(rnd * 6.2831853);

                    var cx = emitter.CenterX + (rnd * 2f - 1f) * emitter.HalfW
                             + (float)Math.Sin(time * (0.4 + rnd) + drift) * style.Sway * emitter.Scale;
                    var cy = emitter.CenterY + (life * 2f - 1f) * emitter.HalfH
                             + style.SpeedY * life * 0.06f / MathF.Max(0.25f, emitter.Scale);

                    var radius = (style.MinSize + (style.MaxSize - style.MinSize) * (float)rnd) * emitter.Scale;
                    var alpha = style.Alpha * MathF.Min(1f, MathF.Min(life * 5f, (1f - life) * 5f));

                    DrawDot(acc, width, height, canvasToOut, scale, cx, cy, radius, alpha, style, tail);
                }
            }
        });
    }

    private static void DrawDot(PixelAccessor<Rgba32> acc, int width, int height, Aff2 canvasToOut, float scale,
        float cx, float cy, float radius, float alpha, Style style, float tail)
    {
        if (alpha <= 0.002f) return;

        var (sx, sy) = canvasToOut.Apply(cx, cy);
        var r = MathF.Max(0.7f, radius * scale);
        var r2 = r * r;
        var tailPx = tail * scale;

        var x0 = Math.Max(0, (int)MathF.Floor(sx - r));
        var x1 = Math.Min(width - 1, (int)MathF.Ceiling(sx + r));
        var y0 = Math.Max(0, (int)MathF.Floor(sy - r - tailPx));
        var y1 = Math.Min(height - 1, (int)MathF.Ceiling(sy + r));
        if (x1 < x0 || y1 < y0) return;

        for (var y = y0; y <= y1; y++)
        {
            var row = acc.GetRowSpan(y);
            for (var x = x0; x <= x1; x++)
            {
                var dx = x - sx;
                var dy = y - sy;
                var d2 = dx * dx + dy * dy;

                float a;
                if (d2 <= r2)
                {
                    var t = 1f - MathF.Sqrt(d2 / r2);
                    a = alpha * t * t;
                }
                else if (tailPx > 0.5f && MathF.Abs(dx) < r * 0.7f && dy < 0 && dy > -tailPx)
                {
                    var t = 1f + dy / tailPx;
                    a = alpha * t * 0.45f;
                }
                else
                {
                    continue;
                }

                if (a <= 0.002f) continue;
                ref var p = ref row[x];
                if (style.Additive)
                {
                    p = new Rgba32(
                        (byte)Math.Clamp(p.R + style.Color.R * a, 0, 255),
                        (byte)Math.Clamp(p.G + style.Color.G * a, 0, 255),
                        (byte)Math.Clamp(p.B + style.Color.B * a, 0, 255),
                        p.A);
                }
                else
                {
                    p = new Rgba32(
                        (byte)(p.R * (1 - a) + style.Color.R * a),
                        (byte)(p.G * (1 - a) + style.Color.G * a),
                        (byte)(p.B * (1 - a) + style.Color.B * a),
                        p.A);
                }
            }
        }
    }

    private static float Hash(int seed, int index)
    {
        unchecked
        {
            var h = (uint)(seed * 73856093) ^ (uint)(index * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
