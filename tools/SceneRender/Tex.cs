using System.Text;
using K4os.Compression.LZ4;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SceneRender;

public enum TexFormat
{
    RGBA8888 = 0,
    DXT5 = 4,
    DXT3 = 6,
    DXT1 = 7,
    RG88 = 8,
    R8 = 9,
}

[Flags]
public enum TexFlags
{
    None = 0,
    NoInterpolation = 1,
    ClampUVs = 2,
    IsGif = 4,
    Unk3 = 8,
    Unk4 = 16,
    IsVideoTexture = 32,
    Unk6 = 64,
    Unk7 = 128,
}

public enum TexMipFormat
{
    Raw,        // RGBA8888 / R8 / RG88
    DXT1,
    DXT3,
    DXT5,
    VideoMp4,
    EmbeddedImage, // PNG/JPEG/... 整份文件直接塞在 mip0
}

public sealed class TexMipmap
{
    public int Width;
    public int Height;
    public bool Lz4;
    public int DecompressedLength;
    public int ByteLength;
    public byte[]? Bytes; // 仅 mip0 保留
    public override string ToString() => $"{Width}x{Height} lz4={Lz4} decompressed={DecompressedLength} bytes={ByteLength}";
}

public sealed class TexImage
{
    public List<TexMipmap> Mipmaps = new();
}

/// <summary>
/// TEX 文件（TEXV0005 / TEXI0001 / TEXB0001..TEXB0004）读取器。
/// 只保留 mip0 数据，其余 mip 直接跳过以节省内存。
/// </summary>
public sealed class TexFile
{
    public string Version = "";
    public string ImageVersion = "";
    public TexFormat Format;
    public TexFlags Flags;
    public int TextureWidth;
    public int TextureHeight;
    public int ImageWidth;
    public int ImageHeight;
    public uint UnkInt0;

    public string ContainerMagic = "";
    public int ContainerVersion;
    public int ContainerImageFormat;
    public bool VideoFlag;
    public bool IsGif => (Flags & TexFlags.IsGif) != 0;
    public bool IsVideoTexture => VideoFlag || (Flags & TexFlags.IsVideoTexture) != 0;

    public List<TexImage> Images = new();
    public TexMipFormat MipFormat;
    public string? GifJson;

    public TexMipmap PrimaryMipmap => Images[0].Mipmaps[0];

    private sealed class Cursor
    {
        public byte[] Data;
        public int Pos;
        public Cursor(byte[] data) { Data = data; }

        public void Need(int n)
        {
            if (n < 0 || Pos + n > Data.Length)
                throw new InvalidDataException($"TEX 解析越界: 需要 {n} 字节，剩余 {Data.Length - Pos}");
        }
        public int Int32()
        {
            Need(4);
            int v = BitConverter.ToInt32(Data, Pos);
            Pos += 4;
            return v;
        }
        public uint UInt32()
        {
            Need(4);
            uint v = BitConverter.ToUInt32(Data, Pos);
            Pos += 4;
            return v;
        }
        public byte[] Bytes(int n)
        {
            Need(n);
            var b = new byte[n];
            Buffer.BlockCopy(Data, Pos, b, 0, n);
            Pos += n;
            return b;
        }
        public void Skip(long n)
        {
            if (n < 0 || Pos + n > Data.Length)
                throw new InvalidDataException($"TEX 解析越界(跳过): {n}");
            Pos += (int)n;
        }
        public string NString(int max = 4096)
        {
            // repkg 的 ReadNString 是「读到 0x00 终止」的 C 字符串（magic 如 "TEXV0005\0"）
            int start = Pos;
            while (Pos < Data.Length && Data[Pos] != 0)
            {
                Pos++;
                if (Pos - start > max)
                    throw new InvalidDataException($"TEX 字符串过长(> {max} 字节)");
            }
            if (Pos >= Data.Length)
                throw new InvalidDataException("TEX 字符串缺少终止符");
            string s = Encoding.UTF8.GetString(Data, start, Pos - start);
            Pos++; // 跳过 '\0'
            return s;
        }
    }

