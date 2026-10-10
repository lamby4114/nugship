using System.Collections;
using System.Collections.Generic;
using OreSeas.Game;
using OreSeas.Net;
using OreSeas.Ore;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>
    /// Server-authoritative battle (PvP quick match / invite code, or solo vs a server AI captain).
    /// Same look as BattleScreen, but every rule lives on the server (PROTOCOL.md): turn_start -> aim (repeat) -> lock at
    /// the same slot for both captains -> round_closed -> reveal (incoming beat, then outgoing beat) -> ... -> match_end,
    /// where we verify the opponent's revealed layout+salt against the commit received in fleets_committed.
    /// The enemy fleet is never on this device until match_end. Nothing is signed or sent on-chain (ServerConfig.LIVE_ORE).
    /// </summary>
    public sealed class OnlineBattleScreen : ScreenBase
    {
        enum Phase { Waiting, Aim, Sent, Locked, Reveal, Over }
        Phase phase = Phase.Waiting;

        MatchSetup setup; OnlineSession session; ServerClient net; Fleet myFleet;
        // turn + clock (slots, re-anchored on turn_start / clock)
        ulong roundId; double anchorSlot, lockSlot, endSlot; float anchorAt, slotMs = 400f, aimTotal = 30f; int maxShots = 3, bonusShots;
        readonly HashSet<int> firedOnThem = new HashSet<int>(); readonly HashSet<int> hitsOnThem = new HashSet<int>();
        readonly List<int> aimed = new List<int>(); bool aimDirty; float lastAimSent; bool autoFilled;
        readonly bool[] mySunk = new bool[3]; readonly int[] myHits = new int[3]; readonly bool[] theirSunk = new bool[3];
        int ratingDelta, newRating; bool haveRating; int volleys, shots, hits, sunk, crits, krakens; double ore;

        // view
        Transform fx, hud, deckAim, deckReveal; BoardView enemyBoard, ownBoard; OceanRenderer ocean; NuggyView nuggy; Shaker shaker;
        float pan;
        PixelText timerText, roundText, hintText, shotsCount, bonusLabel, autoA, autoB; SpriteRenderer roundBg, roundBgIn;
        SpriteRenderer[] shotPips = new SpriteRenderer[5]; SpriteRenderer lever; SpriteAnim wheel; PixelCanvas ring; float ringFrac = -1;
        SpriteRenderer[] youIcons = new SpriteRenderer[3], themIcons = new SpriteRenderer[3];
        static readonly string[] KindKey = { "galleon", "sloop", "raft" }; static readonly int[] IconLen = { 15, 11, 7 };
        Ui.Btn fireBtn, nextBtn; Transform reportRows; PixelText reportTitle;
        static readonly Vector2 GunPos = new Vector2(166, 330);

        bool Practice => session != null && session.IsPractice;

        public override void Build()
        {
            setup = GameApp.Setup; net = GameApp.Net; session = setup?.online;
            if (session == null || setup.fleet == null) { GameApp.Toast("NO ONLINE MATCH"); GameApp.Go<TitleScreen>(); return; }
            if (setup.rival == null) setup.rival = Captains.Rival(session.opponentName.ToUpperInvariant());
            if (setup.biome == null) setup.biome = Biomes.Get(setup.rival.biome);
            myFleet = setup.fleet;
            BuildScene();
            SetRoundLabel(Practice ? "PRACTICE VS SERVER AI" : "WAITING FOR ORE ROUND");
            hintText.Set(session.fleetsCommitted ? "BOTH FLEETS SEALED" : RivalShort + " IS SEALING THEIR FLEET");
            deckAim.gameObject.SetActive(true); fireBtn.SetEnabled(false); fireBtn.SetSub("WAITING FOR NEXT ROUND");
            GameApp.Audio.Music(setup.biome.music, "music_battle_tension");
            StartCoroutine(Pump());
            StartCoroutine(Intro());
        }

        /// <summary>First seconds of every match: who you're fighting + BATTLE STATIONS (tap to skip). Same beat as the local battle.</summary>
        IEnumerator Intro()
        {
            var intro = Gfx.Node(Root, "Intro"); var cap = setup.rival; bool pvp = session.IsPvp;
            string taunt = cap.taunt != null && cap.taunt.Length > 0 ? "\"" + cap.taunt[Random.Range(0, cap.taunt.Length)] + "\"" : "";
            string sub = Practice ? setup.biome.name + " - FREE PRACTICE" : pvp ? "RATING " + session.opponentRating + " - SEALED FLEETS" : setup.biome.name;
            bool avatar = pvp && Avatars.IsValid(session.opponentAvatar);
            CampaignScreen.Dialog(intro, avatar ? "" : "cap_" + cap.id, Lay.Fit(pvp ? session.opponentName.ToUpperInvariant() : cap.name, 180), new[] { taunt, sub }, 300, 180);
            if (avatar) Avatars.Draw(intro, session.opponentAvatar, 12, 307, 182);
            Ui.Ribbon(intro, "ribbon_red_wide", 120, 196, "BATTLE STATIONS!", 182);
            GameApp.Audio.Sfx("whistle");
            float t = 0; while (t < 2.4f && !(InputRouter.Down && t > 0.3f) && phase != Phase.Reveal && phase != Phase.Over) { t += Time.deltaTime; yield return null; }
            Destroy(intro.gameObject);
        }

        void OnDestroy() { GameApp.Audio?.SetTension(0); }

        string RivalShort => HudName(setup.rival.name);

        // ================= scene (mirrors BattleScreen) =================
        void BuildScene()
        {
            var r = Root;
            ocean = Scenery.Build(r, setup.biome, 352);
            var world = Gfx.Node(r, "World"); shaker = world.gameObject.AddComponent<Shaker>();
            var boardsRoot = Gfx.Node(world, "Boards");
            enemyBoard = new BoardView(boardsRoot, setup.biome, "", true);
            ownBoard = new BoardView(boardsRoot, setup.biome, "", false); ownBoard.OffsetX = -Px.W;
            RebuildOwnShips();
            fx = Gfx.Node(world, "Fx");
            Scenery.FgGalleon(r, 100, 100);
            nuggy = NuggyView.Create(r, "s085", 34, 352, 105);
            Scenery.Weather(r, setup.biome, 350, 128);
            BuildHud(r);
            BuildDeck(r);
            hintText = Ui.Hint(r, "", 494, 175);
            SetPan(0);
        }

        void RebuildOwnShips()
        {
            ownBoard.ClearShips();
            for (int i = 0; i < myFleet.ships.Count; i++) { var s = myFleet.ships[i]; ownBoard.AddShip(s, "p", s.kind == ShipKind.Galleon); }
        }

        void BuildHud(Transform r)
        {
            hud = Gfx.Node(r, "HUD"); const int y = 18; var cap = setup.rival;
            Avatars.Draw(hud, Services.Profile.Avatar, 4, y, 131);   // your captain portrait (profile avatar)
            PixelText.Create(hud, Services.Profile.IsSet ? Lay.Fit(HudName(Services.Profile.Name), 66) : "YOU", 34, y + 1, TextStyle.Of(Pal.GoldTxt).Outline(Pal.Ink), 134);
            if (session != null && !session.opponentIsAI && Avatars.IsValid(session.opponentAvatar)) Avatars.Draw(hud, session.opponentAvatar, 210, y, 131);
            else Gfx.Sprite(hud, "cap_" + cap.id + "_f", 210, y, 131);
            PixelText.Create(hud, HudName(cap.name), 206, y + 1, TextStyle.Of(Pal.GoldTxt, TextAlign.Right).Outline(Pal.Ink), 134);
            int lx = 34, rx = 206;
            for (int k = 0; k < 3; k++)
            {
                youIcons[k] = Gfx.Sprite(hud, "ficon_" + KindKey[k] + "_ok", lx, y + 23, 132); lx += IconLen[k] + 3;
                rx -= IconLen[k]; themIcons[k] = Gfx.Sprite(hud, "ficon_" + KindKey[k] + "_ok_f", rx, y + 23, 132); rx -= 3;
            }
            wheel = SpriteAnim.Play(hud, "wheel", 120, 36, 131, 4, true, false);
            ring = new PixelCanvas(32, 32, "ring") { OriginX = 104, OriginY = 20 };
            Gfx.Sprite(hud, ring.Sprite, 104, 20, 133, "ring");
            Gfx.Sprite(hud, "hud_timer", 92, 51, 134);
            timerText = PixelText.Create(hud, "--", 120, 55, TextStyle.Of(Pal.GoldTxt, TextAlign.Center, 2), 136);
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

        static string HudName(string n)
        {
            if (PixelFont.Measure(n) <= 66) return n;
            var w = n.Split(' '); if (w.Length < 2) return n.Length > 10 ? n.Substring(0, 10) : n;
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
            for (int i = 0; i < myFleet.ships.Count; i++)
            {
                int k = (int)myFleet.ships[i].kind;
                string st = mySunk[k] ? "sunk" : myHits[k] > 0 ? "hit" : "ok";
                youIcons[k].sprite = Atlas.Get("ficon_" + KindKey[k] + "_" + st);
            }
            for (int k = 0; k < 3; k++) themIcons[k].sprite = Atlas.Get("ficon_" + KindKey[k] + (theirSunk[k] ? "_sunk_f" : "_ok_f"));
        }

        void BuildDeck(Transform r)
        {
            Gfx.Sprite(r, "deck", 0, 0, 150);
            deckAim = Gfx.Node(r, "DeckAim");
            const int y = 358;
            Gfx.Sprite(deckAim, "deck_strip", 6, y, 152);
            PixelText.Create(deckAim, "SHOTS", 12, y + 4, TextStyle.Of(Pal.Sand2), 153);
            shotsCount = PixelText.Create(deckAim, "3/3", 16 + PixelFont.Measure("SHOTS"), y + 4, TextStyle.Of(Pal.Gold4), 153);
            for (int i = 0; i < shotPips.Length; i++) shotPips[i] = Gfx.Sprite(deckAim, "shot_loaded", 17 + i * 13, y + 20, 153);
            bonusLabel = PixelText.Create(deckAim, "+1", 0, y + 16, TextStyle.Of(Pal.Gold3), 153);
            Gfx.Sprite(deckAim, "pouch", 104, y + 8, 153);
            bool pvp = session.IsPvp;
            bool staked = pvp && ServerConfig.LIVE_ORE && session.fireMode != "virtual";   // LIVE_ORE off: every match is free
            PixelText.Create(deckAim, staked ? session.StakeSol.ToString("0.000") : "FREE", 123, y + 6, TextStyle.Of(Pal.Gold4), 153);
            PixelText.Create(deckAim, staked ? "SOL" : "NO STAKE", 123, y + 16, TextStyle.Of(Pal.Sand2), 153);
            lever = Gfx.Sprite(deckAim, Settings.AutoFire ? "lever_on" : "lever_off", 177, y + 9, 153);
            autoA = PixelText.Create(deckAim, "", 206, y + 6, TextStyle.Of(Pal.Grass), 153);
            autoB = PixelText.Create(deckAim, "", 206, y + 16, TextStyle.Of(Pal.Sand2), 153);
            UiButton.Create(deckAim, 174, y, 60, 30, ToggleAuto, 0, "autofire").sfx = "lock";
            RefreshAuto();
            fireBtn = Ui.Big(deckAim, 8, 396, 224, 72, "FIRE!", 3, "TAP ENEMY TILES TO AIM", "cannon", OnFire, 160);
            if (Practice) Ui.Hint(deckAim, "PRACTICE", 478, 170, Pal.Sand3);

            deckReveal = Gfx.Node(r, "DeckReveal");
            Gfx.Sprite(deckReveal, "parch_224x62", 8, 358, 152);
            foreach (var nx in new[] { 11, 227 }) { Gfx.Sprite(deckReveal, "nail", nx, 361, 153); Gfx.Sprite(deckReveal, "nail", nx, 414, 153); }
            reportTitle = PixelText.Create(deckReveal, "VOLLEY REPORT", 120, 361, TextStyle.Of(Pal.Red1, TextAlign.Center), 153);
            reportRows = Gfx.Node(deckReveal, "rows");
            nextBtn = Ui.Big(deckReveal, 8, 428, 224, 60, "RELOAD", 2, "WAITING FOR NEXT ROUND", "cannon", null, 160);
            nextBtn.SetEnabled(false);
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

        // ================= server message pump =================
        IEnumerator Pump()
        {
            while (true)
            {
                if (session.Inbox.Count == 0) { yield return null; continue; }
                var m = session.Inbox.Dequeue();
                IEnumerator h = Handle(m);
                if (h != null) yield return h;
                if (phase == Phase.Over) yield break;
            }
        }

        IEnumerator Handle(Dictionary<string, object> m)
        {
            switch (J.Str(m, "t", ""))
            {
                case "fleet_ack":
                    if (J.Bool(m, "autoPlaced"))
                    {
                        // placement timer ran out server-side: adopt the server's layout + salt (it is our commit now)
                        var lay = J.Ints(m, "layout"); var f = OnlineSession.FleetFromLayout(lay);
                        if (f != null && f.IsValid())
                        {
                            myFleet = f; setup.fleet = f; session.myLayout = lay.ToArray(); session.mySaltHex = J.Str(m, "salt"); session.myCommit = J.Str(m, "commit");
                            setup.commit = session.myCommit; RebuildOwnShips(); GameApp.Toast("TIME! THE SERVER PLACED YOUR FLEET");
                        }
                    }
                    return null;
                case "opponent_ready": hintText.Set(RivalShort + " SEALED THEIR FLEET"); return null;
                case "fleets_committed":
                    hintText.Set("FLEETS SEALED #" + Short8(session.myCommit) + " VS #" + Short8(session.opponentCommit));
                    if (!string.IsNullOrEmpty(session.myCommit) && J.Str(m, "yourCommit") != session.myCommit) GameApp.Toast("WARNING: SERVER COMMIT MISMATCH");
                    GameApp.Audio.Sfx("lock", 0, 0.6f);
                    return null;
                case "turn_start": BeginAim(m); return null;
                case "aim_ack": maxShots = J.Int(m, "maxShots", maxShots); return null;
                case "opponent_aiming": if (phase == Phase.Aim || phase == Phase.Sent) hintText.Set(RivalShort + " IS AIMING (" + J.Int(m, "count") + ")"); return null;
                case "clock": Anchor(m); return null;
                case "lock": return OnLock(m);
                case "fired_ack": return null;
                case "round_closed":
                    hintText.Set("ORE ROUND " + roundId + " SETTLING..."); timerText.Set("ORE"); wheel.fps = 16; GameApp.Audio.Sfx("tick");
                    return null;
                case "reveal": return Reveal(m);
                case "turn_void":
                    GameApp.Toast("ORE ROUND VOID - NO SHOTS COUNTED"); phase = Phase.Waiting; enemyBoard.ClearTargets(); aimed.Clear();
                    nuggy.Play(NuggyView.Pose.Idle, 0); return null;
                case "opponent_disconnected": hintText.Set(RivalShort + " DROPPED - " + J.Int(m, "graceSeconds", 90) + "S TO RETURN"); return null;
                case "opponent_reconnected": hintText.Set(RivalShort + " IS BACK!"); return null;
                case "match_state": SyncSnapshot(m); return null;
                case "rating_update": ratingDelta = J.Int(m, "delta"); newRating = J.Int(m, "rating"); haveRating = true; return null;
                case "match_end": return EndMatch(m);
                case "session_lost": return SessionLost();
                case "error":
                    // a volley queued across a reconnect lands one round late: the server already moved on - nothing to tell the player
                    bool staleAim = J.Str(m, "code", "") == "bad_aim" && J.Str(m, "message", "").Contains("stale");
                    if (!staleAim) GameApp.Toast(J.Str(m, "code", "error").ToUpperInvariant().Replace('_', ' ') + ": " + Trim(J.Str(m, "message", ""), 26));
                    if (J.Str(m, "ref") == "aim" && phase == Phase.Sent) { phase = Phase.Aim; RefreshSalvo(); }
                    return null;
            }
            return null;
        }

        static string Short8(string c) => string.IsNullOrEmpty(c) ? "--------" : c.Substring(0, Mathf.Min(8, c.Length)).ToUpperInvariant();
        static string Trim(string s, int n) => s.Length > n ? s.Substring(0, n).ToUpperInvariant() : s.ToUpperInvariant();

        void Anchor(Dictionary<string, object> m)
        {
            if (J.Get(m, "slot") == null) return;
            anchorSlot = J.Num(m, "slot"); anchorAt = Time.realtimeSinceStartup;
            slotMs = (float)J.Num(m, "slotMs", slotMs);
            if (J.Get(m, "lockSlot") != null) lockSlot = J.Num(m, "lockSlot");
            if (J.Get(m, "endSlot") != null) endSlot = J.Num(m, "endSlot");
        }

        double EstSlot => anchorSlot + (Time.realtimeSinceStartup - anchorAt) * 1000.0 / Mathf.Max(1f, slotMs);
        float SecondsToLock => (float)((lockSlot - EstSlot) * slotMs / 1000.0);
        float SecondsToEnd => (float)((endSlot - EstSlot) * slotMs / 1000.0);

        // ================= aim =================
        void BeginAim(Dictionary<string, object> m)
        {
            Anchor(m);
            roundId = J.ULong(m, "roundId"); maxShots = J.Int(m, "maxShots", 1); bonusShots = J.Int(m, "bonusShots");
            foreach (var s in J.Ints(m, "alreadyFired")) firedOnThem.Add(s);
            aimTotal = Mathf.Max(1f, SecondsToLock);
            phase = Phase.Aim; aimed.Clear(); enemyBoard.ClearTargets(); aimDirty = false; autoFilled = false;
            deckAim.gameObject.SetActive(true); deckReveal.gameObject.SetActive(false);
            if (pan != 0) StartCoroutine(PanTo(0));
            SetRoundLabel((Practice ? "PRACTICE ROUND " : "ORE ROUND ") + roundId);
            string reason = J.Str(m, "bonusReason");
            hintText.Set(bonusShots > 0 ? "BONUS SHOT" + (reason == "crit" ? " (CRIT)" : reason == "sank" ? " (SANK ONE)" : "") + "! AIM " + maxShots + " CANNONS"
                                        : "AIM " + maxShots + " CANNON" + (maxShots == 1 ? "" : "S") + " AT ENEMY WATERS");
            timerText.SetColor(Pal.GoldTxt); wheel.fps = 4;
            RefreshSalvo();
            nuggy.Play(NuggyView.Pose.Idle, 0);
            GameApp.Audio.Sfx("whistle", 0, 0.5f);
        }

        void RefreshSalvo()
        {
            int n = Mathf.Min(maxShots, shotPips.Length); bool bonus = bonusShots > 0 && n > 1; int used = Mathf.Min(aimed.Count, n);
            int normal = bonus ? n - 1 : n;
            for (int i = 0; i < shotPips.Length; i++)
            {
                var sr = shotPips[i]; sr.enabled = i < n; if (i >= n) continue;
                bool isBonus = bonus && i == n - 1;
                sr.transform.localPosition = Px.P(17 + i * 13 + (isBonus ? 4 : 0), 378);
                sr.sprite = Atlas.Get(isBonus ? (used >= n ? "shot_bonus_spent" : "shot_bonus") : (i < used ? "shot_spent" : "shot_loaded"));
            }
            bonusLabel.SetVisible(bonus);
            if (bonus) bonusLabel.MoveTo(17 + normal * 13 + 4 + 7, 374);
            int left = n - used;
            shotsCount.Set(left + "/" + n); shotsCount.SetColor(left > 0 ? Pal.Gold4 : Pal.Sand2);
            fireBtn.SetEnabled(phase == Phase.Aim && aimed.Count > 0);
            if (phase == Phase.Sent) fireBtn.SetSub("LOCKED IN - WAITING");
            else fireBtn.SetSub(phase != Phase.Aim ? "WAITING FOR NEXT ROUND" : aimed.Count == 0 ? (bonus ? "BONUS SHOT! TAP TILES" : "TAP ENEMY TILES TO AIM")
                : left > 0 ? left + " SHOT" + (left == 1 ? "" : "S") + " LEFT" : "ALL GUNS READY!");
        }

        public override void Pointer()
        {
            if (phase != Phase.Aim || !InputRouter.Down || pan > 0.01f) return;
            if (!BoardGeo.TileAt(InputRouter.Pos, enemyBoard.OffsetX, out int c, out int r)) return;
            int sq = r * 5 + c;
            if (firedOnThem.Contains(sq)) { GameApp.Audio.Sfx("error"); Haptics.Buzz(30); GameApp.Toast("ALREADY FIRED ON " + OreConstants.SquareName(sq)); return; }
            if (aimed.Contains(sq)) { aimed.Remove(sq); enemyBoard.SetTarget(c, r, false); GameApp.Audio.Sfx("tap"); aimDirty = true; RefreshSalvo(); return; }
            if (aimed.Count >= maxShots) { GameApp.Audio.Sfx("error"); Haptics.Buzz(30); GameApp.Toast("ALL GUNS AIMED - TAP A FLAG TO CLEAR"); return; }
            aimed.Add(sq); enemyBoard.SetTarget(c, r, true); aimDirty = true;
            GameApp.Audio.Sfx("select", 0, 1, 0.9f + aimed.Count * 0.08f); Haptics.Buzz(12);
            nuggy.React(NuggyView.Pose.Hop, 0.3f);
            RefreshSalvo();
        }

        void SendAim(bool fireNow)
        {
            // link down: ServerClient would drop it silently - keep it dirty so it goes out again after the resync
            if (net == null || !net.Ready) { aimDirty = true; return; }
            aimDirty = false; lastAimSent = Time.realtimeSinceStartup;
            net.Send(J.Msg("aim", "matchId", session.matchId, "roundId", (long)roundId, "squares", new List<int>(aimed), "fireNow", fireNow && Practice));
        }

        void AutoFill()
        {
            if (autoFilled || aimed.Count >= maxShots) return;
            if (aimed.Count > 0 && !Settings.AutoFire) return;
            autoFilled = true;
            var open = new List<int>(); for (int i = 0; i < 25; i++) if (!firedOnThem.Contains(i) && !aimed.Contains(i)) open.Add(i);
            for (int i = open.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var tmp = open[i]; open[i] = open[j]; open[j] = tmp; }
            for (int i = 0; aimed.Count < maxShots && i < open.Count; i++) { aimed.Add(open[i]); enemyBoard.SetTarget(open[i] % 5, open[i] / 5, true); }
            if (aimed.Count > 0) { GameApp.Toast(Settings.AutoFire ? "AUTO-FIRE: NUGGY PICKED THE REST" : "TIME! NUGGY FIRED FOR YOU"); SendAim(false); RefreshSalvo(); }
        }

        void OnFire()
        {
            if (phase != Phase.Aim || aimed.Count == 0) return;
            SendAim(true);                      // practice: fireNow locks immediately; PvP: final aim, lock comes at lockSlot
            phase = Phase.Sent; RefreshSalvo();
            GameApp.Audio.Sfx("lock"); Haptics.Buzz(40);
            hintText.Set(Practice ? "FIRING..." : "LOCKED IN! " + RivalShort + " IS STILL AIMING");
        }

        bool netDown;
        void Update()
        {
            // our own link dropped: say so (the client auto-reconnects and the server holds the seat for 90 s)
            if (phase != Phase.Over && net != null && net.Ready == netDown)
            {
                netDown = !net.Ready;
                if (netDown && net.Restarting) { hintText.Set("SERVER RESTARTING - RECONNECTING..."); GameApp.Toast("SERVER RESTARTING - HOLD FAST"); }
                else if (netDown) { hintText.Set("CONNECTION LOST - RECONNECTING..."); GameApp.Toast("CONNECTION LOST - RECONNECTING"); }
                else hintText.Set("RECONNECTED - RESYNCING");
            }
            if (phase == Phase.Aim || phase == Phase.Sent)
            {
                float left = Mathf.Max(0, SecondsToLock);
                timerText.Set(Clock(left));
                DrawRing(left / Mathf.Max(1, aimTotal));
                if (left < 10) timerText.SetColor(((int)(Time.time * 4) & 1) == 0 ? Pal.RedTxt : Pal.GoldTxt);
                GameApp.Audio.SetTension(Mathf.Clamp01(1.3f - left / Mathf.Max(1, aimTotal) * 1.6f));
                if (phase == Phase.Aim)
                {
                    if (aimDirty && Time.realtimeSinceStartup - lastAimSent > 0.3f) SendAim(false);   // latest aim before lock wins
                    if (left < 1.5f) AutoFill();
                }
            }
            else if (phase == Phase.Locked)
            {
                float left = Mathf.Max(0, SecondsToEnd);
                if (endSlot > 0 && left > 0) { timerText.Set(Clock(left)); DrawRing(left / Mathf.Max(1f, (float)((endSlot - lockSlot) * slotMs / 1000.0) + 0.01f)); }
            }
        }

        static string Clock(float seconds) { int s = Mathf.CeilToInt(Mathf.Max(0, seconds)); return s / 60 + ":" + (s % 60).ToString("00"); }

        // ================= lock / fire =================
        IEnumerator OnLock(Dictionary<string, object> m)
        {
            Anchor(m);
            var locked = J.Ints(m, "squares");
            phase = Phase.Locked;
            // reflect exactly what the server locked (it may differ if our last aim arrived late)
            enemyBoard.ClearTargets(); aimed.Clear();
            foreach (var s in locked) { aimed.Add(s); enemyBoard.SetTarget(s % 5, s / 5, true); }
            RefreshSalvo(); fireBtn.SetEnabled(false); fireBtn.SetSub(locked.Count > 0 ? "SHOTS AWAY!" : "NO SHOTS THIS ROUND");
            if (J.Bool(m, "fire"))
            {
                // The server wants an on-chain ORE Deploy. HARD GATE: this build never signs or sends one.
                if (!ServerConfig.LIVE_ORE) Debug.LogWarning("[OreSeas] lock.fire=true ignored: LIVE_ORE=false (no deploy sent; the referee will count a misfire)");
            }
            nuggy.Play(NuggyView.Pose.Sleepy, 999f);
            GameApp.Audio.SetTension(0.3f);
            hintText.Set(locked.Count > 0 ? "SHOTS AWAY! " + locked.Count + " CANNON" + (locked.Count == 1 ? "" : "S") : "NO TARGETS LOCKED");
            for (int i = 0; i < locked.Count; i++)
            {
                float gx = 120 + i * 14;
                Fx.Muzzle(Root, gx, 336, 112);
                GameApp.Audio.Sfx("boom", 0, 0.8f, 0.95f + i * 0.05f); Haptics.Buzz(35); if (!Settings.ReduceFlashes) shaker.Shake(1, 0.06f);
                StartCoroutine(Fx.Cannonball(fx, new Vector2(gx + 6, 328), new Vector2(gx + 20 - i * 30, -20), 0.6f, 20));
                yield return new WaitForSeconds(0.16f);
            }
            if (!Practice) { hintText.Set("LOCKED - WAITING FOR ORE ROUND END"); wheel.fps = 10; }
        }

        // ================= reveal =================
        IEnumerator Reveal(Dictionary<string, object> m)
        {
            phase = Phase.Reveal; wheel.fps = 4; DrawRing(1);
            nuggy.Play(NuggyView.Pose.Idle, 0);
            enemyBoard.ClearTargets();
            var oreObj = J.Obj(m, "ore");
            int win = J.Int(oreObj, "winningSquare", -1);
            bool kraken = J.Bool(m, "kraken") || J.Bool(oreObj, "motherlode");
            double krakenShare = 0;
            int oc = win >= 0 ? win % 5 : 0, orr = win >= 0 ? win / 5 : 0;
            SetRoundLabel("ROUND " + J.Str(m, "roundId", roundId.ToString()) + " SETTLED" + (win >= 0 ? " - ORE " + OreConstants.SquareName(win) : ""));
            GameApp.Audio.Sfx("reveal");
            var incoming = J.Obj(m, "incoming"); var outgoing = J.Obj(m, "outgoing");
            var oppInfo = J.Obj(m, "opponent");

            // beat 1: their volley on your waters
            yield return PanTo(1);
            if (win >= 0) ownBoard.ShowOre(oc, orr);
            hintText.Set(J.Bool(incoming, "misfire") ? RivalShort + " MISFIRED!" : RivalShort + "'S VOLLEY!");
            timerText.Set("!!"); timerText.SetColor(Pal.RedTxt);
            yield return new WaitForSeconds(0.25f);
            int nIn = 0;
            foreach (var s in J.Objs(incoming, "shots"))
            {
                nIn++;
                int sq = J.Int(s, "square"); int c = sq % 5, r = sq / 5; bool hit = J.Bool(s, "hit");
                var target = ownBoard.Center(c, r);
                GameApp.Audio.Sfx("whistle", 0, 0.5f);
                yield return Fx.Cannonball(fx, new Vector2(target.x + 30, 70), target, 0.45f, 10, 110);
                if (hit)
                {
                    int k = (int)OnlineSession.KindOf(J.Str(s, "ship", "raft")); myHits[k]++;
                    string sunkType = J.Str(s, "sunk");
                    Fx.Boom(fx, target.x, target.y - 2, 97); GameApp.Audio.Sfx("explode"); Haptics.Buzz(80); HitFeedback(false);
                    nuggy.React(NuggyView.Pose.Wince, sunkType != null ? 1.4f : 0.9f);
                    ownBoard.MarkFire(c, r, false);
                    Fx.Burst(fx, target.x, target.y, 14, new[] { Pal.Wood1, Pal.Red3, Pal.Gold3 }, 120);
                    if (sunkType != null)
                    {
                        int sk = (int)OnlineSession.KindOf(sunkType); mySunk[sk] = true;
                        for (int i = 0; i < myFleet.ships.Count; i++) if (myFleet.ships[i].kind == (ShipKind)sk && i < ownBoard.ships.Count) ownBoard.ships[i].Set(myFleet.ships[i], true);
                        GameApp.Audio.Sfx("sunk", 0.2f); hintText.Set("THEY SANK YOUR " + ShipSpec.Name((ShipKind)sk) + "!");
                        yield return new WaitForSeconds(0.5f);
                    }
                    else timerText.Set("HIT!");
                }
                else
                {
                    Fx.Splash(fx, target.x, target.y + 7, 96); GameApp.Audio.Sfx("splash"); ownBoard.MarkMiss(c, r);
                    if (J.Bool(s, "crit")) Fx.Bubbles(fx, target.x, target.y + 4);
                    timerText.Set("MISS");
                }
                UpdateHud();
                yield return new WaitForSeconds(0.3f);
            }
            yield return new WaitForSeconds(nIn == 0 ? 0.65f : 0.35f);

            // beat 2: your volley on enemy waters
            yield return PanTo(0);
            if (win >= 0) enemyBoard.ShowOre(oc, orr);
            hintText.Set(J.Bool(outgoing, "misfire") ? "YOUR SHOTS MISFIRED (NOT DEPLOYED)" : "YOUR VOLLEY!");
            var report = new List<string[]>();
            if (J.Arr(outgoing, "shots") != null) volleys++;
            foreach (var s in J.Objs(outgoing, "shots"))
            {
                int sq = J.Int(s, "square"); int c = sq % 5, r = sq / 5; bool hit = J.Bool(s, "hit"), crit = J.Bool(s, "crit");
                string sunkType = J.Str(s, "sunk");
                firedOnThem.Add(sq); shots++;
                var target = enemyBoard.Center(c, r);
                GameApp.Audio.Sfx("whistle", 0, 0.5f, 1.1f);
                yield return Fx.Cannonball(fx, GunPos, target, 0.55f, 34, 110);
                double shotOre = OreOf(s);
                if (crit) krakenShare += shotOre;
                if (hit)
                {
                    hits++; hitsOnThem.Add(sq);
                    Fx.Boom(fx, target.x, target.y - 2, 97, crit); GameApp.Audio.Sfx("explode"); Haptics.Buzz(60); HitFeedback(crit);
                    enemyBoard.MarkHitHidden(c, r);
                    Fx.Burst(fx, target.x, target.y, 18, new[] { Pal.Wood1, Pal.Gold3, Pal.Red3 }, 120);
                    nuggy.Play(NuggyView.Pose.Hop, 0.35f); nuggy.Play(NuggyView.Pose.Happy, 1.2f); timerText.Set("HIT!"); timerText.SetColor(Pal.RedTxt);
                    if (crit) { crits++; yield return Crit(shotOre); }
                    if (sunkType != null)
                    {
                        sunk++; int sk = (int)OnlineSession.KindOf(sunkType); theirSunk[sk] = true;
                        RevealSunk(oppInfo, sunkType);
                        GameApp.Audio.Sfx("sunk", 0.1f); hintText.Set("YOU SANK THEIR " + ShipSpec.Name((ShipKind)sk) + "!");
                        Fx.FloatText(fx, "SUNK!", target.x, target.y - 22, Pal.RedTxt, 210, 2);
                        yield return new WaitForSeconds(0.6f);
                    }
                }
                else
                {
                    Fx.Splash(fx, target.x, target.y + 7, 96); GameApp.Audio.Sfx("splash"); enemyBoard.MarkMiss(c, r);
                    timerText.Set("MISS"); timerText.SetColor(Pal.WhiteTxt);
                    if (crit)
                    {
                        crits++; Fx.Bubbles(fx, target.x, target.y + 4); GameApp.Audio.Sfx("chime");
                        Fx.FloatText(fx, "+" + shotOre.ToString("0.0000") + " ORE", target.x, target.y - 14, Pal.GoldTxt);
                        nuggy.Play(NuggyView.Pose.Happy, 1f);
                    }
                    else nuggy.React(NuggyView.Pose.Wince, 0.8f);
                }
                ore += shotOre;
                report.Add(new[] { OreConstants.SquareName(sq), crit && hit ? "CRITICAL HIT" : hit ? (sunkType != null ? "SUNK " + sunkType.ToUpperInvariant() : "HIT") : crit ? "ORE SQUARE" : "MISS",
                                   crit ? "+" + shotOre.ToString("0.0000") + " ORE" : hit ? "BOOM" : "SPLASH", crit || hit ? "g" : "r" });
                UpdateHud();
                yield return new WaitForSeconds(0.3f);
            }
            ShowReport(report);
            if (kraken)
            {
                krakens++;
                // same full KRAKEN RISES overlay as local play (was a 1.6 s ribbon over the board); auto-closes because the ORE clock keeps running
                yield return KrakenOverlay.Run(win >= 0 ? OreConstants.SquareName(win) : "?", krakenShare, 4.5f);
            }
            var next = J.Obj(m, "next");
            int nm = J.Int(next, "maxShots", maxShots), nb = J.Int(next, "bonusShots");
            phase = Phase.Waiting;
            timerText.SetColor(Pal.GoldTxt); timerText.Set("--");
            nextBtn.SetSub(nb > 0 ? "BONUS! NEXT: " + nm + " CANNONS" : "NEXT ROUND: " + nm + " CANNON" + (nm == 1 ? "" : "S"));
            hintText.Set(Practice ? RivalShort + " IS RELOADING..." : "WAITING FOR THE NEXT ORE ROUND");
        }

        static double OreOf(Dictionary<string, object> shot)
        {
            var g = J.Str(shot, "oreGrams");
            return !string.IsNullOrEmpty(g) && double.TryParse(g, out var grams) ? grams / 1e11 : 0;
        }

        void RevealSunk(Dictionary<string, object> oppInfo, string type)
        {
            foreach (var ss in J.Objs(oppInfo, "sunkShips"))
            {
                if (J.Str(ss, "type") != type) continue;
                var p = OnlineSession.PlacementFromCells(OnlineSession.KindOf(type), J.Ints(ss, "cells"));
                var sv = enemyBoard.AddShip(p, "e", false); sv.Set(p, true);
                return;
            }
        }

        void ShowReport(List<string[]> rows)
        {
            deckAim.gameObject.SetActive(false); deckReveal.gameObject.SetActive(true);
            foreach (Transform c in reportRows) Destroy(c.gameObject);
            reportTitle.Set(Practice || session.fireMode == "virtual" ? "VOLLEY REPORT" : "VOLLEY REPORT (LIVE)");
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
            nextBtn.SetEnabled(false);
        }

        void HitFeedback(bool crit)
        {
            if (Settings.ReduceFlashes) { if (crit) shaker.Shake(1, 0.15f); }
            else shaker.Shake(crit ? 2 : 1, crit ? 0.2f : 0.15f);
            if (!crit) ScreenPulse.Hit(Root, 185);
        }

        IEnumerator Crit(double shotOre)
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
                string oreTxt = "+" + shotOre.ToString("0.0000") + " ORE"; int gw = 6 + 4 + PixelFont.Measure(oreTxt); float gl = Mathf.Floor(120 - gw / 2f);
                if (PixelFont.LegacyCenter) { Gfx.Sprite(n, "ore_badge", 120 - pw / 2f + 6, cy + 17, 205); PixelText.Create(n, oreTxt, 128, cy + 16, TextStyle.Of(Pal.GoldTxt, TextAlign.Center).Shadow(Pal.Ink), 205); }
                else { Lay.SpriteTL(n, "ore_badge", gl, cy + 17, 205); PixelText.Create(n, oreTxt, gl + 10, cy + 16, TextStyle.Of(Pal.GoldTxt).Shadow(Pal.Ink), 205); }
            }
            Fx.Nuggets(n, 120, cy, 26);
            SpriteAnim.Play(n, "nugburst", 44, 322, 103, 10, true, false);
            nuggy.Play(NuggyView.Pose.Dance, 2.2f);
            float t = 0; while (t < 1.9f && !(InputRouter.Down && t > 0.6f)) { t += Time.deltaTime; yield return null; }
            Destroy(n.gameObject);
        }

        void SyncSnapshot(Dictionary<string, object> m)
        {
            // reconnect: re-mark what we know (server snapshot is the source of truth)
            var opp = J.Obj(m, "opponent");
            foreach (var sq in J.Ints(opp, "missesOnThem")) { firedOnThem.Add(sq); enemyBoard.MarkMiss(sq % 5, sq / 5); }
            foreach (var sq in J.Ints(opp, "hitsOnThem")) { firedOnThem.Add(sq); hitsOnThem.Add(sq); enemyBoard.MarkHitHidden(sq % 5, sq / 5); }
            if (string.IsNullOrEmpty(session.opponentCommit)) session.opponentCommit = J.Str(opp, "commit");
            hintText.Set("RECONNECTED - " + J.Str(m, "phase", "").ToUpperInvariant());
            // re-send this round's aim (it may have been lost with the link); a stale roundId is ignored by the server
            if (aimed.Count > 0 && (phase == Phase.Aim || phase == Phase.Sent)) SendAim(phase == Phase.Sent);
        }

        IEnumerator SessionLost()
        {
            phase = Phase.Over;
            GameApp.Toast("SERVER RESTARTED - MATCH NOT COUNTED", 3f);   // abandoned matches are never recorded (no loss, no rating change)
            yield return new WaitForSeconds(2f);
            GameApp.Net.ClearSession();
            GameApp.Go<TitleScreen>();
        }

        // ================= end =================
        IEnumerator EndMatch(Dictionary<string, object> m)
        {
            phase = Phase.Over;
            GameApp.Audio.SetTension(0);
            string res = J.Str(m, "result", "draw"), reason = J.Str(m, "reason", "");
            int outcome = res == "win" ? 1 : res == "loss" ? -1 : 0;
            var fleets = J.Obj(m, "fleets"); var you = J.Obj(fleets, "you"); var opp = J.Obj(fleets, "opponent");
            var oppLayout = J.Ints(opp, "layout");
            // commit-reveal: verify locally against the commits we hold (server's commitVerified is informational only)
            bool youOk = OnlineSession.Verify(J.Ints(you, "layout"), J.Str(you, "salt"), session.myCommit);
            bool themOk = oppLayout.Count == 25 && OnlineSession.Verify(oppLayout, J.Str(opp, "salt"), session.opponentCommit);
            if (oppLayout.Count == 25)
            {
                var theirFleet = OnlineSession.FleetFromLayout(oppLayout);
                if (theirFleet != null)
                    foreach (var p in theirFleet.ships)
                    {
                        if (theirSunk[(int)p.kind]) continue;
                        var sv = enemyBoard.AddShip(p, "e", false); sv.Set(p, false); sv.alpha = 0.75f;
                    }
            }
            // ranked: rating_update follows match_end - give it a moment
            if (session.mode == "ranked")
            {
                float wait = 0;
                while (wait < 1.5f && !haveRating)
                {
                    while (session.Inbox.Count > 0) { var x = session.Inbox.Dequeue(); if (J.Str(x, "t") == "rating_update") { ratingDelta = J.Int(x, "delta"); newRating = J.Int(x, "rating"); haveRating = true; } }
                    wait += Time.deltaTime; yield return null;
                }
            }
            var st = J.Obj(m, "stats");
            var sum = new MatchSummary
            {
                mode = session.IsPvp ? (session.mode == "ranked" ? "ranked" : "private") : "solo", opponent = setup.rival.name, result = outcome, rounds = J.Int(m, "turns"),
                volleys = J.Int(st, "volleys", volleys), shotsFired = J.Int(st, "shots", shots), hits = J.Int(st, "hits", hits), shipsSunk = J.Int(st, "sunk", sunk),
                crits = J.Int(st, "crits", crits), krakens = J.Int(st, "kraken", krakens), oreSim = J.Num(st, "ore", ore),
                simulated = session.fireMode == "virtual" || Practice, yourCommit = session.myCommit, theirCommit = session.opponentCommit, commitsVerified = youOk && themOk,
                reason = reason
            };
            GameApp.Leaderboard.Record(sum);
            bool unlocked = false;
            if (outcome == 1 && session.IsSolo && setup.campaignIndex >= 0)
            {
                var ps = GameApp.Leaderboard.Mine;
                if (setup.campaignIndex == ps.campaignBeaten && ps.campaignBeaten < Captains.Campaign.Length) { ps.campaignBeaten++; ps.Save(); unlocked = true; }
            }
            string note = haveRating ? "RATING " + newRating + " (" + (ratingDelta >= 0 ? "+" : "") + ratingDelta + ")" : ReasonText(reason);
            if (!themOk && oppLayout.Count == 25) note = "RIVAL FLEET BROKE ITS COMMIT!";
            yield return new WaitForSeconds(0.6f);
            ClearDeck();
            ResultsOverlay.Open(this, null, sum, unlocked, youOk, themOk, setup, note);
        }

        /// <summary>Match over: the FIRE/RELOAD deck and the live hint would peek out under the results parchment (half a label) - clear them.</summary>
        void ClearDeck() { deckAim.gameObject.SetActive(false); deckReveal.gameObject.SetActive(false); hintText.Set(""); }

        static string ReasonText(string r)
        {
            switch (r)
            {
                case "fleet_sunk": return "FLEET SUNK"; case "mutual_destruction": return "BOTH FLEETS SUNK";
                case "turn_limit": return "15-ROUND LIMIT"; case "resign": return "RESIGNED";
                case "disconnect": return "DISCONNECTED"; case "inactive": return "3 ROUNDS WITHOUT A VOLLEY";
                default: return r.ToUpperInvariant();
            }
        }

        public override void Back()
        {
            if (phase == Phase.Over) return;
            PauseOverlay.Toggle("RESIGN", () => { net.Resign(); GameApp.Toast("STRIKING YOUR COLOURS..."); });
        }
    }
}
