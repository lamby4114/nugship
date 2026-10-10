using UnityEngine;

namespace OreSeas.View
{
    /// <summary>Fades a SpriteRenderer in to a peak alpha, then out, then destroys it (soft lightning bolts).</summary>
    public sealed class FadeOut : MonoBehaviour
    {
        SpriteRenderer sr; float t, peak, attack, release;
        public FadeOut Init(float peakAlpha, float attackSec, float releaseSec)
        {
            sr = GetComponent<SpriteRenderer>(); peak = peakAlpha; attack = Mathf.Max(0.01f, attackSec); release = Mathf.Max(0.01f, releaseSec);
            Set(0); return this;
        }
        void Update()
        {
            t += Time.deltaTime;
            if (t < attack) Set(Ease.OutQuad(t / attack));
            else { float a = (t - attack) / release; if (a >= 1) { Destroy(gameObject); return; } Set((1 - a) * (1 - a)); }
        }
        void Set(float k) { if (sr) { var c = sr.color; c.a = peak * k; sr.color = c; } }
    }
}
