using System;
using System.Collections.Generic;
using UnityEngine;

namespace OreSeas
{
    /// <summary>Pixel-exact layout helpers (centring sprites by their trimmed rect, word wrap by real glyph widths, overlay back stack).</summary>
    public static class Lay
    {
        /// <summary>Places an atlas sprite so its visible rect (x scale) is centred on cx with its top at y. Returns the renderer.</summary>
        public static SpriteRenderer SpriteCentered(Transform parent, string name, float cx, float top, int order, int scale = 1)
        {
            var sp = Atlas.Get(name);
            var sr = Gfx.Sprite(parent, sp, 0, 0, order, name);
            if (sp == null) return sr;
            float w = sp.rect.width * scale, h = sp.rect.height * scale;
            float left = Mathf.Floor(cx - w / 2f + 0.001f);
            sr.transform.localScale = new Vector3(scale, scale, 1);
            sr.transform.localPosition = Px.P(left + sp.pivot.x * scale, top + (sp.rect.height - sp.pivot.y) * scale);
            return sr;
        }

        /// <summary>Places an atlas sprite by the top-left of its visible rect, whatever its pivot.</summary>
        public static SpriteRenderer SpriteTL(Transform parent, string name, float left, float top, int order, int scale = 1)
        {
            var sp = Atlas.Get(name);
            var sr = Gfx.Sprite(parent, sp, 0, 0, order, name);
            if (sp == null) return sr;
            sr.transform.localScale = new Vector3(scale, scale, 1);
            sr.transform.localPosition = Px.P(left + sp.pivot.x * scale, top + (sp.rect.height - sp.pivot.y) * scale);
            return sr;
        }
        public static int Width(string name) { var sp = Atlas.Get(name); return sp == null ? 0 : (int)sp.rect.width; }

        /// <summary>Total width of a horizontal group of items with the given widths and gap.</summary>
        public static int GroupWidth(int gap, params int[] widths) { int w = 0; foreach (var x in widths) w += x; return w + gap * Math.Max(0, widths.Length - 1); }

        /// <summary>Greedy word wrap by measured pixel width.</summary>
        public static List<string> Wrap(string text, int maxW)
        {
            var lines = new List<string>();
            foreach (var para in text.Split('\n'))
            {
                string cur = "";
                foreach (var word in para.Split(' '))
                {
                    var tryS = cur.Length == 0 ? word : cur + " " + word;
                    if (PixelFont.Measure(tryS) <= maxW || cur.Length == 0) cur = tryS;
                    else { lines.Add(cur); cur = word; }
                }
                lines.Add(cur);
            }
            return lines;
        }

        /// <summary>Truncate with ".." so the text fits maxW pixels.</summary>
        public static string Fit(string s, int maxW)
        {
            if (string.IsNullOrEmpty(s) || PixelFont.Measure(s) <= maxW) return s ?? "";
            while (s.Length > 1 && PixelFont.Measure(s + "..") > maxW) s = s.Substring(0, s.Length - 1);
            return s.TrimEnd() + "..";
        }

        // ---- Android back for overlays: the newest open overlay closes first ----
        static readonly List<Action> backStack = new List<Action>();
        public static void PushBack(Action close) { backStack.Add(close); }
        public static void PopBack(Action close) { backStack.Remove(close); }
        public static void ClearBack() { backStack.Clear(); }
        /// <summary>True when an overlay handled the back press.</summary>
        public static bool HandleBack()
        {
            if (backStack.Count == 0) return false;
            var a = backStack[backStack.Count - 1]; backStack.RemoveAt(backStack.Count - 1);
            a?.Invoke(); return true;
        }
    }
}