    public static TexFile FromBytes(byte[] data)
    {
        var c = new Cursor(data);
        var tex = new TexFile { Version = c.NString(16) };
        if (tex.Version != "TEXV0005")
            throw new InvalidDataException($"不是 TEX 文件（magic=\"{tex.Version}\"）");
        tex.ImageVersion = c.NString(16);
        if (tex.ImageVersion != "TEXI0001")
            throw new InvalidDataException($"TEX 图像头 magic 异常: \"{tex.ImageVersion}\"");

        tex.Format = (TexFormat)c.Int32();
        tex.Flags = (TexFlags)c.Int32();
        tex.TextureWidth = c.Int32();
        tex.TextureHeight = c.Int32();
        tex.ImageWidth = c.Int32();
        tex.ImageHeight = c.Int32();
        tex.UnkInt0 = c.UInt32();

        tex.ContainerMagic = c.NString(16);
        int imageCount = c.Int32();
        if (imageCount <= 0 || imageCount > 100000)
            throw new InvalidDataException($"TEX imageCount 异常: {imageCount}");

        tex.ContainerVersion = tex.ContainerMagic switch
        {
            "TEXB0001" => 1,
            "TEXB0002" => 2,
            "TEXB0003" => 3,
            "TEXB0004" => 4,
            _ => throw new InvalidDataException($"未知 TEX 容器版本: \"{tex.ContainerMagic}\""),
        };

        if (tex.ContainerVersion == 3)
        {
            tex.ContainerImageFormat = c.Int32();
        }
        else if (tex.ContainerVersion == 4)
        {
            tex.ContainerImageFormat = c.Int32();
            tex.VideoFlag = c.Int32() == 1;
        }

        // repkg 语义：TEXB0004 只有在实际是 MP4 视频时才按 V4 的 mipmap 结构读，
        // 其它（例如内嵌 PNG）会降级为 V3 结构。
        bool mipmapV4 = tex.ContainerVersion == 4 && tex.VideoFlag;

        tex.MipFormat = DetermineFormat(tex);

        for (int i = 0; i < imageCount; i++)
        {
            var img = new TexImage();
            int mipCount = c.Int32();
            if (mipCount < 0 || mipCount > 64)
                throw new InvalidDataException($"TEX mipmapCount 异常: {mipCount}");

            for (int m = 0; m < mipCount; m++)
            {
                var mip = new TexMipmap();
                if (mipmapV4)
                {
                    int param1 = c.Int32();
                    int param2 = c.Int32();
                    if (param1 != 1 || param2 != 2)
                        throw new InvalidDataException($"TEXB0004 mipmap 参数异常: {param1},{param2}");
                    string condJson = c.NString(65536);
                    int param3 = c.Int32();
                    if (param3 != 1)
                        throw new InvalidDataException($"TEXB0004 mipmap 参数异常(param3): {param3}");
                    if (i == 0 && m == 0 && !string.IsNullOrWhiteSpace(condJson) && condJson != "{}")
                        tex.GifJson ??= condJson;
                    mip.Width = c.Int32();
                    mip.Height = c.Int32();
                    mip.Lz4 = c.Int32() == 1;
                    mip.DecompressedLength = c.Int32();
                    mip.ByteLength = c.Int32();
                }
                else if (tex.ContainerVersion == 1)
                {
                    mip.Width = c.Int32();
                    mip.Height = c.Int32();
                    mip.ByteLength = c.Int32();
                }
                else
                {
                    mip.Width = c.Int32();
                    mip.Height = c.Int32();
                    mip.Lz4 = c.Int32() == 1;
                    mip.DecompressedLength = c.Int32();
                    mip.ByteLength = c.Int32();
                }

                if (mip.ByteLength < 0)
                    throw new InvalidDataException("TEX mipmap 字节数为负");

                bool keep = i == 0 && m == 0;
                if (keep)
                {
                    mip.Bytes = c.Bytes(mip.ByteLength);
                    if (mip.Lz4)
                    {
                        if (mip.DecompressedLength <= 0)
                            throw new InvalidDataException("TEX LZ4 mipmap 解压长度非法");
                        var outBuf = new byte[mip.DecompressedLength];
                        int written = LZ4Codec.Decode(mip.Bytes, 0, mip.Bytes.Length, outBuf, 0, outBuf.Length);
                        if (written != outBuf.Length)
                            Console.Error.WriteLine($"[警告] LZ4 解压长度不符: 期望 {outBuf.Length}, 得到 {written}");
                        mip.Bytes = outBuf;
                    }
                }
                else
                {
                    c.Skip(mip.ByteLength);
                }

                img.Mipmaps.Add(mip);
            }
            tex.Images.Add(img);
        }

        if (tex.Images.Count == 0 || tex.Images[0].Mipmaps.Count == 0)
            throw new InvalidDataException("TEX 没有图像数据");

        return tex;
    }

    private static TexMipFormat DetermineFormat(TexFile tex)
    {
        if (tex.VideoFlag) return TexMipFormat.VideoMp4;
        if (tex.ContainerVersion >= 3 && tex.ContainerImageFormat > 0)
            return TexMipFormat.EmbeddedImage;
        return tex.Format switch
        {
            TexFormat.DXT1 => TexMipFormat.DXT1,
            TexFormat.DXT3 => TexMipFormat.DXT3,
            TexFormat.DXT5 => TexMipFormat.DXT5,
            TexFormat.RGBA8888 or TexFormat.R8 or TexFormat.RG88 => TexMipFormat.Raw,
            _ => throw new InvalidDataException($"不支持的 TEX 像素格式: {(int)tex.Format}" + $" ({(tex.Format)})"),
        };
    }

