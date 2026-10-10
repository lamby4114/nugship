using UnityEngine;

namespace OreSeas.View
{
    /// <summary>Port of sprites.js ocean(): scrolling dashes + twinkling crests, re-rendered at ~24 fps into a PixelCanvas.</summary>
    public sealed class OceanRenderer : MonoBehaviour
    {
        public SeaPalette pal = SeaPalette.Open;
        public int cw = 15, ch = 11;
        public float scrollX;          // parallax pan offset (px)
        public bool sunGlitter; public int sunX = 120; public Color32 glitterA = Pal.Sun, glitterB = Pal.Sky5; public int glitterLines = 5, glitterW = 30;
        public Color32? horizonLine;
        PixelCanvas cv; int x0, y0, w, h; float acc; float t;
        const float FrameTime = 1f / 24f;

        public static OceanRenderer Create(Transform parent, int x, int y, int w, int h, SeaPalette pal, int order, string name = "Ocean")
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = Px.P(x, y);
            var o = go.AddComponent<OceanRenderer>();
            o.x0 = x; o.y0 = y; o.w = w; o.h = h; o.pal = pal;
            o.cv = new PixelCanvas(w, h, name) { OriginX = x, OriginY = y };
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = o.cv.Sprite; sr.sortingOrder = order;
            o.t = Time.time; o.Render();
            return o;
        }

        void Update()
        {
            acc += Time.deltaTime; t += Time.deltaTime;
            if (acc < FrameTime) return;
            acc = 0; Render();
        }

        void Render()
        {
            Draw(cv, x0 + JsMath.RoundI(scrollX), y0, w, h, t, pal, cw, ch, -JsMath.RoundI(scrollX));
            if (sunGlitter)
                for (int i = 0; i < glitterLines; i++)
                {
                    int yy = y0 + 2 + i * 3; int ww = glitterW - i * (glitterW / 6);
                    cv.HLine(JsMath.RoundI(sunX - ww / 2f + Mathf.Round(Mathf.Sin(t * 2 + i) * 2)), yy, ww, i < 2 ? glitterA : glitterB);
                }
            if (horizonLine.HasValue) cv.HLine(x0, y0, w, horizonLine.Value);
            cv.Apply();
        }

        static int FloorI(float v) => Mathf.FloorToInt(v);

        /// <summary>Draws the ocean in logical coords; shift moves the pattern without moving the clip rect.</summary>
        public static void Draw(PixelCanvas c, int x, int y, int w, int h, float t, SeaPalette pal, int cw, int ch, int shift = 0)
        {
            int clipX = x + shift; // the real on-screen rect starts at clipX
            c.Rect(clipX, y, w, h, pal.b);
            for (int yy = 1; yy < h; yy += 3)
            {
                int row = y + yy; int dir = ((yy / 3) & 1) == 1 ? -1 : 1;
                float sp = (0.5f + JsMath.Hash(row, 1) * 0.8f) * pal.spd * dir;
                int s = FloorI(t * sp + JsMath.Hash(row, 2) * 200);
                int per = pal.per + FloorI(JsMath.Hash(row, 9) * 6);
                int q = FloorI(s / (float)per), rem = s - q * per;
                int kMax = Mathf.CeilToInt(w / (float)per) + 2;
                for (int k = -2; k <= kMax; k++)
                {
                    int id = k - q; int xs = x + k * per + rem;
                    int len = 2 + FloorI(JsMath.Hash(row, id, 3) * 6); float v = JsMath.Hash(id, row, 4);
                    c.Rect(xs, row, len, 1, v < 0.55f ? pal.d : (v < 0.85f ? pal.l : pal.l2));
                }
            }
            int jMax = Mathf.CeilToInt(h / (float)ch), iMax = Mathf.CeilToInt(w / (float)cw) + 1;
            int drift = FloorI((t * (pal.spd * 0.35f)) % cw);
            for (int j = 0; j <= jMax; j++)
                for (int i = -2; i <= iMax; i++)
                {
                    float ph = JsMath.Hash(i, j, 5) * 6.283f; float v = Mathf.Sin(t * 1.6f + ph);
                    if (v < 0.15f) continue;
                    int cx = JsMath.RoundI(x + i * cw + JsMath.Hash(i, j, 6) * (cw - 6) + drift), cy = JsMath.RoundI(y + j * ch + JsMath.Hash(i, j, 7) * (ch - 3));
                    if (v < 0.55f) { c.HLine(cx + 1, cy, 2, pal.l2); c.Px(cx, cy + 1, pal.l); c.Px(cx + 3, cy + 1, pal.l); }
                    else if (v < 0.85f) { c.HLine(cx + 1, cy, 3, pal.hi); c.Px(cx, cy + 1, pal.l2); c.Px(cx + 4, cy + 1, pal.l2); c.HLine(cx + 1, cy + 1, 3, pal.d); }
                    else { c.HLine(cx + 1, cy - 1, 2, pal.foam); c.HLine(cx, cy, 4, pal.hi); c.Px(cx - 1, cy + 1, pal.l2); c.Px(cx + 4, cy + 1, pal.l2); c.HLine(cx, cy + 1, 4, pal.d); }
                }
        }
    }

}
