using System.Collections.Generic;
using OreSeas.Game;
using UnityEngine;

namespace OreSeas.View
{
    /// <summary>Board geometry from scenes.js: TILE=38, BX=25, BY=104, BW=190.</summary>
    public static class BoardGeo
    {
        public const int Tile = 38, BX = 25, BY = 104, BW = 190;
        public static Vector2 TileXY(int c, int r) => new Vector2(BX + c * Tile, BY + r * Tile);
        public static bool TileAt(Vector2 p, float offsetX, out int c, out int r)
        {
            float x = p.x - offsetX - BX, y = p.y - BY;
            c = Mathf.FloorToInt(x / Tile); r = Mathf.FloorToInt(y / Tile);
            return x >= 0 && y >= 0 && c >= 0 && r >= 0 && c < 5 && r < 5;
        }
        public const string Cols = "ABCDE";
    }

    /// <summary>One 5x5 sea chart: parchment frame, living map water, grid, ships, marks, ORE square, reticles.</summary>
    public sealed class BoardView
    {
        public readonly Transform root; public readonly BiomeDef biome;
        readonly OceanRenderer water; readonly Transform marks, targetsRoot;
        readonly Dictionary<int, GameObject> markAt = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> targetAt = new Dictionary<int, GameObject>();
        GameObject oreTile; readonly List<GameObject> glints = new List<GameObject>();
        public readonly List<ShipView> ships = new List<ShipView>();
        readonly PixelText titleText;

        public BoardView(Transform parent, BiomeDef biome, string title, bool fog)
        {
            this.biome = biome;
            root = Gfx.Node(parent, "Board_" + title);
            Gfx.Sprite(root, "board_frame", 0, 0, 10, "frame");
            int fx = BoardGeo.BX - 12, fy = BoardGeo.BY - 13, fw = BoardGeo.BW + 24;
            for (int c = 0; c < 5; c++) PixelText.Create(root, BoardGeo.Cols[c].ToString(), BoardGeo.BX + c * BoardGeo.Tile + BoardGeo.Tile / 2f, fy + 3, TextStyle.Of(Pal.MapD, TextAlign.Center), 11);
            for (int r = 0; r < 5; r++) PixelText.Create(root, (r + 1).ToString(), fx + 4, BoardGeo.BY + r * BoardGeo.Tile + BoardGeo.Tile / 2f - 4, TextStyle.Of(Pal.MapD), 11);
            titleText = PixelText.Create(root, "", BoardGeo.BX, fy + fw - 10, TextStyle.Of(Pal.MapB), 11, "title");   // no caption on the frame (user: "ENEMY WATERS" etc. was confusing); SetTitle kept for API
            water = OceanRenderer.Create(root, BoardGeo.BX, BoardGeo.BY, BoardGeo.BW, BoardGeo.BW, biome.map, 11, "MapWater");
            water.cw = 13; water.ch = 10;
            Gfx.Sprite(root, "board_grid", 0, 0, 12, "grid");
            for (int i = 0; i < 3; i++)
            {
                var f = Gfx.Sprite(root, "fish", 0, 0, 13, "fish");
                var d = Drift.Add(f, BoardGeo.BX + i * 70, BoardGeo.BY + 30 + i * 58, 8 + i * 3, BoardGeo.BX - 4, BoardGeo.BX + BoardGeo.BW - 4); d.bobAmp = 3; d.bobFreq = 1;
            }
            if (fog)
                for (int i = 0; i < 5; i++)
                {
                    var p = Gfx.Sprite(root, "fogpuff", 0, 0, 85, "fog"); p.color = new Color(1, 1, 1, 0.8f);
                    Drift.Add(p, BoardGeo.BX + (i * 53) % (BoardGeo.BW + 60) - 30, BoardGeo.BY + 12 + i * 37, 4, BoardGeo.BX + 8, BoardGeo.BX + BoardGeo.BW - 10);
                }
            marks = Gfx.Node(root, "marks"); targetsRoot = Gfx.Node(root, "targets");
        }

        public void SetTitle(string t) => titleText.Set(t);
        public float OffsetX { get => root.localPosition.x; set => root.localPosition = new Vector3(Mathf.Round(value), root.localPosition.y, 0); }
        public OceanRenderer Water => water;

