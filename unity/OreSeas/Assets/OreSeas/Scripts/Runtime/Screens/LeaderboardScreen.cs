using System;
using System.Collections.Generic;
using OreSeas.Game;
using OreSeas.Services;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>Captain's log + leaderboards (spec stats). GLOBAL/WEEKLY stay honest: local row + SERVER OFFLINE.</summary>
    public sealed class LeaderboardScreen : ScreenBase
    {
        Transform content; Ui.Btn[] tabs = new Ui.Btn[3]; int tab;

        public override void Build()
        {
            var r = Root;
            Scenery.Build(r, Biomes.Get(BiomeId.Sunset), 534);
            PixelText.Create(r, "CAPTAIN'S LOG", 120, 20, TextStyle.Of(Pal.GoldTxt, TextAlign.Center, 2).Outline(Pal.Ink, true), 40);
            Gfx.Sprite(r, "parch_216x300", 12, 72, 10);
            string[] names = { "MY LOG", "GLOBAL", "WEEKLY" };
            for (int i = 0; i < 3; i++) { int k = i; tabs[i] = Ui.Plank(r, 12 + i * 73, 46, 70, 20, names[i], null, () => Show(k), 30); }
            content = Gfx.Node(r, "Content");
            var deck = Gfx.Node(r, "Deck", 0, 420 - 350); Gfx.Sprite(deck, "deck", 0, 0, 50);
            Ui.Big(r, 8, 436, 224, 60, "HARBOR", 2, null, "anchor", () => GameApp.Go<TitleScreen>(), 70, 0, "dim");
            Show(0);
        }

        void Show(int k)
        {
            tab = k;
            for (int i = 0; i < 3; i++) tabs[i].label.SetColor(i == k ? Pal.Gold3 : Pal.Sand2);
            foreach (Transform c in content) Destroy(c.gameObject);
            var s = GameApp.Leaderboard.Mine;
            if (k == 0)
            {
                int xp = PlayerRank.Xp(s.gamesPlayed, s.wins, s.shipsSunk, s.crits); int nextAt = PlayerRank.NextAt(xp);
                var rows = new List<(string, string)>
                {
                    ("RANK", PlayerRank.Name(xp)), ("XP", xp + (nextAt > 0 ? " / " + nextAt : "")), ("RATING (ELO)", "..."),
                    ("GAMES PLAYED", s.gamesPlayed.ToString()), ("WINS / LOSSES", s.wins + " / " + s.losses), ("DRAWS", s.draws.ToString()),
                    ("WIN / LOSS", s.WinLossRatio.ToString("0.00")), ("BATTLES (VOLLEYS)", s.volleys.ToString()), ("SHOTS FIRED", s.shotsFired.ToString()),
                    ("ACCURACY", (s.Accuracy * 100).ToString("0") + "%"), ("SHIPS SUNK", s.shipsSunk.ToString()), ("CRITICAL HITS", s.crits.ToString()),
                    ("ORE MINED (SIM)", s.oreMinedSim.ToString("0.0000")), ("KRAKEN SIGHTINGS", s.krakenSightings.ToString()),
                    ("CAMPAIGN", s.campaignBeaten + " / 5"), ("RANKED GAMES", s.rankedGames.ToString())
                };
                PixelText ratingVal = null;
                for (int i = 0; i < rows.Count; i++)
                {
                    float y = 80 + i * 14;
                    PixelText.Create(content, rows[i].Item1, 24, y, TextStyle.Of(Pal.MapD), 12);
                    var v = PixelText.Create(content, rows[i].Item2, 216, y, TextStyle.Of(Pal.Red1, TextAlign.Right), 12);
                    if (i == 2) ratingVal = v;
                    for (int x = 24; x < 216; x += 2) Gfx.Rect(content, x, y + 11, 1, 1, Pal.Sand3, 11);
                }
                var myContent = content;
                if (GameApp.Leaderboard is ServerLeaderboardService srv)
                    srv.FetchProfile((p, err) =>
                    {
                        if (this == null || tab != 0 || myContent == null || ratingVal == null) return;
                        if (p == null) { ratingVal.Set("OFFLINE"); return; }
                        ratingVal.Set(p.rating + "  (" + p.games + " ONLINE, " + p.wins + "W)");
                    });
                else ratingVal.Set("OFFLINE");
                return;
            }
            PixelText.Create(content, "LOADING...", 120, 160, TextStyle.Of(Pal.MapB, TextAlign.Center), 12, "loading");
            int want = k;
            GameApp.Leaderboard.Fetch(k == 1 ? LeaderboardScope.Global : LeaderboardScope.Weekly, (list, note) =>
            {
                if (this == null || tab != want) return;
                foreach (Transform c in content) Destroy(c.gameObject);
                var hdr = TextStyle.Of(Pal.MapB);
                PixelText.Create(content, "#", 18, 82, hdr, 12);
                PixelText.Create(content, "CAPTAIN", 30, 82, hdr, 12);
                PixelText.Create(content, "G", 112, 82, TextStyle.Of(Pal.MapB, TextAlign.Right), 12);
                PixelText.Create(content, "W-L", 142, 82, TextStyle.Of(Pal.MapB, TextAlign.Right), 12);
                PixelText.Create(content, "W/L", 168, 82, TextStyle.Of(Pal.MapB, TextAlign.Right), 12);
                PixelText.Create(content, "BTL", 194, 82, TextStyle.Of(Pal.MapB, TextAlign.Right), 12);
                PixelText.Create(content, "ELO", 222, 82, TextStyle.Of(Pal.MapB, TextAlign.Right), 12);
                for (int x = 18; x < 222; x += 2) Gfx.Rect(content, x, 93, 1, 1, Pal.MapB, 11);
                bool anyAi = false;
                var net = GameApp.Net;
                for (int i = 0; i < list.Count && i < 14; i++)
                {
                    var row = list[i]; float y = 98 + i * 14;
                    bool blocked = !row.isYou && net != null && net.IsBlocked(row.id);
                    var col = row.isYou ? Pal.Red1 : blocked ? Pal.Sand3 : Pal.MapD;
                    string nm = blocked ? "BLOCKED" : (row.isYou && row.name == "YOU") ? "YOU" : (row.name ?? "?").ToUpperInvariant();
                    if (PixelFont.Measure(nm) > 54) { var first = nm.Split(' ')[0]; nm = Lay.Fit(PixelFont.Measure(first) >= 24 ? first : nm, 54); }   // by pixels: the G column starts ~100
                    PixelText.Create(content, row.rank > 0 ? row.rank.ToString() : "-", 18, y, TextStyle.Of(col), 12);
                    if (row.ai)
                    {
                        // scripted / demo captains wear an "AI" chip where the portrait goes, so they never pass for real players
                        anyAi = true;
                        Gfx.Rect(content, 29, y - 2, 13, 12, Pal.Ink, 12, "aichip");
                        Gfx.Rect(content, 30, y - 1, 11, 10, Pal.Plum, 13, "aichip_in");
                        PixelText.Create(content, "AI", 31, y, TextStyle.Of(Pal.Gold4), 14, "ai");
                    }
                    else Avatars.DrawMini(content, blocked ? Avatars.Default : row.avatar, 29, y - 2, 12);
                    PixelText.Create(content, nm, 43, y, TextStyle.Of(col), 13);
                    PixelText.Create(content, row.games.ToString(), 112, y, TextStyle.Of(col, TextAlign.Right), 12);
                    PixelText.Create(content, row.wins + "-" + row.losses, 142, y, TextStyle.Of(col, TextAlign.Right), 12);
                    PixelText.Create(content, row.wl.ToString("0.0"), 168, y, TextStyle.Of(col, TextAlign.Right), 12);
                    PixelText.Create(content, row.battles.ToString(), 194, y, TextStyle.Of(col, TextAlign.Right), 12);
                    PixelText.Create(content, row.rating.ToString(), 222, y, TextStyle.Of(col, TextAlign.Right), 12);
                    // tap a real rival's row: report name / portrait, block (store policy)
                    if (!row.isYou && !row.ai && !string.IsNullOrEmpty(row.id))
                    {
                        var rr = row; int keep = want;
                        UiButton.Create(content, 16, y - 2, 208, 14, () => PlayerActionsOverlay.Open(rr.id, rr.name, rr.avatar, rr.rating, () => { if (this != null && tab == keep) Show(keep); }), 0, "row" + i).sfx = "tap";
                    }
                }
                if (!string.IsNullOrEmpty(note)) Ui.StampCentered(content, note, 120, 300, 14);
                var legend = new List<string> { "G GAMES  BTL VOLLEYS  ELO RATING" };
                if (k == 2) legend.Add("WEEK STARTS MONDAY 00:00 UTC");
                if (anyAi) legend.Add("AI = COMPUTER CAPTAIN");
                if (list.Exists(x => !x.isYou && !x.ai)) legend.Add("TAP A RIVAL TO REPORT OR BLOCK");
                for (int i = 0; i < legend.Count; i++) PixelText.Create(content, legend[i], 120, 318 + i * 12, TextStyle.Of(Pal.MapB, TextAlign.Center), 12);
            });
        }

        public void ShowTab(int k) => Show(k);
        public override void Back() => GameApp.Go<TitleScreen>();
    }
}
