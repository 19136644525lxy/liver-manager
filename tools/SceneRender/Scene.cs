using System.Globalization;
using System.Text.Json;

namespace SceneRender;

public readonly struct Vec2
{
    public readonly float X, Y;
    public Vec2(float x, float y) { X = x; Y = y; }
    public override string ToString() => $"{X:0.###},{Y:0.###}";
}

public readonly struct Vec3
{
    public readonly float X, Y, Z;
    public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
    public override string ToString() => $"{X:0.###},{Y:0.###},{Z:0.###}";
}

public sealed class AnimKey
{
    public int Frame;
    public float Value;
}

public sealed class ValueAnimation
{
    public string Name = "";
    public List<AnimKey> Keys = new();
    public int Fps = 30;
    public int Length;
    public string Mode = "single";

    public float Eval(double timeSeconds)
    {
        if (Keys.Count == 0) return 0f;
        if (Keys.Count == 1) return Keys[0].Value;
        int frame = (int)Math.Floor(timeSeconds * Fps);
        if (Length > 0 && (Mode == "loop" || Mode == "mirror"))
        {
            frame %= Length;
            if (frame < 0) frame += Length;
            if (Mode == "mirror" && (timeSeconds * Fps / Length % 2) >= 1)
                frame = Length - frame;
        }
        if (frame <= Keys[0].Frame) return Keys[0].Value;
        for (int i = 1; i < Keys.Count; i++)
        {
            if (frame <= Keys[i].Frame)
            {
                var a = Keys[i - 1];
                var b = Keys[i];
                if (b.Frame == a.Frame) return b.Value;
                float t = (frame - a.Frame) / (float)(b.Frame - a.Frame);
                return a.Value + (b.Value - a.Value) * t;
            }
        }
        return Keys[^1].Value;
    }
}

public sealed class SceneObject
{
    public int Id;
    public int? Parent;
    public string Name = "";

    public Vec3 Origin;
    public bool OriginAnimated;
    public ValueAnimation? OriginXAnim;
    public ValueAnimation? OriginYAnim;
    public Vec3 Scale = new(1, 1, 1);
    public ValueAnimation? ScaleXAnim;
    public ValueAnimation? ScaleYAnim;
    public Vec3 Angles;          // 弧度
    public ValueAnimation? RotationAnim;
    public float Alpha = 1f;
    public ValueAnimation? AlphaAnim;

    public Vec2 Size;
    public bool HasSize;

    public bool Visible = true;
    public string? VisibleUser;
    public string? VisibleCondition;
    public bool VisibleHasScript;
    public string? VisibleScript;

    public string? Image;
    public string? Particle;
    public string? Text;
    public string? Font;
    public float PointSize;
    public Vec3 Color = new(1, 1, 1);
    public bool Solid;

    public string? RawJson;

    public override string ToString() =>
        $"#{Id} {Name} origin={Origin} size={(HasSize ? Size.ToString() : "-")} scale={Scale} ang={Angles} alpha={Alpha:0.##} visible={Visible}" +
        (Image != null ? $" image={Image}" : "") +
        (Particle != null ? " [particle]" : "") +
        (Text != null ? $" [text:{Text}]" : "");
}

public sealed class SceneDoc
{
    public int BaseWidth = 3840;
    public int BaseHeight = 2160;
    public float Zoom = 1f;
    public Vec3 ClearColor = new(0.7f, 0.7f, 0.7f);
    public bool ClearEnabled = true;
    public List<SceneObject> Objects = new();

    public static SceneDoc Parse(string json)
    {
        var opts = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 512,
        };
        using var doc = JsonDocument.Parse(json, opts);
        var root = doc.RootElement;
        var scene = new SceneDoc();

        if (TryGet(root, "general", out var general))
        {
            if (TryGet(general, "orthogonalprojection", out var proj))
            {
                scene.BaseWidth = (int)GetFloat(proj, "width", 3840);
                scene.BaseHeight = (int)GetFloat(proj, "height", 2160);
            }
            scene.Zoom = GetFloat(general, "zoom", 1f);
            scene.ClearColor = ParseVec3(general, "clearcolor", new Vec3(0.7f, 0.7f, 0.7f));
            scene.ClearEnabled = GetBool(general, "clearenabled", true);
        }

