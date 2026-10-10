using System;
using System.Collections;
using UnityEngine;

namespace OreSeas.View
{
    public sealed class Rise : MonoBehaviour
    {
        float t, x, y; PixelText txt;
        public void Init(float x, float y) { this.x = x; this.y = y; txt = GetComponent<PixelText>(); }
        void Update()
        {
            t += Time.deltaTime;
            txt.MoveTo(x, y - Mathf.Round(Ease.OutQuad(t / 1.2f) * 18));
            if (t > 1.2f) txt.SetAlpha(Mathf.Clamp01(1 - (t - 1.2f) / 0.5f));
            if (t > 1.7f) Destroy(gameObject);
        }
    }
}