        public ShipView AddShip(ShipPlacement p, string team, bool cat)
        {
            var v = ShipView.Create(root, p, team, cat, biome.map.b);
            ships.Add(v); return v;
        }

        public void ClearShips() { foreach (var s in ships) if (s) Object.Destroy(s.gameObject); ships.Clear(); }

        static int Key(int c, int r) => r * 5 + c;

        public void MarkMiss(int c, int r)
        {
            ClearMark(c, r);
            var tl = BoardGeo.TileXY(c, r);
            var a = SpriteAnim.Play(marks, "miss", tl.x + 19, tl.y + 22, 16, 6, true, false); a.timeOffset = c * 0.37f + r * 0.21f;
            markAt[Key(c, r)] = a.gameObject;
        }

        public void MarkFire(int c, int r, bool sunkRow = false)
        {
            ClearMark(c, r);
            var tl = BoardGeo.TileXY(c, r); var n = Gfx.Node(marks, "fire").gameObject;
            int s = (c + r) & 1; int ord = 20 + r * 10 + 6;
            var f = SpriteAnim.Play(n.transform, "flame" + s, tl.x + 18, tl.y + 26 + (sunkRow ? 3 : 0), ord, 12, true, false); f.timeOffset = c * 0.13f;
            var sm = SpriteAnim.Play(n.transform, "smoke" + s, tl.x + 16, tl.y + 14, 80, 7, true, false); sm.timeOffset = r * 0.4f;
            markAt[Key(c, r)] = n;
        }

        /// <summary>Hit on a hidden enemy ship: scorch + flames + smoke (hull stays hidden until sunk).</summary>
        public void MarkHitHidden(int c, int r)
        {
            MarkFire(c, r);
            var tl = BoardGeo.TileXY(c, r);
            var scorch = Gfx.Rect(markAt[Key(c, r)].transform, tl.x + 8, tl.y + 26, 22, 3, Pal.Hex("2a1c12"), 17, "debris");
            scorch.color = new Color32(0x2a, 0x1c, 0x12, 200);
        }

        public void ClearMark(int c, int r) { if (markAt.TryGetValue(Key(c, r), out var g) && g) Object.Destroy(g); markAt.Remove(Key(c, r)); }

        public void ShowOre(int c, int r)
        {
            ClearOre();
            var tl = BoardGeo.TileXY(c, r);
            oreTile = SpriteAnim.Play(root, "oretile", tl.x, tl.y, 15, 8, true, false).gameObject;
        }
        public void ClearOre() { if (oreTile) Object.Destroy(oreTile); oreTile = null; }

        public void AddGlint(int c, int r)
        {
            var tl = BoardGeo.TileXY(c, r);
            var n = Gfx.Node(marks, "glint");
            Gfx.Sprite(n, "ore_badge", tl.x + BoardGeo.Tile - 9, tl.y + 3, 18);
            var s = SpriteAnim.Play(n, "spk_s", tl.x + BoardGeo.Tile - 6, tl.y + 3, 19, 5, true, false); s.timeOffset = c * 0.3f;
            glints.Add(n.gameObject);
        }

        public void SetTarget(int c, int r, bool on)
        {
            int k = Key(c, r);
            if (!on) { if (targetAt.TryGetValue(k, out var g) && g) Object.Destroy(g); targetAt.Remove(k); return; }
            if (targetAt.ContainsKey(k)) return;
            var tl = BoardGeo.TileXY(c, r); var n = Gfx.Node(targetsRoot, "target");
            SpriteAnim.Play(n, "ret", tl.x, tl.y, 90, 8, true, false);
            Gfx.Sprite(n, "xmark", tl.x + 19, tl.y + 19, 91);
            SpriteAnim.Play(n, "flag", tl.x + 24, tl.y + 30, 92, 9, true, false);
            targetAt[k] = n.gameObject;
        }
        public void ClearTargets() { foreach (var g in targetAt.Values) if (g) Object.Destroy(g); targetAt.Clear(); }
        public int TargetCount => targetAt.Count;

        public Vector2 Center(int c, int r) { var tl = BoardGeo.TileXY(c, r); return new Vector2(tl.x + 19 + OffsetX, tl.y + 19); }
    }

}
