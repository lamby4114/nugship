using System;
using System.Collections;
using UnityEngine;

namespace OreSeas.View
{
    /// <summary>Shakes a transform by whole pixels.</summary>
    public sealed class Shaker : MonoBehaviour
    {
        float amt, t; Vector3 rest; bool shaking;
        public void Shake(float pixels, float dur) { if (!shaking) rest = transform.localPosition; shaking = true; amt = pixels; t = dur; }
        void Update()
        {
            if (!shaking) return;
            t -= Time.deltaTime;
            if (t <= 0) { shaking = false; transform.localPosition = rest; return; }
            transform.localPosition = rest + new Vector3(Mathf.Round(UnityEngine.Random.Range(-amt, amt)), Mathf.Round(UnityEngine.Random.Range(-amt, amt)), 0);
        }
    }
}
