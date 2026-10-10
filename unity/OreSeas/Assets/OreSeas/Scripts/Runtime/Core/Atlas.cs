using System;
using System.Collections.Generic;
using UnityEngine;

namespace OreSeas
{
    /// <summary>
    /// Loads the sprite atlases exported from the mockup pipeline (tools/unity_export) from Resources/Atlases.
    /// Each atlas = PNG + JSON (frame rects, anchor pivot in mockup pixels, optional 9-slice border).
    /// </summary>
    public static class Atlas
    {
        [Serializable] class Frame { public string n; public int x, y, w, h, px, py, b; }
        [Serializable] class Sheet { public string texture; public Frame[] frames; }

        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static readonly HashSet<string> warned = new HashSet<string>();
        static Sprite white;
        public static bool Loaded { get; private set; }

        public static void LoadAll()
        {
            if (Loaded) return;
            foreach (var ta in Resources.LoadAll<TextAsset>("Atlases"))
            {
                Sheet sh;
                try { sh = JsonUtility.FromJson<Sheet>(ta.text); } catch (Exception e) { Debug.LogError("[OreSeas] bad atlas json " + ta.name + ": " + e.Message); continue; }
                if (sh == null || sh.frames == null) continue;
                var tex = Resources.Load<Texture2D>("Atlases/" + sh.texture);
                if (tex == null) { Debug.LogError("[OreSeas] atlas texture missing: " + sh.texture); continue; }
                tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp;
                foreach (var f in sh.frames)
                {
                    var pivot = new Vector2(f.px / (float)f.w, (f.h - f.py) / (float)f.h);
                    var sp = Sprite.Create(tex, new Rect(f.x, f.y, f.w, f.h), pivot, 1f, 0, SpriteMeshType.FullRect, new Vector4(f.b, f.b, f.b, f.b));
                    sp.name = f.n;
                    sprites[f.n] = sp;
                }
            }
            Loaded = true;
            Debug.Log("[OreSeas] atlases loaded: " + sprites.Count + " sprites");
        }

        public static Sprite Get(string name)
        {
            if (sprites.TryGetValue(name, out var s)) return s;
            if (warned.Add(name)) Debug.LogWarning("[OreSeas] missing sprite " + name);
            return null;
        }
        public static bool Has(string name) => sprites.ContainsKey(name);

        /// <summary>Frames prefix_0..prefix_(n-1); gaps (empty exported frames) reuse the previous frame.</summary>
        public static Sprite[] Seq(string prefix)
        {
            var list = new List<Sprite>();
            for (int i = 0; i < 256; i++)
            {
                if (sprites.TryGetValue(prefix + "_" + i, out var s)) list.Add(s);
                else if (sprites.ContainsKey(prefix + "_" + (i + 1))) list.Add(list.Count > 0 ? list[list.Count - 1] : null);
                else break;
            }
            if (list.Count == 0 && warned.Add(prefix)) Debug.LogWarning("[OreSeas] missing sequence " + prefix);
            return list.ToArray();
        }

        /// <summary>1x1 white sprite with top-left pivot, tint + scale it for solid rects.</summary>
        public static Sprite White
        {
            get
            {
                if (white) return white;
                var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                t.SetPixel(0, 0, Color.white); t.Apply();
                white = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0, 1), 1f);
                white.name = "white";
                return white;
            }
        }
    }
}
