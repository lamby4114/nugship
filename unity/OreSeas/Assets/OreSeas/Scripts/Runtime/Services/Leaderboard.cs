using System;
using System.Collections.Generic;
using OreSeas.Game;

namespace OreSeas.Services
{
    public enum LeaderboardScope { Global, Weekly }

    [Serializable]
    public sealed class LeaderboardRow { public int rank; public string id, name, avatar; public int games, wins, losses, draws, battles, rating = 1000; public float wl, accuracy; public int shipsSunk, crits; public double ore; public bool isYou, ai; }

    /// <summary>Leaderboard seam. Stats shape follows SPEC: games, wins, losses, W/L, volleys, ships sunk, accuracy, crits, ORE mined, Kraken sightings.</summary>
    public interface ILeaderboardService
    {
        bool Online { get; }
        PlayerStats Mine { get; }
        void Record(MatchSummary m);
        void Fetch(LeaderboardScope scope, Action<List<LeaderboardRow>, string> done);
    }

    /// <summary>Local-only: your stats persist on device; GLOBAL/WEEKLY show only your row and an OFFLINE note (never fabricated rivals).</summary>
    public sealed class LocalLeaderboardService : ILeaderboardService
    {
        readonly PlayerStats stats = PlayerStats.Load();
        public bool Online => false;
        public PlayerStats Mine => stats;

        public void Record(MatchSummary m)
        {
            var wk = PlayerStats.WeekOf(DateTime.UtcNow);
            if (stats.weekKey != wk) { stats.weekKey = wk; stats.weekWins = 0; stats.weekGames = 0; }
            stats.gamesPlayed++; stats.weekGames++;
            if (m.result > 0) { stats.wins++; stats.weekWins++; } else if (m.result < 0) stats.losses++; else stats.draws++;
            if (m.mode == "ranked") stats.rankedGames++;
            stats.volleys += m.volleys; stats.shotsFired += m.shotsFired; stats.hits += m.hits; stats.shipsSunk += m.shipsSunk;
            stats.crits += m.crits; stats.krakenSightings += m.krakens;
            if (m.simulated) stats.oreMinedSim += m.oreSim; else stats.oreMinedReal += m.oreSim;
            stats.Save();
        }

        public void Fetch(LeaderboardScope scope, Action<List<LeaderboardRow>, string> done)
        {
            bool weekly = scope == LeaderboardScope.Weekly;
            var me = new LeaderboardRow
            {
                rank = 0, name = Profile.IsSet ? Profile.Name : "YOU", avatar = Profile.Avatar, isYou = true,
                games = weekly ? stats.weekGames : stats.gamesPlayed, wins = weekly ? stats.weekWins : stats.wins,
                losses = weekly ? Math.Max(0, stats.weekGames - stats.weekWins) : stats.losses,
                draws = weekly ? 0 : stats.draws, battles = stats.volleys,
                wl = stats.WinLossRatio, accuracy = stats.Accuracy, shipsSunk = stats.shipsSunk, crits = stats.crits, ore = stats.oreMinedSim
            };
            done?.Invoke(new List<LeaderboardRow> { me }, "SERVER OFFLINE - LOCAL RECORD ONLY");
        }
    }
}
