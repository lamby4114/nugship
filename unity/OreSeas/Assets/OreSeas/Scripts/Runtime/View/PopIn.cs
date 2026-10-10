using System.Collections.Generic;
using OreSeas.Game;
using UnityEngine;

namespace OreSeas.View
{
    /// <summary>Quick pixel "pop" (scale 1.4 -> 1) for UI feedback; scale stays integer-friendly at the end.</summary>
    public sealed class PopIn : MonoBehaviour
    {
        float t;
        void Update()
        {
            t += Time.deltaTime * 6f;
            float s = t >= 1 ? 1 : 1 + 0.25f * (1 - t);
            transform.localScale = new Vector3(s, s, 1);
            if (t >= 1) Destroy(this);
        }
    }
}
