using OreSeas.Game;
using UnityEngine;

namespace OreSeas.View
{
    public static class Scenery
    {
        public const int Horizon = 80;

        /// <summary>Biome sky (sprite) + drifting clouds + gulls + living ocean down to oceanBottom. Returns the ocean.</summary>
        public static OceanRenderer Build(Transform parent, BiomeDef b, int oceanBottom, bool mountains = false)
        {
            var root = Gfx.Node(parent, "Scenery");
            string sky = mountains && b.id == BiomeId.Sunset ? "sky_sunset_m" : "sky_" + b.key;
            Gfx.Sprite(root, sky, 0, 0, 0, "sky");
            float[] ys = { 22, 36, 54 }; float[] sp = { 1.5f, 1.05f, 0.75f }; float[] x0 = { 20, 150, 230 };
            for (int i = 0; i < 3; i++)
            {
                var c = Gfx.Sprite(root, "cloud_" + b.key + "_" + i, 0, 0, 1 + (i == 2 ? 1 : 0), "cloud");
                Drift.Add(c, (x0[i] % 300) - 40, ys[i], sp[i] * (b.id == BiomeId.Storm ? 4f : 1f), -60, 260);
            }
            if (b.id == BiomeId.Sunset || b.id == BiomeId.Frozen)
            {
                for (int i = 0; i < 2; i++)
                {
                    var g = SpriteAnim.Play(root, "gull", 60 + i * 14, 62 + i * 6, 3, 4, true, false); g.timeOffset = i * 0.3f;
                    var d = Drift.Add(g.Renderer, 60 + i * 14, 62 + i * 6, 3 + i, -10, 250); d.bobAmp = 2; d.bobFreq = 0.5f;
                }
            }
            var ocean = OceanRenderer.Create(root, 0, Horizon, Px.W, oceanBottom - Horizon, b.sea, 3, "Ocean");
            switch (b.id)
            {
                case BiomeId.Sunset: ocean.sunGlitter = true; ocean.sunX = 120; ocean.horizonLine = Pal.Sky3; break;
                case BiomeId.Night: ocean.sunGlitter = true; ocean.sunX = 176; ocean.glitterA = Pal.Hex("f2eede"); ocean.glitterB = Pal.Hex("8aa0c8"); ocean.glitterW = 18; ocean.horizonLine = Pal.Hex("344070"); break;
                case BiomeId.Frozen: ocean.sunGlitter = true; ocean.sunX = 150; ocean.glitterA = Pal.White; ocean.glitterB = Pal.Hex("b8d0e4"); ocean.glitterW = 22; ocean.horizonLine = Pal.Hex("b8d0e4"); break;
                case BiomeId.Volcanic: ocean.sunGlitter = true; ocean.sunX = 150; ocean.glitterA = Pal.Red3; ocean.glitterB = Pal.Red2; ocean.glitterW = 16; ocean.horizonLine = Pal.Hex("7a2a16"); break;
                case BiomeId.Storm: ocean.horizonLine = Pal.Hex("3c4860"); break;
                case BiomeId.Fog: ocean.horizonLine = Pal.Hex("b4b6c2"); break;
            }
            return ocean;
        }

        /// <summary>Weather/atmosphere layer for a biome (rain + lightning, snow, ash, fog banks).</summary>
        public static void Weather(Transform parent, BiomeDef b, int height, int order)
        {
            if (b.weather == "fog")
            {
                for (int i = 0; i < 4; i++)
                {
                    var f = Gfx.Sprite(parent, "fogbank", 0, 0, order, "fogbank"); f.color = new Color(1, 1, 1, 0.55f);
                    Drift.Add(f, i * 80 - 40, 120 + i * 60, 3 + i, -70, 310);
                }
                return;
            }
            WeatherRenderer.Create(parent, b.weather, height, order);
        }

        /// <summary>Your galleon in the foreground sea band (fgSea) + foam.</summary>
        public static void FgGalleon(Transform parent, float x, int order, bool cat = false)
        {
            var n = Gfx.Node(parent, "FgGalleon");
            var hull = SpriteAnim.Play(n, "ship_galleon_p_n", x, 346, order, 6.6f, true, false);
            var foam = SpriteAnim.Play(n, "foam94", x + 2, 347, order + 1, 6, true, false);
            Bob.Add(n, 1, 2, 0);
            if (cat) SpriteAnim.Play(n, "nugtiny", x + 9, 319, order + 2, 4, true, false);
        }
    }

}
