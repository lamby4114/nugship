using System;
using System.Collections;
using UnityEngine;

namespace OreSeas.View
{
    public sealed class Particle : MonoBehaviour
    {
        Vector2 vel, pos; float g, life, t; SpriteRenderer sr;
        public void Init(Vector2 v, float gravity, float lifetime) { vel = v; g = gravity; life = lifetime; var lp = transform.localPosition; pos = new Vector2(lp.x, -lp.y); sr = GetComponent<SpriteRenderer>(); }
        void Update()
        {
            t += Time.deltaTime; vel.y += g * Time.deltaTime; pos += vel * Time.deltaTime;
            transform.localPosition = Px.P(pos.x, pos.y);
            if (t > life * 0.7f && sr) { var c = sr.color; c.a = Mathf.Clamp01(1 - (t - life * 0.7f) / (life * 0.3f)); sr.color = c; }
            if (t >= life) Destroy(gameObject);
        }
    }
}
