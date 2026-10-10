using System.Collections;
using System.Collections.Generic;
using OreSeas.Game;
using OreSeas.Ore;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    public static class ResultsOverlay
    {
        public static void Open(ScreenBase screen, MatchState m, MatchSummary s, bool unlocked, bool youOk, bool themOk, MatchSetup setup, string note = null)
        {
            var root = Gfx.Node(GameApp.OverlayRoot, "Results");
            UiButton.TopLayer = 1;
            var dim = Gfx.Rect(root, 0, 0, Px.W, Px.H, Pal.Ink, 300); dim.color = new Color(0.06f, 0.04f, 0.04f, 0.6f);
            UiButton.Create(root, 0, 0, Px.W, Px.H, null, 1, "blocker").sfx = null;
            Gfx.Sprite(root, "parch_216x300", 12, 104, 301); Ui.Nails(root, 12, 104, 216, 300, 302);
            string title = s.result > 0 ? "VICTORY!" : s.result < 0 ? "DEFEAT" : "DRAW";
            if (s.result > 0) Ui.Ribbon(root, "ribbon_gold", 120, 90, "", 303); else Ui.Ribbon(root, "ribbon_red_wide", 120, 92, "", 303);
            PixelText.Create(root, title, 120, s.result > 0 ? 94 : 94, TextStyle.Of(s.result > 0 ? Pal.RedTxt : Pal.GoldTxt, TextAlign.Center, 2).Outline(Pal.Ink), 305);
            PixelText.Create(root, Lay.Fit(Headline(s, setup.rival.name), 200), 120, 124, TextStyle.Of(Pal.Red1, TextAlign.Center), 303);
            var lines = new List<(string, string)>
            {
                ("ROUNDS", s.rounds.ToString()), ("VOLLEYS", s.volleys.ToString()), ("SHOTS / HITS", s.shotsFired + " / " + s.hits),
                ("ACCURACY", (s.shotsFired == 0 ? 0 : 100f * s.hits / s.shotsFired).ToString("0") + "%"), ("SHIPS SUNK", s.shipsSunk + " / 3"),
                ("CRITICAL HITS", s.crits.ToString()), ("ORE MINED (SIM)", s.oreSim.ToString("0.0000")), ("KRAKEN SIGHTINGS", s.krakens.ToString())
            };
            for (int i = 0; i < lines.Count; i++)
            {
                float y = 140 + i * 13;
                PixelText.Create(root, lines[i].Item1, 26, y, TextStyle.Of(Pal.MapD), 303);
                PixelText.Create(root, lines[i].Item2, 214, y, TextStyle.Of(Pal.Grass4, TextAlign.Right), 303);
            }
            for (int x = 26; x < 214; x += 2) Gfx.Rect(root, x, 248, 1, 1, Pal.Sand3, 302);
            PixelText.Create(root, "FLEET REVEAL (SHA-256 COMMIT)", 120, 254, TextStyle.Of(Pal.MapB, TextAlign.Center), 303);
            PixelText.Create(root, "YOU  #" + Short(s.yourCommit) + (youOk ? " OK" : " BAD"), 26, 267, TextStyle.Of(youOk ? Pal.Grass4 : Pal.Red2), 303);
            PixelText.Create(root, "THEM #" + Short(s.theirCommit) + (themOk ? " OK" : " BAD"), 26, 279, TextStyle.Of(themOk ? Pal.Grass4 : Pal.Red2), 303);
            if (unlocked)
            {
                int nx = GameApp.Leaderboard.Mine.campaignBeaten;
                string msg = nx >= Captains.Campaign.Length ? "CAMPAIGN COMPLETE! HUZZAH!" : "NEW ISLAND: " + Biomes.Get(Captains.Campaign[nx].biome).name;
                Ui.StampCentered(root, msg, 120, 296, 304);
            }
            else if (!string.IsNullOrEmpty(note)) PixelText.Create(root, note, 120, 298, TextStyle.Of(Pal.Red1, TextAlign.Center), 303);
            string quote = s.result > 0 ? setup.rival.loseLine[0] : setup.rival.winLine[0];
            // rival's parting line, wrapped inside the parchment (long names + quotes ran past the edges)
            var ql = Lay.Wrap(setup.rival.name + ": \"" + quote + "\"", 192);
            for (int i = 0; i < ql.Count && i < 2; i++) PixelText.Create(root, ql[i], 120, (ql.Count > 1 ? 313 : 318) + i * 10, TextStyle.Of(Pal.MapD, TextAlign.Center), 303);
            if (s.result >= 0) { var nug = NuggyView.Create(root, "s115", 120, 392, 306); nug.Play(s.result > 0 ? NuggyView.Pose.Dance : NuggyView.Pose.Happy, 999); }
            else { var nug = NuggyView.Create(root, "s115", 120, 392, 306); }
            // PvP: report the rival's name / portrait or block them (store policy) - beside Nuggy, inside the parchment
            var on = setup.online;
            if (on != null && on.IsPvp && !on.opponentIsAI && !string.IsNullOrEmpty(on.opponentId))
                Ui.Plank(root, 144, 386, 80, 16, "REPORT/BLOCK", null, () => PlayerActionsOverlay.Open(on.opponentId, on.opponentName, on.opponentAvatar, on.opponentRating), 310, 1);
            // campaign win that unlocked the next island: the primary button sails straight there (keeps the loop moving)
            int next = GameApp.Leaderboard.Mine.campaignBeaten;
            bool nextIsle = unlocked && setup.campaignIndex >= 0 && next < Captains.Campaign.Length;
            Ui.Small(root, 12, 412, 108, 40, nextIsle ? "NEXT ISLE" : setup.online != null && setup.online.IsPvp ? "NEW DUEL" : "REMATCH", () =>
            {
                if (nextIsle)
                {
                    var cap = Captains.Campaign[next];
                    GameApp.Net?.ClearSession();
                    if (setup.online != null) { CampaignScreen.SailOnline(cap, next); return; }
                    GameApp.Setup = new MatchSetup { mode = "solo", rival = cap, biome = Biomes.Get(cap.biome), campaignIndex = next, seed = UnityEngine.Random.Range(1, int.MaxValue) };
                    GameApp.Go<FleetScreen>();
                    return;
                }
                if (setup.online != null)
                {
                    GameApp.Net.ClearSession();
                    if (setup.online.IsPvp) GameApp.Go<MatchmakingScreen>();
                    else CampaignScreen.SailOnline(setup.rival, setup.campaignIndex);
                    return;
                }
                GameApp.Setup = new MatchSetup { mode = setup.mode, rival = setup.rival, biome = setup.biome, campaignIndex = setup.campaignIndex, seed = UnityEngine.Random.Range(1, int.MaxValue), ticket = setup.ticket };
                GameApp.Go<FleetScreen>();
            }, 310, 1, "red", 2);
            Ui.Small(root, 120, 412, 108, 40, "HARBOR", () => GameApp.Go<TitleScreen>(), 310, 1, "dim", 2);
            if (s.result > 0) { GameApp.Audio.Sting("sting_victory"); GameApp.Audio.Sfx("victory", 0.1f); Haptics.Buzz(150); }
            else if (s.result < 0) { GameApp.Audio.Sting("sting_defeat"); Haptics.Buzz(250); }
            else GameApp.Audio.Sting("sting_found");
            GameApp.Audio.DuckMusic(4f);
        }
        static string Short(string c) => string.IsNullOrEmpty(c) ? "----------" : (c.Length > 10 ? c.Substring(0, 10) : c).ToUpperInvariant();

        /// <summary>Headline under VICTORY/DEFEAT/DRAW that matches how the match actually ended (a resign used to read "SUNK BY ...").</summary>
        static string Headline(MatchSummary s, string rival)
        {
            if (s.result > 0) return s.reason == "resign" ? rival + " STRUCK COLOURS" : s.reason == "disconnect" ? rival + " LEFT THE SEAS" : "YOU BEAT " + rival;
            if (s.result < 0)
                switch (s.reason)
                {
                    case "resign": return "YOU STRUCK YOUR COLOURS";
                    case "disconnect": return "LOST AT SEA - DISCONNECTED";
                    case "inactive": return "NO VOLLEYS - " + rival + " WINS";
                    case "turn_limit": return "OUTGUNNED BY " + rival;
                    default: return "SUNK BY " + rival;
                }
            return s.reason == "turn_limit" ? "STALEMATE WITH " + rival : "MUTUAL RUIN WITH " + rival;
        }
    }
}
