using System;
using System.Collections.Generic;
using UnityEngine;

namespace OreSeas
{

    /// <summary>Builders for the mockup's wooden buttons (exported bodies + live labels/icons).</summary>
    public static class Ui
    {
        public sealed class Btn
        {
            public UiButton button; public Transform root; public SpriteRenderer body; public PixelText label, sub; public SpriteRenderer icon; public SpriteAnim cannon;
            internal SpriteRenderer subPlate, coin0, coin1, coin2; internal float subCx, subY; internal int subMax = 999;
            public string style; public int w, h;
            public void SetStyle(string st) { style = st; if (body) body.sprite = Atlas.Get("btn_" + st + "_" + w + "x" + h); if (label) label.SetColor(st == "dim" ? Pal.WhiteTxt : Pal.GoldTxt); }
            public void SetEnabled(bool e, bool dimWhenOff = true) { button.interactable = e; if (dimWhenOff) SetStyle(e ? (style == "dim" ? "red" : style) : "dim"); }
            public void SetLabel(string l) { label?.Set(l); }
            public void SetSub(string s)
            {
                if (sub == null) return;
                s = FitSub(s, subMax, subCx + 5);   // never let a long status run under the icon or off the button
                sub.Set(s);
                if (subPlate == null || PixelFont.LegacyCenter) return;
                int sw = PixelFont.CenteredWidth(s, subCx + 5) + 16; float sx = Mathf.Round(subCx - sw / 2f);
                Gfx.SetRect(subPlate, sx, subY, sw, 11);
                Gfx.SetRect(coin0, sx + 3, subY + 2, 7, 7); Gfx.SetRect(coin1, sx + 4, subY + 3, 5, 5); Gfx.SetRect(coin2, sx + 5, subY + 4, 3, 3);
            }
            public void SetVisible(bool v) { root.gameObject.SetActive(v); }
            /// <summary>Widest sub text that keeps the plate (text + 16) clear of the icon and inside the button.</summary>
            internal static string FitSub(string s, int max, float anchor)
            {
                if (PixelFont.LegacyCenter || string.IsNullOrEmpty(s) || PixelFont.CenteredWidth(s, anchor) <= max) return s;
                string f = Lay.Fit(s, max - 1);
                if (Debug.isDebugBuild) Debug.LogWarning("[OreSeas] button sub too wide, trimmed: '" + s + "' -> '" + f + "'");
                return f;
            }
        }

        /// <summary>Big primary button (fireButton in sprites.js). icon: "cannon", "anchor", "vault", "chest" or null.</summary>
        public static Btn Big(Transform parent, float x, float y, int w, int h, string label, int sc, string sub, string icon, Action onClick, int order, int layer = 0, string style = "red")
        {
            var b = new Btn { w = w, h = h, style = style };
            b.button = UiButton.Create(parent, x, y, w, h, onClick, layer, "Btn_" + label);
            b.root = b.button.transform;
            var shadow = Gfx.Rect(parent, x, y + 3, w, h, Pal.Ink, order - 1, "shadow"); shadow.transform.SetParent(b.root, true);
            var vis = Gfx.Node(b.root, "visual"); b.button.visual = vis;
            b.body = Gfx.Sprite(vis, "btn_" + style + "_" + w + "x" + h, x, y, order, "body");
            float iy = y + 5, ih = h - 10; float cxl = icon != null ? x + w / 2f + 24 : x + w / 2f;
            int subH = string.IsNullOrEmpty(sub) ? 0 : 12; int lh = 8 * sc;
            float ly = Mathf.Round(iy + (ih - subH - lh) / 2f) - 1;
            b.label = PixelText.Create(vis, label, cxl, ly, TextStyle.Of(style == "dim" ? Pal.WhiteTxt : Pal.GoldTxt, TextAlign.Center, sc).Outline(Pal.Ink, true), order + 3, "label");
            // sub plate is symmetric about cxl: half-width limited by the icon on the left (cannon ~x+60, others ~x+48) and the frame
            int subMax = icon == null ? w - 24 : (int)(2 * Mathf.Min(cxl - (x + (icon == "cannon" ? 60 : 48)), x + w - 6 - cxl)) - 16;
            b.subMax = subMax;
            if (!string.IsNullOrEmpty(sub)) sub = Btn.FitSub(sub, subMax, cxl + 5);
            if (!string.IsNullOrEmpty(sub))
            {
                int sw = PixelFont.CenteredWidth(sub, cxl + 5) + 16; float sx = Mathf.Round(cxl - sw / 2f); // plate symmetric about cxl, text centred in the space right of the coin
                b.subPlate = Gfx.Rect(vis, sx, iy + ih - 13, sw, 11, Pal.Red0, order + 1, "subplate"); b.subCx = cxl; b.subY = iy + ih - 13;
                b.sub = PixelText.Create(vis, sub, cxl + 5, iy + ih - 12, TextStyle.Of(Pal.Sand1, TextAlign.Center), order + 2, "sub");
                float vx = sx + 3, vy = iy + ih - 11;
                b.coin0 = Gfx.Rect(vis, vx, vy, 7, 7, Pal.Gold2, order + 2); b.coin1 = Gfx.Rect(vis, vx + 1, vy + 1, 5, 5, Pal.Ink, order + 3); b.coin2 = Gfx.Rect(vis, vx + 2, vy + 2, 3, 3, Pal.Gold1, order + 4);
            }
            float icx = x + 34;
            switch (icon)
            {
                case "cannon": b.cannon = SpriteAnim.Play(vis, "icon_cannon", x + 36, y + h - 16, order + 2, 14, true, false); break;
                case "anchor": b.icon = Gfx.Sprite(vis, "icon_anchor", icx - 9, y + h / 2f - 12, order + 2); break;
                case "vault": b.icon = Gfx.Sprite(vis, "icon_vault", icx - 12, y + h / 2f - 11, order + 2); break;
                case "chest": b.icon = Gfx.Sprite(vis, "icon_chest", icx - 12, y + h / 2f - 9, order + 2); break;
                case "trophy": b.icon = Gfx.Sprite(vis, "icon_trophy", icx - 6, y + h / 2f - 5, order + 2); break;
            }
            return b;
        }

