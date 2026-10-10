using System.Collections.Generic;
using UnityEngine;

namespace OreSeas.Game
{
    public enum CellKnown { Unknown, Miss, Hit, Sunk }

    public struct ShotResult
    {
        public int c, r; public bool hit; public int shipIndex; public bool sunk; public ShipKind kind;
        public bool oreSquare; public double ore; public double solBack; public bool motherlode;
        public string Name => Ore.OreConstants.SquareName(r * 5 + c);
    }

    /// <summary>One player's waters: their fleet + the shots fired into it.</summary>
    public sealed class BoardState
    {
        public readonly Fleet fleet;
        readonly bool[] shot = new bool[25];
        readonly int[] shipHits;

        public BoardState(Fleet f) { fleet = f; shipHits = new int[f.ships.Count]; }

        public bool IsShot(int c, int r) => shot[r * 5 + c];
        public bool IsSunk(int ship) => shipHits[ship] >= fleet.ships[ship].Length;
        public int ShipsAfloat { get { int n = 0; for (int i = 0; i < shipHits.Length; i++) if (!IsSunk(i)) n++; return n; } }
        public int HullLeft { get { int n = 0; for (int i = 0; i < shipHits.Length; i++) n += fleet.ships[i].Length - shipHits[i]; return n; } }
        public bool AllSunk => ShipsAfloat == 0;
        public int UnshotCount { get { int n = 0; foreach (var s in shot) if (!s) n++; return n; } }
        public int HitsOn(int ship) => shipHits[ship];

        public ShotResult Fire(int c, int r)
        {
            var res = new ShotResult { c = c, r = r, shipIndex = -1 };
            if (shot[r * 5 + c]) return res;
            shot[r * 5 + c] = true;
            int s = fleet.ShipAt(c, r);
            if (s >= 0)
            {
                shipHits[s]++;
                res.hit = true; res.shipIndex = s; res.kind = fleet.ships[s].kind; res.sunk = IsSunk(s);
            }
            return res;
        }

        /// <summary>What the attacker is allowed to know (for AI and the enemy-waters view).</summary>
        public CellKnown Known(int c, int r)
        {
            if (!shot[r * 5 + c]) return CellKnown.Unknown;
            int s = fleet.ShipAt(c, r);
            if (s < 0) return CellKnown.Miss;
            return IsSunk(s) ? CellKnown.Sunk : CellKnown.Hit;
        }

        public CellKnown[,] KnownGrid()
        {
            var g = new CellKnown[5, 5];
            for (int r = 0; r < 5; r++) for (int c = 0; c < 5; c++) g[c, r] = Known(c, r);
            return g;
        }

        public List<int> RemainingShipLengths()
        {
            var l = new List<int>();
            for (int i = 0; i < fleet.ships.Count; i++) if (!IsSunk(i)) l.Add(fleet.ships[i].Length);
            return l;
        }
    }

    public static class Salvo
    {
        /// <summary>Spec: shots per volley = ships afloat (3 -> 1), +1 bonus after sinking a ship or a crit, capped by open tiles.</summary>
        public static int Shots(BoardState own, BoardState target, bool bonus, int extra = 0)
            => Mathf.Clamp(Mathf.Max(own.ShipsAfloat > 0 ? 1 : 0, own.ShipsAfloat + (bonus ? 1 : 0) + extra), 0, target.UnshotCount);   // extra may be -1 (Barnacle); never below 1 while afloat
    }
}
