// OreSeas.Ore (csharp/OreSeas.Ore) dropped into Unity under OreSeas.Ore.Chain (Unity already has OreSeas.Ore.OreConstants).
// Pure System code: always compiled.
using System;
using System.Security.Cryptography;

namespace OreSeas.Ore.Chain
{
    /// <summary>
    /// ORE SEAS fleet commit: commit = hex(sha256(layout[25] || salt[32])).
    /// layout[i] = ship code on square i (0 water, 1 galleon(3), 2 sloop(2), 3 raft(1)); square i = row*5+col (A1=0..E5=24).
    /// Send layout+salt+commit in place_fleet; at match end verify the opponent's revealed layout+salt against the
    /// opponentCommit received in fleets_committed.
    /// </summary>
    public static class FleetCommit
    {
        public const byte Water = 0, Galleon = 1, Sloop = 2, Raft = 3;

        public static byte[] NewSalt()
        {
            var s = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(s);
            return s;
        }

        public static string Compute(byte[] layout, byte[] salt)
        {
            if (layout == null || layout.Length != 25) throw new ArgumentException("layout must be 25 bytes");
            if (salt == null || salt.Length != 32) throw new ArgumentException("salt must be 32 bytes");
            var buf = new byte[57];
            Buffer.BlockCopy(layout, 0, buf, 0, 25);
            Buffer.BlockCopy(salt, 0, buf, 25, 32);
            using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(buf));
        }

        public static bool Verify(byte[] layout, byte[] salt, string commitHex) =>
            string.Equals(Compute(layout, salt), commitHex, StringComparison.OrdinalIgnoreCase);

        public static string ToHex(byte[] b)
        {
            var c = new char[b.Length * 2];
            const string h = "0123456789abcdef";
            for (int i = 0; i < b.Length; i++) { c[2 * i] = h[b[i] >> 4]; c[2 * i + 1] = h[b[i] & 15]; }
            return new string(c);
        }

        public static byte[] FromHex(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(2 * i, 2), 16);
            return b;
        }

        /// <summary>Same rules as the server: exact sizes, straight contiguous lines.</summary>
        public static string Validate(byte[] layout)
        {
            if (layout == null || layout.Length != 25) return "layout must have 25 squares";
            int[] sizes = { 0, 3, 2, 1 };
            for (byte code = 1; code <= 3; code++)
            {
                int n = 0, first = -1, last = -1;
                for (int i = 0; i < 25; i++) if (layout[i] == code) { n++; if (first < 0) first = i; last = i; }
                if (n != sizes[code]) return "wrong size for ship " + code;
                if (n == 1) continue;
                bool horiz = first / 5 == last / 5 && last - first == n - 1;
                bool vert = first % 5 == last % 5 && last - first == 5 * (n - 1);
                if (!horiz && !vert) return "ship " + code + " must be a straight line";
                for (int k = 0; k < n; k++) if (layout[first + k * (horiz ? 1 : 5)] != code) return "ship " + code + " not contiguous";
            }
            for (int i = 0; i < 25; i++) if (layout[i] > 3) return "bad square value";
            return null;
        }
    }
}
