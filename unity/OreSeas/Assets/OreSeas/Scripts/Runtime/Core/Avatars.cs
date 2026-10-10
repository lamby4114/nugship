using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace OreSeas
{
    /// <summary>
    /// Captain portraits. A ref is "a:&lt;id&gt;" (built-in pixel art, atlas av_&lt;id&gt; 20x20 + avm_&lt;id&gt; 10x10) or "u:&lt;hash16&gt;"
    /// (an uploaded 20x20 PNG served by the NUGSHIP server at /api/avatars/&lt;hash16&gt;.png, cached on disk).
    /// The 26x26 frame (ink / gold / ink ring, gold3 top line) is drawn here so built-in and uploaded portraits match.
    /// </summary>
    public static class Avatars
    {
        public static readonly string[] Ids =
        {
            "nuggy", "nuggy_ginger", "nuggy_snow", "nuggy_ghost", "nuggy_royal", "nuggy_tricorn",
            "captain", "barnacle", "brine", "molly", "ghost", "cult",
            "salt", "corsair", "skull", "kraken", "parrot", "nugget"
        };
        public const string Default = "a:captain";
        public const int Inner = 20, Size = 26;

        public static bool IsBuiltin(string r) => r != null && r.StartsWith("a:") && Array.IndexOf(Ids, r.Substring(2)) >= 0;
        public static bool IsUploaded(string r) => r != null && r.Length == 18 && r.StartsWith("u:");
        public static bool IsValid(string r) => IsBuiltin(r) || IsUploaded(r);
        public static string Hash(string r) => IsUploaded(r) ? r.Substring(2) : null;

        static readonly Dictionary<string, Sprite> big = new Dictionary<string, Sprite>(), mini = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, List<Action>> waiting = new Dictionary<string, List<Action>>();

        static string CacheDir { get { var d = Path.Combine(Application.persistentDataPath, "avatars"); Directory.CreateDirectory(d); return d; } }

        /// <summary>20x20 inner art. Uploaded portraits not cached yet return the default and call <paramref name="late"/> once loaded.</summary>
        public static Sprite InnerSprite(string r, Action late = null) => Get(r, false, late);
        /// <summary>10x10 leaderboard mini.</summary>
        public static Sprite MiniSprite(string r, Action late = null) => Get(r, true, late);

        static Sprite Get(string r, bool small, Action late)
        {
            if (!IsValid(r)) r = Default;
            if (IsBuiltin(r)) return Atlas.Get((small ? "avm_" : "av_") + r.Substring(2));
            var h = Hash(r);
            var d = small ? mini : big;
            if (d.TryGetValue(h, out var s) && s) return s;
            // disk cache
            var file = Path.Combine(CacheDir, h + ".png");
            if (File.Exists(file) && Register(h, File.ReadAllBytes(file))) return (small ? mini : big)[h];
            Fetch(h, late);
            return Atlas.Get((small ? "avm_" : "av_") + Default.Substring(2));
        }

        /// <summary>Registers a 20x20 PNG under its hash (used for your own upload and for downloads). Returns false if it isn't a 20x20 image.</summary>
        public static bool Register(string hash, byte[] png)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(png) || tex.width != Inner || tex.height != Inner) { UnityEngine.Object.Destroy(tex); return false; }
            tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp;
            big[hash] = Sprite.Create(tex, new Rect(0, 0, Inner, Inner), new Vector2(0, 1), 1f, 0, SpriteMeshType.FullRect);
            mini[hash] = Sprite.Create(AvatarPixelate.Mini(tex), new Rect(0, 0, 10, 10), new Vector2(0, 1), 1f, 0, SpriteMeshType.FullRect);
            try { File.WriteAllBytes(Path.Combine(CacheDir, hash + ".png"), png); } catch (Exception e) { Debug.LogWarning("[OreSeas] avatar cache: " + e.Message); }
            return true;
        }

        static void Fetch(string hash, Action late)
        {
            if (waiting.TryGetValue(hash, out var list)) { if (late != null) list.Add(late); return; }
            waiting[hash] = new List<Action>(); if (late != null) waiting[hash].Add(late);
            if (GameApp.I != null) GameApp.I.StartCoroutine(Download(hash));
        }

        static IEnumerator Download(string hash)
        {
            using (var req = UnityWebRequest.Get(Net.ServerConfig.HttpBase + "/api/avatars/" + hash + ".png"))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success && Register(hash, req.downloadHandler.data);
                var cbs = waiting[hash]; waiting.Remove(hash);
                if (ok) foreach (var cb in cbs) { try { cb(); } catch (Exception e) { Debug.LogException(e); } }
            }
        }

        /// <summary>Framed 26x26 portrait at (x,y) (top-left, logical px). scale 2 = 52x52 (whole-pixel scale stays crisp).</summary>
        public static Frame Draw(Transform parent, string r, float x, float y, int order, int scale = 1)
        {
            var root = Gfx.Node(parent, "Avatar", x, y);
            root.localScale = new Vector3(scale, scale, 1);
            Gfx.Rect(root, 0, 0, Size, Size, Pal.Ink, order, "ink");
            Gfx.Rect(root, 1, 1, 24, 24, Pal.Gold1, order + 1, "gold");
            Gfx.Rect(root, 2, 2, 22, 22, Pal.Ink, order + 2, "ink2");
            Gfx.Rect(root, 2, 2, 22, 1, Pal.Gold3, order + 4, "hl");
            Gfx.Rect(root, 1, 1, 1, 1, Pal.Gold4, order + 4, "glint");
            var f = new Frame { root = root };
            f.inner = Gfx.Sprite(root, (Sprite)null, 3, 3, order + 3, "inner");
            f.Set(r);
            return f;
        }

        /// <summary>10x10 mini with a 1 px ink ring (12x12) for leaderboard rows.</summary>
        public static SpriteRenderer DrawMini(Transform parent, string r, float x, float y, int order)
        {
            Gfx.Rect(parent, x, y, 12, 12, Pal.Ink, order, "miniring");
            var sr = Gfx.Sprite(parent, (Sprite)null, x + 1, y + 1, order + 1, "mini");
            sr.sprite = MiniSprite(r, () => { if (sr) sr.sprite = MiniSprite(r); });
            return sr;
        }

        public sealed class Frame
        {
            public Transform root; public SpriteRenderer inner; string current;
            public void Set(string r)
            {
                current = r;
                inner.sprite = InnerSprite(r, () => { if (inner && current == r) inner.sprite = InnerSprite(r); });
            }
            /// <summary>Show a not-yet-uploaded picture (profile preview).</summary>
            public void SetSprite(Sprite s) { current = null; inner.sprite = s; }
        }
    }
}
