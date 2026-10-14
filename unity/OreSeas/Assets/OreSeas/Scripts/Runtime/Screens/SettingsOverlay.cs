using System;
using System.Collections.Generic;
using OreSeas.Game;
using OreSeas.Net;
using OreSeas.Services;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>Settings: music / sfx / vibration / reduce flashes / ORE clock speed / demo luck.</summary>
    public static class SettingsOverlay
    {
        static TouchScreenKeyboard kb;

        public static void Open()
        {
            var root = Gfx.Node(GameApp.OverlayRoot, "Settings");
            int prevLayer = UiButton.TopLayer;
            UiButton.TopLayer = 1;
            var dim = Gfx.Rect(root, 0, 0, Px.W, Px.H, Pal.Ink, 300); dim.color = new Color(0.06f, 0.04f, 0.04f, 0.75f);
            UiButton.Create(root, 0, 0, Px.W, Px.H, null, 1, "blocker").sfx = null;
            Gfx.Sprite(root, "parch_216x300", 12, 104, 301); Ui.Nails(root, 12, 104, 216, 300, 302);
            Ui.Ribbon(root, "ribbon_red", 120, 94, "SETTINGS", 303);
            Row(root, 0, "MUSIC", () => Settings.Music, v => Settings.Music = v, "ON", "OFF");
            Row(root, 1, "SOUND FX", () => Settings.Sfx, v => Settings.Sfx = v, "ON", "OFF");
            Row(root, 2, "VIBRATION", () => Settings.Vibrate, v => Settings.Vibrate = v, "ON", "OFF");
            Row(root, 3, "REDUCE FLASHES", () => Settings.ReduceFlashes, v => Settings.ReduceFlashes = v, "ON", "OFF");
            Row(root, 4, "ORE CLOCK", () => Settings.RealClock, v => Settings.RealClock = v, "REAL 77S", "FAST SIM");
            if (LiveMining.Built)
                Row(root, 5, "LIVE MINING (SOL)", () => Settings.LiveMining && Settings.LiveConsent, v =>
                {
                    if (!v) { Settings.LiveMining = false; GameApp.Net?.Disconnect(); return; }
                    LiveConsentSheet.Open(() => { Settings.LiveMining = true; GameApp.Net?.Disconnect(); });   // reconnect signs in with the wallet
                }, "ON", "OFF");
            else Row(root, 5, "DEMO LUCK", () => Settings.DemoLuck, v => Settings.DemoLuck = v, "KRAKEN 1/25", "REAL 1/500");
            Row(root, 6, "SIM RIVAL ROUNDS", () => Settings.LiveRounds, v => Settings.LiveRounds = v, "LIVE ORE", "SIMULATED");
            Row(root, 7, "ONLINE PLAY", () => ServerConfig.OnlineEnabled, v => { ServerConfig.OnlineEnabled = v; if (!v) GameApp.Net?.Disconnect(); }, "ON", "OFF");
            // server URL (adb reverse tcp:8787 tcp:8787 -> default ws://127.0.0.1:8787/ws reaches the PC)
            PixelText.Create(root, "SERVER", 26, 284, TextStyle.Of(Pal.MapD), 303);
            var url = PixelText.Create(root, Fit(ServerConfig.ServerUrl), 214, 284, TextStyle.Of(Pal.Red1, TextAlign.Right), 303);
            var netSt = PixelText.Create(root, NetLine(), 120, 297, TextStyle.Of(Pal.MapB, TextAlign.Center), 303);
            UiButton.Create(root, 18, 280, 204, 16, () =>
            {
                if (TouchScreenKeyboard.isSupported) kb = TouchScreenKeyboard.Open(ServerConfig.ServerUrl, TouchScreenKeyboardType.URL, false, false, false, false, "ws://host:8787/ws");
                else { var clip = GUIUtility.systemCopyBuffer; if (!string.IsNullOrEmpty(clip) && clip.StartsWith("ws")) { ServerConfig.ServerUrl = clip; url.Set(Fit(clip)); GameApp.Net?.Disconnect(); } else GameApp.Toast("COPY A ws:// URL, THEN TAP"); }
            }, 1, "row_server").sfx = "tap";
            var watcher = root.gameObject.AddComponent<KeyboardWatch>();
            watcher.onDone = text => { if (!string.IsNullOrWhiteSpace(text)) { ServerConfig.ServerUrl = text.Trim(); url.Set(Fit(ServerConfig.ServerUrl)); GameApp.Net?.Disconnect(); netSt.Set(NetLine()); } };
            watcher.poll = () => { var k = kb; if (k == null || k.status == TouchScreenKeyboard.Status.Visible) return null; kb = null; return k.status == TouchScreenKeyboard.Status.Done ? k.text : ""; };
            PixelText liveLine = null;
            if (LiveMining.Built)
            {
                Ui.Plank(root, 18, 310, 100, 18, "CAP " + Settings.LiveCapSol.ToString("0.00"), null, () =>
                {
                    Settings.LiveCapIndex = (Settings.LiveCapIndex + 1) % Settings.LiveCaps.Length;
                    liveLine?.Set("MAX " + Settings.LiveCapSol.ToString("0.00") + " SOL PER MATCH");
                }, 310, 1);
                Ui.Plank(root, 122, 310, 100, 18, "CLAIM", null, async () =>
                {
                    liveLine?.Set("APPROVE CLAIM IN WALLET");
                    var r = await LiveMining.ClaimAll();
                    if (liveLine != null) liveLine.Set(r.ok ? "CLAIM SENT" : r.message);
                }, 310, 1);
                liveLine = PixelText.Create(root, Settings.LiveMining && Settings.LiveConsent ? "LIVE MINING ON - YOU SIGN EVERY DEPLOY" : "LIVE MINING OFF - NO SOL, NOTHING SIGNED", 120, 336, TextStyle.Of(Pal.MapB, TextAlign.Center), 303);
            }
            else
            {
                Ui.Plank(root, 18, 310, 100, 18, "RESET URL", null, () => { ServerConfig.ServerUrl = ""; url.Set(Fit(ServerConfig.ServerUrl)); GameApp.Net?.Disconnect(); netSt.Set(NetLine()); }, 310, 1);
                Ui.Plank(root, 122, 310, 100, 18, "TEST", null, () => { netSt.Set("CONNECTING..."); GameApp.Net?.EnsureConnected(ok => { if (netSt != null) netSt.Set(NetLine()); }); }, 310, 1);
                PixelText.Create(root, "LIVE ORE OFF - NO SOL, NOTHING SIGNED", 120, 336, TextStyle.Of(Pal.MapB, TextAlign.Center), 303);
            }
            Action close = null;
            close = () => { Lay.PopBack(close); UiButton.TopLayer = prevLayer; kb = null; if (root) UnityEngine.Object.Destroy(root.gameObject); };
            Lay.PushBack(close);   // Android back closes the overlay instead of leaving the screen underneath
            Ui.Small(root, 66, 356, 108, 34, "CLOSE", close, 310, 1, "red");
        }

        static string Fit(string s) { s = s.Replace("ws://", "").Replace("wss://", "s:"); return s.Length > 26 ? s.Substring(0, 26) : s; }
        static string NetLine() { var n = GameApp.Net; return n == null ? "" : n.Status + (n.Ready && n.PlayerId != null ? " - " + n.PlayerId.ToUpperInvariant() : ""); }   // same upper-case id as the profile card

        /// <summary>Polls the OS keyboard while the overlay is open.</summary>
        sealed class KeyboardWatch : MonoBehaviour
        {
            public Func<string> poll; public Action<string> onDone;
            void Update() { var r = poll?.Invoke(); if (r != null) onDone?.Invoke(r); }
        }

        static void Row(Transform root, int i, string label, Func<bool> get, Action<bool> set, string on, string off)
        {
            float y = 114 + i * 20;
            PixelText.Create(root, label, 20, y + 3, TextStyle.Of(Pal.MapD), 303);
            var val = PixelText.Create(root, get() ? on : off, 182, y + 3, TextStyle.Of(get() ? Pal.Grass4 : Pal.Red1, TextAlign.Right), 303);
            var lever = Gfx.Sprite(root, get() ? "lever_on" : "lever_off", 190, y + 2, 303);
            UiButton.Create(root, 14, y - 1, 212, 19, () =>
            {
                bool v = !get(); set(v); v = get();   // a setter may refuse (live mining waits for the consent sheet)
                val.Set(v ? on : off); val.SetColor(v ? Pal.Grass4 : Pal.Red1);
                lever.sprite = Atlas.Get(v ? "lever_on" : "lever_off");
                if (label == "MUSIC") GameApp.Audio.MusicVolume = GameApp.Audio.MusicVolume; // applied next frame via Settings.Music
            }, 1, "row_" + label).sfx = "lock";
        }
    }
}
