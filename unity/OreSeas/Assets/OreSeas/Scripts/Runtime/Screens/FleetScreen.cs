using OreSeas.Game;
using OreSeas.Net;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>01 FLEET: drag ships on your chart, tap to turn them about, shuffle, then SET SAIL (commit hash sealed).</summary>
    public sealed class FleetScreen : ScreenBase
    {
        MatchSetup setup; Fleet fleet; BoardView board; ShipView[] views = new ShipView[3];
        int selected, drag = -1; Vector2Int grab; Vector2 downPos; bool moved; ShipPlacement dragCandidate, lastShown;
        Transform[] cardSel = new Transform[3]; bool sailing;
        OnlineSession online; float placeLeft; PixelText placeHint;
        /// <summary>Toasts sit over the board's bottom rim, clear of the captain dialog at y308 (the default y300 covered its name line).</summary>
        const float FleetToastY = 289;

        public override void Build()
        {
            setup = GameApp.Setup ?? new MatchSetup { rival = Captains.Campaign[0], biome = Biomes.Get(BiomeId.Sunset) };
            if (setup.biome == null) setup.biome = Biomes.Get(BiomeId.Sunset);
            var r = Root;
            Scenery.Build(r, setup.biome, 352, true);
            PixelText.Create(r, "NUGSHIP", 120, 22, TextStyle.Of(Pal.GoldTxt, TextAlign.Center, 3).Outline(Pal.Ink, true), 40);
            Ui.Ribbon(r, "ribbon_red", 120, 50, "PLACE YOUR FLEET", 41);
            board = new BoardView(r, setup.biome, "YOUR WATERS", false);
            fleet = Fleet.Parse(PlayerPrefs.GetString("os.fleet", "")) ?? Fleet.Random(new System.Random());
            for (int i = 0; i < 3; i++) views[i] = board.AddShip(fleet.ships[i], "p", fleet.ships[i].kind == ShipKind.Galleon);
            CampaignScreen.Dialog(r, "cap_player", "CAPTAIN & NUGGY", new[] { "Drag ships to move them.", "Tap one to turn her about!" }, 308, 45, true);
            Gfx.Sprite(r, "deck", 0, 0, 50);
            for (int i = 0; i < 3; i++) BuildCard(r, i);
            Ui.Plank(r, 8, 400, 108, 20, "ROTATE", "icon_rotate", () => RotateSelected(), 60);
            Ui.Plank(r, 124, 400, 108, 20, "SHUFFLE", "icon_shuffle", Shuffle, 60);
            online = setup.online;
            string sealSub = online != null ? (online.IsPvp ? "SEALS FLEET - SHA-256" : "SEALS FLEET - SERVER AI") : setup.mode == "ranked" ? "SEALS FLEET - SIM RIVAL" : "LOCKS FLEET - NO SOL";
            Ui.Big(r, 8, 428, 224, 60, "SET SAIL", 2, sealSub, "anchor", SetSail, 70);
            string vs = setup.rival != null ? "VS " + setup.rival.name + " - " + setup.biome.name : "3 SHIPS - 6 HULL TILES";
            placeHint = Ui.Hint(r, vs, 494, 70);
            if (online != null) placeLeft = Mathf.Max(5, online.placementSeconds - 2);
            Select(0);
            GameApp.Audio.Music("music_title");
        }

        void BuildCard(Transform r, int i)
        {
            float x = 8 + i * 76, y = 362; var k = ShipSpec.All[i]; int idx = i;
            Gfx.Rect(r, x, y, 72, 32, Pal.Ink, 60); Gfx.Rect(r, x + 1, y + 1, 70, 30, Pal.Wood1, 61);
            var sel = Gfx.Node(r, "sel" + i);
            for (int kk = 0; kk < 70; kk += 2) { Gfx.Rect(sel, x + 1 + kk, y + 1, 1, 1, Pal.Gold3, 62); Gfx.Rect(sel, x + 1 + kk, y + 30, 1, 1, Pal.Gold3, 62); if (kk < 30) { Gfx.Rect(sel, x + 1, y + 1 + kk, 1, 1, Pal.Gold3, 62); Gfx.Rect(sel, x + 70, y + 1 + kk, 1, 1, Pal.Gold3, 62); } }
            cardSel[i] = sel;
            PixelText.Create(r, ShipSpec.Name(k), x + 36, y + 4, TextStyle.Of(Pal.Sand1, TextAlign.Center), 63);
            int n = ShipSpec.Length(k);
            for (int j = 0; j < n; j++) { Gfx.Rect(r, x + 36 - n * 5 + j * 10, y + 16, 8, 8, Pal.Ink, 62); Gfx.Rect(r, x + 37 - n * 5 + j * 10, y + 17, 6, 6, Pal.Red2, 63); }
            Gfx.Sprite(r, "icon_check", x + 62, y + 18, 63);
            UiButton.Create(r, x, y, 72, 32, () => Select(ShipIndexOf(k)), 0, "card" + i).sfx = "select";
        }

        int ShipIndexOf(ShipKind k) { for (int i = 0; i < fleet.ships.Count; i++) if (fleet.ships[i].kind == k) return i; return 0; }

        void Select(int i)
        {
            selected = i;
            var k = fleet.ships[i].kind;
            for (int c = 0; c < 3; c++) cardSel[c].gameObject.SetActive(ShipSpec.All[c] == k);
        }

        void Refresh() { for (int i = 0; i < 3; i++) { views[i].Set(fleet.ships[i], false); views[i].alpha = 1; views[i].ghostInvalid = false; } }

        void Shuffle()
        {
            fleet = Fleet.Random(new System.Random());
            Refresh(); GameApp.Audio.Sfx("whoosh"); Haptics.Buzz(20);
        }

        bool RotateSelected(int pivotC = -1, int pivotR = -1)
        {
            var s = fleet.ships[selected];
            if (s.Length == 1) { views[selected].Set(s, false); GameApp.Audio.Sfx("select"); return true; }
            int pc = pivotC < 0 ? s.c : pivotC, pr = pivotR < 0 ? s.r : pivotR;
            for (int off = 0; off < s.Length; off++)
            {
                var p = s; p.vertical = !s.vertical;
                if (p.vertical) { p.c = pc; p.r = pr - off; } else { p.r = pr; p.c = pc - off; }
                if (fleet.CanPlace(selected, p)) { fleet.ships[selected] = p; Refresh(); GameApp.Audio.Sfx("whoosh"); Haptics.Buzz(15); return true; }
            }
            GameApp.Audio.Sfx("error"); Haptics.Buzz(40); GameApp.Toast("NO ROOM TO TURN HER ABOUT", 2.2f, FleetToastY);
            return false;
        }

        public override void Pointer()
        {
            if (sailing) return;
            var pos = InputRouter.Pos;
            if (InputRouter.Down)
            {
                if (!BoardGeo.TileAt(pos, 0, out int c, out int r)) return;
                int si = fleet.ShipAt(c, r);
                if (si < 0) return;
                drag = si; Select(si); grab = new Vector2Int(c - fleet.ships[si].c, r - fleet.ships[si].r);
                downPos = pos; moved = false; dragCandidate = fleet.ships[si]; lastShown = dragCandidate;
                GameApp.Audio.Sfx("select");
                return;
            }
            if (drag < 0) return;
            if (InputRouter.Held)
            {
                if (!moved && Vector2.Distance(pos, downPos) > 6) moved = true;
                if (!moved) return;
                int c = Mathf.FloorToInt((pos.x - BoardGeo.BX) / BoardGeo.Tile) - grab.x;
                int r = Mathf.FloorToInt((pos.y - BoardGeo.BY - 10) / BoardGeo.Tile) - grab.y;
                var p = fleet.ships[drag]; p.c = c; p.r = r;
                if (p.c != dragCandidate.c || p.r != dragCandidate.r) { dragCandidate = p; Haptics.Buzz(5); }
                bool ok = fleet.CanPlace(drag, p);
                var shown = p;
                if (!p.InBounds) { shown.c = Mathf.Clamp(p.c, 0, p.vertical ? 4 : 5 - p.Length); shown.r = Mathf.Clamp(p.r, 0, p.vertical ? 5 - p.Length : 4); }
                if (shown.c != lastShown.c || shown.r != lastShown.r || shown.vertical != lastShown.vertical || views[drag].alpha == 1f) { views[drag].Set(shown, false); lastShown = shown; }
                views[drag].alpha = 0.8f; views[drag].ghostInvalid = !ok;
                return;
            }
            if (InputRouter.Up)
            {
                int d = drag; drag = -1;
                if (!moved) { var s = fleet.ships[d]; RotateSelected(s.c + grab.x, s.r + grab.y); return; }
                if (fleet.CanPlace(d, dragCandidate)) { fleet.ships[d] = dragCandidate; GameApp.Audio.Sfx("drop"); Haptics.Buzz(18); }
                else { GameApp.Audio.Sfx("error"); Haptics.Buzz(40); }
                Refresh();
            }
        }

        void Update()
        {
            if (online == null || sailing) return;
            // the server auto-places after placementSeconds, or the match may already be over (opponent left)
            if (online.fleetPlaced || online.ended) { sailing = true; GameApp.Go<OnlineBattleScreen>(); return; }
            placeLeft -= Time.deltaTime;
            int s = Mathf.CeilToInt(Mathf.Max(0, placeLeft));
            placeHint.Set("VS " + setup.rival.name + " - SAIL IN 0:" + s.ToString("00"));
            if (placeLeft <= 0) SetSail();   // seal what's on the chart rather than let the server pick
        }

        void SetSail()
        {
            if (sailing || !fleet.IsValid()) return;
            sailing = true;
            PlayerPrefs.SetString("os.fleet", fleet.Serialize()); PlayerPrefs.Save();
            if (online != null)
            {
                // commit = sha256(layout[25] || salt[32]) per PROTOCOL.md; the layout + salt stay on device until match_end
                setup.fleet = fleet.Clone();
                GameApp.Net.Send(online.PlaceFleetMessage(setup.fleet));
                setup.salt = OnlineSession.FromHex(online.mySaltHex); setup.commit = online.myCommit;
                GameApp.Audio.Sfx("lock"); Haptics.Buzz(60);
                GameApp.Toast("FLEET SEALED #" + setup.commit.Substring(0, 8).ToUpperInvariant(), 2.2f, FleetToastY);
                Invoke(nameof(GoOnline), 0.9f);
                return;
            }
            setup.fleet = fleet.Clone(); setup.salt = FleetCommit.NewSalt(); setup.commit = FleetCommit.Commit(setup.fleet, setup.salt);
            GameApp.Audio.Sfx("lock"); Haptics.Buzz(60);
            GameApp.Toast("FLEET SEALED #" + setup.commit.Substring(0, 8).ToUpperInvariant(), 2.2f, FleetToastY);
            Invoke(nameof(Go), 0.9f);
        }
        void Go() => GameApp.Go<BattleScreen>();
        void GoOnline() => GameApp.Go<OnlineBattleScreen>();

        public override void Back()
        {
            if (online != null) { GameApp.Net.Resign(); GameApp.Net.ClearSession(); GameApp.Go<TitleScreen>(); return; }
            if (setup.mode == "ranked") GameApp.Go<TitleScreen>(); else GameApp.Go<CampaignScreen>();
        }
    }
}
