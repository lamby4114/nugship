using UnityEngine;

namespace OreSeas
{
    public static class Settings
    {
        static bool B(string k, bool d) => PlayerPrefs.GetInt("os." + k, d ? 1 : 0) == 1;
        static void B(string k, bool v, bool _) { PlayerPrefs.SetInt("os." + k, v ? 1 : 0); PlayerPrefs.Save(); }

        public static bool Music { get => B("music", true); set => B("music", value, true); }
        public static bool Sfx { get => B("sfx", true); set => B("sfx", value, true); }
        public static bool Vibrate { get => B("vibrate", true); set => B("vibrate", value, true); }
        /// <summary>Simulated ORE clock speed for ranked/clock mode: false = fast demo clock, true = real 77 s rounds.</summary>
        public static bool RealClock { get => B("realclock", false); set => B("realclock", value, true); }
        /// <summary>Demo luck: Motherlode 1/25 instead of ORE's real 1/500 (simulated rounds only; for demo videos).</summary>
        public static bool DemoLuck { get => B("demoluck", false); set => B("demoluck", value, true); }
        /// <summary>Accessibility: gentler hit/crit feedback, no screen shake on normal hits, no storm screen flicker. ON by default.</summary>
        public static bool ReduceFlashes { get => B("reduceflash", true); set => B("reduceflash", value, true); }
        /// <summary>Clock-mode SIM rival follows LIVE mainnet ORE rounds (read-only RPC) instead of a simulated clock.</summary>
        public static bool LiveRounds { get => B("liverounds", false); set => B("liverounds", value, true); }
        public static bool AutoFire { get => B("autofire", true); set => B("autofire", value, true); }
        /// <summary>v1.1.0 LIVE MINING (real SOL, player-signed). OFF by default; only switched on through the consent sheet.</summary>
        public static bool LiveMining { get => B("livemining", false); set => B("livemining", value, true); }
        /// <summary>Player confirmed 18+ and accepted the risk text (stored with the consent version).</summary>
        public static bool LiveConsent { get => PlayerPrefs.GetInt("os.liveconsent", 0) == LiveConsentVersion; set { PlayerPrefs.SetInt("os.liveconsent", value ? LiveConsentVersion : 0); PlayerPrefs.Save(); } }
        public const int LiveConsentVersion = 1;
        /// <summary>Max SOL one match may deploy (player-chosen cap). Hard-limited further per square in LiveTx.</summary>
        public static int LiveCapIndex { get => PlayerPrefs.GetInt("os.livecap", 0); set { PlayerPrefs.SetInt("os.livecap", value); PlayerPrefs.Save(); } }
        public static readonly double[] LiveCaps = { 0.02, 0.05, 0.1 };
        public static double LiveCapSol => LiveCaps[Mathf.Clamp(LiveCapIndex, 0, LiveCaps.Length - 1)];
        public static int StakeIndex { get => PlayerPrefs.GetInt("os.stake", 1); set { PlayerPrefs.SetInt("os.stake", value); PlayerPrefs.Save(); } }
        public static readonly double[] Stakes = { 0.001, 0.005, 0.01 };
        public static double Stake => Stakes[Mathf.Clamp(StakeIndex, 0, Stakes.Length - 1)];
    }
}
