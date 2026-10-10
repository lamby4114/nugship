using UnityEngine;

namespace OreSeas
{
    /// <summary>Logical screen is 240x534 "mockup pixels", y pointing down. World units = pixels, y up.</summary>
    public static class Px
    {
        public const int W = 240, H = 534;
        public const int SafeTop = 16, SafeBottom = 520;

        public static Vector3 P(float x, float y, float z = 0f) => new Vector3(JsMath.Round(x), -JsMath.Round(y), z);
        public static Vector3 Pf(float x, float y) => new Vector3(x, -y, 0f);

        /// <summary>Camera that renders the logical screen (set by GameApp). When present, input maps through it so
        /// Pixel Perfect Camera crop/zoom (camera.pixelRect) is honoured.</summary>
        public static Camera Cam;

        /// <summary>Maps a screen-space position (pixels, origin bottom-left) to logical mockup coords.</summary>
        public static Vector2 ScreenToLogical(Vector2 sp)
        {
            if (Cam != null && Cam.orthographic)
            {
                var wp = Cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -Cam.transform.position.z));
                return new Vector2(wp.x, -wp.y);
            }
            float sw = Screen.width, sh = Screen.height;
            float s = Mathf.Min(sw / W, sh / H);
            float ox = (sw - W * s) * 0.5f, oy = (sh - H * s) * 0.5f;
            return new Vector2((sp.x - ox) / s, H - (sp.y - oy) / s);
        }
    }

    /// <summary>Bit-exact ports of the mockup engine's hash/rng/round helpers (engine.js).</summary>
    public static class JsMath
    {
        public static float Round(float v) => Mathf.Floor(v + 0.5f);
        public static int RoundI(float v) => (int)Mathf.Floor(v + 0.5f);

        public static float Hash(int x, int y, int s = 0)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + s * 982451653;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                uint r = (uint)(h ^ (int)((uint)h >> 16));
                return (float)(r / 4294967296.0);
            }
        }

        /// <summary>mulberry32, same as rng() in engine.js</summary>
        public sealed class Mulberry
        {
            int seed;
            public Mulberry(int s) { seed = s; }
            public float Next()
            {
                unchecked
                {
                    seed = seed + 0x6D2B79F5;
                    int t = (seed ^ (int)((uint)seed >> 15)) * (1 | seed);
                    t = (t + ((t ^ (int)((uint)t >> 7)) * (61 | t))) ^ t;
                    return (float)(((uint)(t ^ (int)((uint)t >> 14))) / 4294967296.0);
                }
            }
        }
    }

    public static class Ease
    {
        public static float OutQuad(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t); }
        public static float InOutCubic(float t) { t = Mathf.Clamp01(t); return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2; }
        public static float OutBack(float t) { t = Mathf.Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1; return 1 + c3 * Mathf.Pow(t - 1, 3) + c1 * Mathf.Pow(t - 1, 2); }
        public static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
    }
}
