using System.Text;

namespace SceneRender;

/// <summary>Wallpaper Engine PKG 包内的一个条目。</summary>
public sealed class PkgEntry
{
    public string Name = "";
    public long Offset;
    public int Length;
    public override string ToString() => $"{Name}\toffset={Offset}\tlength={Length}";
}

/// <summary>
/// scene.pkg 容器读取器（PKGV0001..PKGV0024，实测布局一致）：
///   int32 magicLen + ASCII magic + int32 entryCount
///   entryCount * { int32 nameLen + UTF8 name + int32 offset + int32 length }
///   dataStart = 条目表结束位置；条目数据 = dataStart + offset
/// </summary>
public sealed class ScenePackage : IDisposable
{
    private readonly FileStream _stream;
    private readonly List<PkgEntry> _entries = new();

    public string FilePath { get; }
    public string Magic { get; private set; } = "";
    public long DataStart { get; private set; }
    public long FileSize => _stream.Length;
    public IReadOnlyList<PkgEntry> Entries => _entries;

    private ScenePackage(string path, FileStream stream)
    {
        FilePath = path;
        _stream = stream;
    }

    public static ScenePackage Open(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var pkg = new ScenePackage(full, fs);
        try
        {
            pkg.ReadTable();
            return pkg;
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    private void ReadTable()
    {
        _stream.Position = 0;
        using var br = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);

        int magicLen = br.ReadInt32();
        if (magicLen <= 0 || magicLen > 64)
            throw new InvalidDataException($"PKG magic 长度异常: {magicLen}");
        Magic = Encoding.ASCII.GetString(br.ReadBytes(magicLen));
        if (!Magic.StartsWith("PKGV", StringComparison.Ordinal))
            throw new InvalidDataException($"不是 Wallpaper Engine PKG 文件（magic=\"{Magic}\"）");

        int count = br.ReadInt32();
        if (count < 0 || count > 1_500_000)
            throw new InvalidDataException($"PKG 条目数异常: {count}");

        _entries.Capacity = count;
        for (int i = 0; i < count; i++)
        {
            int nameLen = br.ReadInt32();
            if (nameLen < 0 || nameLen > 8192)
                throw new InvalidDataException($"PKG 条目名长度异常: {nameLen}");
            string name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
            long off = br.ReadInt32();
            int len = br.ReadInt32();
            _entries.Add(new PkgEntry { Name = name, Offset = off, Length = len });
        }

        DataStart = _stream.Position;
        long avail = _stream.Length - DataStart;
        foreach (var e in _entries)
        {
            if (e.Offset < 0 || e.Length < 0 || e.Offset + e.Length > avail)
                Console.Error.WriteLine($"[警告] 条目偏移越界: {e.Name} offset={e.Offset} len={e.Length} avail={avail}");
        }
    }

    public PkgEntry? Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var norm = name.Replace('\\', '/').TrimStart('/');

        foreach (var e in _entries)
            if (string.Equals(e.Name, name, StringComparison.Ordinal)) return e;

        foreach (var e in _entries)
            if (string.Equals(e.Name, norm, StringComparison.OrdinalIgnoreCase)) return e;

        foreach (var e in _entries)
        {
            string n = e.Name.Replace('\\', '/');
            if (n.EndsWith("/" + norm, StringComparison.OrdinalIgnoreCase)) return e;
        }

        // 允许传不带目录的文件名
        string bare = norm.Contains('/') ? norm[(norm.LastIndexOf('/') + 1)..] : norm;
        foreach (var e in _entries)
        {
            string n = e.Name.Replace('\\', '/');
            string eb = n.Contains('/') ? n[(n.LastIndexOf('/') + 1)..] : n;
            if (string.Equals(eb, bare, StringComparison.OrdinalIgnoreCase)) return e;
        }
        return null;
    }

    public byte[] ReadEntryBytes(PkgEntry entry)
    {
        var buf = new byte[entry.Length];
        _stream.Position = DataStart + entry.Offset;
        int read = 0;
        while (read < buf.Length)
        {
            int n = _stream.Read(buf, read, buf.Length - read);
            if (n <= 0) throw new EndOfStreamException($"读取条目失败: {entry.Name}");
            read += n;
        }
        return buf;
    }

    public byte[]? TryReadBytes(string name)
    {
        var e = Find(name);
        return e == null ? null : ReadEntryBytes(e);
    }

    public string? TryReadText(string name)
    {
        var b = TryReadBytes(name);
        return b == null ? null : Encoding.UTF8.GetString(b);
    }

    public void Dispose() => _stream.Dispose();
}
