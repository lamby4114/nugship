using UnityEngine;

namespace OreSeas.Net
{
    /// <summary>
    /// Where the NUGSHIP server lives, plus the hard money gate.
    ///
    /// Phone testing: run the server on the PC (port 8787) and `adb reverse tcp:8787 tcp:8787`; the default
    /// ws://127.0.0.1:8787/ws then reaches the PC from the Seeker. Override in SETTINGS (stored in PlayerPrefs "os.server").
    /// </summary>
    public static class ServerConfig
    {
        /// <summary>
        /// LIVE MINING GATE (v1.1.0, approved by Victor Oct 14 2026). Compile-time: true only when the ORESEAS_LIVE_ORE
        /// define is set (BuildScript.BuildAndroidRelease sets it for v1.1.0+). Even then nothing happens until the player
        /// opts in (Settings.LiveMining, off by default, 18+ consent sheet) and approves EVERY deploy in their own wallet.
        /// Non-custodial: SOL goes from the player's wallet straight to the ORE program.
        /// </summary>
#if ORESEAS_LIVE_ORE
        public const bool LIVE_ORE = true;
#else
        public const bool LIVE_ORE = false;
#endif

        /// <summary>
        /// HARD GATE for Sign-In-With-Solana wallet pairing (profile &lt;-&gt; wallet). While false the client never asks the
        /// wallet to sign a pairing message; profiles stay paired with the guest id only. Needs Victor's OK to flip.
        /// </summary>
#if ORESEAS_LIVE_ORE
        public const bool SIWS_PAIRING = true;    // live builds sign in with the wallet (SIWS) so nobody can claim another wallet's deploys
#else
        public const bool SIWS_PAIRING = false;
#endif
        /// <summary>MWA dApp identity shown in the wallet approval sheet (must be the listed website).</summary>
        public const string DappIdentityUri = "https://lamby4114.github.io/nugship/";
        public const string DappIconPath = "favicon.png";
        public const string SiwsDomain = "lamby4114.github.io";

        /// <summary>Dev builds: the PC server through `adb reverse tcp:8787 tcp:8787` (cleartext, development builds only).</summary>
        public const string DevServer = "ws://127.0.0.1:8787/ws";
        /// <summary>Store / submission builds (BuildScript.BuildAndroidRelease sets NUGSHIP_RELEASE): hosted server, TLS only.</summary>
        public const string ReleaseServer = "wss://nugship-server.fly.dev/ws";
#if NUGSHIP_RELEASE
        public const bool ReleaseBuild = true;
        public const string DefaultServer = ReleaseServer;
#else
        public const bool ReleaseBuild = false;
        public const string DefaultServer = DevServer;
#endif
        /// <summary>Read-only JSON-RPC for live ORE round display (getSlot / getAccountInfo only). Public endpoint, rate limited.</summary>
        public const string DefaultRpc = "https://api.mainnet-beta.solana.com";

        /// <summary>Dev play-test override; null in normal play.</summary>
        internal static string DevUrl;

        public static string ServerUrl
        {
            get
            {
                if (DevUrl != null) return DevUrl;   // play-test harness only (simulates an unreachable server)
                var cli = CommandLine("-nugserver"); if (!string.IsNullOrEmpty(cli)) return cli; // desktop screenshot player only
                var s = PlayerPrefs.GetString("os.server", "").Trim();
                // release builds forbid cleartext (insecureHttpOption NotAllowed): a stale ws:// override from a dev build is ignored
#pragma warning disable CS0162
                if (ReleaseBuild && !s.StartsWith("wss://")) s = "";
#pragma warning restore CS0162
                return s.Length == 0 ? DefaultServer : s;
            }
            set { PlayerPrefs.SetString("os.server", value ?? ""); PlayerPrefs.Save(); }
        }

        public static string RpcUrl
        {
            get { var s = PlayerPrefs.GetString("os.rpc", ""); return string.IsNullOrWhiteSpace(s) ? DefaultRpc : s.Trim(); }
            set { PlayerPrefs.SetString("os.rpc", value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>Try the server at all (OFF = fully offline build, local AI + sim rival only).</summary>
        public static bool OnlineEnabled { get => PlayerPrefs.GetInt("os.online", 1) == 1; set { PlayerPrefs.SetInt("os.online", value ? 1 : 0); PlayerPrefs.Save(); } }

        /// <summary>http(s)://host:port for REST, derived from the ws(s) URL ("/ws" path stripped).</summary>
        public static string HttpBase
        {
            get
            {
                var u = ServerUrl;
                if (u.StartsWith("wss://")) u = "https://" + u.Substring(6);
                else if (u.StartsWith("ws://")) u = "http://" + u.Substring(5);
                int q = u.IndexOf("/", u.IndexOf("//") + 2);
                return q > 0 ? u.Substring(0, q) : u;
            }
        }

        public static string PlayerName
        {
            get => PlayerPrefs.GetString("os.name", "Captain");
            set { PlayerPrefs.SetString("os.name", value ?? "Captain"); PlayerPrefs.Save(); }
        }

        /// <summary>Value after a command-line flag (desktop builds; Android has no command line, so always null there).</summary>
        public static string CommandLine(string flag)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == flag) return a[i + 1];
            return null;
        }
        public static bool HasFlag(string flag) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), flag) >= 0;

        /// <summary>Lamports per shot for the selected (virtual) stake; server validates against its stake options.</summary>
        public static string StakeLamports => ((long)System.Math.Round(Settings.Stake * 1_000_000_000d)).ToString();
    }
}
