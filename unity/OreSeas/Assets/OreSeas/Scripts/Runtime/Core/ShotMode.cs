using System;
using System.Collections;
using System.IO;
using OreSeas.Net;
using OreSeas.Screens;
using UnityEngine;

namespace OreSeas
{
    /// <summary>
    /// Desktop screenshot player (never active on the phone: needs the -nugshot command-line flag).
    ///   Nugship.exe -nugshot C:\out [-nugserver ws://127.0.0.1:8788/ws] [-nugshotimg photo.png] [-legacycenter]
    /// Renders the 240x534 logical canvas into a RenderTexture (pixel perfect camera off), walks the screens, writes
    /// one PNG per screen plus center_audit.txt, then quits. -legacycenter = old centring math for "before" shots.
    /// </summary>
    public sealed class ShotMode : MonoBehaviour
    {
        string outDir; RenderTexture rt; Camera cam; StreamWriter log;

        void Start()
        {
            Application.runInBackground = true;   // keep rendering when the window isn't focused
            outDir = ServerConfig.CommandLine("-nugshot") ?? Path.Combine(Application.persistentDataPath, "shots");
            Directory.CreateDirectory(outDir);
            log = new StreamWriter(Path.Combine(outDir, "shots_log.txt"), false) { AutoFlush = true };
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception || t == LogType.Error) log?.WriteLine("[" + t + "] " + c + "\n" + st); };
            cam = Px.Cam;
            foreach (var b in cam.GetComponents<Behaviour>()) if (b.GetType().Name == "PixelPerfectCamera") b.enabled = false;
            rt = new RenderTexture(Px.W, Px.H, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, antiAliasing = 1 };
            rt.Create();
            cam.targetTexture = rt; cam.rect = new Rect(0, 0, 1, 1); cam.orthographicSize = Px.H / 2f;
            StartCoroutine(Run());
            // heartbeat from a worker thread: tells a hung main thread apart from a slow step
            hb = new System.Threading.Timer(_ => { try { log?.WriteLine("hb frame " + frame + " step " + step); } catch { } }, null, 10000, 10000);
        }

        volatile int frame; volatile string step = "start"; System.Threading.Timer hb;
        void Update() { frame = Time.frameCount; }
        void OnDestroy() { hb?.Dispose(); }

        IEnumerator Settle(float secs = 1.2f)
        {
            float t = 0; while (t < 8 && GameApp.Transitioning) { t += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSecondsRealtime(secs);
        }

        IEnumerator Shot(string name)
        {
            step = "shot " + name;
            yield return new WaitForEndOfFrame();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(Px.W, Px.H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Px.W, Px.H), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            log.WriteLine("shot " + name);
            // audit whatever is on screen right now
            var issues = CenterAudit.Run(name);
            yield return null;
        }

        IEnumerator Go<T>(Action<T> init = null, float secs = 1.4f) where T : ScreenBase
        {
            GameApp.Go<T>(init);
            yield return null;
            yield return Settle(secs);
        }

        IEnumerator Run()
        {
            bool legacy = PixelFont.LegacyCenter;
            yield return Settle(2.5f);
            yield return Shot("title");

            if (!legacy)
            {
                for (int p = 0; p < HelpOverlay.CardCount; p++)
                {
                    HelpOverlay.Open(p); yield return Settle(0.5f);
                    yield return Shot("help_" + (p + 1));
                    Lay.HandleBack(); yield return null;
                }

                yield return Go<ProfileScreen>(null, 2.5f);
                yield return Shot("profile_empty");
                var ps = GameApp.Current as ProfileScreen;
                if (ps != null)
                {
                    ps.SetName("Salty Nugget"); yield return Settle(1.5f);
                    yield return Shot("profile_named");
                    var img = ServerConfig.CommandLine("-nugshotimg");
                    var photo = img != null ? AvatarPicker.Load(img) : null;
                    if (photo != null) { ps.UsePhoto(photo); yield return Settle(0.6f); yield return Shot("profile_upload"); }
                    ps.SendMessage("Save", SendMessageOptions.DontRequireReceiver);
                    yield return Settle(2.5f);
                    yield return Shot("profile_saved");
                    // a name someone else already claimed (seeded on the test server)
                    ps.SetName("Barnacle Bill"); yield return Settle(1.5f);
                    yield return Shot("profile_taken");
                }
                yield return Go<TitleScreen>(null, 2.0f);
                yield return Shot("title_profile");
            }

            yield return Go<LeaderboardScreen>(null, 1.0f);
            yield return Shot("leaderboard_log");
            (GameApp.Current as LeaderboardScreen)?.ShowTab(1); yield return Settle(2.0f);
            yield return Shot("leaderboard_global");

            yield return Go<CampaignScreen>(null, 1.5f);
            yield return Shot("campaign");

            yield return Go<MatchmakingScreen>(null, 2.0f);
            yield return Shot("matchmaking");

            yield return Go<TitleScreen>(null, 1.0f);
            SettingsOverlay.Open(); yield return Settle(0.6f);
            yield return Shot("settings");

            GameApp.Setup = new MatchSetup { mode = "solo", rival = Game.Captains.Campaign[0], biome = Game.Biomes.Get(Game.Captains.Campaign[0].biome), campaignIndex = 0, seed = 7 };
            yield return Go<FleetScreen>(null, 1.5f);
            yield return Shot("fleet");

            GameApp.Setup = new MatchSetup { mode = "solo", seed = 7, campaignIndex = 0 };
            yield return Go<BattleScreen>(null, 5.0f);
            yield return Shot("battle");
            GameApp.Toast("FLEET SEALED #3A9F0C21"); yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("battle_toast");
            var bs = GameApp.Current as BattleScreen;
            if (bs != null && !legacy)
            {
                bs.Back(); yield return Settle(0.6f);
                yield return Shot("pause");
                HelpOverlay.Open(4); yield return Settle(0.5f);
                yield return Shot("pause_help");
                Lay.HandleBack(); Lay.HandleBack(); yield return Settle(0.4f);
            }
            if (bs != null)
            {
                step = "debug end";
                try { bs.DebugEnd(1); } catch (Exception e) { log.WriteLine("DebugEnd failed: " + e); }
                step = "results wait";
                yield return Settle(6.0f);
                yield return Shot("results");
            }

            File.WriteAllText(Path.Combine(outDir, "center_audit.txt"), string.Join("\n", CenterAudit.Log) + "\n\nTOTAL " + CenterAudit.TotalChecks + " checks, " + CenterAudit.TotalIssues + " issues\n");
            log.WriteLine("done");
            hb?.Dispose(); hb = null;
            log.Close(); log = null;
            Application.Quit();
        }
    }
}
