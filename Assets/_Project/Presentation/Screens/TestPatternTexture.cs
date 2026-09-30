using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>Grade clara com borda colorida por número (telas de teste, spec 2B §5). Criada uma vez por tela.</summary>
    public static class TestPatternTexture
    {
        public static Texture2D Create(int number)
        {
            const int width = 256;
            const int height = 144;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "TestPattern" + number,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Color32 accent = Color.HSVToRGB(number * 0.17f % 1f, 0.6f, 0.9f);
            var grid = new Color32(150, 150, 150, 255);
            var background = new Color32(235, 235, 235, 255);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool border = x < 4 || y < 4 || x >= width - 4 || y >= height - 4;
                bool line = x % 16 == 0 || y % 16 == 0;
                pixels[y * width + x] = border ? accent : line ? grid : background;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
