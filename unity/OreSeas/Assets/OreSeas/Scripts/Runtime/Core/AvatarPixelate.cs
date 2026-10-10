using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OreSeas
{
    /// <summary>
    /// Turns any photo into a 20x20 portrait in the game's style: centre square crop, area-average downscale, a little
    /// contrast/saturation, then every pixel snapped to the NUGSHIP palette (no dithering, like the hand-made portraits).
    /// </summary>
    public static class AvatarPixelate
    {
        public const int N = Avatars.Inner;
        static Color32[] palette;

        static Color32[] Palette()
        {
            if (palette != null) return palette;
            var list = new List<Color32>();
            foreach (var f in typeof(Pal).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.FieldType == typeof(Color32) && f.Name != "Clear") { var c = (Color32)f.GetValue(null); if (!list.Exists(x => x.r == c.r && x.g == c.g && x.b == c.b)) list.Add(c); }
            // a few skin / neutral tones so faces don't go grey-green
            foreach (var h in new[] { "c88a64", "a8714c", "7a5238", "e0a07a", "f0c8a0", "5e3c26", "3a2a20", "d8a07a", "b07050", "8a5a3a" }) list.Add(Pal.Hex(h));
            return palette = list.ToArray();
        }

        /// <summary>src must be CPU-readable (Texture2D.LoadImage output is). Returns a point-filtered 20x20 texture.</summary>
        public static Texture2D FromPhoto(Texture2D src)
        {
            int s = Mathf.Min(src.width, src.height), ox = (src.width - s) / 2, oy = (src.height - s) / 2;
            var px = src.GetPixels32();
            var outTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "avatar_upload" };
            var dst = new Color32[N * N];
            var pal = Palette();
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int x0 = ox + x * s / N, x1 = Mathf.Max(x0 + 1, ox + (x + 1) * s / N), y0 = oy + y * s / N, y1 = Mathf.Max(y0 + 1, oy + (y + 1) * s / N);
                    double r = 0, g = 0, b = 0; int n = 0;
                    for (int yy = y0; yy < y1; yy++) { int row = yy * src.width; for (int xx = x0; xx < x1; xx++) { var c = px[row + xx]; float a = c.a / 255f; r += c.r * a + 40 * (1 - a); g += c.g * a + 30 * (1 - a); b += c.b * a + 60 * (1 - a); n++; } }
                    r /= n; g /= n; b /= n;
                    // contrast + saturation lift (small sprites read better slightly punchy)
                    double l = 0.299 * r + 0.587 * g + 0.114 * b;
                    r = l + (r - l) * 1.25; g = l + (g - l) * 1.25; b = l + (b - l) * 1.25;
                    r = (r - 128) * 1.12 + 128; g = (g - 128) * 1.12 + 128; b = (b - 128) * 1.12 + 128;
                    dst[y * N + x] = Nearest(pal, Clamp(r), Clamp(g), Clamp(b));
                }
            outTex.SetPixels32(dst); outTex.Apply(false);
            return outTex;
        }

        static int Clamp(double v) => v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5);

        static Color32 Nearest(Color32[] pal, int r, int g, int b)
        {
            int best = 0; long bd = long.MaxValue;
            for (int i = 0; i < pal.Length; i++)
            {
                var c = pal[i]; int rm = (c.r + r) / 2, dr = c.r - r, dg = c.g - g, db = c.b - b;
                long d = (((512 + rm) * dr * dr) >> 8) + 4 * dg * dg + (((767 - rm) * db * db) >> 8); // "redmean" perceptual distance
                if (d < bd) { bd = d; best = i; }
            }
            var o = pal[best]; o.a = 255; return o;
        }

        /// <summary>10x10 mini (2x2 area average) of a readable 20x20 texture.</summary>
        public static Texture2D Mini(Texture2D t20)
        {
            var src = t20.GetPixels32(); var dst = new Color32[100];
            for (int y = 0; y < 10; y++) for (int x = 0; x < 10; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int k = 0; k < 4; k++) { var c = src[(y * 2 + (k >> 1)) * N + x * 2 + (k & 1)]; r += c.r; g += c.g; b += c.b; }
                    dst[y * 10 + x] = new Color32((byte)(r / 4), (byte)(g / 4), (byte)(b / 4), 255);
                }
            var m = new Texture2D(10, 10, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            m.SetPixels32(dst); m.Apply(false);
            return m;
        }

        public static Sprite ToSprite(Texture2D t) => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0, 1), 1f, 0, SpriteMeshType.FullRect);
    }
}
