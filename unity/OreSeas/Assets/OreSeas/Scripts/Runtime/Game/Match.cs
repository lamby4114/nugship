using System.Collections.Generic;
using OreSeas.Ore;
using UnityEngine;

namespace OreSeas.Game
{
    public sealed class Side
    {
        public string name; public Fleet fleet; public BoardState waters; public byte[] salt; public string commit;
        public bool bonusNext; public int volleys, shots, hits, sunk, crits, krakens; public double ore, solSpent, solBack;
        public Side(string name, Fleet f, byte[] salt, string commit) { this.name = name; fleet = f; waters = new BoardState(f); this.salt = salt; this.commit = commit; }
    }

    public sealed class VolleyResult
    {
        public int round; public OreRoundResult ore;
        public readonly List<ShotResult> yours = new List<ShotResult>(), theirs = new List<ShotResult>();
        public bool yourBonus, theirBonus; public int outcome; // 0 continue, 1 you win, -1 you lose, 2 draw
    }

    /// <summary>
    /// Rules (SPEC): both captains fire simultaneously each ORE round. Shots per volley = ships afloat (+1 after sinking a ship or a crit).
    /// A shot on the ORE winning square mines (simulated) ORE; hit + ORE square = CRITICAL HIT. Motherlode = Kraken.
    /// </summary>
    public sealed class MatchState
    {
        public readonly Side you, them; public int round; public readonly double stake; public readonly int theirExtra;
        public readonly List<string> log = new List<string>();

        public MatchState(Side you, Side them, double stakePerShot, int theirExtraShots)
        { this.you = you; this.them = them; stake = stakePerShot; theirExtra = theirExtraShots; }

        public int ShotsYou => Salvo.Shots(you.waters, them.waters, you.bonusNext);
        public int ShotsThem => Salvo.Shots(them.waters, you.waters, them.bonusNext, theirExtra);
        public bool Over => you.waters.AllSunk || them.waters.AllSunk;

        public VolleyResult Resolve(List<Vector2Int> yourShots, List<Vector2Int> theirShots, OreRoundResult ore)
        {
            round++;
            var v = new VolleyResult { round = round, ore = ore };
            v.yourBonus = Fire(you, them, yourShots, ore, v.yours);
            v.theirBonus = Fire(them, you, theirShots, ore, v.theirs);
            you.bonusNext = v.yourBonus; them.bonusNext = v.theirBonus;
            bool youDead = you.waters.AllSunk, themDead = them.waters.AllSunk;
            v.outcome = youDead && themDead ? 2 : themDead ? 1 : youDead ? -1 : 0;
            if (ore.motherlode) { you.krakens++; them.krakens++; }
            log.Add("R" + round + " ORE " + OreConstants.SquareName(ore.winningSquare) + (ore.motherlode ? " - MOTHERLODE!" : ""));
            foreach (var s in v.yours) log.Add("  YOU  " + s.Name + " " + Describe(s));
            foreach (var s in v.theirs) log.Add("  THEM " + s.Name + " " + Describe(s));
            return v;
        }

        public static string Describe(ShotResult s)
        {
            if (s.hit && s.oreSquare) return s.sunk ? "CRIT! SUNK " + ShipSpec.Name(s.kind) : "CRITICAL HIT";
            if (s.hit) return s.sunk ? "SUNK " + ShipSpec.Name(s.kind) : "HIT";
            return s.oreSquare ? "SPLASH + ORE" : "MISS";
        }

        bool Fire(Side shooter, Side target, List<Vector2Int> shots, OreRoundResult ore, List<ShotResult> into)
        {
            bool bonus = false;
            shooter.volleys++;
            foreach (var p in shots)
            {
                if (target.waters.IsShot(p.x, p.y)) continue;
                var r = target.waters.Fire(p.x, p.y);
                r.oreSquare = OreConstants.Index(p.x, p.y) == ore.winningSquare;
                r.motherlode = r.oreSquare && ore.motherlode;
                if (r.oreSquare) r.ore = OreSim.OreForShot(stake, ore.motherlode);
                r.solBack = OreSim.SolBack(stake, r.oreSquare);
                shooter.shots++; shooter.solSpent += stake; shooter.solBack += r.solBack; shooter.ore += r.ore;
                if (r.hit) shooter.hits++;
                if (r.sunk) { shooter.sunk++; bonus = true; }
                if (r.hit && r.oreSquare) { shooter.crits++; bonus = true; }
                into.Add(r);
            }
            return bonus;
        }
    }
}
