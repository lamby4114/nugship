using System;
using System.Collections.Generic;
using OreSeas.Game;
using OreSeas.Net;

namespace OreSeas.Services
{
    /// <summary>Server profile (GET /api/players/:id) - "global.all" stat block plus Elo rating.</summary>
    public sealed class ProfileStats
    {
        public string id, name; public int rating = 1000, rank;
        public int games, wins, losses, draws, battles, volleys, shots, hits, sunk, crits, kraken; public double wl, accuracy, ore;
        public int weekGames, weekWins;
    }

    /// <summary>XP + naval rank derived from stats (client-side; the server ranks by Elo/wins).</summary>
    public static class PlayerRank
    {
        static readonly string[] Names = { "DECKHAND", "POWDER MONKEY", "BOSUN", "FIRST MATE", "CAPTAIN", "COMMODORE", "ADMIRAL" };
        static readonly int[] Need = { 0, 300, 900, 2000, 4000, 7500, 12000 };
        public static int Xp(int games, int wins, int sunk, int crits) => games * 25 + wins * 100 + sunk * 30 + crits * 50;
        public static int Level(int xp) { int l = 0; for (int i = 0; i < Need.Length; i++) if (xp >= Need[i]) l = i; return l; }
        public static string Name(int xp) => Names[Level(xp)];
        public static int NextAt(int xp) { int l = Level(xp); return l + 1 < Need.Length ? Need[l + 1] : -1; }
    }

    /// <summary>
    /// Server-backed boards (REST /api/leaderboard, /api/players/:id). Your own match history still records locally
    /// (offline play, captain's log); GLOBAL / WEEKLY come from the server and fall back to the local row when offline.
    /// </summary>
    public sealed class ServerLeaderboardService : ILeaderboardService
    {
        readonly LocalLeaderboardService local;
        public ServerLeaderboardService(LocalLeaderboardService local) { this.local = local; }

        public bool Online => ServerClient.I != null && ServerClient.I.Ready;
        public PlayerStats Mine => local.Mine;
        public void Record(MatchSummary m) => local.Record(m);

        public void Fetch(LeaderboardScope scope, Action<List<LeaderboardRow>, string> done)
        {
            var net = ServerClient.I;
            if (net == null || !ServerConfig.OnlineEnabled) { local.Fetch(scope, done); return; }
            net.EnsureConnected(ok =>
            {
                // REST works without the socket too; try it either way
                string q = "/api/leaderboard?period=" + (scope == LeaderboardScope.Weekly ? "week" : "all") + "&mode=all&sort=rating&limit=12&minGames=1";
                net.GetJson(q, (d, err) =>
                {
                    if (d == null) { local.Fetch(scope, (rows, _) => done?.Invoke(rows, "SERVER OFFLINE - LOCAL RECORD ONLY")); return; }
                    var list = new List<LeaderboardRow>();
                    foreach (var e in J.Objs(d, "entries")) list.Add(Row(e, net.PlayerId));
                    done?.Invoke(list, list.Count == 0 ? "NO RANKED CAPTAINS YET" : null);
                });
            });
        }

        static LeaderboardRow Row(Dictionary<string, object> e, string me) => new LeaderboardRow
        {
            rank = J.Int(e, "rank"), id = J.Str(e, "id"), name = J.Str(e, "name", "?"), avatar = J.Str(e, "avatar"), rating = J.Int(e, "rating", 1000),
            games = J.Int(e, "games"), wins = J.Int(e, "wins"), losses = J.Int(e, "losses"), draws = J.Int(e, "draws"), battles = J.Int(e, "volleys"), // BATTLES = volleys fought (server "battles" mirrors games)
            wl = (float)J.Num(e, "wl"), accuracy = (float)J.Num(e, "accuracy"), shipsSunk = J.Int(e, "sunk"), crits = J.Int(e, "crits"), ore = J.Num(e, "ore"),
            isYou = !string.IsNullOrEmpty(me) && J.Str(e, "id") == me,
            ai = J.Bool(e, "ai")   // scripted / demo accounts: tagged AI on the board
        };

        /// <summary>Your server profile (needs a welcome'd session for the player id).</summary>
        public void FetchProfile(Action<ProfileStats, string> done)
        {
            var net = ServerClient.I;
            if (net == null || !ServerConfig.OnlineEnabled) { done?.Invoke(null, "OFFLINE"); return; }
            net.EnsureConnected(ok =>
            {
                if (!ok || string.IsNullOrEmpty(net.PlayerId)) { done?.Invoke(null, "SERVER OFFLINE"); return; }
                net.GetJson("/api/players/" + UnityEngine.Networking.UnityWebRequest.EscapeURL(net.PlayerId), (d, err) =>
                {
                    if (d == null) { done?.Invoke(null, err ?? "NO PROFILE"); return; }
                    var all = J.Obj(J.Obj(d, "global"), "all"); var wk = J.Obj(J.Obj(d, "weekly"), "all");
                    done?.Invoke(new ProfileStats
                    {
                        id = J.Str(d, "id"), name = J.Str(d, "name"), rating = J.Int(d, "rating", 1000),
                        games = J.Int(all, "games"), wins = J.Int(all, "wins"), losses = J.Int(all, "losses"), draws = J.Int(all, "draws"),
                        battles = J.Int(all, "volleys"), volleys = J.Int(all, "volleys"), shots = J.Int(all, "shots"), hits = J.Int(all, "hits"),
                        sunk = J.Int(all, "sunk"), crits = J.Int(all, "crits"), kraken = J.Int(all, "kraken"), wl = J.Num(all, "wl"),
                        accuracy = J.Num(all, "accuracy"), ore = J.Num(all, "ore"), weekGames = J.Int(wk, "games"), weekWins = J.Int(wk, "wins")
                    }, null);
                });
            });
        }
    }
}
