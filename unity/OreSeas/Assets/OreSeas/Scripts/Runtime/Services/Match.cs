using System;
using System.Collections.Generic;
using UnityEngine;

namespace OreSeas.Services
{
    public sealed class MatchTicket
    {
        public string matchId, rivalName, rivalId; public bool simulated; public int seed; public double stakeSol;
    }

    /// <summary>Opponent's volley as relayed by the server for a given ORE round.</summary>
    public sealed class RemoteVolley { public string matchId; public ulong roundId; public List<int> squares = new List<int>(); }

    /// <summary>
    /// PvP seam (server-authoritative matchmaking + relay). Flow: FindMatch -> SubmitCommit(sha256(layout||salt)) ->
    /// per ORE round SubmitVolley/OpponentVolley -> RevealFleet at the end (both sides verify the commits).
    /// </summary>
    public interface IMatchService
    {
        bool Online { get; }
        string Status { get; }
        void FindMatch(double stakeSol, Action<MatchTicket> onFound, Action<string> onUnavailable);
        void Cancel();
        void SubmitCommit(string matchId, string commitHex);
        void SubmitVolley(string matchId, ulong roundId, IList<int> squares);
        void RevealFleet(string matchId, string layout, string saltHex);
        event Action<RemoteVolley> OpponentVolley;
    }

    /// <summary>
    /// Offline stand-in: there is no PvP server yet, so matchmaking reports OFFLINE after a short search and the UI offers a
    /// clearly-labelled SIMULATED rival (an AI captain on a real-time simulated ORE clock). No fake human players.
    /// </summary>
    public sealed class LocalSimMatchService : IMatchService
    {
        public bool Online => false;
        public string Status => "PVP SERVER OFFLINE";
        public event Action<RemoteVolley> OpponentVolley { add { } remove { } }
        float searchUntil = -1; Action<string> pendingFail;
        public static readonly string[] SimNames = { "SIM: SALTY SAL", "SIM: ONE-EYED JO", "SIM: CAPT. KIPPER", "SIM: RED REEF", "SIM: DREDGE" };

        public void FindMatch(double stakeSol, Action<MatchTicket> onFound, Action<string> onUnavailable)
        {
            searchUntil = Time.unscaledTime + 3.2f; pendingFail = onUnavailable;
        }

        public MatchTicket CreateSimTicket(double stake)
        {
            int seed = UnityEngine.Random.Range(1, int.MaxValue);
            return new MatchTicket { matchId = "sim-" + seed.ToString("x"), rivalName = SimNames[seed % SimNames.Length], rivalId = "sim", simulated = true, seed = seed, stakeSol = stake };
        }

        /// <summary>Pump from the matchmaking screen.</summary>
        public void Tick()
        {
            if (searchUntil > 0 && Time.unscaledTime >= searchUntil) { searchUntil = -1; var f = pendingFail; pendingFail = null; f?.Invoke(Status); }
        }

        public void Cancel() { searchUntil = -1; pendingFail = null; }
        public void SubmitCommit(string matchId, string commitHex) { }
        public void SubmitVolley(string matchId, ulong roundId, IList<int> squares) { }
        public void RevealFleet(string matchId, string layout, string saltHex) { }
    }
}
