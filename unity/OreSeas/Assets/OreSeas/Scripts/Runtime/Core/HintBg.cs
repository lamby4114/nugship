using System;
using System.Collections.Generic;
using UnityEngine;

namespace OreSeas
{
    sealed class HintBg : MonoBehaviour
    {
        public SpriteRenderer bg; PixelText t; string last;
        void Awake() { t = GetComponent<PixelText>(); }
        void LateUpdate()
        {
            if (t == null || bg == null || t.Text == last) return;
            if (!PixelFont.LegacyCenter && PixelFont.Measure(t.Text) > Ui.HintMax)
            {
                if (Debug.isDebugBuild) Debug.LogWarning("[OreSeas] hint too wide, trimmed: '" + t.Text + "'");
                t.Set(Lay.Fit(t.Text, Ui.HintMax));
            }
            last = t.Text; int w = PixelFont.CenteredWidth(last, Px.W / 2f) + 10;
            var y = -bg.transform.localPosition.y;
            Gfx.SetRect(bg, Px.W / 2f - w / 2f, y, w, 11);
            bg.enabled = !string.IsNullOrEmpty(last);
        }
    }
}
