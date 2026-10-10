using System.Collections.Generic;
using UnityEngine;

namespace OreSeas
{
    /// <summary>REELM 8px bitmap font (exported from the mockups' reelm_font.json to Resources/Data/reelm_font.txt).</summary>
    public static class PixelFont
    {
        struct Glyph { public int w; public string[] rows; }
        static Dictionary<int, Glyph> glyphs;

        static void Load()
        {
            if (glyphs != null) return;
            glyphs = new Dictionary<int, Glyph>();
            var ta = Resources.Load<TextAsset>("Data/reelm_font");
            if (ta == null) { Debug.LogError("[OreSeas] font missing: Resources/Data/reelm_font.txt"); return; }
            foreach (var line in ta.text.Split('\n'))
            {
                var p = line.TrimEnd('\r').Split('\t');
                if (p.Length < 3) continue;
                int cp = int.Parse(p[0]); int w = int.Parse(p[1]);
                glyphs[cp] = new Glyph { w = w, rows = p[2].Split(',') };
            }
        }

        static Glyph Get(char ch)
        {
            Load();
            if (glyphs.TryGetValue(ch, out var g)) return g;
            if (glyphs.TryGetValue(char.ToUpperInvariant(ch), out g)) return g;
            if (glyphs.TryGetValue('?', out g)) return g;
            return new Glyph { w = 3, rows = new string[0] };
        }

        public static int Measure(string s, int sc = 1)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0; foreach (char ch in s) w += (Get(ch).w + 1) * sc;
            return Mathf.Max(0, w - sc);
        }

        public static void Glyphs(PixelCanvas c, string s, int x, int y, Color32[] rowCols, int sc, int gapAfter = -1)
        {
            for (int ci = 0; ci < s.Length; ci++)
            {
                char ch = s[ci];
                var f = Get(ch);
                for (int r = 0; r < 8 && r < f.rows.Length; r++)
                {
                    string row = f.rows[r]; if (string.IsNullOrEmpty(row)) continue;
                    var col = rowCols[Mathf.Min(r, rowCols.Length - 1)];
                    for (int i = 0; i < row.Length; i++) if (row[i] == '1') c.Rect(x + i * sc, y + r * sc, sc, sc, col);
                }
                x += (f.w + 1) * sc + (ci == gapAfter ? 1 : 0);
            }
        }

        /// <summary>Desktop screenshot flag -legacycenter: old Measure-based centring, for before/after captures only.</summary>
        public static bool LegacyCenter;

        /// <summary>
        /// Pixel-exact centring. A string of width w centred on anchor ax starts at ax - w/2, which is only a whole pixel when the
        /// parity of w matches the anchor (integer anchor -> even w, half-pixel anchor -> odd w). Otherwise the text would sit
        /// 1 px off-centre after rounding, so we widen one inter-character gap (the space nearest the middle, else the middle
        /// boundary) by 1 px. Returns the index of the character after which the extra pixel goes, or -1.
        /// </summary>
        public static int CenterGap(string s, float anchorX, int sc = 1)
        {
            if (LegacyCenter || string.IsNullOrEmpty(s) || s.Length < 2) return -1;
            int w = Measure(s, sc);
            int a2 = Mathf.RoundToInt(anchorX * 2f);
            bool needOdd = (a2 & 1) != 0;
            if (((w & 1) != 0) == needOdd) return -1;
            int best = -1, bestD = int.MaxValue, acc = 0, half = w / 2;
            for (int i = 0; i < s.Length - 1; i++)
            {
                acc += (Get(s[i]).w + 1) * sc;
                int d = Mathf.Abs(acc - half) - (s[i] == ' ' || s[i + 1] == ' ' ? 1000 : 0);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Rendered width of a centred string at this anchor (Measure + the centring pixel, if any).</summary>
        public static int CenteredWidth(string s, float anchorX, int sc = 1) => Measure(s, sc) + (CenterGap(s, anchorX, sc) >= 0 ? 1 : 0);

        static readonly int[,] Ring = { { -1, 0 }, { 1, 0 }, { 0, -1 }, { 0, 1 }, { -1, -1 }, { 1, -1 }, { -1, 1 }, { 1, 1 } };

        /// <summary>Same semantics as text() in engine.js; x,y is the already-aligned top-left.</summary>
        public static void Draw(PixelCanvas c, string s, int x, int y, TextStyle st, int gapAfter = -1)
        {
            int sc = Mathf.Max(1, st.scale);
            if (st.hasShadow) { int d = sc; Glyphs(c, s, x, y + d, Solid(st.shadow), sc, gapAfter); if (st.shadowX) Glyphs(c, s, x + d, y + d, Solid(st.shadow), sc, gapAfter); }
            if (st.hasOutline)
            {
                for (int i = 0; i < 8; i++) Glyphs(c, s, x + Ring[i, 0], y + Ring[i, 1], Solid(st.outline), sc, gapAfter);
                if (st.sh2) { Glyphs(c, s, x, y + 2 + sc, Solid(st.outline), sc, gapAfter); Glyphs(c, s, x + 1, y + 2 + sc, Solid(st.outline), sc, gapAfter); Glyphs(c, s, x - 1, y + 2 + sc, Solid(st.outline), sc, gapAfter); }
            }
            Glyphs(c, s, x, y, st.rows ?? Solid(Pal.Sand1), sc, gapAfter);
        }

        static readonly Dictionary<uint, Color32[]> solids = new Dictionary<uint, Color32[]>();
        public static Color32[] Solid(Color32 c)
        {
            uint k = (uint)(c.r | c.g << 8 | c.b << 16 | c.a << 24);
            if (!solids.TryGetValue(k, out var a)) { a = new[] { c }; solids[k] = a; }
            return a;
        }
    }

    public enum TextAlign { Left, Center, Right }

    public struct TextStyle
    {
        public Color32[] rows; public int scale; public TextAlign align;
        public bool hasShadow; public Color32 shadow; public bool shadowX;
        public bool hasOutline; public Color32 outline; public bool sh2;

        public static TextStyle Of(Color32 c, TextAlign a = TextAlign.Left, int sc = 1) => new TextStyle { rows = PixelFont.Solid(c), scale = sc, align = a };
        public static TextStyle Of(Color32[] rows, TextAlign a = TextAlign.Left, int sc = 1) => new TextStyle { rows = rows, scale = sc, align = a };
        public TextStyle Shadow(Color32 c) { hasShadow = true; shadow = c; return this; }
        public TextStyle Outline(Color32 c, bool extraBottom = false) { hasOutline = true; outline = c; sh2 = extraBottom; return this; }
    }
}