    /// <summary>mip0 是否其实是 MP4（部分包不设置 video flag）。</summary>
    public bool MipLooksLikeMp4()
    {
        var b = PrimaryMipmap.Bytes;
        if (b == null || b.Length < 12) return false;
        string ft = Encoding.ASCII.GetString(b, 4, 8);
        return ft is "ftypisom" or "ftypmsnv" or "ftypmp42" or "ftypavc1" or "ftypM4V " or "ftypdash";
    }

    /// <summary>解码 mip0 为 RGBA 图像；视频贴图抛 VideoTextureException。</summary>
    public Image<Rgba32> DecodeImage()
    {
        var mip = PrimaryMipmap;
        var bytes = mip.Bytes ?? throw new InvalidDataException("TEX mip0 数据为空");

        if (MipFormat == TexMipFormat.VideoMp4 || MipLooksLikeMp4())
            throw new VideoTextureException(bytes);

        Image<Rgba32> image = MipFormat switch
        {
            TexMipFormat.EmbeddedImage => LoadEmbedded(bytes),
            TexMipFormat.Raw => LoadRaw(mip, bytes),
            TexMipFormat.DXT1 => LoadDxt(mip, bytes, DxtKind.Dxt1),
            TexMipFormat.DXT3 => LoadDxt(mip, bytes, DxtKind.Dxt3),
            TexMipFormat.DXT5 => LoadDxt(mip, bytes, DxtKind.Dxt5),
            _ => throw new InvalidDataException($"不支持的 mip 格式: {MipFormat}"),
        };


        int cropW = ImageWidth > 0 ? ImageWidth : image.Width;
        int cropH = ImageHeight > 0 ? ImageHeight : image.Height;
        if (image.Width != cropW || image.Height != cropH)
        {
            int w = Math.Min(cropW, image.Width);
            int h = Math.Min(cropH, image.Height);
            if (w != image.Width || h != image.Height)
                image.Mutate(x => x.Crop(new Rectangle(0, 0, w, h)));
        }
        return image;
    }

    private static Image<Rgba32> LoadEmbedded(byte[] bytes)
    {
        using var img = Image.Load(bytes);
        return img.CloneAs<Rgba32>();
    }

    private Image<Rgba32> LoadRaw(TexMipmap mip, byte[] bytes)
    {
        int w = mip.Width, h = mip.Height;
        switch (Format)
        {
            case TexFormat.RGBA8888:
            {
                int need = w * h * 4;
                if (bytes.Length < need)
                    throw new InvalidDataException($"RGBA8888 数据不足: 需要 {need}, 实际 {bytes.Length}");
                return Image.LoadPixelData<Rgba32>(bytes, w, h);
            }
            case TexFormat.R8:
            {
                int need = w * h;
                if (bytes.Length < need)
                    throw new InvalidDataException($"R8 数据不足: 需要 {need}, 实际 {bytes.Length}");
                using var gray = Image.LoadPixelData<L8>(bytes, w, h);
                return gray.CloneAs<Rgba32>();
            }
            case TexFormat.RG88:
            {
                int need = w * h * 2;
                if (bytes.Length < need)
                    throw new InvalidDataException($"RG88 数据不足: 需要 {need}, 实际 {bytes.Length}");
                var img = new Image<Rgba32>(w, h);
                img.ProcessPixelRows(acc =>
                {
                    for (int y = 0; y < h; y++)
                    {
                        var row = acc.GetRowSpan(y);
                        for (int x = 0; x < w; x++)
                        {
                            int i = (y * w + x) * 2;
                            row[x] = new Rgba32(bytes[i], bytes[i + 1], 0, 255);
                        }
                    }
                });
                return img;
            }
            default:
                throw new NotSupportedException($"不支持的原始像素格式: {Format}");
        }
    }

    private static Image<Rgba32> LoadDxt(TexMipmap mip, byte[] bytes, DxtKind kind)
    {
        var rgba = Dxt.Decode(bytes, mip.Width, mip.Height, kind);
        return Image.LoadPixelData<Rgba32>(rgba, mip.Width, mip.Height);
    }
}

/// <summary>TEX 的 mip0 是内嵌 MP4 视频（Wallpaper Engine 视频贴图）。</summary>
public sealed class VideoTextureException : Exception
{
    public byte[] Mp4Bytes { get; }
    public VideoTextureException(byte[] mp4Bytes)
        : base("该 TEX 是内嵌 MP4 视频贴图，需要先抽帧")
    {
        Mp4Bytes = mp4Bytes;
    }
}
