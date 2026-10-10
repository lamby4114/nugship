using System.Collections;
using System.Collections.Generic;
using OreSeas.Game;
using OreSeas.Ore;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>
    /// 02/03 BATTLE + REVEAL. Each volley is one (simulated) ORE round:
    /// aim -> FIRE locks both volleys -> ORE round settles -> beat 1: pan to YOUR WATERS (their volley lands)
    /// -> beat 2: pan to ENEMY WATERS (your volley lands, ORE square revealed, crits / Kraken) -> report -> reload.
    /// </summary>
    public sealed class BattleScreen : ScreenBase
    {
        enum Phase { Intro, Aim, Locked, Reveal, Between, Over }
        Phase phase = Phase.Intro;

        MatchSetup setup; MatchState match; IOreRoundSource ore; SimulatedOreRoundSource sim; IAiCaptain ai; System.Random rng;
        bool solo; float aimTotal = 30f, aimLeft; bool aiLocked; float aiLockAt;
        readonly List<Vector2Int> aimed = new List<Vector2Int>(); List<Vector2Int> lockedYours, lockedTheirs;
        ulong lockedRound;

        // view
        Transform boardsRoot, fx, hud, deckAim, deckReveal; BoardView enemyBoard, ownBoard; OceanRenderer ocean; NuggyView nuggy; Shaker shaker;
        float pan; // 0 = enemy waters, 1 = your waters
        PixelText timerText, roundText, hintText, shotsCount, bonusLabel, stakeA, stakeB, autoA, autoB; SpriteRenderer roundBg, roundBgIn;
        SpriteRenderer[] shotPips = new SpriteRenderer[5]; SpriteRenderer lever; SpriteAnim wheel; PixelCanvas ring; float ringFrac = -1;
        SpriteRenderer[] youIcons = new SpriteRenderer[3], themIcons = new SpriteRenderer[3];   // ships-left, indexed by ShipKind
        static readonly string[] KindKey = { "galleon", "sloop", "raft" }; static readonly int[] IconLen = { 15, 11, 7 };
        Ui.Btn fireBtn, reloadBtn; Transform reportRows; PixelText reportTitle;
        readonly HashSet<int> shownEnemyShips = new HashSet<int>();

        static readonly Vector2 GunPos = new Vector2(166, 330);

        public override void Build()
        {
            setup = GameApp.Setup;
            if (setup == null || setup.fleet == null)
            {
                var f = Fleet.Random(new System.Random()); var salt = FleetCommit.NewSalt();
                setup = new MatchSetup { rival = Captains.Campaign[0], biome = Biomes.Get(BiomeId.Sunset), fleet = f, salt = salt, commit = FleetCommit.Commit(f, salt), seed = 1 };
            }
            if (setup.rival == null) setup.rival = Captains.Campaign[0];
            if (setup.biome == null) setup.biome = Biomes.Get(setup.rival.biome);
            solo = setup.mode != "ranked";
            rng = new System.Random(setup.seed == 0 ? System.Environment.TickCount : setup.seed);

            var theirFleet = Fleet.Random(rng); var theirSalt = new byte[16]; rng.NextBytes(theirSalt);
            var you = new Side("YOU", setup.fleet, setup.salt, setup.commit);
            var them = new Side(setup.rival.name, theirFleet, theirSalt, FleetCommit.Commit(theirFleet, theirSalt));
            match = new MatchState(you, them, Settings.Stake, setup.rival.bonusShots);
            ai = AiFactory.Create(setup.rival.ai);

            int odds = Settings.DemoLuck ? 25 : OreConstants.MotherlodeOdds;
            sim = solo ? new SimulatedOreRoundSource(true, OreConstants.SlotSeconds, odds)
                       : new SimulatedOreRoundSource(false, Settings.RealClock ? OreConstants.SlotSeconds : 0.1f, odds);
            ore = sim;
            if (!solo && Settings.LiveRounds) { var live = LiveOreRoundSource.Shared; live.Tick(0); if (live.HasData) ore = live; else GameApp.Toast("LIVE ORE NOT READY - SIM CLOCK"); }
            ore.RoundSettled += OnRoundSettled;
            ore.RoundOpened += OnRoundOpened;

            BuildScene();
            if (ore == sim) sim.Start();
            GameApp.Audio.Music(setup.biome.music, "music_battle_tension");
            StartCoroutine(Intro());
        }

        void OnDestroy()
        {
            if (ore != null) { ore.RoundSettled -= OnRoundSettled; ore.RoundOpened -= OnRoundOpened; }
            GameApp.Audio?.SetTension(0);
        }

        // ================= scene =================
        void BuildScene()
        {
            var r = Root;
            ocean = Scenery.Build(r, setup.biome, 352);
            var world = Gfx.Node(r, "World"); shaker = world.gameObject.AddComponent<Shaker>();
            boardsRoot = Gfx.Node(world, "Boards");
            enemyBoard = new BoardView(boardsRoot, setup.biome, "ENEMY WATERS", true);
            ownBoard = new BoardView(boardsRoot, setup.biome, "YOUR WATERS", false); ownBoard.OffsetX = -Px.W;
            for (int i = 0; i < match.you.fleet.ships.Count; i++) { var s = match.you.fleet.ships[i]; ownBoard.AddShip(s, "p", s.kind == ShipKind.Galleon); }
            fx = Gfx.Node(world, "Fx");
            Scenery.FgGalleon(r, 100, 100);
            nuggy = NuggyView.Create(r, "s085", 34, 352, 105);   // reactive mascot, no label (see NuggyView)
            Scenery.Weather(r, setup.biome, 350, 128);
            BuildHud(r);
            BuildDeck(r);
            hintText = Ui.Hint(r, "", 494, 175);
            SetPan(0);
        }

        // Scoreboard: YOU (your captain portrait + your ships afloat) | wheel + timer + ORE round | OPPONENT (portrait + name + ships).
        // Nuggy is not up here: he is the reactive mascot bottom-left, next to the SHOTS counter.
        void BuildHud(Transform r)
        {
            hud = Gfx.Node(r, "HUD"); const int y = 18; var cap = setup.rival;
            Avatars.Draw(hud, Services.Profile.Avatar, 4, y, 131);   // your captain portrait (profile avatar)
            PixelText.Create(hud, Services.Profile.IsSet ? Lay.Fit(HudName(Services.Profile.Name), 66) : "YOU", 34, y + 1, TextStyle.Of(Pal.GoldTxt).Outline(Pal.Ink), 134);
            Gfx.Sprite(hud, "cap_" + cap.id + "_f", 210, y, 131);
            PixelText.Create(hud, HudName(cap.name), 206, y + 1, TextStyle.Of(Pal.GoldTxt, TextAlign.Right).Outline(Pal.Ink), 134);
            int lx = 34, rx = 206;
            for (int k = 0; k < 3; k++)
            {
                youIcons[k] = Gfx.Sprite(hud, "ficon_" + KindKey[k] + "_ok", lx, y + 23, 132); lx += IconLen[k] + 3;
                rx -= IconLen[k]; themIcons[k] = Gfx.Sprite(hud, "ficon_" + KindKey[k] + "_ok_f", rx, y + 23, 132); rx -= 3;
            }
            wheel = SpriteAnim.Play(hud, "wheel", 120, 36, 131, 4, true, false);
            ring = new PixelCanvas(32, 32, "ring") { OriginX = 104, OriginY = 20 };
            var ringSr = Gfx.Sprite(hud, ring.Sprite, 104, 20, 133, "ring");
            Gfx.Sprite(hud, "hud_timer", 92, 51, 134);
            timerText = PixelText.Create(hud, "0:30", 120, 55, TextStyle.Of(Pal.GoldTxt, TextAlign.Center, 2), 136);
            roundBg = Gfx.Rect(hud, 0, 76, 10, 11, Pal.Ink, 134); roundBgIn = Gfx.Rect(hud, 0, 77, 8, 9, Pal.Plum, 135);
            roundText = PixelText.Create(hud, "", 120, 77, TextStyle.Of(Pal.Sand1, TextAlign.Center), 136);
            PauseButton(hud);
            UpdateHud();
        }

        /// <summary>26x20 plank with a pause glyph (two 3x8 bars, 2 px apart: 8 px wide -> 9 px margins each side).</summary>
        void PauseButton(Transform p)
        {
            var b = Ui.Plank(p, 210, 50, 26, 20, "", null, Back, 140);
            b.button.sfx = "tap";
            Gfx.Rect(b.button.visual, 219, 56, 3, 8, Pal.Sand1, 144, "bar"); Gfx.Rect(b.button.visual, 224, 56, 3, 8, Pal.Sand1, 144, "bar");
        }

        /// <summary>Opponent name must fit between the wheel and the portrait (~66 px): "ADMIRAL BRINE" -> "BRINE", "GHOST CAPTAIN" -> "GHOST".</summary>
        static string HudName(string n)
        {
            if (PixelFont.Measure(n) <= 66) return n;
            var w = n.Split(' '); if (w.Length < 2) return n;
            return w[0] == "ADMIRAL" || w[0] == "CAPTAIN" || w[0] == "MAD" ? w[w.Length - 1] : w[0];
        }

        void SetRoundLabel(string s)
        {
            roundText.Set(s); int w = PixelFont.CenteredWidth(s, 120) + 8;
            Gfx.SetRect(roundBg, 120 - w / 2f, 76, w, 11); Gfx.SetRect(roundBgIn, 121 - w / 2f, 77, w - 2, 9);
        }

        void DrawRing(float frac)
        {
            frac = Mathf.Clamp01(frac);
            if (Mathf.Abs(frac - ringFrac) < 0.01f) return; ringFrac = frac;
            ring.Clear(Pal.Clear); const float cx = 120, cy = 36, rr = 13;
            int n = JsMath.RoundI(2 * Mathf.PI * rr * 1.5f);
            for (int i = 0; i < n; i++)
            {
                float a = -Mathf.PI / 2 + i / (float)n * Mathf.PI * 2; if (i / (float)n > frac) break;
                ring.Px(cx + Mathf.Cos(a) * (rr - 0.5f), cy + Mathf.Sin(a) * (rr - 0.5f), Pal.Gold3);
                ring.Px(cx + Mathf.Cos(a) * (rr + 0.5f), cy + Mathf.Sin(a) * (rr + 0.5f), Pal.Gold2);
            }
            ring.Apply();
        }

        void UpdateHud()
        {
            // yours: ok / hit (damaged) / sunk. Theirs: only sunk is known (hits on hidden ships stay hidden).
            for (int i = 0; i < match.you.fleet.ships.Count; i++)
            {
                int k = (int)match.you.fleet.ships[i].kind; var w = match.you.waters;
                string st = w.IsSunk(i) ? "sunk" : w.HitsOn(i) > 0 ? "hit" : "ok";
                youIcons[k].sprite = Atlas.Get("ficon_" + KindKey[k] + "_" + st);
            }
            for (int i = 0; i < match.them.fleet.ships.Count; i++)
            {
                int k = (int)match.them.fleet.ships[i].kind;
                themIcons[k].sprite = Atlas.Get("ficon_" + KindKey[k] + (match.them.waters.IsSunk(i) ? "_sunk_f" : "_ok_f"));
            }
        }

        void BuildDeck(Transform r)
        {
            Gfx.Sprite(r, "deck", 0, 0, 150);
            deckAim = Gfx.Node(r, "DeckAim");
            const int y = 358;
            Gfx.Sprite(deckAim, "deck_strip", 6, y, 152);
            // SHOTS n/N: one cannonball per shot this volley (ships afloat + bonus); a ball empties as each shot is placed
            PixelText.Create(deckAim, "SHOTS", 12, y + 4, TextStyle.Of(Pal.Sand2), 153);
            shotsCount = PixelText.Create(deckAim, "3/3", 16 + PixelFont.Measure("SHOTS"), y + 4, TextStyle.Of(Pal.Gold4), 153);
            for (int i = 0; i < shotPips.Length; i++) shotPips[i] = Gfx.Sprite(deckAim, "shot_loaded", 17 + i * 13, y + 20, 153);
            bonusLabel = PixelText.Create(deckAim, "+1", 0, y + 16, TextStyle.Of(Pal.Gold3), 153);
            Gfx.Sprite(deckAim, "pouch", 104, y + 8, 153);
            bool staked = !solo && Net.ServerConfig.LIVE_ORE;   // LIVE_ORE off: the SIM rival duel is free too, no pretend SOL
            stakeA = PixelText.Create(deckAim, staked ? Settings.Stake.ToString("0.000") : "FREE", 123, y + 6, TextStyle.Of(Pal.Gold4), 153);
            stakeB = PixelText.Create(deckAim, staked ? "SOL SIM" : "NO STAKE", 123, y + 16, TextStyle.Of(Pal.Sand2), 153);
            lever = Gfx.Sprite(deckAim, Settings.AutoFire ? "lever_on" : "lever_off", 177, y + 9, 153);
            autoA = PixelText.Create(deckAim, "", 206, y + 6, TextStyle.Of(Pal.Grass), 153);
            autoB = PixelText.Create(deckAim, "", 206, y + 16, TextStyle.Of(Pal.Sand2), 153);
            UiButton.Create(deckAim, 174, y, 60, 30, ToggleAuto, 0, "autofire").sfx = "lock";
            RefreshAuto();
            fireBtn = Ui.Big(deckAim, 8, 396, 224, 72, "FIRE!", 3, "TAP ENEMY TILES TO AIM", "cannon", OnFire, 160);
            if (solo) Ui.Hint(deckAim, "PRACTICE", 478, 170, Pal.Sand3);   // one small subtle tag, practice only; no "AT RISK"/"EXAMPLE" stamps anywhere

            deckReveal = Gfx.Node(r, "DeckReveal");
            Gfx.Sprite(deckReveal, "parch_224x62", 8, 358, 152);
            foreach (var nx in new[] { 11, 227 }) { Gfx.Sprite(deckReveal, "nail", nx, 361, 153); Gfx.Sprite(deckReveal, "nail", nx, 414, 153); }
            reportTitle = PixelText.Create(deckReveal, "VOLLEY REPORT", 120, 361, TextStyle.Of(Pal.Red1, TextAlign.Center), 153);
            reportRows = Gfx.Node(deckReveal, "rows");
            reloadBtn = Ui.Big(deckReveal, 8, 428, 224, 60, "RELOAD", 2, "NEXT VOLLEY", "cannon", OnReload, 160);
            deckReveal.gameObject.SetActive(false);
        }

        void ToggleAuto() { Settings.AutoFire = !Settings.AutoFire; RefreshAuto(); }
        void RefreshAuto()
        {
            bool on = Settings.AutoFire; lever.sprite = Atlas.Get(on ? "lever_on" : "lever_off");
            autoA.Set(on ? "AUTO" : "OFF"); autoA.SetColor(on ? Pal.Grass : Pal.Sand1);
            autoB.Set(on ? "FIRE" : "1 BY 1");
        }

        void SetPan(float p)
        {
            pan = p; float off = Mathf.Round(p * Px.W);
            enemyBoard.OffsetX = off; ownBoard.OffsetX = off - Px.W;
            ocean.scrollX = -off * 0.5f;
        }

        IEnumerator PanTo(float target, float dur = 0.55f)
        {
            float from = pan, t = 0; GameApp.Audio.Sfx("whoosh", 0, 0.6f);
            while (t < 1) { t += Time.deltaTime / dur; SetPan(Mathf.Lerp(from, target, Ease.InOutCubic(t))); yield return null; }
            SetPan(target);
        }

        // ================= flow =================
        IEnumerator Intro()
        {
            phase = Phase.Intro;
            var intro = Gfx.Node(Root, "Intro");
            var cap = setup.rival;
            CampaignScreen.Dialog(intro, "cap_" + cap.id, cap.name + (setup.mode == "ranked" ? " (SIMULATED)" : ""), new[] { "\"" + cap.taunt[rng.Next(cap.taunt.Length)] + "\"", setup.biome.name + (solo ? " - FREE PRACTICE" : " - LIVE SIM ORE CLOCK") }, 300, 180);
            var rib = Ui.Ribbon(intro, "ribbon_red_wide", 120, 196, "BATTLE STATIONS!", 182);
            GameApp.Audio.Sfx("whistle");
            SetRoundLabel("ORE ROUND " + ore.Current.roundId);
            float t = 0; while (t < 2.4f && !(InputRouter.Down && t > 0.3f)) { t += Time.deltaTime; yield return null; }
            Destroy(intro.gameObject);
            BeginAim();
        }

        void OnRoundOpened(OreRoundInfo info)
        {
            if (phase == Phase.Aim) SetRoundLabel("ORE ROUND " + info.roundId);
            if (!solo && phase == Phase.Between) BeginAim(); // ranked: rounds keep rolling, like real ORE
        }

        void BeginAim()
        {
            phase = Phase.Aim; aimed.Clear(); enemyBoard.ClearTargets(); aiLocked = false; lockedTheirs = null; lockedYours = null;
            deckAim.gameObject.SetActive(true); deckReveal.gameObject.SetActive(false);
            if (pan != 0) StartCoroutine(PanTo(0));
            aimTotal = solo ? 30f : Mathf.Max(1f, ore.SecondsUntilLock(LockMargin));
            aimLeft = aimTotal;
            aiLockAt = solo ? 0 : aimTotal * (0.25f + (float)rng.NextDouble() * 0.5f);
            SetRoundLabel("ORE ROUND " + ore.Current.roundId);
            hintText.Set(match.you.bonusNext ? "BONUS SHOT! AIM " + match.ShotsYou + " CANNONS" : "AIM " + match.ShotsYou + " CANNON" + (match.ShotsYou == 1 ? "" : "S") + " AT ENEMY WATERS");
            timerText.SetColor(Pal.GoldTxt);
            RefreshSalvo();
            nuggy.Play(NuggyView.Pose.Idle, 0);
        }

        int LockMargin => Settings.AutoFire ? OreConstants.LockMarginAutoFire : OreConstants.LockMarginPerShot;

        void RefreshSalvo()
        {
            int n = match.ShotsYou; bool bonus = match.you.bonusNext && n > 1; int used = Mathf.Min(aimed.Count, n);
            int normal = bonus ? n - 1 : n;
            for (int i = 0; i < shotPips.Length; i++)
            {
                var sr = shotPips[i]; sr.enabled = i < n; if (i >= n) continue;
                bool isBonus = bonus && i == n - 1;
                float x = 17 + i * 13 + (isBonus ? 4 : 0);
                sr.transform.localPosition = Px.P(x, 378);
                sr.sprite = Atlas.Get(isBonus ? (used >= n ? "shot_bonus_spent" : "shot_bonus") : (i < used ? "shot_spent" : "shot_loaded"));
            }
            bonusLabel.SetVisible(bonus);
            if (bonus) bonusLabel.MoveTo(17 + normal * 13 + 4 + 7, 374);
            int left = n - used;
            shotsCount.Set(left + "/" + n); shotsCount.SetColor(left > 0 ? Pal.Gold4 : Pal.Sand2);
            bool can = aimed.Count > 0 && phase == Phase.Aim;
            fireBtn.SetEnabled(can);
            fireBtn.SetSub(aimed.Count == 0 ? (bonus ? "BONUS SHOT! TAP TILES" : "TAP ENEMY TILES TO AIM") : left > 0 ? left + " SHOT" + (left == 1 ? "" : "S") + " LEFT" : "ALL GUNS READY!");
        }

        void Update()
        {
            if (solo && (PauseOverlay.IsOpen || HelpOverlay.IsOpen)) return;   // local practice truly pauses (clock + aim timer)
            ore?.Tick(Time.deltaTime);
            if (phase == Phase.Aim)
            {
                aimLeft = solo ? aimLeft - Time.deltaTime : ore.SecondsUntilLock(LockMargin);
                float shown = Mathf.Max(0, aimLeft);
                bool timed = !solo || Settings.AutoFire;
                timerText.Set(timed ? Clock(shown) : "--");
                DrawRing(timed ? shown / Mathf.Max(1, aimTotal) : 1);
                if (timed && shown < 10) { timerText.SetColor(((int)(Time.time * 4) & 1) == 0 ? Pal.RedTxt : Pal.GoldTxt); }
                GameApp.Audio.SetTension(timed ? Mathf.Clamp01(1.3f - shown / Mathf.Max(1, aimTotal) * 1.6f) : 0.15f);
                if (!solo && !aiLocked && aimTotal - aimLeft >= aiLockAt) { aiLocked = true; hintText.Set(setup.rival.name + " LOCKED THEIR VOLLEY!"); GameApp.Audio.Sfx("lock", 0, 0.6f); }
                if (timed && aimLeft <= 0) { AutoFill(); Lock(); }
            }
            else if (phase == Phase.Locked && !solo)
            {
                float left = (float)(ore.Current.SlotsLeft * ore.SlotSeconds);
                timerText.Set(ore.Current.phase == OrePhase.Deploying ? Clock(left) : "ORE");
                DrawRing(ore.Current.phase == OrePhase.Deploying ? left / (OreConstants.RoundSlots * ore.SlotSeconds) : 0);
            }
        }

        static string Clock(float seconds) { int s = Mathf.CeilToInt(Mathf.Max(0, seconds)); return s / 60 + ":" + (s % 60).ToString("00"); }

        public override void Pointer()
        {
            if (phase != Phase.Aim || !InputRouter.Down || pan > 0.01f) return;
            if (!BoardGeo.TileAt(InputRouter.Pos, enemyBoard.OffsetX, out int c, out int r)) return;
            var cell = new Vector2Int(c, r);
            if (match.them.waters.IsShot(c, r)) { GameApp.Audio.Sfx("error"); Haptics.Buzz(30); GameApp.Toast("ALREADY FIRED ON " + OreConstants.SquareName(r * 5 + c)); return; }
            if (aimed.Contains(cell)) { aimed.Remove(cell); enemyBoard.SetTarget(c, r, false); GameApp.Audio.Sfx("tap"); RefreshSalvo(); return; }
            if (aimed.Count >= match.ShotsYou) { GameApp.Audio.Sfx("error"); Haptics.Buzz(30); GameApp.Toast("ALL GUNS AIMED - TAP A FLAG TO CLEAR"); return; }
            aimed.Add(cell); enemyBoard.SetTarget(c, r, true);
            GameApp.Audio.Sfx("select", 0, 1, 0.9f + aimed.Count * 0.08f); Haptics.Buzz(12);
            nuggy.React(NuggyView.Pose.Hop, 0.3f);
            RefreshSalvo();
        }

        void AutoFill()
        {
            int n = match.ShotsYou;
            if (aimed.Count >= n) return;
            if (aimed.Count > 0 && !Settings.AutoFire) return;
            var open = new List<Vector2Int>();
            for (int r = 0; r < 5; r++) for (int c = 0; c < 5; c++) if (!match.them.waters.IsShot(c, r) && !aimed.Contains(new Vector2Int(c, r))) open.Add(new Vector2Int(c, r));
            AiFactory.Shuffle(open, rng);
            for (int i = 0; aimed.Count < n && i < open.Count; i++) { aimed.Add(open[i]); enemyBoard.SetTarget(open[i].x, open[i].y, true); }
            if (aimed.Count > 0) GameApp.Toast(Settings.AutoFire ? "AUTO-FIRE: NUGGY PICKED THE REST" : "TIME! NUGGY FIRED FOR YOU");
        }

        void OnFire() { if (phase == Phase.Aim && aimed.Count > 0) Lock(); }

        void Lock()
        {
            if (phase != Phase.Aim) return;
            phase = Phase.Locked;
            lockedYours = new List<Vector2Int>(aimed);
            lockedTheirs = ai.ChooseVolley(match.you.waters.KnownGrid(), match.you.waters.RemainingShipLengths(), match.ShotsThem, rng);
            lockedRound = ore.Current.roundId;
            fireBtn.SetEnabled(false); fireBtn.SetSub("SHOTS AWAY!");
            nuggy.Play(NuggyView.Pose.Sleepy, 999f);   // dozes while the ORE round settles; woken by the reveal
            GameApp.Audio.SetTension(0.3f);
            StartCoroutine(FireAnim());
        }

        IEnumerator FireAnim()
        {
            hintText.Set("SHOTS AWAY! " + lockedYours.Count + " CANNON" + (lockedYours.Count == 1 ? "" : "S"));
            for (int i = 0; i < lockedYours.Count; i++)
            {
                float gx = 120 + i * 14;
                Fx.Muzzle(Root, gx, 336, 112);
                GameApp.Audio.Sfx("boom", 0, 0.8f, 0.95f + i * 0.05f); Haptics.Buzz(35); if (!Settings.ReduceFlashes) shaker.Shake(1, 0.06f);
                StartCoroutine(Fx.Cannonball(fx, new Vector2(gx + 6, 328), new Vector2(gx + 20 - i * 30, -20), 0.6f, 20));
                yield return new WaitForSeconds(0.16f);
            }
            if (solo)
            {
                yield return new WaitForSeconds(0.25f);
                hintText.Set("ORE ROUND SETTLING...");
                timerText.Set("ORE"); wheel.fps = 16; GameApp.Audio.Sfx("tick");
                ore.CloseRoundNow();
            }
            else
            {
                hintText.Set("LOCKED IN - WAITING FOR THE ORE ROUND");
                wheel.fps = 10;
            }
        }

        void OnRoundSettled(OreRoundResult res)
        {
            if (phase != Phase.Locked || res.roundId != lockedRound) return;
            StartCoroutine(Reveal(res));
        }

        // ================= reveal =================
        IEnumerator Reveal(OreRoundResult res)
        {
            phase = Phase.Reveal; wheel.fps = 4; DrawRing(1);
            nuggy.Play(NuggyView.Pose.Idle, 0);
            var v = match.Resolve(lockedYours, lockedTheirs, res);
            enemyBoard.ClearTargets();
            string sq = OreConstants.SquareName(res.winningSquare);
            SetRoundLabel("ROUND " + res.roundId + " SETTLED - ORE " + sq);
            GameApp.Audio.Sfx("reveal");
            int oc = res.winningSquare % 5, orr = res.winningSquare / 5;

            // beat 1: your waters
            yield return PanTo(1);
            ownBoard.ShowOre(oc, orr);
            hintText.Set(setup.rival.name + "'S VOLLEY!");
            timerText.Set("!!"); timerText.SetColor(Pal.RedTxt);
            yield return new WaitForSeconds(0.25f);
            foreach (var s in v.theirs)
            {
                var target = ownBoard.Center(s.c, s.r);
                GameApp.Audio.Sfx("whistle", 0, 0.5f);
                yield return Fx.Cannonball(fx, new Vector2(target.x + 30, 70), target, 0.45f, 10, 110);
                if (s.hit)
                {
                    Fx.Boom(fx, target.x, target.y - 2, 97); GameApp.Audio.Sfx("explode"); Haptics.Buzz(80); HitFeedback(false);
                    nuggy.React(NuggyView.Pose.Wince, s.sunk ? 1.4f : 0.9f);   // your ship got hit
                    ownBoard.MarkFire(s.c, s.r, false);
                    Fx.Burst(fx, target.x, target.y, 14, new[] { Pal.Wood1, Pal.Red3, Pal.Gold3 }, 120);
                    if (s.sunk)
                    {
                        var sv = ownBoard.ships[s.shipIndex]; sv.Set(match.you.fleet.ships[s.shipIndex], true);
                        GameApp.Audio.Sfx("sunk", 0.2f); hintText.Set("THEY SANK YOUR " + ShipSpec.Name(s.kind) + "!");
                        yield return new WaitForSeconds(0.5f);
                    }
                    else timerText.Set("HIT!");
                }
                else
                {
                    Fx.Splash(fx, target.x, target.y + 7, 96); GameApp.Audio.Sfx("splash"); ownBoard.MarkMiss(s.c, s.r);
                    if (s.oreSquare) Fx.Bubbles(fx, target.x, target.y + 4);
                    timerText.Set("MISS");
                }
                UpdateHud();
                yield return new WaitForSeconds(0.3f);
            }
            if (v.theirs.Count == 0) yield return new WaitForSeconds(0.3f);
            yield return new WaitForSeconds(0.35f);

            // beat 2: enemy waters
            yield return PanTo(0);
            enemyBoard.ShowOre(oc, orr);
            hintText.Set("YOUR VOLLEY!");
            var report = new List<string[]>();
            foreach (var s in v.yours)
            {
                var target = enemyBoard.Center(s.c, s.r);
                GameApp.Audio.Sfx("whistle", 0, 0.5f, 1.1f);
                yield return Fx.Cannonball(fx, GunPos, target, 0.55f, 34, 110);
                if (s.hit)
                {
                    Fx.Boom(fx, target.x, target.y - 2, 97, s.oreSquare); GameApp.Audio.Sfx("explode"); Haptics.Buzz(60); HitFeedback(s.oreSquare);
                    enemyBoard.MarkHitHidden(s.c, s.r);
                    Fx.Burst(fx, target.x, target.y, 18, new[] { Pal.Wood1, Pal.Gold3, Pal.Red3 }, 120);
                    nuggy.Play(NuggyView.Pose.Hop, 0.35f); nuggy.Play(NuggyView.Pose.Happy, 1.2f); timerText.Set("HIT!"); timerText.SetColor(Pal.RedTxt);
                    if (s.oreSquare) yield return Crit(s);
                    if (s.sunk)
                    {
                        RevealEnemyShip(s.shipIndex, true);
                        GameApp.Audio.Sfx("sunk", 0.1f); hintText.Set("YOU SANK THEIR " + ShipSpec.Name(s.kind) + "!");
                        Fx.FloatText(fx, "SUNK!", target.x, target.y - 22, Pal.RedTxt, 210, 2);
                        yield return new WaitForSeconds(0.6f);
                    }
                }
                else
                {
                    Fx.Splash(fx, target.x, target.y + 7, 96); GameApp.Audio.Sfx("splash"); enemyBoard.MarkMiss(s.c, s.r);
                    timerText.Set("MISS"); timerText.SetColor(Pal.WhiteTxt);
                    if (!s.oreSquare) nuggy.React(NuggyView.Pose.Wince, 0.8f);
                    if (s.oreSquare)
                    {
                        Fx.Bubbles(fx, target.x, target.y + 4); GameApp.Audio.Sfx("chime");
                        Fx.FloatText(fx, "+" + s.ore.ToString("0.0000") + " ORE", target.x, target.y - 14, Pal.GoldTxt);
                        nuggy.Play(NuggyView.Pose.Happy, 1f);
                    }
                }
                report.Add(ReportRow(s));
                UpdateHud();
                yield return new WaitForSeconds(0.3f);
            }
            ShowReport(v, report);

            if (res.motherlode) yield return KrakenOverlay.Run(this, match, v);

            if (v.outcome != 0) { yield return new WaitForSeconds(0.6f); EndMatch(v.outcome); yield break; }

            phase = Phase.Between;
            timerText.SetColor(Pal.GoldTxt); timerText.Set("--");
            if (solo)
            {
                reloadBtn.SetSub(v.yourBonus ? "BONUS SHOT EARNED!" : "ROUND " + (match.round + 1) + " - " + match.ShotsYou + " CANNONS");
                hintText.Set(setup.rival.name + " IS RELOADING...");
            }
            else
            {
                reloadBtn.SetSub("AIM NOW - ROUND " + ore.Current.roundId);
                hintText.Set(setup.rival.name + " IS AIMING...");
                if (ore.Current.phase == OrePhase.Deploying && ore.SecondsUntilLock(LockMargin) < 4f) { reloadBtn.SetEnabled(false); reloadBtn.SetSub("WAITING FOR NEXT ORE ROUND"); }
            }
        }

        string[] ReportRow(ShotResult s)
        {
            string desc = MatchState.Describe(s);
            string val;
            if (solo || !Net.ServerConfig.LIVE_ORE) val = s.oreSquare ? "+" + s.ore.ToString("0.0000") + " ORE" : s.hit ? "BOOM" : "SPLASH";
            else val = s.oreSquare ? "+" + s.ore.ToString("0.0000") + " ORE" : "-" + (match.stake - s.solBack).ToString("0.0000") + " SOL";
            return new[] { s.Name, desc, val, s.oreSquare || s.hit ? "g" : "r" };
        }

        void ShowReport(VolleyResult v, List<string[]> rows)
        {
            deckAim.gameObject.SetActive(false); deckReveal.gameObject.SetActive(true);
            foreach (Transform c in reportRows) Destroy(c.gameObject);
            reportTitle.Set(solo ? "VOLLEY REPORT" : "VOLLEY REPORT (SIM)");
            int shown = Mathf.Min(3, rows.Count);
            for (int i = 0; i < shown; i++)
            {
                float y = 373 + i * 11; var r = rows[i];
                PixelText.Create(reportRows, r[0], 14, y, TextStyle.Of(Pal.MapD), 153);
                PixelText.Create(reportRows, r[1], 34, y, TextStyle.Of(Pal.MapD), 153);
                PixelText.Create(reportRows, r[2], 226, y, TextStyle.Of(r[3] == "g" ? Pal.Grass4 : Pal.Red1, TextAlign.Right), 153);
            }
            if (rows.Count > 3) PixelText.Create(reportRows, "+" + (rows.Count - 3) + " MORE", 226, 406, TextStyle.Of(Pal.MapB, TextAlign.Right), 153);
            if (rows.Count == 0) PixelText.Create(reportRows, "NO SHOTS FIRED", 120, 384, TextStyle.Of(Pal.MapD, TextAlign.Center), 153);
            reloadBtn.SetEnabled(true);
        }

        void RevealEnemyShip(int idx, bool sunk)
        {
            if (shownEnemyShips.Contains(idx)) return;
            shownEnemyShips.Add(idx);
            var p = match.them.fleet.ships[idx];
            var sv = enemyBoard.AddShip(p, "e", false); sv.Set(p, sunk);
            if (!sunk) sv.alpha = 0.75f;
        }

        /// <summary>Small, short shake + warm vignette pulse (no white flash). Reduce flashes: no shake on normal hits.</summary>
        void HitFeedback(bool crit)
        {
            if (Settings.ReduceFlashes) { if (crit) shaker.Shake(1, 0.15f); }
            else shaker.Shake(crit ? 2 : 1, crit ? 0.2f : 0.15f);
            if (!crit) ScreenPulse.Hit(Root, 185);
        }

        IEnumerator Crit(ShotResult s)
        {
            var n = Gfx.Node(Root, "Crit"); const float cy = 246;
            ScreenPulse.Crit(Root, 190);
            GameApp.Audio.Sfx("gold"); GameApp.Audio.Sfx("coins", 0.25f); Haptics.Buzz(120);
            var rays = SpriteAnim.Play(n, "rays", 120, cy, 200, Settings.ReduceFlashes ? 3 : 6, true, false);
            if (Settings.ReduceFlashes) rays.Renderer.color = new Color(1, 1, 1, 0.45f);
            Ui.Ribbon(n, "ribbon_gold", 120, cy - 12, "", 202);
            PixelText.Create(n, "CRITICAL HIT!", 120, cy - 8, TextStyle.Of(Pal.RedTxt, TextAlign.Center, 2).Outline(Pal.Ink), 204);
            const int pw = 112;
            Gfx.Rect(n, 120 - pw / 2f, cy + 12, pw, 16, Pal.Ink, 203); Gfx.Rect(n, 121 - pw / 2f, cy + 13, pw - 2, 14, Pal.Plum, 204);
            {
                // ore badge (6 px) + 4 px + amount as one group centred on x=120 inside the pill
                string oreTxt = "+" + s.ore.ToString("0.0000") + " ORE (SIM)"; int gw = 6 + 4 + PixelFont.Measure(oreTxt); float gl = Mathf.Floor(120 - gw / 2f);
                if (PixelFont.LegacyCenter) { Gfx.Sprite(n, "ore_badge", 120 - pw / 2f + 6, cy + 17, 205); PixelText.Create(n, oreTxt, 128, cy + 16, TextStyle.Of(Pal.GoldTxt, TextAlign.Center).Shadow(Pal.Ink), 205); }
                else { Lay.SpriteTL(n, "ore_badge", gl, cy + 17, 205); PixelText.Create(n, oreTxt, gl + 10, cy + 16, TextStyle.Of(Pal.GoldTxt).Shadow(Pal.Ink), 205); }
            }
            Fx.Nuggets(n, 120, cy, 26);
            var burst = SpriteAnim.Play(n, "nugburst", 44, 322, 103, 10, true, false);
            nuggy.Play(NuggyView.Pose.Dance, 2.2f);
            float t = 0; while (t < 1.9f && !(InputRouter.Down && t > 0.6f)) { t += Time.deltaTime; yield return null; }
            Destroy(n.gameObject);
        }

        void OnReload()
        {
            if (phase != Phase.Between) return;
            if (!solo && (ore.Current.phase != OrePhase.Deploying || ore.SecondsUntilLock(LockMargin) < 4f)) { GameApp.Toast("WAITING FOR NEXT ORE ROUND"); return; }
            BeginAim();
        }

        // ================= end =================
        void EndMatch(int outcome)
        {
            phase = Phase.Over;
            GameApp.Audio.SetTension(0);
            for (int i = 0; i < match.them.fleet.ships.Count; i++) RevealEnemyShip(i, match.them.waters.IsSunk(i));
            bool youOk = FleetCommit.Verify(match.you.fleet, match.you.salt, match.you.commit);
            bool themOk = FleetCommit.Verify(match.them.fleet, match.them.salt, match.them.commit);
            var sum = new MatchSummary
            {
                mode = setup.mode, opponent = setup.rival.name, result = outcome == 1 ? 1 : outcome == -1 ? -1 : 0, rounds = match.round,
                volleys = match.you.volleys, shotsFired = match.you.shots, hits = match.you.hits, shipsSunk = match.you.sunk, crits = match.you.crits,
                krakens = match.you.krakens, oreSim = match.you.ore, simulated = true, yourCommit = match.you.commit, theirCommit = match.them.commit, commitsVerified = youOk && themOk
            };
            GameApp.Leaderboard.Record(sum);
            bool unlocked = false;
            if (outcome == 1 && setup.mode == "solo" && setup.campaignIndex >= 0)
            {
                var st = GameApp.Leaderboard.Mine;
                if (setup.campaignIndex == st.campaignBeaten && st.campaignBeaten < Captains.Campaign.Length) { st.campaignBeaten++; st.Save(); unlocked = true; }
            }
            ClearDeck();
            ResultsOverlay.Open(this, match, sum, unlocked, youOk, themOk, setup);
        }

        public override void Back()
        {
            if (phase == Phase.Over) return;
            PauseOverlay.Toggle("ABANDON", () => { if (setup.campaignIndex >= 0) GameApp.Go<CampaignScreen>(); else GameApp.Go<TitleScreen>(); }, !solo);
        }

        /// <summary>Desktop screenshot player only: end the match now to show the results overlay.</summary>
        /// <summary>Match over: the FIRE/RELOAD deck and the live hint would peek out under the results parchment (half a label) - clear them.</summary>
        void ClearDeck() { deckAim.gameObject.SetActive(false); deckReveal.gameObject.SetActive(false); hintText.Set(""); }

        public void DebugEnd(int outcome) { if (phase != Phase.Over) EndMatch(outcome); }

        public Transform FxRoot => fx;
        public NuggyView Nuggy => nuggy;
    }

}
