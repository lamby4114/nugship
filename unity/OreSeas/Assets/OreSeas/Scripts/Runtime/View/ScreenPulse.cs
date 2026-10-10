using UnityEngine;

namespace OreSeas.View
{
    /// <summary>
    /// Gentle full-screen feedback that replaces the old white flashes: a warm/gold/lavender tint shaped as a
    /// vignette (edges strongest, centre ~25%), eased out. Alpha is hard-capped at 15% and the colour is never white.
    /// Use attack &gt; 0 for a slow swell (Kraken lightning flicker).
    /// </summary>
    public sealed class ScreenPulse : MonoBehaviour
    {
        public const float MaxAlpha = 0.15f;
        public static readonly Color32 Warm = Pal.Hex("e0783a"), Gold = Pal.Hex("f0c050"), Storm = Pal.Hex("8a80c8");

        SpriteRenderer sr; float t, attack, release, peak; Color c;
        static Sprite vignette;

        /// <summary>Hit feedback: warm vignette, 150 ms ease-out (half strength with Reduce flashes).</summary>
        public static ScreenPulse Hit(Transform p, int order = 185) => Play(p, Warm, Settings.ReduceFlashes ? 0.08f : 0.15f, 0.15f, order);
        /// <summary>Crit feedback: soft gold glow.</summary>
        public static ScreenPulse Crit(Transform p, int order = 190) => Play(p, Gold, Settings.ReduceFlashes ? 0.10f : 0.15f, 0.4f, order, 0.05f);
        /// <summary>One slow, dim lightning flicker (Kraken / storms).</summary>
        public static ScreenPulse Lightning(Transform p, int order) => Play(p, Storm, Settings.ReduceFlashes ? 0.06f : 0.12f, 0.9f, order, 0.2f);

        public static ScreenPulse Play(Transform parent, Color32 color, float peakAlpha, float releaseSec, int order, float attackSec = 0f)
        {
            var go = new GameObject("pulse"); go.transform.SetParent(parent, false);
            go.transform.localPosition = Px.P(0, 0);
            var r = go.AddComponent<SpriteRenderer>(); r.sprite = Vignette; r.sortingOrder = order;
            go.transform.localScale = new Vector3(Px.W / (float)VW, (Px.H + 1) / (float)VH, 1);
            var p = go.AddComponent<ScreenPulse>();
            p.sr = r; p.c = (Color)color; p.peak = Mathf.Clamp(peakAlpha, 0f, MaxAlpha);
            p.attack = Mathf.Max(0f, attackSec); p.release = Mathf.Max(0.05f, releaseSec);
            p.Apply(0f);
            return p;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k;
            if (t < attack) k = Ease.OutQuad(t / attack);
            else { float a = (t - attack) / release; if (a >= 1f) { Destroy(gameObject); return; } k = (1 - a) * (1 - a); }
            Apply(k);
        }

        void Apply(float k) { sr.color = new Color(c.r, c.g, c.b, peak * k); }

        const int VW = 48, VH = 107;
        static Sprite Vignette
        {
            get
            {
                if (vignette) return vignette;
                var tex = new Texture2D(VW, VH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "vignette" };
                var px = new Color32[VW * VH];
                for (int y = 0; y < VH; y++)
                    for (int x = 0; x < VW; x++)
                    {
                        float dx = (x + 0.5f - VW / 2f) / (VW / 2f), dy = (y + 0.5f - VH / 2f) / (VH / 2f);
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = 0.25f + 0.75f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.25f, d));
                        px[y * VW + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                    }
                tex.SetPixels32(px); tex.Apply();
                vignette = Sprite.Create(tex, new Rect(0, 0, VW, VH), new Vector2(0, 1), 1f);
                vignette.name = "vignette";
                return vignette;
            }
        }
    }
}
