using UnityEngine;

namespace OreSeas
{
    /// <summary>Palette from the mockups (engine.js P).</summary>
    public static class Pal
    {
        public static Color32 Hex(string h)
        {
            if (h.StartsWith("#")) h = h.Substring(1);
            byte r = System.Convert.ToByte(h.Substring(0, 2), 16), g = System.Convert.ToByte(h.Substring(2, 2), 16), b = System.Convert.ToByte(h.Substring(4, 2), 16);
            return new Color32(r, g, b, 255);
        }
        public static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        public static readonly Color32 Ink = Hex("1a1210"), Plum = Hex("2a223e"), Night = Hex("10142a"), Navy = Hex("201a40");
        public static readonly Color32 Sky0 = Hex("201a40"), Sky1 = Hex("3a2e5e"), Sky2 = Hex("8a4e88"), Sky3 = Hex("c86a6e"), Sky4 = Hex("f09a4a"), Sky5 = Hex("ffd27a"), Sun = Hex("ffeeb8");
        public static readonly Color32 Sea0 = Hex("183046"), Sea1 = Hex("1e3a52"), Sea2 = Hex("2e5468"), Sea3 = Hex("3a6a80"), SeaHi = Hex("7ea8aa"), Foam = Hex("e8ecf0");
        public static readonly Color32 Map0 = Hex("30606e"), Map1 = Hex("407682"), Map2 = Hex("5c949e"), Map3 = Hex("7ea8aa");
        public static readonly Color32 Sand = Hex("d8c498"), Sand1 = Hex("e6d6b0"), Sand2 = Hex("bea06e"), Sand3 = Hex("968464"), MapB = Hex("7a603a"), MapD = Hex("4a402c");
        public static readonly Color32 Grass = Hex("7ea660"), Grass2 = Hex("689254"), Grass3 = Hex("4c7646"), Grass4 = Hex("3a5240");
        public static readonly Color32 Wood0 = Hex("46301e"), Wood1 = Hex("5a3e28"), Wood2 = Hex("6e4e32"), Wood3 = Hex("805c3c"), Wood4 = Hex("9a7048"), Wood5 = Hex("b8935f");
        public static readonly Color32 Gold0 = Hex("7a5a1c"), Gold1 = Hex("a87c2c"), Gold2 = Hex("c99a3c"), Gold3 = Hex("ffc864"), Gold4 = Hex("ffeeb8"), GoldM = Hex("e8b04a");
        public static readonly Color32 Red0 = Hex("4a1414"), Red1 = Hex("8e3a2a"), Red2 = Hex("d6463c"), Red3 = Hex("ff7a3a");
        public static readonly Color32 White = Hex("f2eede"), Cream = Hex("e8e0c8"), Grey0 = Hex("2a2a30"), Grey1 = Hex("4e4e5a"), Grey2 = Hex("7890a8"), Grey3 = Hex("a0aaba"), Grey4 = Hex("d6d0be");
        public static readonly Color32 Smoke0 = Hex("4a4458"), Smoke1 = Hex("6e687c"), Smoke2 = Hex("a49eac");
        public static readonly Color32 Purp0 = Hex("2a1838"), Purp1 = Hex("4a2a5a"), Purp2 = Hex("6e3e7a"), Purp3 = Hex("9a5a9a"), Purp4 = Hex("c88ab8");

        public static readonly Color32[] GoldTxt = { Gold4, Gold4, Gold3, Gold3, GoldM, Gold2, Gold1, Gold1 };
        public static readonly Color32[] RedTxt = { Hex("ffb08a"), Hex("ff9a6a"), Red3, Red3, Red2, Red2, Red1, Red1 };
        public static readonly Color32[] WhiteTxt = { White, White, White, White, Grey4, Grey4, Grey3, Grey3 };
        public static readonly Color32[] GreenTxt = { Hex("c8e8a0"), Hex("c8e8a0"), Grass, Grass, Grass, Grass2, Grass3, Grass3 };
    }

    /// <summary>Sea palette (sprites.js SEA_*).</summary>
    public sealed class SeaPalette
    {
        public Color32 b, d, l, l2, hi, foam; public int per; public float spd;
        public SeaPalette(string b, string d, string l, string l2, string hi, string foam, int per, float spd)
        { this.b = Pal.Hex(b); this.d = Pal.Hex(d); this.l = Pal.Hex(l); this.l2 = Pal.Hex(l2); this.hi = Pal.Hex(hi); this.foam = Pal.Hex(foam); this.per = per; this.spd = spd; }
        public static readonly SeaPalette Open = new SeaPalette("1e3a52", "183046", "2e5468", "3a6a80", "7ea8aa", "e8ecf0", 20, 5);
        public static readonly SeaPalette Map = new SeaPalette("407682", "30606e", "5c949e", "7ea8aa", "7ea8aa", "e8ecf0", 16, 4);
        public static readonly SeaPalette Storm = new SeaPalette("162a40", "0e1c2e", "24405a", "3a5a74", "7890a8", "c8d4e0", 14, 12);
    }
}
