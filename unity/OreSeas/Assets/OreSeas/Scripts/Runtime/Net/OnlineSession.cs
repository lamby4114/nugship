using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using OreSeas.Game;

namespace OreSeas.Net
{
    /// <summary>
    /// One server-authoritative match as seen by this client. ServerClient fills it from match_found and queues every
    /// later match message in Inbox; FleetScreen / OnlineBattleScreen consume them in order (so nothing is lost while a
    /// screen fades in or an animation runs).
    /// </summary>
    public sealed class OnlineSession
    {
        public string matchId, mode, fireMode, source, stakeLamports;
        public int seat, placementSeconds = 60;
        public string opponentName, opponentId, opponentCaptain, opponentWallet, opponentAvatar; public bool opponentIsAI; public int opponentRating;
        // our fleet commit (place_fleet) + the opponent's commit (fleets_committed), verified at match_end
        public int[] myLayout; public string mySaltHex, myCommit, opponentCommit;
        public bool fleetPlaced, fleetsCommitted, ended;
        public readonly Queue<Dictionary<string, object>> Inbox = new Queue<Dictionary<string, object>>();

        public bool IsPractice => mode == "practice";
        public bool IsSolo => mode == "practice" || mode == "solo_live";
        public bool IsPvp => mode == "ranked" || mode == "private";
        public double StakeSol => double.TryParse(stakeLamports, out var l) ? l / 1e9 : 0;

        public static OnlineSession FromMatchFound(Dictionary<string, object> m)
        {
            var o = J.Obj(m, "opponent");
            return new OnlineSession
            {
                matchId = J.Str(m, "matchId"), mode = J.Str(m, "mode", "ranked"), seat = J.Int(m, "seat"), fireMode = J.Str(m, "fireMode", "virtual"),
                source = J.Str(m, "source", "live"), stakeLamports = J.Str(m, "stakeLamports", "0"), placementSeconds = J.Int(m, "placementSeconds", 60),
                opponentName = J.Str(o, "name", "RIVAL"), opponentId = J.Str(o, "id"), opponentIsAI = J.Bool(o, "isAI"),
                opponentCaptain = J.Str(o, "captain"), opponentRating = J.Int(o, "rating", 1000), opponentWallet = J.Str(o, "wallet"), opponentAvatar = J.Str(o, "avatar")
            };
        }

        /// <summary>Seal our fleet for place_fleet: layout codes 1/2/3, fresh 32-byte salt, commit = sha256(layout25 || salt32).</summary>
        public Dictionary<string, object> PlaceFleetMessage(Fleet f)
        {
            myLayout = LayoutFromFleet(f);
            var salt = new byte[32]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(salt);
            mySaltHex = Hex(salt); myCommit = Commit(myLayout, salt);
            return J.Msg("place_fleet", "matchId", matchId, "layout", myLayout, "salt", mySaltHex, "commit", myCommit);
        }

        // ---------------- layout <-> Fleet ----------------
        public static int Code(ShipKind k) => k == ShipKind.Galleon ? 1 : k == ShipKind.Sloop ? 2 : 3;
        public static ShipKind KindOf(int code) => code == 1 ? ShipKind.Galleon : code == 2 ? ShipKind.Sloop : ShipKind.Raft;
        public static ShipKind KindOf(string type) => type == "galleon" ? ShipKind.Galleon : type == "sloop" ? ShipKind.Sloop : ShipKind.Raft;

        /// <summary>Square i = row*5 + col (A1 = 0 .. E5 = 24), same as the board's (c, r) -> r*5+c.</summary>
        public static int[] LayoutFromFleet(Fleet f)
        {
            var lay = new int[25];
            foreach (var s in f.ships) foreach (var p in s.Cells()) lay[p.y * 5 + p.x] = Code(s.kind);
            return lay;
        }

        public static ShipPlacement PlacementFromCells(ShipKind kind, IList<int> cells)
        {
            int min = 99, max = -1; foreach (var c in cells) { min = Math.Min(min, c); max = Math.Max(max, c); }
            if (min > 24) min = 0;
            bool vertical = cells.Count > 1 && (max - min) % 5 == 0 && max / 5 != min / 5;
            return new ShipPlacement { kind = kind, c = min % 5, r = min / 5, vertical = vertical };
        }

        public static Fleet FleetFromLayout(IList<int> lay)
        {
            if (lay == null || lay.Count != 25) return null;
            var f = new Fleet();
            for (int code = 1; code <= 3; code++)
            {
                var cells = new List<int>(); for (int i = 0; i < 25; i++) if (lay[i] == code) cells.Add(i);
                if (cells.Count > 0) f.ships.Add(PlacementFromCells(KindOf(code), cells));
            }
            return f;
        }

        // ---------------- commit-reveal ----------------
        public static string Commit(IList<int> layout, byte[] salt)
        {
            if (layout == null || layout.Count != 25 || salt == null || salt.Length != 32) return "";
            var buf = new byte[57];
            for (int i = 0; i < 25; i++) buf[i] = (byte)layout[i];
            Buffer.BlockCopy(salt, 0, buf, 25, 32);
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(buf));
        }

        public static bool Verify(IList<int> layout, string saltHex, string commitHex)
        {
            try { return !string.IsNullOrEmpty(commitHex) && string.Equals(Commit(layout, FromHex(saltHex)), commitHex, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public static string Hex(byte[] b) { var sb = new StringBuilder(b.Length * 2); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
        public static byte[] FromHex(string h)
        {
            if (string.IsNullOrEmpty(h) || (h.Length & 1) == 1) throw new FormatException("hex");
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16);
            return b;
        }

        /// <summary>PROTOCOL.md test vector (also checked by the TS server and the C# OreSeas.Ore tests).</summary>
        public static bool SelfTest()
        {
            var lay = new[] { 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 3 };
            var salt = new byte[32]; for (int i = 0; i < 32; i++) salt[i] = (byte)i;
            if (Commit(lay, salt) != "f16a694e03790b6af47baee163085adab6b145ebc39165db6bfc7cf16ef6cf2a") return false;
            var f = FleetFromLayout(lay); if (f == null || !f.IsValid()) return false;
            var back = LayoutFromFleet(f); for (int i = 0; i < 25; i++) if (back[i] != lay[i]) return false;
            return true;
        }
    }
}
