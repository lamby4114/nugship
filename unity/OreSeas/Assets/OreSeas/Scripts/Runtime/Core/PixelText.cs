using UnityEngine;

namespace OreSeas
{
    /// <summary>A crisp pixel-font label rendered to its own small texture; re-renders only when text/style changes.</summary>
    public sealed class PixelText : MonoBehaviour
    {
        const int M = 3; // margin for outline/shadow
        SpriteRenderer sr; PixelCanvas canvas;
        string text; TextStyle style; float ax, ay; int gap = -1;

        public static PixelText Create(Transform parent, string text, float x, float y, TextStyle style, int order, string name = "Text")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var pt = go.AddComponent<PixelText>();
            pt.sr = go.AddComponent<SpriteRenderer>();
            pt.sr.sortingOrder = order;
            pt.ax = x; pt.ay = y; pt.style = style;
            pt.Set(text, true);
            return pt;
        }

        public string Text => text;
        public int Order { get => sr.sortingOrder; set => sr.sortingOrder = value; }
        public void SetVisible(bool v) { if (sr) sr.enabled = v; }
        public void SetAlpha(float a) { if (sr) sr.color = new Color(1, 1, 1, a); }

        public void SetStyle(TextStyle st) { style = st; Set(text, true); }
        public void SetColor(Color32 c) { style.rows = PixelFont.Solid(c); Set(text, true); }
        public void SetColor(Color32[] rows) { style.rows = rows; Set(text, true); }
        public void MoveTo(float x, float y) { bool re = style.align == TextAlign.Center && PixelFont.CenterGap(text, x, Mathf.Max(1, style.scale)) != gap; ax = x; ay = y; if (re) Set(text, true); else Place(); }

        public void Set(string t, bool force = false)
        {
            t = t ?? "";
            if (!force && t == text) return;
            text = t;
            int sc = Mathf.Max(1, style.scale);
            gap = style.align == TextAlign.Center ? PixelFont.CenterGap(t, ax, sc) : -1;
            int w = PixelFont.Measure(t, sc) + (gap >= 0 ? 1 : 0);
            int h = 8 * sc;
            canvas?.Destroy();
            canvas = new PixelCanvas(Mathf.Max(1, w + M * 2 + sc + 2), h + M * 2 + sc + 3, "txt");
            canvas.Clear(Pal.Clear);
            PixelFont.Draw(canvas, t, M, M, style, gap);
            canvas.Apply();
            sr.sprite = canvas.Sprite;
            Place();
        }

        public int Width => PixelFont.Measure(text, Mathf.Max(1, style.scale)) + (gap >= 0 ? 1 : 0);
        public float AnchorX => ax;
        public TextAlign Align => style.align;
        public int Scale => Mathf.Max(1, style.scale);
        public bool Visible => sr != null && sr.enabled && sr.color.a > 0.05f;
        /// <summary>Left edge of the glyph box in logical px (after rounding).</summary>
        public float Left { get { int w = Width; float x = style.align == TextAlign.Center ? ax - w / 2f : style.align == TextAlign.Right ? ax - w : ax; return JsMath.Round(x); } }

        void Place()
        {
            int w = Width;
            float x = style.align == TextAlign.Center ? ax - w / 2f : style.align == TextAlign.Right ? ax - w : ax;
            transform.localPosition = Px.P(JsMath.Round(x) - M, ay - M);
        }

        void OnDestroy() { canvas?.Destroy(); }
    }
}
