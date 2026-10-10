using System;
using System.Collections.Generic;
using OreSeas.Game;
using OreSeas.Net;
using OreSeas.Services;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>
    /// 1v1 PvP via the NUGSHIP server: QUICK MATCH (Elo queue) or a private INVITE CODE (host / join).
    /// While LIVE_ORE=false every match is off-chain ("virtual" shots on live ORE rounds, no wallet, no SOL).
    /// Server unreachable -> honest OFFLINE + the clearly labelled SIM rival (local AI on a simulated / live-read ORE clock).
    /// </summary>
    public sealed class MatchmakingScreen : ScreenBase
    {
        enum St { Connecting, Menu, Queued, Hosting, Joining, Offline, Found }
        St st = St.Connecting;
        PixelText status, timer, line1, line2, codeText, netText, stakeText; float t; Transform wheel;
        Ui.Btn main, inviteB, joinB, cancelB, stakeB;
        TouchScreenKeyboard kb; string hostCode;

        ServerClient Net => GameApp.Net;

        public override void Build()
        {
            var r = Root;
            Scenery.Build(r, Biomes.Get(BiomeId.Night), 400);
            PixelText.Create(r, "PVP 1V1", 120, 20, TextStyle.Of(Pal.GoldTxt, TextAlign.Center, 3).Outline(Pal.Ink, true), 40);
            Ui.Ribbon(r, "ribbon_red_wide", 120, 50, "MATCHMAKING", 41);
            Gfx.Sprite(r, "parch_224x150", 8, 104, 10); Ui.Nails(r, 8, 104, 224, 150, 11);
            wheel = SpriteAnim.Play(r, "wheel", 120, 140, 12, 8, true, false).transform;
            status = PixelText.Create(r, "CONNECTING...", 120, 168, TextStyle.Of(Pal.Red1, TextAlign.Center), 12);
            timer = PixelText.Create(r, "", 120, 180, TextStyle.Of(Pal.MapD, TextAlign.Center), 12);
            codeText = PixelText.Create(r, "", 120, 124, TextStyle.Of(Pal.Red1, TextAlign.Center, 3), 13);
            line1 = PixelText.Create(r, "Both captains fire every ORE round.", 120, 200, TextStyle.Of(Pal.MapB, TextAlign.Center), 12);
            line2 = PixelText.Create(r, "Fleets sealed by hash, revealed at the end.", 120, 211, TextStyle.Of(Pal.MapB, TextAlign.Center), 12);
            stakeText = PixelText.Create(r, "", 120, 226, TextStyle.Of(Pal.MapD, TextAlign.Center), 12);
            netText = PixelText.Create(r, "", 120, 238, TextStyle.Of(Pal.MapB, TextAlign.Center), 12);
            var deck = Gfx.Node(r, "Deck", 0, 392 - 350); Gfx.Sprite(deck, "deck", 0, 0, 50);
            main = Ui.Big(r, 8, 404, 224, 60, "QUICK MATCH", 2, "RANKED ELO - OFF-CHAIN", "cannon", OnMain, 70);
            inviteB = Ui.Plank(r, 8, 476, 108, 20, "INVITE CODE", "icon_trophy", HostCode, 70);
            joinB = Ui.Plank(r, 124, 476, 108, 20, "JOIN CODE", null, JoinCode, 70);
            // no STAKE plank while LIVE_ORE is off -> HARBOR/CANCEL sits centred instead of leaving a hole on the right
            cancelB = Ui.Plank(r, ServerConfig.LIVE_ORE ? 8 : 66, 502, 108, 20, "HARBOR", null, Back, 70);
            stakeB = Ui.Plank(r, 124, 502, 108, 20, "STAKE", "pouch", CycleStake, 70);
            RefreshStake();
            Net.Message += OnMessage; Net.MatchFound += OnFound;
            SetState(St.Connecting);
            Net.EnsureConnected(ok => { if (this == null) return; SetState(ok ? St.Menu : St.Offline); });
            GameApp.Audio.Music("music_battle_calm");
        }

        void OnDestroy() { if (GameApp.Net != null) { GameApp.Net.Message -= OnMessage; GameApp.Net.MatchFound -= OnFound; } }

        void SetState(St s)
        {
            st = s; t = 0;
            bool online = s != St.Offline && s != St.Connecting;
            wheel.gameObject.SetActive(s == St.Connecting || s == St.Queued || s == St.Hosting || s == St.Joining);
            codeText.Set(s == St.Hosting && hostCode != null ? hostCode : "");
            if (s == St.Hosting && hostCode != null) wheel.gameObject.SetActive(false);
            inviteB.SetVisible(online); joinB.SetVisible(online);
            stakeB.SetVisible((s == St.Menu || s == St.Offline) && ServerConfig.LIVE_ORE);   // no stakes while LIVE_ORE is off
            cancelB.SetLabel(s == St.Queued || s == St.Hosting || s == St.Joining ? "CANCEL" : "HARBOR");
            switch (s)
            {
                case St.Connecting: Say("CONNECTING TO THE NUGSHIP SERVER...", Pal.Red1, "", ""); main.SetVisible(false); break;
                case St.Menu:
                    Say("SERVER ONLINE - RATING " + Net.Rating, Pal.Grass4, "Quick match pairs you by Elo.", "Or share a 6-letter invite code.");
                    main.SetVisible(true); main.SetLabel("QUICK MATCH"); main.SetSub(ServerConfig.LIVE_ORE ? "RANKED ELO" : "RANKED ELO - OFF-CHAIN"); main.SetEnabled(true);
                    break;
                case St.Queued: Say("SEARCHING THE SEAS...", Pal.Red1, "Both captains fire every ORE round.", "Fleets sealed by hash, revealed at the end."); main.SetVisible(false); break;
                case St.Hosting: Say(hostCode == null ? "CREATING INVITE..." : "SHARE THIS CODE - WAITING", Pal.Red1, "Your rival taps JOIN CODE and types it.", "Code expires in 10 minutes."); main.SetVisible(false); break;
                case St.Joining: Say("JOINING...", Pal.Red1, "", ""); main.SetVisible(false); break;
                case St.Offline:
                    Say(Net.Status == "ONLINE" ? "PVP SERVER OFFLINE" : Net.Status, Pal.Red2, "Live PvP needs the NUGSHIP server.", "Until then, duel a clearly SIMULATED rival.");
                    main.SetVisible(true); main.SetLabel("DUEL SIM RIVAL"); main.SetSub(Settings.LiveRounds ? "AI ON LIVE ORE ROUNDS" : "AI ON A SIM ORE CLOCK"); main.SetEnabled(true);
                    break;
                case St.Found: Say("RIVAL FOUND!", Pal.Grass4, "", ""); main.SetVisible(false); inviteB.SetVisible(false); joinB.SetVisible(false); break;
            }
            netText.Set(s == St.Offline ? ServerConfig.ServerUrl : "");
        }

        void Say(string s, Color32 c, string a, string b) { status.Set(s); status.SetColor(c); line1.Set(a); line2.Set(b); }

        void RefreshStake() => stakeText.Set(ServerConfig.LIVE_ORE ? "STAKE " + Settings.Stake.ToString("0.000") + " SOL/SHOT" : "NO STAKES - NOTHING ON CHAIN");
        void CycleStake() { Settings.StakeIndex = (Settings.StakeIndex + 1) % Settings.Stakes.Length; RefreshStake(); }

        void OnMain()
        {
            if (st == St.Menu) { Net.QueueRanked(); SetState(St.Queued); GameApp.Audio.Sfx("search"); return; }
            if (st == St.Offline) DuelSim();
        }

        void HostCode() { if (st != St.Menu) return; hostCode = null; Net.CreatePrivate(); SetState(St.Hosting); }

        /// <summary>Dev play-test hook: a code to "paste" (the detached test players have no clipboard). Never set in normal play.</summary>
        internal static string DevCode;

        void JoinCode()
        {
            if (st != St.Menu) return;
            if (TouchScreenKeyboard.isSupported) { kb = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.ASCIICapable, false, false, false, false, "INVITE CODE", 6); return; }
            // Editor / desktop: paste the code into the clipboard, then tap JOIN CODE
            var clip = DevCode ?? GUIUtility.systemCopyBuffer ?? ""; DevCode = null;
            if (clip.Trim().Length == 6) { Net.JoinPrivate(clip); SetState(St.Joining); }
            else GameApp.Toast("COPY A 6-LETTER CODE, THEN TAP JOIN");
        }

        void Update()
        {
            t += Time.deltaTime;
            if (st == St.Queued || st == St.Hosting) timer.Set(((int)t / 60) + ":" + ((int)t % 60).ToString("00")); else if (st != St.Offline) timer.Set("");
            if (kb != null && kb.status != TouchScreenKeyboard.Status.Visible)
            {
                var k = kb; kb = null;
                if (k.status == TouchScreenKeyboard.Status.Done && !string.IsNullOrWhiteSpace(k.text)) { Net.JoinPrivate(k.text); SetState(St.Joining); }
            }
            (GameApp.Match as LocalSimMatchService)?.Tick();
            if (st != St.Offline && st != St.Found && st != St.Connecting && !Net.Ready) SetState(St.Offline);
            else if (st == St.Offline && Net.Ready && ServerConfig.OnlineEnabled) SetState(St.Menu);   // back after a server restart / reconnect
            else if (st == St.Offline && status.Text != Net.Status && !Net.Ready && Net.Status != "ONLINE") SetState(St.Offline); // e.g. SERVER RESTARTING -> SERVER UNREACHABLE
        }

        void OnMessage(Dictionary<string, object> m)
        {
            switch (J.Str(m, "t"))
            {
                case "queued": status.Set("SEARCHING THE SEAS... #" + J.Int(m, "position", 1)); break;
                case "queue_left": if (st == St.Queued) SetState(St.Menu); break;
                case "private_created": hostCode = J.Str(m, "code"); SetState(St.Hosting); GameApp.Audio.Sfx("chime"); break;
                case "error":
                    {
                        string code = J.Str(m, "code", "error"), msg = J.Str(m, "message", "");
                        GameApp.Toast(Friendly(code)); GameApp.Audio.Sfx("error");
                        Debug.Log("[OreSeas] matchmaking error " + code + ": " + msg);
                        if (st == St.Queued || st == St.Hosting || st == St.Joining) SetState(St.Menu);
                        break;
                    }
            }
        }

        static string Friendly(string code)
        {
            switch (code)
            {
                case "wallet_required": return "SERVER WANTS A WALLET (LIVE ORE IS OFF)";
                case "wallet_unverified": return "SERVER WANTS A WALLET SIGN-IN";
                case "bad_code": return "INVITE CODE NOT FOUND OR EXPIRED";
                case "self_join": return "THAT'S YOUR OWN CODE";
                case "in_match": return "ALREADY IN A MATCH";
                case "bad_stake": return "STAKE NOT OFFERED BY SERVER";
                case "server_restarting": return "SERVER RESTARTING - TRY AGAIN SOON";
                default: return code.ToUpperInvariant().Replace('_', ' ');
            }
        }

        void OnFound(OnlineSession s)
        {
            if (st == St.Found) return;
            SetState(St.Found);
            status.Set("VS " + s.opponentName.ToUpperInvariant() + (s.IsPvp ? " (" + s.opponentRating + ")" : ""));
            GameApp.Audio.Sting("sting_found"); Haptics.Buzz(80);
            LaunchOnline(s);
        }

        /// <summary>Shared by matchmaking + campaign: server match -> fleet placement (place_fleet within placementSeconds).</summary>
        public static void LaunchOnline(OnlineSession s, int campaignIndex = -1)
        {
            CaptainDef rival = null;
            if (s.opponentIsAI) { var id = ServerClient.UnityCaptainId(s.opponentCaptain); foreach (var c in Captains.Campaign) if (c.id == id) rival = c; }
            if (rival == null) rival = Captains.Rival(s.opponentName.ToUpperInvariant());
            var biomes = new[] { BiomeId.Sunset, BiomeId.Night, BiomeId.Storm, BiomeId.Fog, BiomeId.Frozen, BiomeId.Volcanic };
            var biome = s.opponentIsAI ? Biomes.Get(rival.biome) : Biomes.Get(biomes[Mathf.Abs((s.matchId ?? "").GetHashCode()) % biomes.Length]);
            GameApp.Setup = new MatchSetup { mode = s.mode, rival = rival, biome = biome, campaignIndex = campaignIndex, seed = (s.matchId ?? "").GetHashCode(), online = s };
            GameApp.Go<FleetScreen>();
        }

        void DuelSim()
        {
            var tk = ((LocalSimMatchService)GameApp.Match).CreateSimTicket(Settings.Stake);
            var rival = Captains.Rival(tk.rivalName);
            var biomes = new[] { BiomeId.Sunset, BiomeId.Night, BiomeId.Storm, BiomeId.Fog, BiomeId.Frozen, BiomeId.Volcanic };
            GameApp.Setup = new MatchSetup { mode = "ranked", rival = rival, biome = Biomes.Get(biomes[Mathf.Abs(tk.seed) % biomes.Length]), seed = tk.seed, ticket = tk };
            GameApp.Go<FleetScreen>();
        }

        public override void Back()
        {
            if (st == St.Queued) { Net.CancelQueue(); SetState(St.Menu); return; }
            if (st == St.Hosting || st == St.Joining) { Net.Disconnect(); Net.EnsureConnected(ok => { if (this != null) SetState(ok ? St.Menu : St.Offline); }); SetState(St.Connecting); return; }
            GameApp.Go<TitleScreen>();
        }
    }
}
