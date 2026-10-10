using System.Collections;
using OreSeas.Game;
using OreSeas.Ore;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>Motherlode (1 in 500 ORE rounds): THE KRAKEN RISES! Full-screen overlay; tap to continue.</summary>
    public static class KrakenOverlay
    {
        public static IEnumerator Run(MonoBehaviour host, MatchState m, VolleyResult v)
        {
            double share = 0; foreach (var s in v.yours) if (s.oreSquare) share += s.ore;
            return Run(OreConstants.SquareName(v.ore.winningSquare), share, 0f);
        }

        /// <summary>Shared overlay. autoClose &gt; 0 (online: the ORE clock keeps running) dismisses itself after that many seconds.</summary>
        public static IEnumerator Run(string sq, double share, float autoClose)
        {
            var root = Gfx.Node(GameApp.OverlayRoot, "Kraken");
            UiButton.TopLayer = 1;
            GameApp.Audio.Music("music_kraken"); GameApp.Audio.Sfx("kraken"); GameApp.Audio.Sfx("thunder", 0.4f); Haptics.Buzz(300);
            UiButton.Create(root, 0, 0, Px.W, Px.H, null, 1, "blocker").sfx = null;
            Gfx.Sprite(root, "kraken_sky", 0, 0, 301);
            var ocean = OceanRenderer.Create(root, 0, 122, Px.W, 350 - 122, SeaPalette.Storm, 302, "KrakenSea"); ocean.cw = 12; ocean.ch = 8; ocean.horizonLine = Pal.Hex("5a6a90");
            // one slow, dim lightning flicker (no strobing bolt, no white frame)
            var bolt1 = Gfx.Sprite(root, "bolt_2", 52, 0, 302, "bolt");
            bolt1.gameObject.AddComponent<FadeOut>().Init(Settings.ReduceFlashes ? 0.45f : 0.7f, 0.2f, 1.2f);
            ScreenPulse.Lightning(root, 495);
            SpriteAnim.Play(root, "kraken_back", 0, 0, 303, 8, true, false);
            SpriteAnim.Play(root, "ship_galleon_p_n", -4, 206, 304, 6.6f, true, false);
            var sl = SpriteAnim.Play(root, "ship_sloop_p_n", 170 + 72, 200, 304, 6.6f, true, false); sl.Renderer.flipX = true;
            Gfx.Rect(root, 0, 206, 112, 6, SeaPalette.Storm.b, 305); Gfx.Rect(root, 170, 200, 72, 6, SeaPalette.Storm.b, 305);
            SpriteAnim.Play(root, "kraken_front", 0, 0, 306, 8, true, false);
            const float cy = 328;
            SpriteAnim.Play(root, "chest", 120, cy, 307, 6, true, false);
            Fx.Nuggets(root, 120, cy - 30, 30, 308);
            PixelText.Create(root, "THE KRAKEN", 120, 20, TextStyle.Of(Pal.RedTxt, TextAlign.Center, 3).Outline(Pal.Ink, true), 310);
            PixelText.Create(root, "RISES!", 120, 46, TextStyle.Of(Pal.RedTxt, TextAlign.Center, 2).Outline(Pal.Ink), 310);
            PixelText.Create(root, "MOTHERLODE!", 120, 258, TextStyle.Of(Pal.GoldTxt, TextAlign.Center, 2).Outline(Pal.Ink, true), 310);
            WeatherRenderer.Create(root, "rain", 350, 311);
            Gfx.Sprite(root, "deck", 0, 0, 320);
            Gfx.Sprite(root, "parch_224x62", 8, 358, 321);
            foreach (var nx in new[] { 11, 227 }) { Gfx.Sprite(root, "nail", nx, 361, 322); Gfx.Sprite(root, "nail", nx, 414, 322); }
            PixelText.Create(root, "THE DEEP PAYS OUT (SIM)", 120, 361, TextStyle.Of(Pal.Red1, TextAlign.Center), 322);
            PixelText.Create(root, "MOTHERLODE POT", 14, 374, TextStyle.Of(Pal.MapD), 322);
            PixelText.Create(root, "+" + OreConstants.SimMotherlodePot.ToString("0.0") + " ORE", 226, 374, TextStyle.Of(Pal.Grass4, TextAlign.Right), 322);
            PixelText.Create(root, share > 0 ? "YER SHARE (" + sq + ")" : "NO SHOT ON " + sq, 14, 385, TextStyle.Of(Pal.MapD), 322);
            PixelText.Create(root, share > 0 ? "+" + share.ToString("0.00") + " ORE" : "0 ORE", 226, 385, TextStyle.Of(share > 0 ? Pal.Grass4 : Pal.Red1, TextAlign.Right), 322);
            for (int x = 14; x < 226; x += 2) Gfx.Rect(root, x, 396, 1, 1, Pal.Sand3, 322);
            PixelText.Create(root, "About 1 in 500 ORE rounds,", 14, 399, TextStyle.Of(Pal.MapB), 322);
            PixelText.Create(root, "split by miners on the square.", 14, 409, TextStyle.Of(Pal.MapB), 322);
            bool done = false;
            Ui.Big(root, 8, 428, 224, 60, "HUZZAH!", 3, "SIMULATED MOTHERLODE", "chest", () => done = true, 330, 1);
            Ui.Hint(root, "THE KRAKEN STIRS THE DEEP...", 494, 330);
            float t0 = Time.unscaledTime;
            while (!done && (autoClose <= 0 || Time.unscaledTime - t0 < autoClose)) yield return null;
            Object.Destroy(root.gameObject);
            UiButton.TopLayer = 0;
            GameApp.Audio.Music("music_battle_calm", "music_battle_tension");
        }
    }
}
