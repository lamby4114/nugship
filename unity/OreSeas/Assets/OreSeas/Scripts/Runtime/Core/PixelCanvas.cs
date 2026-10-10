using UnityEngine;

namespace OreSeas
{
    /// <summary>
    /// CPU pixel buffer (y-down, like the mockup canvas) backed by a point-filtered Texture2D + Sprite (pivot top-left).
    /// Used for procedural layers ported from the mockup engine: ocean, board water, weather, text, transitions.
    /// </summary>
    public sealed class PixelCanvas
    {
        public readonly int W, H;
        public readonly Color32[] Buf;
        public readonly Texture2D Tex;
        public readonly Sprite Sprite;
        public int OriginX, OriginY; // logical coords of pixel (0,0); drawing calls use logical coords

        public PixelCanvas(int w, int h, string name = "PixelCanvas")
        {
            W = Mathf.Max(1, w); H = Mathf.Max(1, h);
            Buf = new Color32[W * H];
            Tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            Sprite = Sprite.Create(Tex, new Rect(0, 0, W, H), new Vector2(0, 1), 1f, 0, SpriteMeshType.FullRect);
            Sprite.name = name;
        }

        public void Destroy()
        {
            if (Sprite) Object.Destroy(Sprite);
            if (Tex) Object.Destroy(Tex);
        }

        public void Clear(Color32 c) { for (int i = 0; i < Buf.Length; i++) Buf[i] = c; }
        public void Apply() { Tex.SetPixels32(Buf); Tex.Apply(false); }

        public void Px(int x, int y, Color32 c)
        {
            x -= OriginX; y -= OriginY;
            if ((uint)x >= (uint)W || (uint)y >= (uint)H) return;
            Buf[(H - 1 - y) * W + x] = c;
        }
        public void Px(float x, float y, Color32 c) => Px(JsMath.RoundI(x), JsMath.RoundI(y), c);

        public void Rect(int x, int y, int w, int h, Color32 c)
        {
            int x0 = Mathf.Max(x - OriginX, 0), y0 = Mathf.Max(y - OriginY, 0);
            int x1 = Mathf.Min(x - OriginX + w, W), y1 = Mathf.Min(y - OriginY + h, H);
            for (int yy = y0; yy < y1; yy++) { int row = (H - 1 - yy) * W; for (int xx = x0; xx < x1; xx++) Buf[row + xx] = c; }
        }
        public void Rect(float x, float y, float w, float h, Color32 c) => Rect(JsMath.RoundI(x), JsMath.RoundI(y), JsMath.RoundI(w), JsMath.RoundI(h), c);
        public void HLine(int x, int y, int w, Color32 c) => Rect(x, y, w, 1, c);
        public void VLine(int x, int y, int h, Color32 c) => Rect(x, y, 1, h, c);

        public void Checker(int x, int y, int w, int h, Color32 c, int phase = 0)
        { for (int yy = 0; yy < h; yy++) for (int xx = (yy + phase) & 1; xx < w; xx += 2) Px(x + xx, y + yy, c); }
        public void Sparse(int x, int y, int w, int h, Color32 c, int phase = 0)
        { for (int yy = 0; yy < h; yy += 2) for (int xx = (((yy >> 1) + phase) % 2) * 2; xx < w; xx += 4) Px(x + xx, y + yy, c); }

        public void Disc(float cx, float cy, float r, Color32 c)
        {
            int ri = Mathf.CeilToInt(r); int icx = JsMath.RoundI(cx), icy = JsMath.RoundI(cy);
            for (int y = -ri; y <= ri; y++) for (int x = -ri; x <= ri; x++) if (x * x + y * y <= r * r) Px(icx + x, icy + y, c);
        }
        /// <summary>dithered disc (engine ddisc)</summary>
        public void DDisc(float cx, float cy, float r, Color32 c, int ph = 0)
        {
            int ri = Mathf.CeilToInt(r); int icx = JsMath.RoundI(cx), icy = JsMath.RoundI(cy);
            for (int y = -ri; y <= ri; y++) for (int x = -ri; x <= ri; x++) if (x * x + y * y <= r * r && ((x + y + ph) & 1) == 0) Px(icx + x, icy + y, c);
        }
        public void Ellipse(float cx, float cy, float rx, float ry, Color32 c)
        {
            int n = Mathf.Max(16, JsMath.RoundI((rx + ry) * 3));
            for (int i = 0; i < n; i++) { float a = i / (float)n * Mathf.PI * 2; Px(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry, c); }
        }
    }
}
