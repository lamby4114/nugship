using System;
using System.Collections.Generic;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>
    /// HOW TO PLAY: nine short picture cards on a parchment (title + pixel illustration + 3-5 plain lines).
    /// Tap the right/left half or the arrows to flip; CLOSE or Android back. Opens from the title "?" and the pause menu.
    /// Rules text mirrors Game/Match.cs + server match.ts (shots = ships afloat, +1 next volley after a sink or a crit).
    /// </summary>
    public static class HelpOverlay
    {
        public const int CardCount = 10;
        /// <summary>$NUG (Nuggy) on Solana. Source: Dexscreener NUG/ORE Raydium CPMM pair DG8WFbep49tDTwr6sz4SYkSWaNCvzzyV9PKuyowRR4w9.
        /// Empty NUG_DEX_URL hides the buy button.</summary>
        public const string NUG_CA = "251DUme9Gjp34Pv3YWuHhPfoEmfhFXQe769FAtr8LuWj";
        public const string NUG_DEX_URL = "https://dexscreener.com/solana/DG8WFbep49tDTwr6sz4SYkSWaNCvzzyV9PKuyowRR4w9";
        static int curLayer;
        static Transform openRoot;
        public static bool IsOpen => openRoot != null;
        const int TextW = 196;
        static readonly Color32 Ink = Pal.MapD;

        sealed class Card { public string title; public string body; public Action<Transform, int> art; public Action<Transform, int> extra; }

        static readonly Card[] Cards =
        {
            new Card { title = "ONE ROUND = ONE ORE ROUND", art = ArtClock,
                body = "Every volley is one round of ORE, the Solana mining game. A round lasts about a minute. The ORE clock on top of the screen counts it down." },
            new Card { title = "PICK YOUR SHOTS", art = ArtAim,
                body = "Tap squares on the enemy grid to aim. You get one shot for every ship you still have afloat: 3 ships, 3 shots." },
            new Card { title = "BOTH CAPTAINS LOCK", art = ArtLock,
                body = "Press FIRE to lock your shots. When both captains are locked, or the clock runs out, both volleys fire at once." },
            new Card { title = "HITS AND SPLASHES", art = ArtHits,
                body = "Fire = a hit on a hidden ship. A splash = open water. Sink the galleon (3), sloop (2) and raft (1) to win." },
            new Card { title = "CRITICAL HITS", art = ArtCrit,
                body = "Each ORE round picks one winning square. Hit a ship on that square = CRITICAL HIT. Miss on it = gold bubbles and a little ORE." },
            new Card { title = "BONUS SHOTS", art = ArtBonus,
                body = "Shots = ships afloat. Sink a ship or land a crit and you get +1 bonus shot on the next volley." },
            new Card { title = "THE KRAKEN", art = ArtKraken,
                body = "About once in 500 rounds ORE hits the Motherlode. The Kraken rises, and shots on the winning square haul up a big ORE prize." },
            new Card { title = "ORE, PLAINLY", art = ArtOre,
                body = "ORE is a mining game on Solana: every minute miners pick squares on a 5x5 grid and one square wins. NUGSHIP uses those public rounds as fair dice." },
            new Card { title = "NO WAGERING", art = ArtNoBet,
                body = "No bets, no stakes, no SOL. Nothing is signed or spent in this build. Play for glory, rating and the leaderboard." },
            new Card { title = "ABOUT NUGGY", art = ArtNuggy, extra = NuggyExtra,
                body = "Nuggy is the ship's cat of the NUGSHIP fleet. He sniffs out ORE on every grid and dances after every hit." },
        };

        /// <summary>Opens above whatever is showing. page: starting card.</summary>
        public static Transform Open(int page = 0)
        {
            int prevLayer = UiButton.TopLayer, layer = prevLayer + 1, o = 500 + 100 * prevLayer;
            UiButton.TopLayer = layer; curLayer = layer;
            var root = Gfx.Node(GameApp.OverlayRoot, "Help");
            var dim = Gfx.Rect(root, 0, 0, Px.W, Px.H, Pal.Ink, o); dim.color = new Color(0.06f, 0.04f, 0.04f, 0.78f);
            UiButton.Create(root, 0, 0, Px.W, Px.H, null, layer, "blocker").sfx = null;
            Gfx.Sprite(root, "parch_228x330", 6, 100, o + 1); Ui.Nails(root, 6, 100, 228, 330, o + 2);
            Ui.Ribbon(root, "ribbon_gold", 120, 90, "HOW TO PLAY", o + 3, Pal.GoldTxt, null, 1);
            var cardRoot = Gfx.Node(root, "Card");
            var dots = new List<SpriteRenderer>();
            // page dots: 4 px dots, 2 px gaps -> 6n-2 px wide (even) so they centre on x=120 exactly
            int dw = 6 * CardCount - 2; float dx0 = 120 - dw / 2f;
            for (int i = 0; i < CardCount; i++) dots.Add(Gfx.Rect(root, dx0 + i * 6, 404, 4, 4, Pal.Sand3, o + 3, "dot"));
            PixelText pageText = PixelText.Create(root, "", 120, 412, TextStyle.Of(Pal.MapB, TextAlign.Center), o + 3, "page");

            int cur = -1;
            Action<int> show = null;
            show = p =>
            {
                p = (p % CardCount + CardCount) % CardCount; if (p == cur) return; cur = p;
                foreach (Transform c in cardRoot) UnityEngine.Object.Destroy(c.gameObject);
                var c2 = Cards[p];
                var node = Gfx.Node(cardRoot, "card" + p);
                PixelText.Create(node, c2.title, 120, 126, TextStyle.Of(Pal.Red1, TextAlign.Center), o + 4, "title");
                Gfx.Rect(node, 40, 137, 160, 1, Pal.Sand3, o + 4, "rule");
                c2.art(Gfx.Node(node, "art", 0, 18), o + 5);   // illustrations are authored for y 132..230; shifted below the ribbon
                var lines = Lay.Wrap(c2.body, TextW);
                for (int i = 0; i < lines.Count; i++) PixelText.Create(node, lines[i], 120, 268 + i * 12, TextStyle.Of(Ink, TextAlign.Center), o + 4, "line");
                c2.extra?.Invoke(node, o + 6);
                for (int i = 0; i < dots.Count; i++) dots[i].color = (Color)(i == p ? Pal.Red2 : Pal.Sand3);
                pageText.Set((p + 1) + " / " + CardCount);
                GameApp.Audio?.Sfx("tap");
            };
            // tap zones: left half = back, right half = next (inside the card), plus arrow planks
            UiButton.Create(root, 6, 122, 114, 272, () => show(cur - 1), layer, "prevZone").sfx = null;
            UiButton.Create(root, 120, 122, 114, 272, () => show(cur + 1), layer, "nextZone").sfx = null;
            Ui.Plank(root, 14, 398, 40, 22, "<", null, () => show(cur - 1), o + 6, layer);
            Ui.Plank(root, 186, 398, 40, 22, ">", null, () => show(cur + 1), o + 6, layer);
            Action close = null;
            openRoot = root;
            close = () => { Lay.PopBack(close); UiButton.TopLayer = prevLayer; if (root) UnityEngine.Object.Destroy(root.gameObject); if (openRoot == root) openRoot = null; };
            Ui.Small(root, 66, 440, 108, 34, "CLOSE", close, o + 6, layer, "red");
            Lay.PushBack(close);
            show(page);
            CenterAudit.RunDeferred(GameApp.I, "HelpOverlay");
            var flip = root.gameObject.AddComponent<HelpFlip>(); flip.show = d => show(cur + d);
            return root;
        }

        /// <summary>Desktop arrow keys flip cards (the screenshot player uses Show()).</summary>
        sealed class HelpFlip : MonoBehaviour
        {
            public Action<int> show;
            void Update()
            {
                if (Input.GetKeyDown(KeyCode.RightArrow)) show(1);
                if (Input.GetKeyDown(KeyCode.LeftArrow)) show(-1);
            }
        }

        // ---------------- illustrations (centred on x=120, band y 130..290) ----------------
        static void Frame(Transform p, float x, float y, float w, float h, int o, Color32 fill)
        {
            Gfx.Rect(p, x, y, w, h, Pal.Ink, o); Gfx.Rect(p, x + 1, y + 1, w - 2, h - 2, fill, o + 1);
        }

        /// <summary>Mini 5x5 grid: 13 px cells, 66 px square, centred at cx. Returns the cell top-left function.</summary>
        static Func<int, int, Vector2> Grid(Transform p, float cx, float top, int o, Color32 sea)
        {
            float left = cx - 33;
            Gfx.Rect(p, left, top, 66, 66, Pal.Ink, o);
            for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++) Gfx.Rect(p, left + 1 + x * 13, top + 1 + y * 13, 12, 12, (x + y) % 2 == 0 ? sea : Pal.Sea3, o + 1);
            return (x, y) => new Vector2(left + 1 + x * 13, top + 1 + y * 13);
        }

        static void Caption(Transform p, string s, float cx, float y, int o, Color32? c = null) => PixelText.Create(p, s, cx, y, TextStyle.Of(c ?? Pal.MapB, TextAlign.Center), o, "cap");

        // Each illustration is built as one horizontal group whose total width is computed first, so it centres on x=120
        // (odd-width groups take the extra pixel on the right: left = floor(120 - w/2)).
        static float GroupLeft(int w) => Mathf.Floor(120 - w / 2f);

        static void ArtClock(Transform p, int o)
        {
            int ww = Lay.Width("wheel_0"), gw = Lay.GroupWidth(12, ww, 56); float l = GroupLeft(gw);
            Lay.SpriteTL(p, "wheel_0", l, 150, o);
            Lay.SpriteTL(p, "hud_timer", l + ww + 12, 158, o);
            PixelText.Create(p, "0:59", l + ww + 12 + 28, 165, TextStyle.Of(Pal.Gold4, TextAlign.Center), o + 2, "t");
            Caption(p, "1 VOLLEY = 1 ORE ROUND", 120, 206, o, Pal.Red1);
            Caption(p, "ABOUT 1 MINUTE", 120, 220, o);
        }

        static void ArtAim(Transform p, int o)
        {
            var cell = Grid(p, 120, 132, o, Pal.Sea2);
            foreach (var c in new[] { new Vector2Int(1, 1), new Vector2Int(3, 2), new Vector2Int(2, 4) })
            {
                var v = cell(c.x, c.y);
                Gfx.Rect(p, v.x - 1, v.y - 1, 14, 14, Pal.Gold3, o + 2); Gfx.Rect(p, v.x + 1, v.y + 1, 10, 10, Pal.Sea2, o + 3);
                Gfx.Rect(p, v.x + 5, v.y + 3, 2, 6, Pal.Gold3, o + 4); Gfx.Rect(p, v.x + 3, v.y + 5, 6, 2, Pal.Gold3, o + 4);
            }
            int sw = Lay.Width("shot_loaded"), gw = Lay.GroupWidth(4, sw, sw, sw); float l = GroupLeft(gw);
            for (int i = 0; i < 3; i++) Lay.SpriteTL(p, "shot_loaded", l + i * (sw + 4), 206, o);
            Caption(p, "3 SHIPS AFLOAT = 3 SHOTS", 120, 222, o);
        }

        static void ArtLock(Transform p, int o)
        {
            int lw = Lay.Width("icon_lock"), gw = Lay.GroupWidth(20, 26, lw, 26); float l = GroupLeft(gw);
            Avatars.Draw(p, Services.Profile.Avatar, l, 150, o);
            Lay.SpriteTL(p, "icon_lock", l + 26 + 20, 158, o);
            Lay.SpriteTL(p, "cap_barnacle_f", l + 26 + 20 + lw + 20, 150, o);
            Caption(p, "YOU", l + 13, 180, o); Caption(p, "RIVAL", l + 26 + 20 + lw + 20 + 13, 180, o);
            Caption(p, "LOCKED + LOCKED = FIRE!", 120, 206, o, Pal.Red1);
        }

        static void ArtHits(Transform p, int o)
        {
            var cell = Grid(p, 120, 132, o, Pal.Sea2);
            foreach (var h in new[] { new Vector2Int(1, 2), new Vector2Int(2, 2) })
            { var a = cell(h.x, h.y); Gfx.Rect(p, a.x, a.y, 12, 12, Pal.Wood2, o + 2); Lay.SpriteTL(p, "xmark", a.x + 1, a.y + 1, o + 3); }
            foreach (var m in new[] { new Vector2Int(3, 0), new Vector2Int(0, 4), new Vector2Int(4, 3) })
            { var d = cell(m.x, m.y); Gfx.Rect(p, d.x + 4, d.y + 4, 4, 4, Pal.Foam, o + 3); }
            Caption(p, "X = HIT    DOT = SPLASH", 120, 206, o);
            int g1 = Lay.Width("ficon_galleon_ok"), g2 = Lay.Width("ficon_sloop_ok"), g3 = Lay.Width("ficon_raft_ok");
            int gw = Lay.GroupWidth(6, g1, g2, g3); float l = GroupLeft(gw);
            Lay.SpriteTL(p, "ficon_galleon_ok", l, 220, o); Lay.SpriteTL(p, "ficon_sloop_ok", l + g1 + 6, 221, o); Lay.SpriteTL(p, "ficon_raft_ok", l + g1 + g2 + 12, 223, o);
        }

        static void ArtCrit(Transform p, int o)
        {
            var cell = Grid(p, 120, 132, o, Pal.Sea2);
            var w = cell(2, 1);
            Gfx.Rect(p, w.x, w.y, 12, 12, Pal.Gold2, o + 2); Gfx.Rect(p, w.x + 1, w.y + 1, 10, 10, Pal.Gold3, o + 3);
            Lay.SpriteTL(p, "xmark", w.x + 1, w.y + 1, o + 4);
            var m = cell(4, 3); Gfx.Rect(p, m.x, m.y, 12, 12, Pal.Gold1, o + 2);
            foreach (var bb in new[] { new Vector2(3, 3), new Vector2(7, 5), new Vector2(4, 8) }) Gfx.Rect(p, m.x + bb.x, m.y + bb.y, 2, 2, Pal.Gold4, o + 3);
            Caption(p, "CRITICAL HIT!", 120, 206, o, Pal.Red1);
            Caption(p, "GOLD = ORE'S WINNING SQUARE", 120, 220, o);
        }

        static void ArtBonus(Transform p, int o)
        {
            int sw = Lay.Width("shot_loaded"), bw = Lay.Width("shot_bonus"), gw = Lay.GroupWidth(4, sw, sw, sw, bw); float l = GroupLeft(gw);
            for (int i = 0; i < 3; i++) Lay.SpriteTL(p, "shot_loaded", l + i * (sw + 4), 161, o);
            Lay.SpriteTL(p, "shot_bonus", l + 3 * (sw + 4), 160, o + 1);
            Caption(p, "+1", l + 3 * (sw + 4) + bw / 2f, 175, o, Pal.Red1);
            Caption(p, "AFTER A SINK OR A CRIT", 120, 206, o);
            Caption(p, "SHOTS = SHIPS AFLOAT (+1 BONUS)", 120, 220, o, Pal.Red1);
        }

        static void ArtKraken(Transform p, int o)
        {
            Avatars.Draw(p, "a:kraken", 120 - 26, 140, o, 2);
            Caption(p, "THE KRAKEN RISES!", 120, 206, o, Pal.Red1);
            Caption(p, "MOTHERLODE: ABOUT 1 IN 500", 120, 220, o);
        }

        static void ArtOre(Transform p, int o)
        {
            var cell = Grid(p, 120, 132, o, Pal.Map1);
            var w = cell(3, 3); Gfx.Rect(p, w.x, w.y, 12, 12, Pal.Gold3, o + 2);
            Lay.SpriteTL(p, "nug_big", w.x + 1, w.y + 2, o + 3);
            Caption(p, "25 SQUARES - ONE WINS", 120, 206, o);
            Caption(p, "YOUR SHOTS DON'T MINE OR SPEND", 120, 220, o, Pal.Red1);
        }

        static void ArtNuggy(Transform p, int o)
        {
            // avatar portrait (left) + dancing action shot (right) with ORE nuggets, one centred group
            Frame(p, 48, 134, 56, 56, o, Pal.Sand2);
            Avatars.Draw(p, "a:nuggy", 50, 136, o + 2, 2);
            Caption(p, "CAPTAIN CAT", 76, 196, o, Pal.Red1);
            var nug = NuggyView.Create(p, "s085", 168, 164, o + 3); nug.Play(NuggyView.Pose.Dance, 99999f);
            Lay.SpriteTL(p, "nug_big", 138, 180, o + 2); Lay.SpriteTL(p, "nug_big", 190, 176, o + 2); Lay.SpriteTL(p, "nug_big", 164, 192, o + 2);
            Caption(p, "HIT! HIT! HIT!", 166, 206, o, Pal.Red1);
            Caption(p, "SHIP'S CAT - ORE SNIFFER", 120, 222, o);
        }

        static void NuggyExtra(Transform p, int o)
        {
            PixelText.Create(p, "$NUG ON SOLANA", 120, 314, TextStyle.Of(Pal.Red1, TextAlign.Center), o, "nugTitle");
            string ca = string.IsNullOrEmpty(NUG_CA) ? "CA COMING SOON" : NUG_CA;
            if (PixelFont.Measure(ca) <= TextW) PixelText.Create(p, ca, 120, 328, TextStyle.Of(Pal.MapB, TextAlign.Center), o, "ca");
            else { int h = ca.Length / 2; PixelText.Create(p, ca.Substring(0, h), 120, 326, TextStyle.Of(Pal.MapB, TextAlign.Center), o, "ca1"); PixelText.Create(p, ca.Substring(h), 120, 336, TextStyle.Of(Pal.MapB, TextAlign.Center), o, "ca2"); }
            if (!string.IsNullOrEmpty(NUG_DEX_URL) && !string.IsNullOrEmpty(NUG_CA))
                Ui.Small(p, 40, 350, 160, 26, "BUY ON DEXSCREENER", () => Application.OpenURL(NUG_DEX_URL), o, curLayer, "green");
        }

        static void ArtNoBet(Transform p, int o)
        {
            Lay.SpriteCentered(p, "icon_vault", 120, 142, o, 2);
            Gfx.Rect(p, 96, 162, 48, 3, Pal.Red2, o + 2);
            Caption(p, "FREE TO PLAY - NO SOL", 120, 206, o, Pal.Red1);
            Caption(p, "LIVE_ORE IS OFF", 120, 220, o);
        }
    }
}
