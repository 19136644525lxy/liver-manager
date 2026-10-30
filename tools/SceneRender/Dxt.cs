namespace SceneRender;

public enum DxtKind
{
    Dxt1,
    Dxt3,
    Dxt5,
}

/// <summary>
/// DXT/BC 解码器（BC1/BC2/BC3），移植自 libsquish 的常见实现。
/// 输出 RGBA8888 直通 alpha（非预乘）。
/// </summary>
public static class Dxt
{
    public static byte[] Decode(byte[] src, int width, int height, DxtKind kind)
    {
        int blockBytes = kind == DxtKind.Dxt1 ? 8 : 16;
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        int expected = blocksX * blocksY * blockBytes;
        if (src.Length < expected)
            throw new InvalidDataException($"DXT 数据不足: 需要 {expected} 字节，实际 {src.Length}");

        var dst = new byte[width * height * 4];
        var block = new byte[64]; // 4x4 像素 × RGBA
        int p = 0;

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                Array.Clear(block);
                int colorOff = 0;
                if (kind == DxtKind.Dxt3)
                {
                    DecodeAlphaDxt3(src, p, block);
                    colorOff = 8;
                }
                else if (kind == DxtKind.Dxt5)
                {
                    DecodeAlphaDxt5(src, p, block);
                    colorOff = 8;
                }

                DecodeColor(src, p + colorOff, block, kind == DxtKind.Dxt1);

                int baseX = bx * 4, baseY = by * 4;
                for (int py = 0; py < 4; py++)
                {
                    int y = baseY + py;
                    if (y >= height) break;
                    int row = y * width;
                    for (int px = 0; px < 4; px++)
                    {
                        int x = baseX + px;
                        if (x >= width) break;
                        int si = (py * 4 + px) * 4;
                        int di = (row + x) * 4;
                        dst[di] = block[si];
                        dst[di + 1] = block[si + 1];
                        dst[di + 2] = block[si + 2];
                        dst[di + 3] = block[si + 3];
                    }
                }

                p += blockBytes;
            }
        }
        return dst;
    }

    private static void DecodeColor(byte[] src, int off, byte[] block, bool dxt1)
    {
        ushort c0 = (ushort)(src[off] | (src[off + 1] << 8));
        ushort c1 = (ushort)(src[off + 2] | (src[off + 3] << 8));

        byte r0 = (byte)(((c0 >> 11) & 0x1F) * 255 / 31);
        byte g0 = (byte)(((c0 >> 5) & 0x3F) * 255 / 63);
        byte b0 = (byte)((c0 & 0x1F) * 255 / 31);
        byte r1 = (byte)(((c1 >> 11) & 0x1F) * 255 / 31);
        byte g1 = (byte)(((c1 >> 5) & 0x3F) * 255 / 63);
        byte b1 = (byte)((c1 & 0x1F) * 255 / 31);

        byte[] cr = new byte[4], cg = new byte[4], cb = new byte[4], ca = new byte[4];
        cr[0] = r0; cg[0] = g0; cb[0] = b0; ca[0] = 255;
        cr[1] = r1; cg[1] = g1; cb[1] = b1; ca[1] = 255;

        bool transparentMode = dxt1 && c0 <= c1;
        if (transparentMode)
        {
            cr[2] = (byte)((r0 + r1) / 2);
            cg[2] = (byte)((g0 + g1) / 2);
            cb[2] = (byte)((b0 + b1) / 2);
            ca[2] = 255;
            ca[3] = 0; // 透明黑
        }
        else
        {
            cr[2] = (byte)((2 * r0 + r1) / 3);
            cg[2] = (byte)((2 * g0 + g1) / 3);
            cb[2] = (byte)((2 * b0 + b1) / 3);
            ca[2] = 255;
            cr[3] = (byte)((r0 + 2 * r1) / 3);
            cg[3] = (byte)((g0 + 2 * g1) / 3);
            cb[3] = (byte)((b0 + 2 * b1) / 3);
            ca[3] = 255;
        }

        uint bits = (uint)(src[off + 4] | (src[off + 5] << 8) | (src[off + 6] << 16) | (src[off + 7] << 24));
        for (int i = 0; i < 16; i++)
        {
            int idx = (int)((bits >> (2 * i)) & 0x3);
            int o = i * 4;
            block[o] = cr[idx];
            block[o + 1] = cg[idx];
            block[o + 2] = cb[idx];
            block[o + 3] = ca[idx];
        }
    }

    private static void DecodeAlphaDxt3(byte[] src, int off, byte[] block)
    {
        for (int i = 0; i < 8; i++)
        {
            byte q = src[off + i];
            int lo = q & 0x0F;
            int hi = q & 0xF0;
            block[8 * i + 3] = (byte)(lo | (lo << 4));
            block[8 * i + 7] = (byte)(hi | (hi >> 4));
        }
    }

    private static void DecodeAlphaDxt5(byte[] src, int off, byte[] block)
    {
        byte a0 = src[off];
        byte a1 = src[off + 1];
        var codes = new byte[8];
        codes[0] = a0;
        codes[1] = a1;
        if (a0 <= a1)
        {
            for (int i = 1; i < 5; i++)
                codes[1 + i] = (byte)(((5 - i) * a0 + i * a1) / 5);
            codes[6] = 0;
            codes[7] = 255;
        }
        else
        {
            for (int i = 1; i < 7; i++)
                codes[1 + i] = (byte)(((7 - i) * a0 + i * a1) / 7);
        }

        ulong bits = 0;
        for (int i = 0; i < 6; i++)
            bits |= (ulong)src[off + 2 + i] << (8 * i);

        for (int i = 0; i < 16; i++)
        {
            int idx = (int)((bits >> (3 * i)) & 0x7);
            block[i * 4 + 3] = codes[idx];
        }
    }
}
