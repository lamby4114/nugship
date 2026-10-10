using System.Collections.Generic;
using OreSeas.Game;
using UnityEngine;

namespace OreSeas.View
{
    /// <summary>A ship sprite on a board (horizontal side-view hull or top-down vertical hull) with bobbing + foam.</summary>
    public sealed class ShipView : MonoBehaviour
    {
        public ShipPlacement place; public string team = "p"; public bool sunk; public bool cat; public int baseOrder = 20;
        SpriteRenderer hull, foam, sunkCover, nug; Sprite[] frames, foamFrames, nugFrames; float t, ph; Color32 cover = Pal.Map1;
        public float alpha = 1f; public bool ghostInvalid;

        public static ShipView Create(Transform parent, ShipPlacement p, string team, bool cat, Color32 waterCover)
        {
            var go = new GameObject("Ship_" + p.kind); go.transform.SetParent(parent, false);
            var v = go.AddComponent<ShipView>(); v.team = team; v.cat = cat; v.cover = waterCover; v.ph = p.c + p.r;
            v.hull = Gfx.Sprite(go.transform, (Sprite)null, 0, 0, 0, "hull");
            v.foam = Gfx.Sprite(go.transform, (Sprite)null, 0, 0, 0, "foam");
            v.sunkCover = Gfx.Rect(go.transform, 0, 0, 1, 3, waterCover, 0, "sunkcover"); v.sunkCover.enabled = false;
            if (cat) { v.nug = Gfx.Sprite(go.transform, (Sprite)null, 0, 0, 0, "nuggy"); v.nugFrames = Atlas.Seq("nugtiny"); }
            v.Set(p, false);
            return v;
        }

        public void Set(ShipPlacement p, bool isSunk)
        {
            place = p; sunk = isSunk;
            string k = ShipSpec.Key(p.kind);
            frames = Atlas.Seq((p.vertical ? "vship_" : "ship_") + k + "_" + team + "_" + (sunk ? "s" : "n"));
            int span = p.Length * BoardGeo.Tile;
            if (p.vertical) { int fw = p.kind == ShipKind.Galleon ? 24 : p.kind == ShipKind.Sloop ? 20 : 16; foamFrames = Atlas.Seq("vfoam" + fw); }
            else foamFrames = Atlas.Seq("foam" + (span - 8));
            int ord = baseOrder + p.r * 10 + (p.vertical ? p.Length - 1 : 0) * 10;
            hull.sortingOrder = ord + 2; foam.sortingOrder = ord + 1; sunkCover.sortingOrder = ord + 3; if (nug) nug.sortingOrder = ord + 4;
            if (nug) nug.enabled = cat && !sunk;
            sunkCover.enabled = sunk && !p.vertical;
            Layout();
        }

        void Update() { t += Time.deltaTime; Layout(); }

        void Layout()
        {
            var p = place; int span = p.Length * BoardGeo.Tile;
            int bob = sunk ? 0 : JsMath.RoundI(Mathf.Sin(t * 2.2f + ph) * 1);
            int fi = frames.Length > 0 ? (int)(t * 6.6f) % frames.Length : 0;
            hull.sprite = frames.Length > 0 ? frames[fi] : null;
            foam.sprite = foamFrames.Length > 0 ? foamFrames[(int)(t * 6) % foamFrames.Length] : null;
            var tl = BoardGeo.TileXY(p.c, p.r);
            if (!p.vertical)
            {
                int L = p.kind == ShipKind.Galleon ? 108 : p.kind == ShipKind.Sloop ? 72 : 34;
                float x = tl.x + Mathf.Round((span - L) / 2f), y = tl.y + 29 + bob + (sunk ? 3 : 0);
                hull.transform.localPosition = Px.P(x, y);
                foam.transform.localPosition = Px.P(tl.x + 4, y + 1);
                if (sunk) Gfx.SetRect(sunkCover, tl.x + 2, y + 2, span - 4, 3);
                if (nug) { nug.transform.localPosition = Px.P(x + (team == "e" ? L - 24 : 9), y - 27); nug.sprite = nugFrames.Length > 0 ? nugFrames[(int)(t * 4) % nugFrames.Length] : null; }
            }
            else
            {
                float cx = tl.x + BoardGeo.Tile / 2f, y = tl.y + bob;
                hull.transform.localPosition = Px.P(cx, y);
                int fw = p.kind == ShipKind.Galleon ? 24 : p.kind == ShipKind.Sloop ? 20 : 16;
                foam.transform.localPosition = Px.P(cx - fw / 2f, tl.y + span - 6 + bob);
                if (nug) { nug.transform.localPosition = Px.P(cx - 10, y + 22); nug.sprite = nugFrames.Length > 0 ? nugFrames[(int)(t * 4) % nugFrames.Length] : null; }
            }
            var col = ghostInvalid ? new Color(1f, 0.55f, 0.55f, alpha) : new Color(1, 1, 1, alpha);
            hull.color = col; foam.color = new Color(1, 1, 1, alpha); if (nug) nug.color = col;
        }
    }
}
