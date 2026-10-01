using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.Tests.Screens;

public class CursorBitmapTests
{
    // 2×1 monocromático: stride de 4 bytes por linha; linha 0 = AND, linha 1 = XOR.
    private static byte[] Mono(bool and0, bool and1, bool xor0, bool xor1)
    {
        var m = new byte[8];
        m[0] = (byte)((and0 ? 0x80 : 0) | (and1 ? 0x40 : 0));
        m[4] = (byte)((xor0 ? 0x80 : 0) | (xor1 ? 0x40 : 0));
        return m;
    }

    private static (byte b, byte g, byte r, byte a) Px(CursorImage c, int x, int y)
    {
        int i = (y * c.Width + x) * 4;
        return (c.Bgra[i], c.Bgra[i + 1], c.Bgra[i + 2], c.Bgra[i + 3]);
    }

    [Fact]
    public void Monochrome_BlackWhiteAndTransparent()
    {
        var black = CursorBitmap.FromMonochrome(2, 1, Mono(false, false, false, true), 0, 0);
        Assert.Equal((0, 0, 0, 255), ToInts(Px(black, 0, 0)));
        Assert.Equal((255, 255, 255, 255), ToInts(Px(black, 1, 0)));
        var clear = CursorBitmap.FromMonochrome(2, 1, Mono(true, true, false, false), 0, 0);
        Assert.Equal(0, Px(clear, 0, 0).a);
    }

    [Fact]
    public void Invert_BecomesBlackWithWhiteOutline()
    {
        // pixel 0 = inversão, pixel 1 = transparente vizinho → contorno branco
        var ibeam = CursorBitmap.FromMonochrome(2, 1, Mono(true, true, true, false), 0, 0);
        Assert.Equal((0, 0, 0, 255), ToInts(Px(ibeam, 0, 0)));
        Assert.Equal((255, 255, 255, 255), ToInts(Px(ibeam, 1, 0)));
    }

    [Fact]
    public void ColorWithoutAlpha_UsesTheAndMask()
    {
        var bgra = new byte[] { 10, 20, 30, 0, 40, 50, 60, 0 };
        var mask = new byte[] { 0x40, 0, 0, 0 }; // pixel 1 transparente
        var c = CursorBitmap.FromColor(2, 1, bgra, mask, 0, 0);
        Assert.Equal((10, 20, 30, 255), ToInts(Px(c, 0, 0)));
        Assert.Equal(0, Px(c, 1, 0).a);
    }

    [Fact]
    public void ColorWithAlpha_IsKept()
    {
        var bgra = new byte[] { 1, 2, 3, 128 };
        var c = CursorBitmap.FromColor(1, 1, bgra, null, 0, 0);
        Assert.Equal((1, 2, 3, 128), ToInts(Px(c, 0, 0)));
    }

    [Fact]
    public void Big_IsHalvedToFit_AndKeepsThePhysicalSize()
    {
        var bgra = Enumerable.Repeat((byte)255, 256 * 256 * 4).ToArray();
        var c = CursorBitmap.FromColor(256, 256, bgra, null, 100, 40);
        Assert.True(c.Bgra.Length <= CursorBitmap.MaxBytes);
        Assert.Equal(256, c.Width * c.Scale);
        Assert.Equal((25, 10), (c.HotX, c.HotY));
    }

    [Fact]
    public void SameShape_SameId_DifferentShape_DifferentId()
    {
        var a = CursorBitmap.FromMonochrome(2, 1, Mono(false, false, false, true), 0, 0);
        var b = CursorBitmap.FromMonochrome(2, 1, Mono(false, false, false, true), 0, 0);
        var other = CursorBitmap.FromMonochrome(2, 1, Mono(false, false, true, true), 0, 0);
        Assert.Equal(a.Id, b.Id);
        Assert.NotEqual(a.Id, other.Id);
        Assert.NotEqual(0u, a.Id);
    }

    private static (int, int, int, int) ToInts((byte b, byte g, byte r, byte a) p) => (p.b, p.g, p.r, p.a);
}
