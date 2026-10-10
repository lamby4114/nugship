using System;
using System.Collections.Generic;
using UnityEngine;

namespace OreSeas
{
    /// <summary>Tappable area in logical px. Pressed state nudges the visual down 2 px (like the mockup fireButton).</summary>
    public sealed class UiButton : MonoBehaviour
    {
        public Rect rect; public int layer; public bool interactable = true; public Action onClick; public string sfx = "tap";
        public Transform visual; public int buzz = 12;
        bool pressed;
        static readonly List<UiButton> all = new List<UiButton>();
        static UiButton active;
        public static int TopLayer;

        public static UiButton Create(Transform parent, float x, float y, float w, float h, Action onClick, int layer = 0, string name = "Button")
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var b = go.AddComponent<UiButton>(); b.rect = new Rect(x, y, w, h); b.onClick = onClick; b.layer = layer;
            b.visual = go.transform;
            return b;
        }

        void OnEnable() { all.Add(this); }
        void OnDisable() { all.Remove(this); if (active == this) active = null; SetPressed(false); }

        public bool Contains(Vector2 p) => rect.Contains(p);

        void SetPressed(bool p)
        {
            if (pressed == p || visual == null) return;
            pressed = p;
            visual.localPosition += new Vector3(0, p ? -2 : 2, 0);
        }

        /// <summary>Called once per frame by GameApp after InputRouter.Poll(). Returns true if a button consumed the input.</summary>
        public static bool Route()
        {
            if (InputRouter.Down)
            {
                active = null;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    var b = all[i];
                    if (b == null || !b.interactable || b.layer != TopLayer || !b.isActiveAndEnabled) continue;
                    if (b.Contains(InputRouter.Pos)) { active = b; b.SetPressed(true); break; }
                }
                return active != null;
            }
            if (active != null)
            {
                if (InputRouter.Held) { active.SetPressed(active.Contains(InputRouter.Pos)); return true; }
                if (InputRouter.Up)
                {
                    var b = active; active = null; b.SetPressed(false);
                    if (b.Contains(InputRouter.Pos) && b.interactable)
                    {
                        if (!string.IsNullOrEmpty(b.sfx)) GameApp.Audio?.Sfx(b.sfx);
                        if (b.buzz > 0) Haptics.Buzz(b.buzz);
                        b.onClick?.Invoke();
                    }
                    return true;
                }
            }
            return false;
        }
        public static bool Busy => active != null;
    }
}
