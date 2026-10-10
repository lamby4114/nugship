using System;
using System.Collections;
using UnityEngine;

namespace OreSeas.View
{
    /// <summary>Combat FX built from the exported flipbooks + tiny pixel particles.</summary>
    public static class Fx
    {
        public static void Splash(Transform p, float x, float y, int order = 96) => SpriteAnim.Play(p, "splash", x, y, order, 18, false, true);
        public static void Boom(Transform p, float x, float y, int order = 97, bool big = false) => SpriteAnim.Play(p, big ? "boom5" : "boom0", x, y, order, 20, false, true);
        public static void Muzzle(Transform p, float x, float y, int order = 112) => SpriteAnim.Play(p, "muzzle", x, y, order, 16, false, true);

        /// <summary>Gold bubbles rising (miss on the ORE square).</summary>
        public static void Bubbles(Transform p, float x, float y, int order = 98)
        {
            for (int i = 0; i < 7; i++)
            {
                var a = SpriteAnim.Play(p, "bubble", x - 10 + UnityEngine.Random.value * 20, y, order, 6, false, true, i * 0.08f);
                a.gameObject.AddComponent<Particle>().Init(new Vector2((UnityEngine.Random.value - 0.5f) * 8, -18 - UnityEngine.Random.value * 14), 0, 1.4f);
            }
        }

        /// <summary>Burst of pixels (debris / gold / embers) with gravity.</summary>
        public static void Burst(Transform p, float x, float y, int n, Color32[] cols, int order = 120, float speed = 60, float gravity = 120, float life = 0.9f)
        {
            for (int i = 0; i < n; i++)
            {
                float a = UnityEngine.Random.value * Mathf.PI * 2, s = speed * (0.3f + UnityEngine.Random.value);
                var sr = Gfx.Rect(p, x, y, UnityEngine.Random.value < 0.3f ? 2 : 1, 1, cols[i % cols.Length], order, "px");
                sr.gameObject.AddComponent<Particle>().Init(new Vector2(Mathf.Cos(a) * s, Mathf.Sin(a) * s * 0.75f - speed * 0.4f), gravity, life * (0.6f + UnityEngine.Random.value * 0.6f));
            }
        }

        /// <summary>Nuggets fountain (crit / motherlode).</summary>
        public static void Nuggets(Transform p, float x, float y, int n, int order = 205)
        {
            for (int i = 0; i < n; i++)
            {
                string spr = i % 5 == 0 ? "nug_big" : (i % 2 == 0 ? "nug_a" : "nug_b");
                var sr = Gfx.Sprite(p, spr, x, y, order, "nugget");
                float a = -Mathf.PI * (0.1f + UnityEngine.Random.value * 0.8f), s = 70 + UnityEngine.Random.value * 90;
                sr.gameObject.AddComponent<Particle>().Init(new Vector2(Mathf.Cos(a) * s, Mathf.Sin(a) * s), 220, 1.3f + UnityEngine.Random.value * 0.5f);
            }
        }

        public static IEnumerator Cannonball(Transform p, Vector2 from, Vector2 to, float dur, float arc, int order = 110, Action onLand = null)
        {
            var sr = Gfx.Sprite(p, "ball", from.x, from.y, order, "ball");
            float t = 0;
            while (t < 1)
            {
                t += Time.deltaTime / dur;
                float k = Mathf.Clamp01(t);
                var pos = Vector2.Lerp(from, to, k); pos.y -= Mathf.Sin(k * Mathf.PI) * arc;
                if (sr) sr.transform.localPosition = Px.P(pos.x, pos.y);
                yield return null;
            }
            if (sr) UnityEngine.Object.Destroy(sr.gameObject);
            onLand?.Invoke();
        }

        /// <summary>Floating text that rises and fades.</summary>
        public static void FloatText(Transform p, string s, float x, float y, Color32[] col, int order = 210, int sc = 1)
        {
            var t = PixelText.Create(p, s, x, y, TextStyle.Of(col, TextAlign.Center, sc).Outline(Pal.Ink), order);
            t.gameObject.AddComponent<Rise>().Init(x, y);
        }
    }

}
