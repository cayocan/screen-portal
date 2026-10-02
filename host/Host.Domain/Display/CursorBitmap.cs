namespace ScreenPortal.Host.Domain.Display;

/// <summary>Forma de cursor pronta para o headset: BGRA de cima para baixo, alfa direto (spec 3B §3).</summary>
public sealed record CursorImage(uint Id, int Width, int Height, int HotX, int HotY, int Scale, byte[] Bgra);

/// <summary>
/// Converte os bitmaps de um cursor do Windows (GetIconInfo/GetDIBits) em BGRA. Monocromático: AND/XOR — o pixel de
/// inversão (AND=1, XOR=1, ex.: I-beam) vira preto, e os transparentes em volta ganham contorno branco de 1 px (visível
/// em qualquer fundo). Reduz pela metade até caber em <see cref="MaxBytes"/> (o quadro de controle tem 64 KB).
/// </summary>
public static class CursorBitmap
{
    public const int MaxBytes = 60_000;

    /// <param name="bgra">32 bpp de cima para baixo (largura × altura × 4).</param>
    /// <param name="andMask">máscara AND 1 bpp (linhas alinhadas a 32 bits), usada se o bitmap não tiver alfa; null = opaco.</param>
    public static CursorImage FromColor(int width, int height, byte[] bgra, byte[]? andMask, int hotX, int hotY)
    {
        var pixels = (byte[])bgra.Clone();
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) { hasAlpha = true; break; }
        if (!hasAlpha)
        {
            int stride = MaskStride(width);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[(y * width + x) * 4 + 3] = andMask != null && Bit(andMask, stride, x, y) ? (byte)0 : (byte)255;
        }
        return Finish(width, height, pixels, hotX, hotY);
    }

    /// <param name="mask">AND (linhas 0..h−1) seguida de XOR (linhas h..2h−1), 1 bpp, linhas alinhadas a 32 bits, de cima para baixo.</param>
    public static CursorImage FromMonochrome(int width, int height, byte[] mask, int hotX, int hotY)
    {
        int stride = MaskStride(width);
        var pixels = new byte[width * height * 4];
        var invert = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool and = Bit(mask, stride, x, y), xor = Bit(mask, stride, x, y + height);
                int i = (y * width + x) * 4;
                if (and && !xor) continue; // transparente
                byte v = !and && xor ? (byte)255 : (byte)0; // branco, preto ou inversão (preto)
                pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
                pixels[i + 3] = 255;
                invert[y * width + x] = and && xor;
            }
        }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (pixels[i + 3] != 0 || !NextToInvert(invert, width, height, x, y)) continue;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 255;
            }
        }
        return Finish(width, height, pixels, hotX, hotY);
    }

    private static bool NextToInvert(bool[] invert, int w, int h, int x, int y) =>
        (x > 0 && invert[y * w + x - 1]) || (x < w - 1 && invert[y * w + x + 1])
        || (y > 0 && invert[(y - 1) * w + x]) || (y < h - 1 && invert[(y + 1) * w + x]);

    private static int MaskStride(int width) => (width + 31) / 32 * 4;

    private static bool Bit(byte[] mask, int stride, int x, int y)
    {
        int at = y * stride + (x >> 3);
        return at < mask.Length && (mask[at] & (0x80 >> (x & 7))) != 0;
    }

    private static CursorImage Finish(int width, int height, byte[] pixels, int hotX, int hotY)
    {
        int scale = 1;
        while (width * height * 4 > MaxBytes && width > 1 && height > 1)
        {
            pixels = Half(pixels, width, height);
            width /= 2;
            height /= 2;
            hotX /= 2;
            hotY /= 2;
            scale *= 2;
        }
        return new CursorImage(Hash(width, height, hotX, hotY, scale, pixels), width, height, hotX, hotY, scale, pixels);
    }

    /// <summary>2×2 → 1, média com alfa pré-multiplicado (bordas sem halo escuro).</summary>
    private static byte[] Half(byte[] src, int w, int h)
    {
        int hw = w / 2, hh = h / 2;
        var dst = new byte[hw * hh * 4];
        for (int y = 0; y < hh; y++)
        {
            for (int x = 0; x < hw; x++)
            {
                int a = 0, b = 0, g = 0, r = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int i = ((y * 2 + dy) * w + x * 2 + dx) * 4;
                        int alpha = src[i + 3];
                        b += src[i] * alpha;
                        g += src[i + 1] * alpha;
                        r += src[i + 2] * alpha;
                        a += alpha;
                    }
                int o = (y * hw + x) * 4;
                if (a > 0)
                {
                    dst[o] = (byte)(b / a);
                    dst[o + 1] = (byte)(g / a);
                    dst[o + 2] = (byte)(r / a);
                }
                dst[o + 3] = (byte)(a / 4);
            }
        }
        return dst;
    }

    /// <summary>FNV-1a: a mesma forma tem sempre o mesmo id (o headset guarda por id); 0 é reservado para "sem cursor".</summary>
    private static uint Hash(int w, int h, int hx, int hy, int scale, byte[] pixels)
    {
        uint hash = 2166136261;
        void Mix(int v) { for (int i = 0; i < 4; i++) { hash ^= (byte)(v >> (i * 8)); hash *= 16777619; } }
        Mix(w); Mix(h); Mix(hx); Mix(hy); Mix(scale);
        foreach (var p in pixels) { hash ^= p; hash *= 16777619; }
        return hash == 0 ? 1 : hash;
    }
}
