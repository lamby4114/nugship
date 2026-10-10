using System;
using UnityEngine;

namespace OreSeas.Game
{
    /// <summary>Lifetime stats (spec leaderboard fields). Stored locally; a server reports the same shape.</summary>
    [Serializable]
    public sealed class PlayerStats
    {
        public int gamesPlayed, wins, losses, draws, volleys, shotsFired, hits, shipsSunk, crits, krakenSightings, rankedGames;
        public double oreMinedSim, oreMinedReal;
        public int campaignBeaten; // islands beaten (0..5)
        public string dailyDate; public int dailyBestRounds;
        public string weekKey; public int weekWins, weekGames;

        public float WinLossRatio => losses == 0 ? wins : wins / (float)losses;
        public float Accuracy => shotsFired == 0 ? 0 : hits / (float)shotsFired;

        const string Key = "os.stats.v1";
        public static PlayerStats Load()
        {
            var j = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(j)) return new PlayerStats();
            try { return JsonUtility.FromJson<PlayerStats>(j) ?? new PlayerStats(); } catch { return new PlayerStats(); }
        }
        public void Save() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }

        public static string WeekOf(DateTime d) { var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar; return d.Year + "-W" + cal.GetWeekOfYear(d, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday); }
    }

    [Serializable]
    public sealed class MatchSummary
    {
        public string mode; // solo|daily|ranked
        public string opponent; public int result; // 1 win, 0 draw, -1 loss
        public int rounds, volleys, shotsFired, hits, shipsSunk, crits, krakens;
        public double oreSim; public bool simulated = true;
        public string yourCommit, theirCommit; public bool commitsVerified;
        /// <summary>Server end reason (fleet_sunk, resign, disconnect, inactive, turn_limit, mutual_destruction); null for local matches.</summary>
        [System.NonSerialized] public string reason;
    }
}
