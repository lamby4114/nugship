using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OreSeas.Game;
using OreSeas.Net;
using OreSeas.Screens;
using OreSeas.View;
using UnityEngine;

namespace OreSeas
{
    /// <summary>
    /// Desktop end-to-end play-test harness (never on the phone: needs -nugplay on the command line).
    ///   Nugship.exe -nugplay C:\out -nugrole A|B -nugsync C:\sync -nugserver ws://127.0.0.1:8788/ws [-nugshotimg photo.jpg]
    /// Plays the game through synthetic taps fed into InputRouter (same Down/Held/Up path as a finger), renders every
    /// frame at the Seeker's 1200x2670 through the real PixelPerfectCamera, and writes a numbered PNG at each state plus
    /// the layout audit (CenterAudit) for each one. Role A walks every screen; role B is the second PvP captain.
    /// Nothing here touches a wallet: WALLET / CONNECT are never tapped and LIVE_ORE stays false.
    /// </summary>
    public sealed class PlayTest : MonoBehaviour
    {
        string outDir, role, sync; RenderTexture rt; StreamWriter log; int seq; readonly System.Random rng = new System.Random(7);
        readonly HashSet<string> once = new HashSet<string>();
        volatile int frame; volatile string step = "start"; System.Threading.Timer hb;
        public static bool Active;

        void Awake() { Active = true; }

        void Start()
        {
            Application.runInBackground = true;
            outDir = ServerConfig.CommandLine("-nugplay"); role = (ServerConfig.CommandLine("-nugrole") ?? "A").ToUpperInvariant();
            sync = ServerConfig.CommandLine("-nugsync") ?? outDir;
            Directory.CreateDirectory(outDir); Directory.CreateDirectory(sync);
            log = new StreamWriter(Path.Combine(outDir, "play_log.txt"), false) { AutoFlush = true };
            Application.logMessageReceived += (c, st, t) => { if (t != LogType.Log) try { log?.WriteLine("[" + t + "] " + c + (t == LogType.Exception ? "\n" + st : "")); } catch { } };
            InputRouter.Synthetic = true;
            var cam = Px.Cam;
            rt = new RenderTexture(Px.W * 5, Px.H * 5, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, antiAliasing = 1 };
            rt.Create();
            cam.targetTexture = rt;   // pixel perfect camera stays on: 1200x2670 = exactly 5x, like the Seeker
            hb = new System.Threading.Timer(_ => { try { log?.WriteLine("hb frame " + frame + " step " + step); } catch { } }, null, 15000, 15000);
            StartCoroutine(Main());
        }

        void Update() { frame = Time.frameCount; }
        void OnDestroy() { hb?.Dispose(); }

        // ------------------------------------------------------------------ primitives
        void Note(string s) { log.WriteLine("[" + Time.realtimeSinceStartup.ToString("0.0") + "s] " + s); }