        if (TryGet(root, "objects", out var objects) && objects.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in objects.EnumerateArray())
            {
                var o = ParseObject(el);
                if (o != null) scene.Objects.Add(o);
            }
        }
        return scene;
    }

    private static SceneObject? ParseObject(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var o = new SceneObject { RawJson = el.GetRawText() };

        o.Id = (int)GetFloat(el, "id", -1);
        if (TryGet(el, "parent", out var p))
        {
            if (p.ValueKind == JsonValueKind.Number) o.Parent = (int)p.GetDouble();
            else if (p.ValueKind == JsonValueKind.Object && TryGet(p, "id", out var pid) && pid.ValueKind == JsonValueKind.Number)
                o.Parent = (int)pid.GetDouble();
        }
        o.Name = GetString(el, "name", "");
        o.Solid = GetBool(el, "solid", false);

        if (TryGet(el, "origin", out var origin))
        {
            if (origin.ValueKind == JsonValueKind.Object)
            {
                o.OriginAnimated = true;
                if (TryGet(origin, "value", out var ov) && ov.ValueKind == JsonValueKind.String)
                    o.Origin = ParseVec3String(ov.GetString()!, o.Origin);
                o.OriginXAnim = ParseChannel(origin, "c0");
                o.OriginYAnim = ParseChannel(origin, "c1");
            }
            else o.Origin = ParseVec3Element(origin, o.Origin);
        }
        if (TryGet(el, "scale", out var scale))
        {
            if (scale.ValueKind == JsonValueKind.String) o.Scale = ParseVec3String(scale.GetString()!, new Vec3(1, 1, 1));
            else if (scale.ValueKind == JsonValueKind.Object && TryGet(scale, "value", out var sv))
            {
                if (sv.ValueKind == JsonValueKind.String) o.Scale = ParseVec3String(sv.GetString()!, new Vec3(1, 1, 1));
                else if (sv.ValueKind == JsonValueKind.Number) { float s = sv.GetSingle(); o.Scale = new Vec3(s, s, s); }
                o.ScaleXAnim = ParseChannel(scale, "c0");
                o.ScaleYAnim = ParseChannel(scale, "c1");
            }
        }
        if (TryGet(el, "angles", out var angles))
        {
            if (angles.ValueKind == JsonValueKind.String) o.Angles = ParseVec3String(angles.GetString()!, o.Angles);
            else if (angles.ValueKind == JsonValueKind.Object && TryGet(angles, "value", out var av) && av.ValueKind == JsonValueKind.String)
            {
                o.Angles = ParseVec3String(av.GetString()!, o.Angles);
                o.RotationAnim = ParseChannel(angles, "c0");
            }
        }
        if (TryGet(el, "size", out var size))
        {
            if (size.ValueKind == JsonValueKind.String)
            {
                o.Size = ParseVec2String(size.GetString()!, o.Size);
                o.HasSize = o.Size.X > 0 && o.Size.Y > 0;
            }
        }
        o.Alpha = GetFloat(el, "alpha", 1f);
        if (TryGet(el, "alpha", out var alphaEl) && alphaEl.ValueKind == JsonValueKind.Object)
        {
            o.Alpha = GetFloat(alphaEl, "value", o.Alpha);
            o.AlphaAnim = ParseValueAnim(alphaEl);
        }
        ParseVisible(el, o);

        o.Image = GetStringOrNull(el, "image");
        o.Particle = GetStringOrNull(el, "particle");
        o.Text = GetStringOrNull(el, "text");
        o.Font = GetStringOrNull(el, "font");
        o.PointSize = GetFloat(el, "pointsize", 0);
        o.Color = ParseVec3(el, "color", o.Color);
        return o;
    }

    private static void ParseVisible(JsonElement el, SceneObject o)
    {
        if (!TryGet(el, "visible", out var vis)) return;
        if (vis.ValueKind == JsonValueKind.True) { o.Visible = true; return; }
        if (vis.ValueKind == JsonValueKind.False) { o.Visible = false; return; }
        if (vis.ValueKind != JsonValueKind.Object) return;

        if (TryGet(vis, "script", out var script) && script.ValueKind == JsonValueKind.String)
        {
            o.VisibleHasScript = true;
            o.VisibleScript = script.GetString();
        }
        if (TryGet(vis, "user", out var user))
        {
            if (user.ValueKind == JsonValueKind.String) o.VisibleUser = user.GetString();
            else if (user.ValueKind == JsonValueKind.Object)
            {
                o.VisibleUser = GetStringOrNull(user, "name");
                o.VisibleCondition = GetStringOrNull(user, "condition");
            }
        }
        if (TryGet(vis, "value", out var value))
        {
            if (value.ValueKind == JsonValueKind.True) o.Visible = true;
            else if (value.ValueKind == JsonValueKind.False) o.Visible = false;
            else if (value.ValueKind == JsonValueKind.Object && TryGet(value, "value", out var vv))
                o.Visible = vv.ValueKind != JsonValueKind.False && vv.ValueKind != JsonValueKind.Null;
        }
        // 带脚本的 visible：WE 编辑器里脚本运行后的值不可静态推断（例如 timeVarying 联动），
        // 而用户保存时 value 往往记录的是"被脚本隐藏"的状态。场景美术层通常应当显示，
        // 因此这里对带脚本的层一律视为可见（否则会整层丢失，例如 3470764447 的背景）。
        if (o.VisibleHasScript) o.Visible = true;
    }

    public static ValueAnimation? ParseValueAnim(JsonElement owner)
    {
        if (!TryGet(owner, "animation", out var anim) || anim.ValueKind != JsonValueKind.Object) return null;
        var va = new ValueAnimation();
        foreach (var p in anim.EnumerateObject())
        {
            if (!p.Name.StartsWith("c", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var kf in p.Value.EnumerateArray())
            {
                if (kf.ValueKind != JsonValueKind.Object) continue;
                var key = new AnimKey
                {
                    Frame = (int)GetFloat(kf, "frame", 0),
                    Value = GetFloat(kf, "value", 0),
                };
                va.Keys.Add(key);
            }
            va.Name = p.Name;
            break;
        }
        if (TryGet(anim, "options", out var opt))
        {
            va.Fps = (int)GetFloat(opt, "fps", 30);
            va.Length = (int)GetFloat(opt, "length", 0);
            va.Mode = GetString(opt, "mode", "single");
        }
        if (va.Keys.Count == 0) return null;
        va.Keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        return va;
    }

    /// <summary>
    /// 解析单个动画通道（c0 / c1 …）。用于 origin（c0=X, c1=Y）、scale（c0/c1）、angles（c0=旋转）。
    /// 结构：{ "animation": { "c0": [ {"frame":0,"value":1}, … ], "options": { "fps":30, "length":90, "mode":"loop" } } }
    /// </summary>
    public static ValueAnimation? ParseChannel(JsonElement owner, string channel)
    {
        if (!TryGet(owner, "animation", out var anim) || anim.ValueKind != JsonValueKind.Object) return null;
        if (!TryGet(anim, channel, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;

        var va = new ValueAnimation { Name = channel };
        foreach (var kf in arr.EnumerateArray())
        {
            if (kf.ValueKind != JsonValueKind.Object) continue;
            va.Keys.Add(new AnimKey
            {
                Frame = (int)GetFloat(kf, "frame", 0),
                Value = GetFloat(kf, "value", 0),
            });
        }
        if (TryGet(anim, "options", out var opt))
        {
            va.Fps = (int)GetFloat(opt, "fps", 30);
            va.Length = (int)GetFloat(opt, "length", 0);
            va.Mode = GetString(opt, "mode", "single");
        }
        if (va.Keys.Count == 0) return null;
        va.Keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        return va;
    }

    // ---------- JSON 小工具（属性名大小写不敏感；重复键取最后一个） ----------

    public static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        bool found = false;
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                found = true;
            }
        }
        return found;
    }

    public static float GetFloat(JsonElement obj, string name, float def)
    {
        if (!TryGet(obj, name, out var v)) return def;
        return ToFloat(v, def);
    }

    public static float ToFloat(JsonElement v, float def)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                return v.GetSingle();
            case JsonValueKind.String:
                return float.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;
            case JsonValueKind.Object:
                if (TryGet(v, "value", out var inner)) return ToFloat(inner, def);
                return def;
            default:
                return def;
        }
    }

    public static bool GetBool(JsonElement obj, string name, bool def)
    {
        if (!TryGet(obj, name, out var v)) return def;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => TryGet(v, "value", out var inner) && inner.ValueKind == JsonValueKind.True,
            _ => def,
        };
    }

    public static string GetString(JsonElement obj, string name, string def)
    {
        if (!TryGet(obj, name, out var v)) return def;
        return v.ValueKind == JsonValueKind.String ? v.GetString()! : def;
    }

    public static string? GetStringOrNull(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind == JsonValueKind.Object && TryGet(v, "value", out var inner) && inner.ValueKind == JsonValueKind.String)
            return inner.GetString();
        return null;
    }

    public static Vec3 ParseVec3(JsonElement obj, string name, Vec3 def)
    {
        if (!TryGet(obj, name, out var v)) return def;
        return ParseVec3Element(v, def);
    }

    public static Vec3 ParseVec3Element(JsonElement v, Vec3 def)
    {
        if (v.ValueKind == JsonValueKind.String) return ParseVec3String(v.GetString()!, def);
        if (v.ValueKind == JsonValueKind.Object && TryGet(v, "value", out var inner))
        {
            if (inner.ValueKind == JsonValueKind.String) return ParseVec3String(inner.GetString()!, def);
            if (inner.ValueKind == JsonValueKind.Number) { float s = inner.GetSingle(); return new Vec3(s, s, s); }
        }
        if (v.ValueKind == JsonValueKind.Number) { float s = v.GetSingle(); return new Vec3(s, s, s); }
        return def;
    }

    public static Vec3 ParseVec3String(string s, Vec3 def)
    {
        var parts = SplitNumbers(s);
        if (parts.Length >= 3) return new Vec3(parts[0], parts[1], parts[2]);
        if (parts.Length == 2) return new Vec3(parts[0], parts[1], 0);
        if (parts.Length == 1) return new Vec3(parts[0], parts[0], parts[0]);
        return def;
    }

    public static Vec2 ParseVec2String(string s, Vec2 def)
    {
        var parts = SplitNumbers(s);
        if (parts.Length >= 2) return new Vec2(parts[0], parts[1]);
        if (parts.Length == 1) return new Vec2(parts[0], parts[0]);
        return def;
    }

    private static float[] SplitNumbers(string s)
    {
        var fields = s.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<float>(fields.Length);
        foreach (var f in fields)
        {
            if (float.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) list.Add(v);
        }
        return list.ToArray();
    }
}
