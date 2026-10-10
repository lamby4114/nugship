using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OreSeas
{
    /// <summary>
    /// Dev-only horizontal centring audit (runs after every screen/overlay build in development builds and in the
    /// desktop screenshot player). Checks, in logical pixels on the 240 px canvas:
    ///  1. every centred PixelText lands exactly on its anchor (left + width/2 == anchor; anchors on whole or half px);
    ///  2. every button's label (or icon+label group) is centred in the button rect;
    ///  3. rows of 2+ buttons spanning at least half the screen, and single buttons/panels/ribbons 100 px or wider,
    ///     are centred on the screen (x = 120);
    ///  4. hint/stamp/toast plates are centred on the screen and on their text.
    /// Anything off by half a pixel or more is reported. Debug.isDebugBuild only; release builds never run it.
    /// </summary>
    /// <summary>Marks a transform as part of a horizontal group whose union must be screen-centred (CenterAudit).</summary>
    public sealed class AuditGroup : MonoBehaviour
    {
        public string key;
        public static void Add(Transform t, string key) { if (t != null) t.gameObject.AddComponent<AuditGroup>().key = key; }
    }

    public static class CenterAudit
    {
        public static bool Enabled => Debug.isDebugBuild || Application.isEditor;
        public static int TotalChecks, TotalIssues;
        public static readonly List<string> Log = new List<string>();

        const float Mid = Px.W / 2f;

        public static void RunDeferred(MonoBehaviour host, string label)
        {
            if (!Enabled || host == null) return;
            host.StartCoroutine(Later(label));
        }

        static IEnumerator Later(string label) { yield return null; yield return new WaitForEndOfFrame(); Run(label); }

        /// <summary>Audits everything currently active under the app root. Returns the issue list.</summary>
        public static List<string> Run(string label)
        {
            var issues = new List<string>(); var notes = new List<string>(); int checks = 0;
            var root = GameApp.I != null ? GameApp.I.transform : null;
            if (root == null) return issues;
            void Off(string what, float off) { checks++; if (Mathf.Abs(off) >= 0.5f - 1e-4f) issues.Add(what + " off " + off.ToString("+0.0;-0.0") + "px"); }

            // 1. text on its anchor
            foreach (var t in root.GetComponentsInChildren<PixelText>(false))
            {
                if (t.Align != TextAlign.Center || string.IsNullOrEmpty(t.Text)) continue;
                checks++;
                if (Mathf.Abs(t.AnchorX * 2 - Mathf.Round(t.AnchorX * 2)) > 1e-3f) issues.Add("text '" + t.Text + "' anchor " + t.AnchorX + " not on a half pixel");
                float toff = WorldLeft(t) + t.Width / 2f - WorldAnchor(t);
                // a single glyph has no inter-letter gap to widen: an odd glyph on a whole-pixel centre is as close as pixels allow
                if (t.Text.Length == 1 && Mathf.Abs(Mathf.Abs(toff) - 0.5f) < 1e-3f) { notes.Add("'" + t.Text + "'"); continue; }
                Off("text '" + t.Text + "'", toff);
            }

            // 2. labels inside buttons
            var buttons = root.GetComponentsInChildren<UiButton>(false).Where(b => b.enabled).ToList();
            foreach (var b in buttons)
            {
                var label2 = b.GetComponentsInChildren<PixelText>(false).FirstOrDefault(p => p.name == "label");
                if (label2 == null || string.IsNullOrEmpty(label2.Text)) continue;
                var icon = b.visual != null ? b.visual.GetComponentsInChildren<SpriteRenderer>(false).FirstOrDefault(s => s.sprite != null && s.sprite.name.StartsWith("icon_")) : null;
                float rc = b.rect.x + b.rect.width / 2f;
                string nm = "button '" + label2.Text + "'";
                if (label2.Align == TextAlign.Center)
                {
                    float a = WorldAnchor(label2);
                    // Big buttons with an icon centre the label in the space right of the icon (x+48 .. x+w)
                    float alt = b.rect.x + (48 + b.rect.width) / 2f;
                    Off(nm + " label", Mathf.Abs(a - alt) < 1e-3f && icon != null ? 0 : a - rc);
                }
                else if (icon != null)
                {
                    float l = Mathf.Min(icon.bounds.min.x, WorldLeft(label2)), r = Mathf.Max(icon.bounds.max.x, WorldLeft(label2) + label2.Width);
                    float off = (l + r) / 2f - rc;
                    // an odd group in an even button can only be centred to half a pixel: floor rule (left gap = right gap - 1)
                    Off(nm + " icon+label group", Mathf.Abs(Mathf.Abs(off) - 0.5f) < 1e-3f ? 0 : off);
                }
            }

            // 3a. rows of buttons
            foreach (var row in buttons.Where(b => b.rect.width < Px.W - 1).GroupBy(b => (Mathf.Round(b.rect.y), Mathf.Round(b.rect.height), b.layer)))
            {
                var list = row.ToList(); if (list.Count < 2) continue;
                float l = list.Min(b => b.rect.xMin), r = list.Max(b => b.rect.xMax);
                if (r - l < Px.W / 2f) continue;
                Off("button row y" + row.Key.Item1 + " (" + string.Join("/", list.Select(Name)) + ")", (l + r) / 2f - Mid);
            }
            // 3a'. declared groups (e.g. portrait + name plank): the union of each group is screen-centred
            var grouped = new HashSet<Transform>();
            foreach (var grp in root.GetComponentsInChildren<AuditGroup>(false).GroupBy(g => g.key))
            {
                float l = float.MaxValue, r = float.MinValue;
                foreach (var g in grp)
                {
                    grouped.Add(g.transform);
                    var ub = g.GetComponent<UiButton>();
                    if (ub != null) { l = Mathf.Min(l, ub.rect.xMin); r = Mathf.Max(r, ub.rect.xMax); continue; }
                    foreach (var sr in g.GetComponentsInChildren<SpriteRenderer>(false)) if (sr.enabled && sr.sprite != null) { l = Mathf.Min(l, sr.bounds.min.x); r = Mathf.Max(r, sr.bounds.max.x); }
                }
                if (l < r) Off("group '" + grp.Key + "'", (l + r) / 2f - Mid);
            }
            // 3b. wide single buttons
            foreach (var b in buttons) if (!grouped.Contains(b.transform) && b.rect.width >= 100 && b.rect.width < Px.W - 1 && !buttons.Any(o => o != b && Mathf.Abs(o.rect.y - b.rect.y) < 0.5f && Mathf.Abs(o.rect.height - b.rect.height) < 0.5f && o.layer == b.layer && o.rect.width < Px.W - 1))
                    Off("button " + Name(b), b.rect.center.x - Mid);
            // 3c. wide panels / ribbons / bodies
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(false))
            {
                if (!sr.enabled || sr.sprite == null) continue;
                string n = sr.sprite.name;
                bool wide = sr.sprite.rect.width >= 100 && (n.StartsWith("parch_") || n.StartsWith("ribbon") || n == "title_logo" || n == "title_signore" || n == "deck" || n == "title_rock");
                if (!wide) continue;
                if (sr.GetComponentInParent<UiButton>() != null) continue; // covered by the button checks
                Off("sprite " + n + " @" + sr.transform.parent?.name, sr.bounds.center.x - Mid);
            }
            // 4. plates named hintbg / toast plates
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(false))
                if (sr.enabled && (sr.name == "hintbg")) Off("hint plate", sr.bounds.center.x - Mid);

            // 5. fit: text clipped by the screen edge or the camera hole, text running into other text, button subs under icons
            var texts = root.GetComponentsInChildren<PixelText>(false).Where(t => !string.IsNullOrEmpty(t.Text) && t.Visible).ToList();
            texts = texts.Where(t => { var b = Box(t); return b.xMax > 0 && b.xMin < Px.W && b.yMax > 0 && b.yMin < Px.H; }).ToList();   // wholly off-screen (slid-away boards) is not clipping
            var boxes = texts.Select(Box).ToList();
            for (int i = 0; i < texts.Count; i++)
            {
                var bx = boxes[i]; checks++;
                if (bx.xMin < 0 || bx.xMax > Px.W) issues.Add("text '" + texts[i].Text + "' clipped by the screen edge (x " + bx.xMin + ".." + bx.xMax + ")");
                if (bx.yMin < CamHoleBottom && bx.xMax > CamHoleL && bx.xMin < CamHoleR) issues.Add("text '" + texts[i].Text + "' under the camera hole");
                for (int j = i + 1; j < texts.Count; j++)
                {
                    if (Mathf.Abs(texts[i].Order - texts[j].Order) >= 40) continue;          // a different layer (overlay over screen) may cover
                    var o = boxes[j];
                    if (Mathf.Min(bx.xMax, o.xMax) - Mathf.Max(bx.xMin, o.xMin) >= 1 && Mathf.Min(bx.yMax, o.yMax) - Mathf.Max(bx.yMin, o.yMin) >= 1)
                    { checks++; issues.Add("text '" + texts[i].Text + "' overlaps '" + texts[j].Text + "'"); }
                }
            }
            foreach (var b in buttons)
            {
                var vis = b.visual != null ? b.visual : b.transform;
                var srs = vis.GetComponentsInChildren<SpriteRenderer>(false);
                var plate = srs.FirstOrDefault(r => r.name == "subplate" && r.enabled);
                var icon = srs.FirstOrDefault(r => r.enabled && r.sprite != null && r.sprite.name.StartsWith("icon_"));
                var lbl = b.GetComponentsInChildren<PixelText>(false).FirstOrDefault(p => p.name == "label" && !string.IsNullOrEmpty(p.Text));
                if (plate != null)
                {
                    checks++;
                    if (icon != null && plate.bounds.min.x < icon.bounds.max.x - 0.01f) issues.Add("button '" + Name(b) + "' sub plate runs under its icon");
                    if (plate.bounds.min.x < b.rect.xMin + 2 || plate.bounds.max.x > b.rect.xMax - 2) issues.Add("button '" + Name(b) + "' sub plate leaves the button");
                }
                if (lbl != null)
                {
                    checks++;
                    var lb = Box(lbl);
                    if (lb.xMin < b.rect.xMin + 2 || lb.xMax > b.rect.xMax - 2) issues.Add("button '" + lbl.Text + "' label wider than the button");
                    if (icon != null && lb.xMin < icon.bounds.max.x && lb.xMax > icon.bounds.min.x && lb.yMax > -icon.bounds.max.y && lb.yMin < -icon.bounds.min.y) issues.Add("button '" + lbl.Text + "' label overlaps its icon");
                }
            }

            TotalChecks += checks; TotalIssues += issues.Count;
            var sb = new StringBuilder();
            sb.Append("[CenterAudit] ").Append(label).Append(": ").Append(checks).Append(" checks, ").Append(issues.Count).Append(issues.Count == 0 ? " issues - ALL CENTRED" : " issues");
            foreach (var i in issues) sb.Append("\n   - ").Append(i);
            if (notes.Count > 0) sb.Append("\n   (best possible: ").Append(notes.Count).Append(" single-glyph labels ").Append(string.Join(" ", notes.Distinct())).Append(" sit 0.5 px right - an odd glyph cannot split evenly around a whole pixel)");
            Log.Add(sb.ToString());
            if (issues.Count == 0) Debug.Log(sb.ToString()); else Debug.LogWarning(sb.ToString());
            return issues;
        }

        // Seeker punch-hole camera, top centre (approximate keep-out in logical px)
        const float CamHoleL = 106, CamHoleR = 134, CamHoleBottom = 12;

        /// <summary>Glyph box of a text in logical px (x right, y down).</summary>
        static Rect Box(PixelText t)
        {
            float left = WorldLeft(t);
            float top = -t.transform.position.y + 3;   // PixelText sits at (left - 3, top - 3) with y flipped
            return new Rect(left, top, t.Width, 8 * t.Scale);
        }

        static string Name(UiButton b) { var l = b.GetComponentsInChildren<PixelText>(false).FirstOrDefault(p => p.name == "label"); return l != null ? l.Text : b.name; }

        // PixelText Left/AnchorX are local to the text's parent; screen roots sit at the origin, but scaled/moved parents
        // (avatar frames, sliding cards) are converted through the transform.
        static float WorldAnchor(PixelText t) => ToWorldX(t.transform.parent, t.AnchorX);
        static float WorldLeft(PixelText t) => ToWorldX(t.transform.parent, t.Left);
        static float ToWorldX(Transform parent, float x) => parent == null ? x : parent.TransformPoint(new Vector3(x, 0, 0)).x;

        /// <summary>Pure math check of the centring rule (SelfTest): every string at whole and half anchors.</summary>
        public static bool ParitySelfTest(out string fail)
        {
            fail = null;
            string[] samples = { "SOLO CAMPAIGN", "PVP 1V1", "WALLET", "RANKS", "SETTINGS", "HOW TO PLAY", "A", "II", "SINK SHIPS. MINE ORE.", "ROUND 431893", "PROFILE", "SAVE", "?", "NAME AVAILABLE" };
            float[] anchors = { 120f, 120.5f, 13f, 54f, 196f, 60.5f };
            int[] scales = { 1, 2 };
            foreach (var s in samples) foreach (var a in anchors) foreach (var sc in scales)
                    {
                        int gap = PixelFont.CenterGap(s, a, sc);
                        int w = PixelFont.Measure(s, sc) + (gap >= 0 ? 1 : 0);
                        float left = a - w / 2f;
                        bool whole = Mathf.Abs(left - Mathf.Round(left)) < 1e-4f;
                        bool canFix = s.Length >= 2;
                        if (!whole && canFix) { fail = s + " @" + a + " x" + sc + " left=" + left; return false; }
                        if (gap >= s.Length - 1) { fail = s + " gap index " + gap; return false; }
                    }
            return true;
        }
    }
}
