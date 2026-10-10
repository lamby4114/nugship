using UnityEngine;

namespace OreSeas.View
{
    /// <summary>Simple drifting sprite (clouds, fog banks, fish, gulls).</summary>
    public sealed class Drift : MonoBehaviour
    {
        public float speed, minX = -70, maxX = 300, baseY, bobAmp, bobFreq = 1f, phase;
        float x;
        public static Drift Add(SpriteRenderer sr, float x, float y, float speed, float minX = -70, float maxX = 300)
        {
            var d = sr.gameObject.AddComponent<Drift>(); d.x = x; d.baseY = y; d.speed = speed; d.minX = minX; d.maxX = maxX; d.phase = Random.value * 6f;
            return d;
        }
        void Update()
        {
            x += speed * Time.deltaTime;
            if (speed > 0 && x > maxX) x = minX; if (speed < 0 && x < minX) x = maxX;
            float y = baseY + (bobAmp > 0 ? Mathf.Round(Mathf.Sin(Time.time * bobFreq + phase) * bobAmp) : 0);
            transform.localPosition = Px.P(x, y);
        }
    }
}
