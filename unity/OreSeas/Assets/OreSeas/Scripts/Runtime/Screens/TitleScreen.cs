using OreSeas.Game;
using OreSeas.Services;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>00 TITLE: sunset cove, Nuggy's galleon sailing in the midground + lighthouse, Nuggy on his rock, the NUGSHIP sign
    /// studded with glinting ORE nuggets (two rope-tied ore chunks hang under it), profile + help badges, deck menu.</summary>
    public sealed class TitleScreen : ScreenBase
    {
        PixelText walletText; SpriteRenderer walletDot; Avatars.Frame badge;
        const int HZ = 236, DeckY = 392;

        public override void Build()
        {
            var r = Root;
            Gfx.Sprite(r, "title_sky", 0, 0, 0);
            for (int i = 0; i < 7; i++)
            {
                var c = Gfx.Sprite(r, "tcloud_" + i, 0, 0, 1, "cloud");
                Drift.Add(c, ((i * 83) % 330) - 60, 104 + i * 17, 2 + i * 0.6f, -60, 270);
            }
            var ocean = OceanRenderer.Create(r, 0, HZ, Px.W, DeckY + 4 - HZ, SeaPalette.Open, 3, "Ocean");
            ocean.sunGlitter = true; ocean.sunX = 120; ocean.glitterLines = 7; ocean.glitterW = 60; ocean.horizonLine = Pal.Sky4;
            Gfx.Sprite(r, "title_ships", 0, 0, 4);
            SpriteAnim.Play(r, "title_ship", 0, 0, 5, 24f / 3.6f, true, false);   // 24-frame seamless loop (bob, sail ripple, flags, wake, lantern)
            SpriteAnim.Play(r, "title_lighthouse", 0, 0, 5, 8 / (Mathf.PI * 2 / 0.8f), true, false);
            Gfx.Sprite(r, "title_rock", 0, 0, 6);
            var nug = NuggyView.Create(r, "s250", 120, 336, 8);
            for (int i = 0; i < 7; i++)
            {
                var s = SpriteAnim.Play(r, i % 2 == 0 ? "spk_b" : "spk_s", 60 + JsMath.Hash(i, 3) * 120, 230 + JsMath.Hash(i, 4) * 100, 9, 4, true, false);
                s.timeOffset = i * 0.7f;
            }
            Gfx.Sprite(r, "title_chunk_l", 0, 0, 9);    // rope-tied ore chunks under the sign corners (behind the frame)
            Gfx.Sprite(r, "title_chunk_r", 0, 0, 9);
            Gfx.Sprite(r, "title_logo", 0, 0, 10);      // NUGSHIP sign (top y 34) - "SINK SHIPS. MINE ORE."
            Gfx.Sprite(r, "title_signore", 0, 0, 11);   // ORE nuggets studded around the frame (mirror-symmetric, 11..229)
            // staggered glints on the nuggets: each pair is mirrored (pixel column c <-> 239 - c), timings differ
            int gi = 0;
            foreach (var g in Glints)
            {
                Glint.Add(r, g.x, g.y, 12, g.z > 0, 3.2f + JsMath.Hash(gi, 7) * 2.4f, JsMath.Hash(gi, 9) * 5f); gi++;
                Glint.Add(r, 239 - g.x, g.y, 12, g.z > 0, 3.2f + JsMath.Hash(gi, 7) * 2.4f, JsMath.Hash(gi, 9) * 5f); gi++;
            }
            // profile badge (left) + help badge (right): mirrored, below the sign, clear of the camera hole + rounded corners
            badge = Avatars.Draw(r, Profile.Avatar, 14, 118, 30);
            UiButton.Create(r, 14, 118, 26, 26, () => GameApp.Go<ProfileScreen>(), 0, "Btn_profile").visual = badge.root;
            var help = Avatars.Draw(r, Avatars.Default, 200, 118, 30); help.SetSprite(null);
            PixelText.Create(help.root, "?", 13, 5, TextStyle.Of(Pal.GoldTxt, TextAlign.Center, 2).Outline(Pal.Ink), 35, "q");
            UiButton.Create(r, 200, 118, 26, 26, () => HelpOverlay.Open(), 0, "Btn_help").visual = help.root;
            PixelText.Create(r, "CAPTAIN", 27, 147, TextStyle.Of(Pal.Sand1, TextAlign.Center).Shadow(Pal.Ink), 31, "badgecap");
            PixelText.Create(r, "HELP", 213, 147, TextStyle.Of(Pal.Sand1, TextAlign.Center).Shadow(Pal.Ink), 31, "helpcap");
            // wallet chip under the logo
            Gfx.Rect(r, 60, 100, 120, 13, Pal.Ink, 11); Gfx.Rect(r, 61, 101, 118, 11, Pal.Plum, 12); Gfx.Rect(r, 61, 101, 118, 1, Pal.Sky1, 13);
            walletDot = Gfx.Rect(r, 65, 104, 5, 5, Pal.Grey1, 13);
            walletText = PixelText.Create(r, "", 79, 103, TextStyle.Of(Pal.Sand1), 14);
            // deck menu
            var deck = Gfx.Node(r, "Deck", 0, DeckY - 350);
            Gfx.Sprite(deck, "deck", 0, 0, 20);
            Ui.Big(r, 8, 400, 224, 44, "SOLO CAMPAIGN", 2, "5 CAPTAINS - DAILY TIDE", "cannon", () => GameApp.Go<CampaignScreen>(), 30);
            Ui.Big(r, 8, 450, 224, 36, "PVP 1V1", 2, null, "trophy", () => GameApp.Go<MatchmakingScreen>(), 30, 0, "green");
            Ui.Small(r, 8, 488, 72, 28, "WALLET", OnWallet, 30);
            Ui.Small(r, 84, 488, 72, 28, "RANKS", () => GameApp.Go<LeaderboardScreen>(), 30);
            Ui.Small(r, 160, 488, 72, 28, "SETTINGS", () => SettingsOverlay.Open(), 30);
            GameApp.Audio.Music("music_title");
            GameApp.Wallet.Changed += RefreshWallet;
            Profile.Changed += RefreshBadge;
            RefreshWallet();
            GameApp.Net.EnsureConnected();   // warm the NUGSHIP server session (no-op when ONLINE PLAY is off)
        }

        Net.ServerClient.NetState lastNet = (Net.ServerClient.NetState)(-1);
        void Update() { if (GameApp.Net != null && GameApp.Net.State != lastNet) { lastNet = GameApp.Net.State; RefreshWallet(); } }

        void OnDestroy() { if (GameApp.Wallet != null) GameApp.Wallet.Changed -= RefreshWallet; Profile.Changed -= RefreshBadge; }

        void RefreshBadge() { if (badge != null && badge.root != null) badge.Set(Profile.Avatar); }

        // nugget glint points (left half; x = pixel column, z = 1 for the bigger sparkle)
        static readonly Vector3[] Glints = { new Vector3(15, 33, 1), new Vector3(15, 93, 1), new Vector3(61, 33, 0), new Vector3(16, 63, 0), new Vector3(49, 95, 0), new Vector3(28, 107, 0) };

        /// <summary>A sparkle that plays once every period seconds (staggered), otherwise hidden.</summary>
        sealed class Glint : MonoBehaviour
        {
            Sprite[] frames; float period, offset; SpriteRenderer sr; const float Fps = 10;
            public static void Add(Transform p, float x, float y, int order, bool big, float period, float offset)
            {
                var sr = Gfx.Sprite(p, (Sprite)null, x, y, order, "glint");
                var g = sr.gameObject.AddComponent<Glint>(); g.sr = sr; g.period = period; g.offset = offset;
                g.frames = Atlas.Seq(big ? "spk_b" : "spk_s");
            }
            void Update()
            {
                if (frames == null || frames.Length == 0) return;
                float t = (Time.time + offset) % period; int i = (int)(t * Fps);
                sr.sprite = i < frames.Length ? frames[i] : null;
            }
        }

        void RefreshWallet()
        {
            if (walletText == null) return;
            var w = GameApp.Wallet;
            if (w.IsConnected) { walletText.Set(WalletFactory.Short(w.PublicKey) + " " + w.Cluster); walletDot.color = (Color)Pal.Grass; }
            else
            {
                // no wallet needed (LIVE_ORE off): the chip reports the game server instead of a scary "OFFLINE - NO WALLET" while online
                var ns = GameApp.Net != null ? GameApp.Net.State : Net.ServerClient.NetState.Offline;
                walletText.Set(ns == Net.ServerClient.NetState.Ready ? "ONLINE - FREE PLAY" : ns == Net.ServerClient.NetState.Connecting ? "CONNECTING..." : "OFFLINE - LOCAL AI");
                walletDot.color = ns == Net.ServerClient.NetState.Ready ? (Color)Pal.Gold3 : (Color)Pal.Grey1;
            }
            // dot (5) + gap + text as one group centred in the chip (60..180, centre 120); gap 4 or 5 keeps the group even
            int tw = PixelFont.Measure(walletText.Text), gap = (5 + 4 + tw) % 2 == 0 ? 4 : 5, gw = 5 + gap + tw;
            float l = PixelFont.LegacyCenter ? 65 : 120 - gw / 2f;
            Gfx.SetRect(walletDot, l, 104, 5, 5);
            if (PixelFont.LegacyCenter) { walletText.SetStyle(TextStyle.Of(Pal.Sand1, TextAlign.Center)); walletText.MoveTo(124, 103); }
            else walletText.MoveTo(l + 5 + gap, 103);
        }

        async void OnWallet()
        {
            var w = GameApp.Wallet;
            if (!w.IsAvailable) { GameApp.Toast(w.Status); return; }
            if (w.IsConnected) { w.Disconnect(); GameApp.Toast("WALLET DISCONNECTED"); return; }
            var res = await w.Connect();
            GameApp.Toast(res.ok ? "CONNECTED " + WalletFactory.Short(w.PublicKey) : res.message);
            if (res.ok) GameApp.Audio.Sfx("chime");
            RefreshWallet();
        }

        static float lastBack = -10f;
        public override void Back()
        {
            // Android back on the title: ask before leaving (press again within 2 s to quit).
            if (Time.unscaledTime - lastBack < 2f) { Application.Quit(); return; }
            lastBack = Time.unscaledTime; GameApp.Toast("PRESS BACK AGAIN TO QUIT", 2f);
        }
    }
}
