using UnityEngine;

namespace OreSeas
{
    /// <summary>Small factory helpers for building scenes out of SpriteRenderers in mockup coordinates.</summary>
    public static class Gfx
    {
        public static Transform Node(Transform parent, string name, float x = 0, float y = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Px.P(x, y);
            return go.transform;
        }

        public static SpriteRenderer Sprite(Transform parent, string spriteName, float x, float y, int order, string name = null)
        {
            var go = new GameObject(name ?? spriteName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Px.P(x, y);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Atlas.Get(spriteName);
            sr.sortingOrder = order;
            return sr;
        }

        public static SpriteRenderer Sprite(Transform parent, Sprite sprite, float x, float y, int order, string name = "sprite")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Px.P(x, y);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Solid rectangle (x,y = top-left in mockup px).</summary>
        public static SpriteRenderer Rect(Transform parent, float x, float y, float w, float h, Color32 c, int order, string name = "rect")
        {
            var sr = Sprite(parent, Atlas.White, x, y, order, name);
            sr.transform.localScale = new Vector3(Mathf.Max(0, w), Mathf.Max(0, h), 1);
            sr.color = c;
            return sr;
        }

        public static void SetRect(SpriteRenderer sr, float x, float y, float w, float h)
        {
            sr.transform.localPosition = Px.P(x, y);
            sr.transform.localScale = new Vector3(Mathf.Max(0, w), Mathf.Max(0, h), 1);
        }

        /// <summary>9-slice/tiled panel from a bordered atlas sprite (parch9, wood9, woodn9).</summary>
        public static SpriteRenderer Panel(Transform parent, string sprite9, float x, float y, float w, float h, int order, bool tiled = true)
        {
            var sr = Sprite(parent, sprite9, 0, 0, order, sprite9);
            if (sr.sprite == null) return sr;
            sr.drawMode = tiled ? SpriteDrawMode.Tiled : SpriteDrawMode.Sliced;
            if (tiled) sr.tileMode = SpriteTileMode.Continuous;
            sr.size = new Vector2(w, h);
            // tiled/sliced sprites are drawn around the pivot; our 9-slice sprites have a top-left pivot (0,1)
            sr.transform.localPosition = Px.P(x, y);
            return sr;
        }

        /// <summary>Dark label plate like the mockup name plates: ink border, plum fill, sky1 top line.</summary>
        public static void Plate(Transform parent, float x, float y, float w, float h, int order)
        {
            Rect(parent, x, y, w, h, Pal.Ink, order);
            Rect(parent, x + 1, y + 1, w - 2, h - 2, Pal.Plum, order + 1);
            Rect(parent, x + 1, y + 1, w - 2, 1, Pal.Sky1, order + 2);
        }

        public static void SetOrder(Transform root, int delta)
        {
            foreach (var r in root.GetComponentsInChildren<SpriteRenderer>(true)) r.sortingOrder += delta;
        }
    }
}