        /// <summary>Small wooden button (btn_*_WxH body + centred label).</summary>
        public static Btn Small(Transform parent, float x, float y, int w, int h, string label, Action onClick, int order, int layer = 0, string style = "dim", int sc = 1)
        {
            var b = new Btn { w = w, h = h, style = style };
            b.button = UiButton.Create(parent, x, y, w, h, onClick, layer, "Btn_" + label);
            b.root = b.button.transform;
            Gfx.Rect(b.root, x, y + 2, w, h, Pal.Ink, order - 1, "shadow");
            var vis = Gfx.Node(b.root, "visual"); b.button.visual = vis;
            b.body = Gfx.Sprite(vis, "btn_" + style + "_" + w + "x" + h, x, y, order, "body");
            b.label = PixelText.Create(vis, label, x + w / 2f, y + h / 2f - 4 * sc - (sc > 1 ? 1 : 0), TextStyle.Of(style == "dim" ? Pal.WhiteTxt : Pal.GoldTxt, TextAlign.Center, sc).Outline(Pal.Ink), order + 3, "label");
            return b;
        }

        /// <summary>Flat plank button (ROTATE / SHUFFLE in the fleet mockup).</summary>
        public static Btn Plank(Transform parent, float x, float y, int w, int h, string label, string icon, Action onClick, int order, int layer = 0)
        {
            var b = new Btn { w = w, h = h, style = "plank" };
            b.button = UiButton.Create(parent, x, y, w, h, onClick, layer, "Plank_" + label);
            b.root = b.button.transform;
            var vis = Gfx.Node(b.root, "visual"); b.button.visual = vis;
            Gfx.Rect(vis, x, y, w, h, Pal.Ink, order); Gfx.Rect(vis, x + 1, y + 1, w - 2, h - 2, Pal.Wood2, order + 1); Gfx.Rect(vis, x + 1, y + 1, w - 2, 1, Pal.Wood4, order + 2);
            var isp = icon != null ? Atlas.Get(icon) : null;
            if (isp != null && !PixelFont.LegacyCenter)
            {
                // icon + 4 px + label as one group, centred in the plank (whole-pixel left edge; the label takes its centring gap)
                int iw = (int)isp.rect.width, tw = PixelFont.Measure(label); int g = iw + 4 + tw;
                float gl = x + Mathf.Floor((w - g) / 2f);
                b.label = PixelText.Create(vis, label, gl + iw + 4, y + h / 2f - 4, TextStyle.Of(Pal.Sand1, TextAlign.Left).Shadow(Pal.Ink), order + 3, "label");
                b.icon = Gfx.Sprite(vis, icon, gl + isp.pivot.x, y + Mathf.Floor((h - isp.rect.height) / 2f) + (isp.rect.height - isp.pivot.y), order + 3);
                return b;
            }
            b.label = PixelText.Create(vis, label, x + w / 2f + (icon != null ? 8 : 0), y + h / 2f - 4, TextStyle.Of(Pal.Sand1, TextAlign.Center).Shadow(Pal.Ink), order + 3, "label");
            if (icon != null) b.icon = Gfx.Sprite(vis, icon, x + 14, y + h / 2f - 4, order + 3);
            return b;
        }

