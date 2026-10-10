using System;
using UnityEngine;

namespace OreSeas.Ore
{
    /// <summary>Facts measured from mainnet ORE (see /timing/FINDINGS.md, regolith-labs/ore @48c203b).</summary>
    public static class OreConstants
    {
        public const int Squares = 25;              // 5x5 board, A1..E5 == ORE squares 0..24 (index = row*5+col)
        public const int RoundSlots = 240;          // deploy window
        public const int IntermissionSlots = 48;    // end_slot -> reset (winning square known ~end_slot+48)
        public const float SlotSeconds = 0.268f;    // measured 0.266-0.276 s
        public const int MotherlodeOdds = 500;      // rng.reverse_bits() % 500 == 0
        public const int LockMarginAutoFire = 20;   // targets lock at end_slot-20 when the session key fires
        public const int LockMarginPerShot = 60;    // end_slot-60 when every shot needs a Seed Vault approval
        public const double FeeLosingSquare = 0.109; // 1% admin + 10% of remainder
        public const double FeeWinningSquare = 0.01;
        public const double TypicalSolPerSquare = 0.42; // ~10.5 SOL per round / 25 squares (sampled rounds)
        public const double OrePerRound = 1.0;           // ORE minted to the winning square per round (sim assumption)
        public const double SimMotherlodePot = 14.6;     // example pot used by the mockups (sim only)

        public static string SquareName(int idx) => "ABCDE"[idx % 5].ToString() + (idx / 5 + 1);
        public static int Index(int c, int r) => r * 5 + c;
    }

    public enum OrePhase { Deploying, Intermission }

    [Serializable]
    public struct OreRoundInfo
    {
        public ulong roundId, startSlot, endSlot;
        public double currentSlot;
        public OrePhase phase;
        public double SlotsLeft => Math.Max(0, endSlot - currentSlot);
    }

    [Serializable]
    public struct OreRoundResult
    {
        public ulong roundId;
        public int winningSquare;   // 0..24
        public bool motherlode;
        public double totalDeployedSol;
        public bool simulated;
    }

    /// <summary>
    /// Source of ORE rounds. The game never talks to ORE directly: a battle turn is one ORE round.
    /// SimulatedOreRoundSource powers offline/solo play. A chain-backed source (RPC reads of the ORE Board/Round
    /// accounts, slot-driven countdown) plugs in here later; it must never deploy or sign anything by itself.
    /// </summary>
    public interface IOreRoundSource
    {
        bool IsSimulated { get; }
        /// <summary>True when the round closes as soon as both volleys are locked (fast solo turns, no ORE wait).</summary>
        bool PlayerPaced { get; }
        OreRoundInfo Current { get; }
        float SlotSeconds { get; }
        event Action<OreRoundInfo> RoundOpened;
        event Action<OreRoundResult> RoundSettled;
        void Tick(float dt);
        /// <summary>Player-paced only: close the current round now (both captains have locked their volleys).</summary>
        void CloseRoundNow();
        float SecondsUntilLock(int marginSlots);
    }

    /// <summary>Offline ORE: random winning square each round, rare Motherlode. No chain, no SOL.</summary>
    public sealed class SimulatedOreRoundSource : IOreRoundSource
    {
        readonly System.Random rng;
        readonly float slotSeconds;
        readonly int motherlodeOdds;
        readonly bool playerPaced;
        readonly float pacedSettleSeconds;
        OreRoundInfo cur;
        float intermissionLeft = -1;
        public bool IsSimulated => true;
        public bool PlayerPaced => playerPaced;
        public OreRoundInfo Current => cur;
        public float SlotSeconds => slotSeconds;
        public event Action<OreRoundInfo> RoundOpened;
        public event Action<OreRoundResult> RoundSettled;
        public int? ForceNextWinningSquare; public bool ForceNextMotherlode; // debug/demo hooks

        public SimulatedOreRoundSource(bool playerPaced, float slotSeconds = OreConstants.SlotSeconds, int motherlodeOdds = OreConstants.MotherlodeOdds, int seed = 0, float pacedSettleSeconds = 1.1f)
        {
            this.playerPaced = playerPaced; this.slotSeconds = slotSeconds; this.motherlodeOdds = Math.Max(1, motherlodeOdds);
            this.pacedSettleSeconds = pacedSettleSeconds;
            rng = seed == 0 ? new System.Random() : new System.Random(seed);
            ulong start = 345_000_000UL + (ulong)rng.Next(0, 100000);
            cur = new OreRoundInfo { roundId = 431_892UL + (ulong)rng.Next(0, 5000), startSlot = start, endSlot = start + OreConstants.RoundSlots, currentSlot = start, phase = OrePhase.Deploying };
        }

        public void Start() { RoundOpened?.Invoke(cur); }

        public float SecondsUntilLock(int marginSlots)
        {
            if (playerPaced || cur.phase != OrePhase.Deploying) return float.PositiveInfinity;
            return (float)Math.Max(0, (cur.endSlot - (double)marginSlots - cur.currentSlot) * slotSeconds);
        }

        public void CloseRoundNow()
        {
            if (cur.phase != OrePhase.Deploying) return;
            cur.endSlot = (ulong)Math.Ceiling(cur.currentSlot);
            BeginIntermission();
        }

        void BeginIntermission()
        {
            cur.phase = OrePhase.Intermission;
            intermissionLeft = playerPaced ? pacedSettleSeconds : OreConstants.IntermissionSlots * slotSeconds;
        }

        public void Tick(float dt)
        {
            if (cur.phase == OrePhase.Deploying)
            {
                if (playerPaced) return; // waits for CloseRoundNow
                cur.currentSlot += dt / slotSeconds;
                if (cur.currentSlot >= cur.endSlot) BeginIntermission();
                return;
            }
            cur.currentSlot += dt / slotSeconds;
            intermissionLeft -= dt;
            if (intermissionLeft > 0) return;
            var res = new OreRoundResult
            {
                roundId = cur.roundId,
                winningSquare = ForceNextWinningSquare ?? rng.Next(0, OreConstants.Squares),
                motherlode = ForceNextMotherlode || rng.Next(0, motherlodeOdds) == 0,
                totalDeployedSol = 10.0 + rng.NextDouble() * 0.9,
                simulated = true
            };
            ForceNextWinningSquare = null; ForceNextMotherlode = false;
            RoundSettled?.Invoke(res);
            ulong start = (ulong)Math.Ceiling(cur.currentSlot) + 1;
            cur = new OreRoundInfo { roundId = cur.roundId + 1, startSlot = start, endSlot = start + OreConstants.RoundSlots, currentSlot = start, phase = OrePhase.Deploying };
            RoundOpened?.Invoke(cur);
        }
    }

    /// <summary>Simulated economics shown in the volley report (labelled SIM in the UI).</summary>
    public static class OreSim
    {
        public static double OreForShot(double shotSol, bool motherlode)
        {
            double share = shotSol / (OreConstants.TypicalSolPerSquare + shotSol);
            return share * (OreConstants.OrePerRound + (motherlode ? OreConstants.SimMotherlodePot : 0));
        }
        public static double SolBack(double shotSol, bool winningSquare) => shotSol * (1 - (winningSquare ? OreConstants.FeeWinningSquare : OreConstants.FeeLosingSquare));
    }
}
