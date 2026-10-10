using UnityEngine;

namespace OreSeas.View
{
    /// <summary>Weather layer: rain / snow / ash / fog drift / lightning, drawn into a screen-space canvas above the boards.</summary>
    public sealed class WeatherRenderer : MonoBehaviour
    {
        public string kind = ""; public float intensity = 1f;
        PixelCanvas cv; float t, acc, nextBolt = 6f;
        Transform root; int h;
        static readonly Color32 RainA = Pal.Hex("5a6a90"), RainB = Pal.Hex("8a9ac0"), Snow = Pal.Hex("f2f6fc"), Snow2 = Pal.Hex("c8d8ec"), AshA = Pal.Hex("6a5a5e"), Ember = Pal.Hex("ff7a3a");

        public static WeatherRenderer Create(Transform parent, string kind, int height, int order)
        {
            var go = new GameObject("Weather"); go.transform.SetParent(parent, false);
            var w = go.AddComponent<WeatherRenderer>();
            w.kind = kind ?? ""; w.h = height; w.root = parent;
            w.cv = new PixelCanvas(Px.W, height, "weather");
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = w.cv.Sprite; sr.sortingOrder = order;
                        w.t = Random.value * 10f;
            w.enabled = w.kind == "rain" || w.kind == "snow" || w.kind == "ash";
            if (!w.enabled) { sr.enabled = false; }
            return w;
        }

        void Update()
        {
            t += Time.deltaTime; acc += Time.deltaTime;
            if (kind == "rain")
            {
                nextBolt -= Time.deltaTime;
                if (nextBolt <= 0)
                {
                    nextBolt = 7f + Random.value * 9f;
                    var b = Gfx.Sprite(root, "bolt_" + Random.Range(0, 3), 30 + Random.value * 180, 0, cvOrder - 1, "bolt");
                    b.gameObject.AddComponent<FadeOut>().Init(Settings.ReduceFlashes ? 0.4f : 0.65f, 0.08f, 0.5f);
                    if (!Settings.ReduceFlashes) ScreenPulse.Lightning(root, cvOrder + 1);
                    GameApp.Audio?.Sfx("thunder", 0.15f, 0.7f);
                }
            }
            if (acc < 1f / 30f) return; acc = 0;
            cv.Clear(Pal.Clear);
            int W = Px.W;
            if (kind == "rain")
            {
                int n = (int)(160 * intensity);
                for (int i = 0; i < n; i++)
                {
                    int x = Mathf.FloorToInt(JsMath.Hash(i, 1) * (W + 60)); float y0 = JsMath.Hash(i, 2) * h;
                    float y = (y0 + t * 260) % h; float xx = x - ((t * 130 + y * 0.5f) % (W + 60));
                    float X = ((xx % (W + 60)) + W + 60) % (W + 60) - 30;
                    cv.Px(X, y, RainA); cv.Px(X - 1, y + 2, RainA); cv.Px(X - 1, y + 3, i % 3 != 0 ? RainA : RainB);
                }
            }
            else if (kind == "snow")
            {
                for (int i = 0; i < 90; i++)
                {
                    float sp = 14 + JsMath.Hash(i, 3) * 20; float y = (JsMath.Hash(i, 2) * h + t * sp) % h;
                    float x = (JsMath.Hash(i, 1) * W + Mathf.Sin(t * 0.8f + i) * 6 + t * 4) % W;
                    var c = i % 4 == 0 ? Snow2 : Snow; cv.Px(x, y, c); if (i % 5 == 0) { cv.Px(x + 1, y, c); cv.Px(x, y + 1, c); cv.Px(x + 1, y + 1, c); }
                }
            }
            else if (kind == "ash")
            {
                for (int i = 0; i < 70; i++)
                {
                    float sp = 8 + JsMath.Hash(i, 3) * 12; float y = (JsMath.Hash(i, 2) * h + t * sp) % h;
                    float x = (JsMath.Hash(i, 1) * W + Mathf.Sin(t * 0.6f + i * 0.7f) * 8 - t * 6) % W; if (x < 0) x += W;
                    bool ember = i % 9 == 0;
                    cv.Px(x, y, ember ? (Mathf.Sin(t * 6 + i) > 0 ? Ember : Pal.Gold3) : AshA);
                }
            }
            cv.Apply();
        }
        int cvOrder => GetComponent<SpriteRenderer>().sortingOrder;
        void OnDestroy() { cv?.Destroy(); }
    }
}