        public static void Nails(Transform p, float x, float y, float w, float h, int order)
        {
            foreach (var nx in new[] { x + 3, x + w - 5 }) foreach (var ny in new[] { y + 3, y + h - 5 }) Gfx.Sprite(p, "nail", nx, ny, order, "nail");
        }

        /// <summary>Plum label plate with centred text (mockup round label / bottom hints).</summary>
        public const int HintMax = 224;   // plate = text + 10 must stay inside the 240 px screen with a margin
        public static PixelText Hint(Transform p, string s, float y, int order, Color32? col = null)
        {
            s = Lay.Fit(s, HintMax);
            int w = PixelFont.CenteredWidth(s, Px.W / 2f) + 10; // even when the text gets its centring gap -> plate starts on a whole pixel
            var bg = Gfx.Rect(p, Px.W / 2f - w / 2f, y, w, 11, Pal.Ink, order, "hintbg");
            var t = PixelText.Create(p, s, Px.W / 2f, y + 1, TextStyle.Of(col ?? Pal.Sand1, TextAlign.Center), order + 1, "hint");
            t.gameObject.AddComponent<HintBg>().bg = bg;
            return t;
        }

        /// <summary>Ribbon banner from the atlas: ribbon_red (112 wide), ribbon_red_wide (166), ribbon_gold (196).</summary>
        public static Transform Ribbon(Transform p, string kind, float cx, float y, string text, int order, Color32[] col = null, Color32? shadow = null, int sc = 1)
        {
            int bodyW = kind == "ribbon_gold" ? 196 : kind == "ribbon_red_wide" ? 166 : 112;
            var n = Gfx.Node(p, kind);
            Gfx.Sprite(n, kind, cx - bodyW / 2f, y, order);
            var st = TextStyle.Of(col ?? PixelFont.Solid(Pal.Sand1), TextAlign.Center, sc);
            if (sc > 1) st = st.Outline(Pal.Ink); else st = st.Shadow(shadow ?? Pal.Red0);
            PixelText.Create(n, text, cx, y + (kind == "ribbon_gold" ? 4 : 3), st, order + 1);
            return n;
        }

        /// <summary>Stamp centred on cx: the text gets its centring gap so plate and text share one whole-pixel centre.</summary>
        public static void StampCentered(Transform p, string s, float cx, float y, int order)
        {
            int w = PixelFont.CenteredWidth(s, cx) + 6; float xx = cx - w / 2f;
            if (PixelFont.LegacyCenter) { Stamp(p, s, JsMath.Round(cx - w / 2f), y, order); return; }
            Gfx.Rect(p, xx, y, w, 12, Pal.Red2, order); Gfx.Rect(p, xx + 1, y + 1, w - 2, 10, Pal.Sand1, order + 1); Gfx.Rect(p, xx + 2, y + 2, w - 4, 8, Pal.Red2, order + 2);
            PixelText.Create(p, s, cx, y + 2, TextStyle.Of(Pal.Sand1, TextAlign.Center), order + 3, "stamp");
        }

        public static void Stamp(Transform p, string s, float x, float y, int order, bool alignRight = false)
        {
            int w = PixelFont.Measure(s) + 6; float xx = alignRight ? x - w : x;
            Gfx.Rect(p, xx, y, w, 12, Pal.Red2, order); Gfx.Rect(p, xx + 1, y + 1, w - 2, 10, Pal.Sand1, order + 1); Gfx.Rect(p, xx + 2, y + 2, w - 4, 8, Pal.Red2, order + 2);
            PixelText.Create(p, s, xx + 3, y + 2, TextStyle.Of(Pal.Sand1), order + 3);
        }
    }

}
