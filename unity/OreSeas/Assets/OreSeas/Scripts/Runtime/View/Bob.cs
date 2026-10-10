using UnityEngine;

namespace OreSeas.View
{
    /// <summary>Bobs a transform by whole pixels around its rest position (mockup: round(sin(t*f+ph)*amp)).</summary>
    public sealed class Bob : MonoBehaviour
    {
        public float amp = 1, freq = 2.2f, phase; public Vector3 rest;
        public static Bob Add(Transform t, float amp, float freq, float phase) { var b = t.gameObject.AddComponent<Bob>(); b.amp = amp; b.freq = freq; b.phase = phase; b.rest = t.localPosition; return b; }
        void Update() { transform.localPosition = rest + new Vector3(0, -Mathf.Round(Mathf.Sin(Time.time * freq + phase) * amp), 0); }
    }
}
