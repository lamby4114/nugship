using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace OreSeas.Game
{
    public enum ShipKind { Galleon = 0, Sloop = 1, Raft = 2 }

    public static class ShipSpec
    {
        public static readonly ShipKind[] All = { ShipKind.Galleon, ShipKind.Sloop, ShipKind.Raft };
        public static int Length(ShipKind k) => k == ShipKind.Galleon ? 3 : k == ShipKind.Sloop ? 2 : 1;
        public static string Name(ShipKind k) => k == ShipKind.Galleon ? "GALLEON" : k == ShipKind.Sloop ? "SLOOP" : "RAFT";
        public static string Key(ShipKind k) => k == ShipKind.Galleon ? "galleon" : k == ShipKind.Sloop ? "sloop" : "raft";
        public const int TotalHull = 6;
    }

    [Serializable]
    public struct ShipPlacement
    {
        public ShipKind kind; public int c, r; public bool vertical;
        public int Length => ShipSpec.Length(kind);
        public IEnumerable<Vector2Int> Cells() { for (int i = 0; i < Length; i++) yield return vertical ? new Vector2Int(c, r + i) : new Vector2Int(c + i, r); }
        public bool InBounds => c >= 0 && r >= 0 && (vertical ? (c < 5 && r + Length <= 5) : (r < 5 && c + Length <= 5));
        public bool Covers(int cc, int rr) => vertical ? (cc == c && rr >= r && rr < r + Length) : (rr == r && cc >= c && cc < c + Length);
    }

    /// <summary>A hidden 5x5 fleet: galleon(3), sloop(2), raft(1). Ships may touch but not overlap.</summary>
    [Serializable]
    public sealed class Fleet
    {
        public List<ShipPlacement> ships = new List<ShipPlacement>();

        public Fleet Clone() { var f = new Fleet(); f.ships.AddRange(ships); return f; }

        public int ShipAt(int c, int r) { for (int i = 0; i < ships.Count; i++) if (ships[i].Covers(c, r)) return i; return -1; }

        public bool IsValid()
        {
            var seen = new bool[25];
            foreach (var s in ships)
            {
                if (!s.InBounds) return false;
                foreach (var p in s.Cells()) { int i = p.y * 5 + p.x; if (seen[i]) return false; seen[i] = true; }
            }
            return ships.Count == 3;
        }

        public bool CanPlace(int shipIndex, ShipPlacement p)
        {
            if (!p.InBounds) return false;
            for (int i = 0; i < ships.Count; i++)
            {
                if (i == shipIndex) continue;
                foreach (var cell in p.Cells()) if (ships[i].Covers(cell.x, cell.y)) return false;
            }
            return true;
        }

        public static Fleet Random(System.Random rng)
        {
            for (int attempt = 0; attempt < 500; attempt++)
            {
                var f = new Fleet();
                bool ok = true;
                foreach (var k in ShipSpec.All)
                {
                    bool placed = false;
                    for (int t = 0; t < 100 && !placed; t++)
                    {
                        var p = new ShipPlacement { kind = k, vertical = rng.Next(2) == 0, c = rng.Next(5), r = rng.Next(5) };
                        if (f.CanPlace(-1, p)) { f.ships.Add(p); placed = true; }
                    }
                    if (!placed) { ok = false; break; }
                }
                if (ok && f.IsValid()) return f;
            }
            throw new InvalidOperationException("could not place fleet");
        }

        public static Fleet Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try
            {
                var f = new Fleet();
                foreach (var part in s.Split(';'))
                {
                    var kind = part[0] == 'G' ? ShipKind.Galleon : part[0] == 'S' ? ShipKind.Sloop : ShipKind.Raft;
                    var xy = part.Substring(1, part.Length - 2).Split(',');
                    f.ships.Add(new ShipPlacement { kind = kind, c = int.Parse(xy[0]), r = int.Parse(xy[1]), vertical = part[part.Length - 1] == 'V' });
                }
                return f.IsValid() ? f : null;
            }
            catch { return null; }
        }

        /// <summary>Canonical layout string, e.g. "G0,1H;S3,3V;R1,4H".</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            var sorted = new List<ShipPlacement>(ships); sorted.Sort((a, b) => a.kind.CompareTo(b.kind));
            for (int i = 0; i < sorted.Count; i++)
            {
                var s = sorted[i];
                if (i > 0) sb.Append(';');
                sb.Append("GSR"[(int)s.kind]).Append(s.c).Append(',').Append(s.r).Append(s.vertical ? 'V' : 'H');
            }
            return sb.ToString();
        }
    }

    /// <summary>Commit-reveal for hidden fleets: commit = sha256(layout || salt). Revealed + verified at match end.</summary>
    public static class FleetCommit
    {
        public static byte[] NewSalt()
        {
            var salt = new byte[16];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(salt);
            return salt;
        }

        public static string Commit(Fleet f, byte[] salt)
        {
            var layout = Encoding.UTF8.GetBytes(f.Serialize());
            var buf = new byte[layout.Length + salt.Length];
            Buffer.BlockCopy(layout, 0, buf, 0, layout.Length);
            Buffer.BlockCopy(salt, 0, buf, layout.Length, salt.Length);
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(buf));
        }

        public static bool Verify(Fleet f, byte[] salt, string commitHex) => string.Equals(Commit(f, salt), commitHex, StringComparison.OrdinalIgnoreCase);

        public static string Hex(byte[] b) { var sb = new StringBuilder(b.Length * 2); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
    }
}
