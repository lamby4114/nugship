using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OreSeas.Game
{
    public enum AiStyle { RandomHunter, HuntTargetParity, HuntTargetCenter, ProbabilityDensity }

    /// <summary>AI captain brain: picks a volley of distinct targets from what it is allowed to know.</summary>
    public interface IAiCaptain
    {
        List<Vector2Int> ChooseVolley(CellKnown[,] known, List<int> remainingLengths, int count, System.Random rng);
    }

    public static class AiFactory
    {
        public static IAiCaptain Create(AiStyle s)
        {
            switch (s)
            {
                case AiStyle.RandomHunter: return new RandomHunterAi();
                case AiStyle.HuntTargetParity: return new HuntTargetAi(true, false);
                case AiStyle.HuntTargetCenter: return new HuntTargetAi(false, true);
                default: return new ProbabilityAi();
            }
        }

        public static List<Vector2Int> Unknown(CellKnown[,] k)
        {
            var l = new List<Vector2Int>();
            for (int r = 0; r < 5; r++) for (int c = 0; c < 5; c++) if (k[c, r] == CellKnown.Unknown) l.Add(new Vector2Int(c, r));
            return l;
        }

        public static void Shuffle<T>(IList<T> l, System.Random rng) { for (int i = l.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = l[i]; l[i] = l[j]; l[j] = t; } }
    }

    /// <summary>Barnacle: fires at random open water.</summary>
    public sealed class RandomHunterAi : IAiCaptain
    {
        public List<Vector2Int> ChooseVolley(CellKnown[,] known, List<int> rem, int count, System.Random rng)
        {
            var open = AiFactory.Unknown(known); AiFactory.Shuffle(open, rng);
            return open.Take(count).ToList();
        }
    }

    /// <summary>Hunt/target: finishes wounded ships first (extending lines of hits), otherwise hunts on a checkerboard parity.</summary>
    public sealed class HuntTargetAi : IAiCaptain
    {
        readonly bool parity, centerBias;
        public HuntTargetAi(bool parity, bool centerBias) { this.parity = parity; this.centerBias = centerBias; }

        public List<Vector2Int> ChooseVolley(CellKnown[,] known, List<int> rem, int count, System.Random rng)
        {
            var picks = new List<Vector2Int>();
            var k = (CellKnown[,])known.Clone();
            for (int n = 0; n < count; n++)
            {
                var t = TargetCell(k, rng) ?? HuntCell(k, rem, rng);
                if (t == null) break;
                picks.Add(t.Value);
                k[t.Value.x, t.Value.y] = CellKnown.Miss; // assume nothing while planning the rest of the salvo
            }
            return picks;
        }

        static bool In(int c, int r) => c >= 0 && r >= 0 && c < 5 && r < 5;
        static readonly Vector2Int[] Dirs = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };

        Vector2Int? TargetCell(CellKnown[,] k, System.Random rng)
        {
            var hits = new List<Vector2Int>();
            for (int r = 0; r < 5; r++) for (int c = 0; c < 5; c++) if (k[c, r] == CellKnown.Hit) hits.Add(new Vector2Int(c, r));
            if (hits.Count == 0) return null;
            // lines first: two adjacent hits -> extend along that axis
            var line = new List<Vector2Int>();
            foreach (var h in hits) foreach (var d in Dirs)
                {
                    var n = h + d;
                    if (In(n.x, n.y) && k[n.x, n.y] == CellKnown.Hit)
                    {
                        var a = h - d; var b = n + d;
                        if (In(a.x, a.y) && k[a.x, a.y] == CellKnown.Unknown) line.Add(a);
                        if (In(b.x, b.y) && k[b.x, b.y] == CellKnown.Unknown) line.Add(b);
                    }
                }
            if (line.Count > 0) return line[rng.Next(line.Count)];
            var adj = new List<Vector2Int>();
            foreach (var h in hits) foreach (var d in Dirs) { var n = h + d; if (In(n.x, n.y) && k[n.x, n.y] == CellKnown.Unknown) adj.Add(n); }
            return adj.Count > 0 ? adj[rng.Next(adj.Count)] : (Vector2Int?)null;
        }

        Vector2Int? HuntCell(CellKnown[,] k, List<int> rem, System.Random rng)
        {
            var open = AiFactory.Unknown(k);
            if (open.Count == 0) return null;
            int minLen = rem.Count > 0 ? rem.Min() : 1;
            var pool = open;
            if (parity && minLen >= 2) { var p = open.Where(v => ((v.x + v.y) & 1) == 0).ToList(); if (p.Count > 0) pool = p; }
            if (centerBias)
            {
                var w = pool.Select(v => 1f + 2.5f / (1f + Mathf.Abs(v.x - 2) + Mathf.Abs(v.y - 2))).ToList();
                float sum = w.Sum(), x = (float)rng.NextDouble() * sum;
                for (int i = 0; i < pool.Count; i++) { x -= w[i]; if (x <= 0) return pool[i]; }
            }
            return pool[rng.Next(pool.Count)];
        }
    }

    /// <summary>Ghost Captain / Kraken Cult: counts every legal placement of the remaining ships and fires at the densest cells.</summary>
    public sealed class ProbabilityAi : IAiCaptain
    {
        public List<Vector2Int> ChooseVolley(CellKnown[,] known, List<int> rem, int count, System.Random rng)
        {
            var picks = new List<Vector2Int>();
            var k = (CellKnown[,])known.Clone();
            for (int n = 0; n < count; n++)
            {
                var d = Density(k, rem);
                Vector2Int? best = null; float bv = -1;
                for (int r = 0; r < 5; r++) for (int c = 0; c < 5; c++)
                    {
                        if (k[c, r] != CellKnown.Unknown) continue;
                        float v = d[c, r] + (float)rng.NextDouble() * 0.01f;
                        if (v > bv) { bv = v; best = new Vector2Int(c, r); }
                    }
                if (best == null) break;
                picks.Add(best.Value);
                k[best.Value.x, best.Value.y] = CellKnown.Miss;
            }
            return picks;
        }

        static float[,] Density(CellKnown[,] k, List<int> rem)
        {
            var d = new float[5, 5];
            foreach (int len in rem)
                for (int vert = 0; vert < 2; vert++)
                    for (int r = 0; r < 5; r++) for (int c = 0; c < 5; c++)
                        {
                            if (vert == 0 && c + len > 5) continue;
                            if (vert == 1 && r + len > 5) continue;
                            bool ok = true; int hitsCovered = 0;
                            for (int i = 0; i < len; i++)
                            {
                                int cc = vert == 0 ? c + i : c, rr = vert == 1 ? r + i : r;
                                var s = k[cc, rr];
                                if (s == CellKnown.Miss || s == CellKnown.Sunk) { ok = false; break; }
                                if (s == CellKnown.Hit) hitsCovered++;
                            }
                            if (!ok) continue;
                            float w = hitsCovered > 0 ? 1 + 25f * hitsCovered : 1;
                            for (int i = 0; i < len; i++) { int cc = vert == 0 ? c + i : c, rr = vert == 1 ? r + i : r; if (k[cc, rr] == CellKnown.Unknown) d[cc, rr] += w; }
                        }
            return d;
        }
    }
}