        IEnumerator Settle(float secs = 0.8f)
        {
            float t = 0; while (t < 10 && GameApp.Transitioning) { t += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSecondsRealtime(secs);
        }

        IEnumerator Shot(string name)
        {
            seq++; string fn = seq.ToString("000") + "_" + role + "_" + name; step = "shot " + fn;
            yield return new WaitForEndOfFrame();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(outDir, fn + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Note("shot " + fn + " | " + (GameApp.Current != null ? GameApp.Current.GetType().Name : "-") + " | hint: " + HintOf(GameApp.Current));
            CenterAudit.Run(fn);
            yield return null;
        }

        IEnumerator ShotOnce(string key) { if (once.Add(key)) yield return Shot(key); }

        IEnumerator Tap(float x, float y)
        {
            InputRouter.SynthPos = new Vector2(x, y); InputRouter.SynthPressed = true; yield return null; yield return null;
            InputRouter.SynthPressed = false; yield return null; yield return null;
        }

        IEnumerator Drag(Vector2 a, Vector2 b, int steps = 14)
        {
            InputRouter.SynthPos = a; InputRouter.SynthPressed = true; yield return null; yield return null;
            for (int i = 1; i <= steps; i++) { InputRouter.SynthPos = Vector2.Lerp(a, b, i / (float)steps); yield return null; }
            yield return null; InputRouter.SynthPressed = false; yield return null; yield return null;
        }

        IEnumerator Back() { InputRouter.SynthBack = true; yield return null; yield return null; }

        static UiButton Find(string label)
        {
            foreach (var b in FindObjectsByType<UiButton>(FindObjectsSortMode.None))
            {
                if (b == null || !b.isActiveAndEnabled || b.layer != UiButton.TopLayer) continue;
                var l = b.GetComponentsInChildren<PixelText>(false).FirstOrDefault(p => p.name == "label");
                if ((l != null && string.Equals(l.Text, label, StringComparison.OrdinalIgnoreCase)) || b.name == label || b.name == "Btn_" + label || b.name == "Plank_" + label) return b;
            }
            return null;
        }

        static UiButton FindPrefix(string prefix)
        {
            UiButton best = null;
            foreach (var b in FindObjectsByType<UiButton>(FindObjectsSortMode.None))
                if (b != null && b.isActiveAndEnabled && b.layer == UiButton.TopLayer && b.name.StartsWith(prefix) && (best == null || b.rect.center.y < best.rect.center.y)) best = b;
            return best;
        }

        IEnumerator TapBtn(string label, float wait = 0.7f)
        {
            UiButton b = null; float t = 0;
            while ((b = Find(label)) == null && t < 4f) { t += Time.unscaledDeltaTime; yield return null; }
            if (b == null) { Note("!! BUTTON NOT FOUND: " + label + " on " + GameApp.Current?.GetType().Name); yield break; }
            if (!b.interactable) Note("button '" + label + "' is disabled");
            yield return Tap(b.rect.center.x, b.rect.center.y);
            yield return Settle(wait);
        }

        bool waitOk; public bool LastWaitOk => waitOk;
        IEnumerator WaitFor(Func<bool> cond, float timeout, string what)
        {
            step = "wait " + what; float t = 0; waitOk = false;
            while (t < timeout) { bool ok = false; try { ok = cond(); } catch { } if (ok) { waitOk = true; yield break; } t += Time.unscaledDeltaTime; yield return null; }
            Note("!! TIMEOUT waiting for " + what + " (" + timeout + "s) on " + GameApp.Current?.GetType().Name);
        }

        IEnumerator Go<T>(float secs = 1.0f) where T : ScreenBase { GameApp.Go<T>(); yield return null; yield return Settle(secs); }
        bool On<T>() where T : ScreenBase => GameApp.Current is T && !GameApp.Transitioning;
        static bool Overlay(string name) => GameApp.OverlayRoot != null && GameApp.OverlayRoot.Find(name) != null;

        const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        static object F(object o, string n) => o == null ? null : o.GetType().GetField(n, BF)?.GetValue(o);
        static string HintOf(object scr) => (F(scr, "hintText") as PixelText)?.Text ?? "";
        static string TimerOf(object scr) => (F(scr, "timerText") as PixelText)?.Text ?? "";

        void Flag(string name) { File.WriteAllText(Path.Combine(sync, name), DateTime.Now.ToString("o")); Note("flag " + name); }
        bool HasFlag(string name) => File.Exists(Path.Combine(sync, name));

        // ------------------------------------------------------------------ script
        IEnumerator Main()
        {
            yield return Settle(3f);
            if (role == "B") yield return RoleB(); else yield return RoleA();
            File.WriteAllText(Path.Combine(outDir, "audit_" + role + ".txt"), string.Join("\n", CenterAudit.Log) + "\n\nTOTAL " + CenterAudit.TotalChecks + " checks, " + CenterAudit.TotalIssues + " issues\n");
            Note("done");
            Flag(role + "_done");
            hb?.Dispose(); hb = null; log.Close(); log = null;
            Application.Quit();
        }

        IEnumerator RoleA()
        {
            // 1. first launch
            yield return Shot("title_first_launch");

            // 2. HOW TO PLAY from the title "?" badge
            yield return TapBtn("Btn_help", 0.6f);
            for (int p = 1; p <= HelpOverlay.CardCount; p++)
            {
                yield return Shot("help_card_" + p);
                if (p == 2) { yield return TapBtn("<", 0.4f); yield return Shot("help_back_to_1"); yield return TapBtn(">", 0.4f); }
                if (p < HelpOverlay.CardCount) yield return TapBtn(">", 0.4f);
            }
            yield return TapBtn("CLOSE", 0.5f);

            // 3. profile creation
            yield return TapBtn("Btn_profile", 1.5f);
            yield return Shot("profile_empty");
            var ps = GameApp.Current as ProfileScreen;
            if (ps != null)
            {
                foreach (var bad in new[] { ("AB", "too_short"), ("$$$ PIRATE", "bad_chars"), ("12345", "needs_letter"), ("ADMIN", "reserved"), ("BARNACLE BILL", "taken") })
                { ps.SetName(bad.Item1); yield return Settle(1.0f); yield return Shot("profile_name_" + bad.Item2); }
                ps.SetName("Salty Nugget"); yield return Settle(1.2f); yield return Shot("profile_name_ok");
                yield return Tap(22 + 2 * 34 + 13, 166 + 1 * 34 + 13); yield return Settle(0.5f); yield return Shot("profile_pick_portrait");
                var img = ServerConfig.CommandLine("-nugshotimg"); var photo = img != null ? AvatarPicker.Load(img) : null;
                if (photo != null) { ps.UsePhoto(photo); yield return Settle(0.6f); yield return Shot("profile_upload_photo"); }
                yield return TapBtn("SAVE", 2.5f); yield return Shot("profile_saved");
                yield return TapBtn("HARBOR", 1.2f);
            }
            yield return Shot("title_with_profile");

            // 4. leaderboards, every tab
            yield return TapBtn("RANKS", 1.5f);
            foreach (var tab in new[] { "MY LOG", "GLOBAL", "WEEKLY" }) { yield return TapBtn(tab, 1.6f); yield return Shot("ranks_" + tab.Replace(' ', '_').ToLowerInvariant()); }
            yield return TapBtn("HARBOR", 1.0f);

            // 5. settings (Android back closes it)
            yield return TapBtn("SETTINGS", 0.6f); yield return Shot("settings");
            yield return TapBtn("row_DEMO LUCK", 0.3f); yield return Shot("settings_demo_luck_on");
            yield return Back(); yield return Settle(0.5f); yield return Shot("settings_closed_by_back");

            // 6. solo campaign, five islands through the server AI
            yield return TapBtn("SOLO CAMPAIGN", 1.5f); yield return Shot("campaign_map");
            yield return TapBtn("Node2", 0.4f); yield return Shot("campaign_locked_island_toast");
            for (int isle = 0; isle < Captains.Campaign.Length; isle++)
            {
                int tries = 0;
                while (true)
                {
                    if (On<CampaignScreen>())
                    {
                        yield return TapBtn("Node" + isle, 0.5f); yield return Shot("campaign_isle" + (isle + 1) + "_intro");
                        yield return TapBtn("SET SAIL", 0.2f); yield return Shot("campaign_hoisting_sails");
                    }
                    yield return WaitFor(() => On<FleetScreen>(), 20, "fleet screen"); yield return Settle(0.6f);
                    yield return FleetPlacement("isle" + (isle + 1), isle == 0);
                    yield return PlayOnline("isle" + (isle + 1), isle == 0);
                    var won = ResultWin();
                    Note("isle " + (isle + 1) + " result " + (won ? "WIN" : "LOSS/DRAW"));
                    if (won) { if (isle < Captains.Campaign.Length - 1) { yield return TapBtn("NEXT ISLE", 1.0f); } break; }
                    if (++tries > 4) { Note("!! could not beat isle " + (isle + 1)); yield return TapBtn("HARBOR", 1.2f); yield return TapBtn("SOLO CAMPAIGN", 1.2f); break; }
                    yield return TapBtn("REMATCH", 1.0f);
                }
            }
            yield return TapBtn("HARBOR", 1.2f);

            // 7. daily tide: local AI battle on the device's own simulated ORE clock
            yield return TapBtn("SOLO CAMPAIGN", 1.2f); yield return Shot("campaign_all_beaten");
            yield return TapBtn("DAILY TIDE", 1.2f);
            yield return WaitFor(() => On<FleetScreen>(), 10, "daily fleet"); yield return Settle(0.5f); yield return Shot("daily_fleet");
            yield return TapBtn("SET SAIL", 0.3f);
            yield return PlayLocal("daily");
            yield return TapBtn("HARBOR", 1.2f);

            // 8. PvP quick match vs captain B
            yield return TapBtn("PVP 1V1", 1.5f); yield return WaitFor(() => Find("QUICK MATCH") != null, 10, "matchmaking menu");
            yield return Shot("pvp_menu");
            Flag("A_quick");
            yield return TapBtn("QUICK MATCH", 0.5f); yield return Shot("pvp_searching");
            yield return WaitFor(() => On<FleetScreen>(), 60, "pvp fleet"); yield return Settle(0.5f); yield return Shot("pvp_fleet");
            yield return TapBtn("SET SAIL", 0.3f);
            yield return PlayOnline("pvp", false);
            // store policy: report / block straight from the PvP results
            yield return TapBtn("REPORT/BLOCK", 0.6f); yield return Shot("pvp_results_report_block");
            yield return TapBtn("REPORT NAME", 0.5f); yield return Shot("pvp_results_reported_toast");
            yield return TapBtn("HARBOR", 1.2f);

            // 9. invite code: host, B joins, B drops its connection, then we resign from the pause menu
            yield return WaitFor(() => HasFlag("B_ready_code"), 120, "B ready for code");
            yield return TapBtn("PVP 1V1", 1.5f); yield return WaitFor(() => Find("INVITE CODE") != null, 10, "invite button");
            yield return TapBtn("INVITE CODE", 1.2f);
            var mm = GameApp.Current as MatchmakingScreen; string code = F(mm, "hostCode") as string;
            yield return Shot("pvp_invite_code");
            if (!string.IsNullOrEmpty(code)) File.WriteAllText(Path.Combine(sync, "code.txt"), code); else Note("!! no invite code");
            yield return WaitFor(() => On<FleetScreen>(), 60, "private fleet"); yield return Settle(0.5f); yield return Shot("private_fleet");
            yield return TapBtn("SET SAIL", 0.3f);
            yield return PlayOnline("private", false, resignAtRound: 4);
            yield return TapBtn("HARBOR", 1.2f);

            // 10. matchmaking edge cases: bad code, queue + cancel
            yield return TapBtn("PVP 1V1", 1.5f); yield return WaitFor(() => Find("JOIN CODE") != null, 10, "join button");
            Screens.MatchmakingScreen.DevCode = "ZZZZZZ"; yield return TapBtn("JOIN CODE", 1.0f); yield return Shot("pvp_bad_code");
            yield return Settle(1.5f);
            yield return TapBtn("QUICK MATCH", 0.8f); yield return TapBtn("CANCEL", 0.8f); yield return Shot("pvp_queue_cancelled");
            // 10b. deploy: server_restart + close 1012 -> SERVER RESTARTING, backoff reconnect, back to the menu
            GameApp.Net.DebugServerRestart(); yield return Settle(0.4f); yield return Shot("pvp_server_restarting");
            yield return WaitFor(() => GameApp.Net.Ready && Find("QUICK MATCH") != null, 30, "reconnect after restart"); yield return Settle(0.5f); yield return Shot("pvp_server_back");
            yield return TapBtn("HARBOR", 1.0f);

            // 10c. leaderboard after PvP: AI tags on scripted rows, tap a real rival -> report / block / unblock
            yield return TapBtn("RANKS", 1.5f); yield return TapBtn("GLOBAL", 1.8f); yield return Shot("ranks_global_after_pvp");
            var row = FindPrefix("row");
            if (row != null)
            {
                yield return Tap(row.rect.center.x, row.rect.center.y); yield return Settle(0.5f); yield return Shot("ranks_player_actions");
                yield return TapBtn("BLOCK", 1.0f); yield return Shot("ranks_rival_blocked");
                row = FindPrefix("row");
                if (row != null) { yield return Tap(row.rect.center.x, row.rect.center.y); yield return Settle(0.5f); yield return Shot("ranks_player_actions_blocked"); yield return TapBtn("UNBLOCK", 1.0f); }
            }
            else Note("!! no tappable rival row on GLOBAL");
            yield return TapBtn("HARBOR", 1.0f);
            Flag("A_ranks_done");
            // B deletes its profile: the row must vanish from the board
            yield return WaitFor(() => HasFlag("B_deleted"), 180, "B deleted");
            yield return TapBtn("RANKS", 1.5f); yield return TapBtn("GLOBAL", 1.8f); yield return Shot("ranks_global_after_b_deleted");
            yield return TapBtn("HARBOR", 1.0f);

            // 11. server unreachable: honest OFFLINE + the labelled SIM rival
            ServerConfig.DevUrl = "ws://127.0.0.1:8799/ws"; GameApp.Net.Disconnect();   // nothing listens on 8799
            yield return TapBtn("PVP 1V1", 3.0f); yield return WaitFor(() => Find("DUEL SIM RIVAL") != null, 30, "offline menu"); yield return Shot("pvp_offline");
            yield return TapBtn("DUEL SIM RIVAL", 1.2f); yield return WaitFor(() => On<FleetScreen>(), 10, "sim fleet"); yield return Shot("sim_fleet");
            yield return TapBtn("SET SAIL", 0.3f);
            yield return PlayLocal("sim_rival", abandonAtRound: 3);
            ServerConfig.DevUrl = null; GameApp.Net.Disconnect();
            yield return Settle(1.0f);
            yield return Shot("title_end");
        }

        IEnumerator RoleB()
        {
            yield return Go<ProfileScreen>(1.5f);
            var ps = GameApp.Current as ProfileScreen;
            if (ps != null) { ps.SetName("Kraken Bait"); yield return Settle(1.2f); yield return Tap(22 + 5 * 34 + 13, 166 + 2 * 34 + 13); yield return Settle(0.3f); yield return TapBtn("SAVE", 2.5f); yield return Shot("b_profile_saved"); }
            yield return Go<TitleScreen>(1.0f);
            yield return WaitFor(() => HasFlag("A_quick"), 1500, "A quick match");
            yield return TapBtn("PVP 1V1", 1.5f); yield return WaitFor(() => Find("QUICK MATCH") != null, 10, "matchmaking menu");
            yield return TapBtn("QUICK MATCH", 0.5f);
            yield return WaitFor(() => On<FleetScreen>(), 90, "pvp fleet"); yield return Settle(0.5f); yield return Shot("b_pvp_fleet");
            yield return TapBtn("SET SAIL", 0.3f);
            yield return PlayOnline("b_pvp", false);
            yield return TapBtn("HARBOR", 1.2f);
            Flag("B_ready_code");
            yield return WaitFor(() => File.Exists(Path.Combine(sync, "code.txt")), 300, "invite code");
            var code = File.Exists(Path.Combine(sync, "code.txt")) ? File.ReadAllText(Path.Combine(sync, "code.txt")).Trim() : "";
            yield return TapBtn("PVP 1V1", 1.5f); yield return WaitFor(() => Find("JOIN CODE") != null, 10, "join button");
            Screens.MatchmakingScreen.DevCode = code; yield return TapBtn("JOIN CODE", 0.3f); yield return Shot("b_joining");
            yield return WaitFor(() => On<FleetScreen>(), 30, "private fleet"); yield return Settle(0.5f);
            yield return TapBtn("SET SAIL", 0.3f);
            yield return PlayOnline("b_private", false, dropAtRound: 2);
            yield return Settle(1f);
            // store policy: in-app profile deletion (after A has seen B on the board)
            yield return WaitFor(() => HasFlag("A_ranks_done"), 600, "A ranks done");
            yield return Go<TitleScreen>(1.0f);
            yield return Go<ProfileScreen>(1.5f); yield return Shot("b_profile_delete_button");
            yield return TapBtn("DELETE PROFILE", 0.6f); yield return Shot("b_delete_confirm");
            yield return TapBtn("KEEP", 0.5f); yield return Shot("b_delete_kept");
            yield return TapBtn("DELETE PROFILE", 0.6f); yield return TapBtn("DELETE", 0.2f); yield return Shot("b_deleting");
            yield return WaitFor(() => On<TitleScreen>(), 15, "title after delete"); yield return Settle(0.6f); yield return Shot("b_profile_deleted");
            Flag("B_deleted");
            yield return Go<ProfileScreen>(2.5f); yield return Shot("b_profile_after_delete");
            yield return Go<TitleScreen>(1.0f);
        }

        // ------------------------------------------------------------------ fleet
        IEnumerator FleetPlacement(string tag, bool full)
        {
            yield return Shot(tag + "_fleet");
            if (full)
            {
                yield return TapBtn("ROTATE", 0.4f); yield return Shot(tag + "_fleet_rotated");
                yield return TapBtn("SHUFFLE", 0.4f); yield return Shot(tag + "_fleet_shuffled");
                var fs = GameApp.Current as FleetScreen; var fleet = F(fs, "fleet") as Fleet;
                if (fleet != null)
                {
                    var raft = fleet.ships.FirstOrDefault(s => s.kind == ShipKind.Raft);
                    var from = new Vector2(BoardGeo.BX + raft.c * BoardGeo.Tile + 19, BoardGeo.BY + raft.r * BoardGeo.Tile + 19);
                    // try every empty tile until the raft moves
                    for (int sq = 0; sq < 25; sq++)
                    {
                        int c = sq % 5, r = sq / 5; if (fleet.ShipAt(c, r) >= 0) continue;
                        yield return Drag(from, new Vector2(BoardGeo.BX + c * BoardGeo.Tile + 19, BoardGeo.BY + r * BoardGeo.Tile + 19 + 10));
                        break;
                    }
                    yield return Settle(0.4f); yield return Shot(tag + "_fleet_dragged");
                }
            }
            yield return TapBtn("SET SAIL", 0.15f);
            yield return Shot(tag + "_fleet_sealed_toast");
        }

        // ------------------------------------------------------------------ battles
        bool ResultWin()
        {
            var r = GameApp.OverlayRoot != null ? GameApp.OverlayRoot.Find("Results") : null;
            if (r == null) return false;
            return r.GetComponentsInChildren<PixelText>(false).Any(t => t.Text == "VICTORY!");
        }

        List<int> PickTargets(HashSet<int> fired, HashSet<int> hits, HashSet<int> sunkCells, int n)
        {
            var pick = new List<int>();
            var live = hits.Where(h => !sunkCells.Contains(h)).ToList();
            var cand = new List<int>();
            foreach (var h in live) foreach (var d in new[] { -5, 5, -1, 1 })
                {
                    int s = h + d; if (s < 0 || s >= 25) continue; if ((d == -1 || d == 1) && s / 5 != h / 5) continue;
                    if (!fired.Contains(s) && !cand.Contains(s)) cand.Add(s);
                }
            cand = cand.OrderBy(_ => rng.Next()).ToList();
            var par = Enumerable.Range(0, 25).Where(s => !fired.Contains(s) && ((s / 5 + s % 5) & 1) == 0).OrderBy(_ => rng.Next()).ToList();
            var any = Enumerable.Range(0, 25).Where(s => !fired.Contains(s)).OrderBy(_ => rng.Next()).ToList();
            foreach (var s in cand.Concat(par).Concat(any)) { if (pick.Count >= n) break; if (!pick.Contains(s)) pick.Add(s); }
            return pick;
        }

        static Vector2 TileCenter(int sq) => new Vector2(BoardGeo.BX + (sq % 5) * BoardGeo.Tile + 19, BoardGeo.BY + (sq / 5) * BoardGeo.Tile + 19);

        IEnumerator Events(ScreenBase scr, string tag, int round)
        {
            if (scr == null) yield break;
            string hint = HintOf(scr), timer = TimerOf(scr);
            if (scr.transform.Find("Intro") != null) yield return ShotOnce(tag + "_battle_stations");
            if (scr.transform.Find("Crit") != null) { yield return new WaitForSecondsRealtime(0.35f); yield return ShotOnce(tag + "_critical_hit"); }
            if (scr.transform.Find("Kraken") != null || Overlay("Kraken")) { yield return new WaitForSecondsRealtime(0.6f); yield return ShotOnce(tag + "_kraken"); if (Overlay("Kraken")) yield return TapBtn("HUZZAH!", 0.4f); }
            if (hint.StartsWith("YOU SANK THEIR")) yield return ShotOnce(tag + "_sank_" + hint.Substring(15).TrimEnd('!').ToLowerInvariant());
            if (hint.StartsWith("THEY SANK YOUR")) yield return ShotOnce(tag + "_lost_" + hint.Substring(15).TrimEnd('!').ToLowerInvariant());
            if (hint.StartsWith("BONUS SHOT")) yield return ShotOnce(tag + "_bonus_shot");
            if (hint.Contains("CONNECTION LOST")) yield return ShotOnce(tag + "_connection_lost");
            if (hint.StartsWith("RECONNECTED")) yield return ShotOnce(tag + "_reconnected");
            if (hint.Contains("DROPPED")) yield return ShotOnce(tag + "_opponent_dropped");
            if (hint.Contains("IS BACK")) yield return ShotOnce(tag + "_opponent_back");
            if (hint.EndsWith("'S VOLLEY!")) yield return ShotOnce(tag + "_incoming_volley");
            if (timer == "HIT!" && hint == "YOUR VOLLEY!") yield return ShotOnce(tag + "_first_hit");
            if (timer == "MISS" && hint == "YOUR VOLLEY!") yield return ShotOnce(tag + "_first_splash");
            var deck = F(scr, "deckReveal") as Transform;
            if (deck != null && deck.gameObject.activeInHierarchy && round <= 2) yield return ShotOnce(tag + "_report_round" + round);
            if (deck != null && deck.gameObject.activeInHierarchy && deck.GetComponentsInChildren<PixelText>(false).Any(t => t.Text == "ORE SQUARE" || t.Text == "CRITICAL HIT")) yield return ShotOnce(tag + "_report_ore_square");
        }

        IEnumerator PlayOnline(string tag, bool pauseTest, int resignAtRound = -1, int dropAtRound = -1)
        {
            yield return WaitFor(() => On<OnlineBattleScreen>() || Overlay("Results"), 40, tag + " online battle");
            var scr = GameApp.Current; once.RemoveWhere(k => k.StartsWith(tag + "_"));
            int round = 0; float t = 0; bool paused = false, dropped = false;
            var sunkCells = new HashSet<int>();
            while (t < 900 && !Overlay("Results"))
            {
                yield return Events(scr, tag, round);
                string ph = F(scr, "phase")?.ToString();
                float pan = F(scr, "pan") is float pf ? pf : 0;
                var aimed = F(scr, "aimed") as List<int>;
                if (ph == "Aim" && pan < 0.01f && aimed != null && aimed.Count == 0 && scr.transform.Find("Intro") == null)
                {
                    round++;
                    if (pauseTest && round == 2 && !paused)
                    {
                        paused = true; yield return Back(); yield return Settle(0.5f); yield return Shot(tag + "_pause");
                        yield return TapBtn("HOW TO PLAY", 0.5f); yield return Shot(tag + "_pause_help");
                        yield return Back(); yield return Settle(0.3f); yield return TapBtn("RESUME", 0.4f);
                    }
                    if (resignAtRound > 0 && round == resignAtRound)
                    {
                        yield return Back(); yield return Settle(0.5f); yield return Shot(tag + "_pause_resign");
                        yield return Tap(122 + 54, 232 + 17); yield return Settle(0.4f); yield return Shot(tag + "_resign_confirm");
                        yield return Tap(122 + 54, 232 + 17); yield return Settle(0.5f);
                        yield return WaitFor(() => Overlay("Results"), 20, "resign results"); break;
                    }
                    if (dropAtRound > 0 && round == dropAtRound && !dropped)
                    {
                        dropped = true; GameApp.Net.DebugDrop(); yield return Settle(0.4f); yield return Shot(tag + "_dropped_self");
                        yield return WaitFor(() => GameApp.Net.Ready, 30, "reconnect"); yield return Settle(1.5f); yield return Shot(tag + "_after_reconnect");
                        continue;
                    }
                    var fired = F(scr, "firedOnThem") as HashSet<int>; var hits = F(scr, "hitsOnThem") as HashSet<int>; int max = (int)F(scr, "maxShots");
                    var picks = PickTargets(fired ?? new HashSet<int>(), hits ?? new HashSet<int>(), sunkCells, max);
                    foreach (var sq in picks) { yield return Tap(TileCenter(sq).x, TileCenter(sq).y); yield return new WaitForSecondsRealtime(0.08f); }
                    if (round == 1) yield return Shot(tag + "_aimed_round1");
                    yield return TapBtn("FIRE!", 0.05f);
                    if (round == 1) yield return Shot(tag + "_fired_round1");
                    if (F(scr, "phase")?.ToString() == "Sent") yield return ShotOnce(tag + "_locked_waiting");
                }
                if (GameApp.Current != scr && !Overlay("Results")) { Note(tag + ": screen changed to " + GameApp.Current?.GetType().Name); break; }
                yield return null; t += Time.unscaledDeltaTime;
            }
            yield return Settle(1.6f);
            yield return Shot(tag + "_results");
        }

        IEnumerator PlayLocal(string tag, int abandonAtRound = -1)
        {
            yield return WaitFor(() => On<BattleScreen>(), 20, tag + " local battle");
            var scr = GameApp.Current as BattleScreen; once.RemoveWhere(k => k.StartsWith(tag + "_"));
            int round = 0; float t = 0;
            while (t < 600 && !Overlay("Results") && scr != null)
            {
                yield return Events(scr, tag, round);
                string ph = F(scr, "phase")?.ToString(); float pan = F(scr, "pan") is float pf ? pf : 0;
                var aimed = F(scr, "aimed") as List<Vector2Int>; var match = F(scr, "match") as MatchState;
                if (ph == "Aim" && pan < 0.01f && aimed != null && aimed.Count == 0 && match != null)
                {
                    round++;
                    if (abandonAtRound > 0 && round == abandonAtRound)
                    {
                        yield return Back(); yield return Settle(0.5f); yield return Shot(tag + "_pause");
                        yield return TapBtn("ABANDON", 0.3f); yield return Shot(tag + "_abandon_confirm"); yield return Tap(122 + 54, 232 + 17);
                        yield return Settle(1.5f); yield return Shot(tag + "_abandoned"); yield break;
                    }
                    var known = match.them.waters.KnownGrid(); var fired = new HashSet<int>(); var hits = new HashSet<int>(); var sunk = new HashSet<int>();
                    for (int r = 0; r < 5; r++) for (int c = 0; c < 5; c++) { var k = known[c, r]; int sq = r * 5 + c; if (k != CellKnown.Unknown) fired.Add(sq); if (k == CellKnown.Hit) hits.Add(sq); if (k == CellKnown.Sunk) { hits.Add(sq); sunk.Add(sq); } }
                    var picks = PickTargets(fired, hits, sunk, match.ShotsYou);
                    foreach (var sq in picks) { yield return Tap(TileCenter(sq).x, TileCenter(sq).y); yield return new WaitForSecondsRealtime(0.08f); }
                    if (round == 1) yield return Shot(tag + "_aimed_round1");
                    yield return TapBtn("FIRE!", 0.05f);
                }
                if (ph == "Between") { if (round <= 1) yield return ShotOnce(tag + "_report_round1"); yield return TapBtn("RELOAD", 0.2f); }
                yield return null; t += Time.unscaledDeltaTime;
            }
            yield return Settle(1.6f);
            yield return Shot(tag + "_results");
        }
    }
}
