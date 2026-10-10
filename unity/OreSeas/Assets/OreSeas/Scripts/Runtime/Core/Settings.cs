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
        public static int StakeIndex { get => PlayerPrefs.GetInt("os.stake", 1); set { PlayerPrefs.SetInt("os.stake", value); PlayerPrefs.Save(); } }
        public static readonly double[] Stakes = { 0.001, 0.005, 0.01 };
        public static double Stake => Stakes[Mathf.Clamp(StakeIndex, 0, Stakes.Length - 1)];
    }
}
