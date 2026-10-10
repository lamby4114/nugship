using System;
using System.Collections.Generic;
using OreSeas.Game;
using OreSeas.Net;
using OreSeas.Services;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>Solo campaign chart: five island captains + the Daily Tide.</summary>
    public sealed class CampaignScreen : ScreenBase
    {
        static readonly Vector2[] Nodes = { new Vector2(56, 316), new Vector2(160, 276), new Vector2(66, 214), new Vector2(166, 160), new Vector2(98, 98) };
        int selected = -1; Transform info; Ui.Btn sail; SpriteRenderer pointer;

        public override void Build()
        {
            var r = Root;
            Scenery.Build(r, Biomes.Get(BiomeId.Sunset), 420);
            PixelText.Create(r, "SOLO CAMPAIGN", 120, 18, TextStyle.Of(Pal.GoldTxt, TextAlign.Center, 2).Outline(Pal.Ink, true), 40);
            Gfx.Sprite(r, "parch_228x330", 6, 40, 10);
            Gfx.Sprite(r, "chart_sea", 14, 50, 11);
            Gfx.Rect(r, 13, 49, 214, 1, Pal.MapD, 12); Gfx.Rect(r, 13, 350, 214, 1, Pal.MapD, 12); Gfx.Rect(r, 13, 49, 1, 302, Pal.MapD, 12); Gfx.Rect(r, 226, 49, 1, 302, Pal.MapD, 12);
            Gfx.Sprite(r, "compass", 202, 74, 13);
            int beaten = GameApp.Leaderboard.Mine.campaignBeaten;
            for (int i = 0; i < Nodes.Length - 1; i++) DotPath(r, Nodes[i], Nodes[i + 1], i < beaten ? Pal.Red1 : Pal.MapD);
            for (int i = 0; i < Captains.Campaign.Length; i++)
            {
                var c = Captains.Campaign[i]; var p = Nodes[i]; bool unlocked = i <= beaten; int idx = i;
                Gfx.Sprite(r, "isle_" + Biomes.Get(c.biome).key, p.x, p.y, 20);
                // centre label + skulls on the island art's own centre (isle sprites are 41/42 px wide, pivot 20 px in)
                var isle = Atlas.Get("isle_" + Biomes.Get(c.biome).key);
                float cx = PixelFont.LegacyCenter || isle == null ? p.x : p.x - isle.pivot.x + isle.rect.width / 2f;
                int w = PixelFont.CenteredWidth(c.name, cx) + 8;
                Gfx.Rect(r, cx - w / 2f, p.y + 10, w, 11, Pal.Ink, 21); Gfx.Rect(r, cx - w / 2f + 1, p.y + 11, w - 2, 9, unlocked ? Pal.Plum : Pal.Grey0, 22);
                PixelText.Create(r, c.name, cx, p.y + 11, TextStyle.Of(unlocked ? Pal.GoldTxt : Pal.WhiteTxt, TextAlign.Center), 23);
                // skulls are 5 px wide, 9 px pitch: group width 9n-4
                float sk0 = PixelFont.LegacyCenter ? p.x - c.skulls * 4.5f : cx - (9 * c.skulls - 4) / 2f;
                for (int k = 0; k < c.skulls; k++) Gfx.Sprite(r, "icon_skull", sk0 + k * 9, p.y + 23, 23);
                if (i < beaten) Gfx.Sprite(r, "icon_check", cx + w / 2f - 2, p.y + 6, 24);
                else if (!unlocked) Gfx.Sprite(r, "icon_lock", PixelFont.LegacyCenter ? p.x - 5 : cx - 3.5f, p.y - 26, 24);
                UiButton.Create(r, p.x - 30, p.y - 30, 60, 62, () => Select(idx), 0, "Node" + i).sfx = unlocked ? "select" : "error";
            }
            pointer = Gfx.Sprite(r, "hand_0", 0, 0, 30); pointer.gameObject.SetActive(false);
            var deck = Gfx.Node(r, "Deck", 0, 372 - 350); Gfx.Sprite(deck, "deck", 0, 0, 50);
            info = Gfx.Node(r, "Info");
            sail = Ui.Big(r, 8, 428, 224, 60, "SET SAIL", 2, "CHOOSE AN ISLAND", "anchor", Sail, 70);
            sail.SetEnabled(false);
            Ui.Plank(r, 8, 494, 108, 20, "HARBOR", null, () => GameApp.Go<TitleScreen>(), 70);
            Ui.Plank(r, 124, 494, 108, 20, "DAILY TIDE", "icon_trophy", Daily, 70);
            Select(Mathf.Min(beaten, Captains.Campaign.Length - 1));
            GameApp.Audio.Music("music_title");
            GameApp.Net.EnsureConnected(ok => { if (this != null && selected >= 0) sail.SetSub(SailSub(Captains.Campaign[selected], ok)); });
        }

        static void DotPath(Transform r, Vector2 a, Vector2 b, Color32 c)
        {
            float len = Vector2.Distance(a, b); int n = (int)(len / 5);
            for (int i = 2; i < n - 1; i++) { var p = Vector2.Lerp(a, b, i / (float)n); Gfx.Rect(r, Mathf.Round(p.x), Mathf.Round(p.y), 2, 1, c, 14); }
        }

        void Select(int i)
        {
            int beaten = GameApp.Leaderboard.Mine.campaignBeaten;
            if (i > beaten) { GameApp.Toast("BEAT " + Captains.Campaign[i - 1].name + " FIRST"); return; }
            selected = i;
            foreach (Transform c in info) Destroy(c.gameObject);
            var cap = Captains.Campaign[i]; var b = Biomes.Get(cap.biome);
            Dialog(info, "cap_" + cap.id, cap.name + " - " + b.name, new[] { "\"" + cap.taunt[0] + "\"", AiBlurb(cap) }, 380, 60);
            sail.SetEnabled(true); sail.SetSub(SailSub(cap, GameApp.Net != null && GameApp.Net.Ready));
            pointer.gameObject.SetActive(true);
            pointer.transform.localPosition = Px.P(Nodes[i].x + 14, Nodes[i].y - 22);
            var bob = pointer.GetComponent<Bob>();
            if (bob == null) Bob.Add(pointer.transform, 2, 5, 0); else bob.rest = pointer.transform.localPosition;
        }

        /// <summary>"VS BARNACLE - SERVER AI"; long names drop the suffix so the sub never runs under the anchor.</summary>
        static string SailSub(CaptainDef c, bool online)
        {
            string s = "VS " + c.name + (online ? " - SERVER AI" : " - NO SOL");
            return PixelFont.Measure(s) <= 146 ? s : "VS " + c.name;
        }

        static string AiBlurb(CaptainDef c)
        {
            switch (c.ai)
            {
                case AiStyle.RandomHunter: return "Fires blind. A gentle start.";
                case AiStyle.HuntTargetParity: return "Hunts in a checker, finishes hits.";
                case AiStyle.HuntTargetCenter: return "Loves the middle. Bites hard.";
                default: return c.bonusShots > 0 ? "Reads the odds. +1 shot a volley!" : "Reads the odds of every tile.";
            }
        }

        /// <summary>sceneDialog(): parchment scroll with wooden rods + portrait.</summary>
        public static void Dialog(Transform p, string portrait, string name, string[] lines, float y, int order, bool mate = false)
        {
            float x = 6;
            Gfx.Rect(p, x + 2, y + 3, 228, 40, Pal.Sea0, order - 1);
            Gfx.Sprite(p, "parch_228x40", x, y, order);
            Gfx.Sprite(p, "dialog_rods", x, y, order + 1);
            if (!string.IsNullOrEmpty(portrait)) Gfx.Sprite(p, portrait, x + 6, y + 7, order + 2);
            if (mate) Gfx.Sprite(p, "firstmate_0", x + 33, y + 10, order + 2);
            float tx = x + (mate ? 58 : 37);
            PixelText.Create(p, name, tx, y + 5, TextStyle.Of(Pal.Red1), order + 3);
            for (int i = 0; i < lines.Length; i++) PixelText.Create(p, lines[i], tx, y + 16 + i * 10, TextStyle.Of(Pal.MapD), order + 3);
        }

        void Sail()
        {
            if (selected < 0) return;
            SailOnline(Captains.Campaign[selected], selected);
        }

        static void SailLocal(CaptainDef cap, int idx)
        {
            GameApp.Setup = new MatchSetup { mode = "solo", rival = cap, biome = Biomes.Get(cap.biome), campaignIndex = idx, seed = UnityEngine.Random.Range(1, int.MaxValue) };
            GameApp.Go<FleetScreen>();
        }

        /// <summary>Solo vs AI through the server (start_solo, practice rounds, no SOL); local AI when the server is unreachable.</summary>
        public static void SailOnline(CaptainDef cap, int idx)
        {
            var net = GameApp.Net;
            if (net == null || !net.Ready || cap == null || idx < 0) { SailLocal(cap ?? Captains.Campaign[0], Mathf.Max(0, idx)); return; }
            Action<OnlineSession> found = null; Action<Dictionary<string, object>> err = null;
            found = s => { net.MatchFound -= found; net.Message -= err; MatchmakingScreen.LaunchOnline(s, idx); };
            err = m =>
            {
                if (J.Str(m, "t") != "error" || J.Str(m, "ref") != "start_solo") return;
                net.MatchFound -= found; net.Message -= err;
                { var code = J.Str(m, "code", "error"); GameApp.Toast((code == "server_restarting" ? "SERVER RESTARTING" : "SERVER: " + code.ToUpperInvariant().Replace('_', ' ')) + " - LOCAL AI"); } SailLocal(cap, idx);
            };
            net.MatchFound += found; net.Message += err;
            net.ClearSession();
            net.StartSolo(ServerClient.ServerCaptainId(cap.id), true);
            GameApp.Toast("HOISTING SAILS...");
        }

        void Daily()
        {
            var cap = Captains.Daily(DateTime.UtcNow.Date, out int seed);
            GameApp.Setup = new MatchSetup { mode = "daily", rival = cap, biome = Biomes.Get(cap.biome), seed = seed };
            GameApp.Go<FleetScreen>();
        }

        public override void Back() => GameApp.Go<TitleScreen>();
    }

}
