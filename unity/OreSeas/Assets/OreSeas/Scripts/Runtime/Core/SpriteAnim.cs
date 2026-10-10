using UnityEngine;

namespace OreSeas
{
    /// <summary>Flipbook animation on a SpriteRenderer (frame sequences exported from the mockups).</summary>
    public sealed class SpriteAnim : MonoBehaviour
    {
        public Sprite[] frames; public float fps = 12; public bool loop = true; public bool destroyOnEnd; public float delay;
        public float timeOffset;
        SpriteRenderer sr; float t;

        public static SpriteAnim Play(Transform parent, string seq, float x, float y, int order, float fps, bool loop, bool destroyOnEnd, float delay = 0)
        {
            var sr = Gfx.Sprite(parent, (Sprite)null, x, y, order, seq);
            var a = sr.gameObject.AddComponent<SpriteAnim>();
            a.frames = Atlas.Seq(seq); a.fps = fps; a.loop = loop; a.destroyOnEnd = destroyOnEnd; a.delay = delay;
            a.sr = sr; a.Apply();
            return a;
        }

        public void SetFrames(Sprite[] f, bool restart = true) { frames = f; if (restart) t = 0; Apply(); }
        public void Restart() { t = 0; Apply(); }
        public SpriteRenderer Renderer => sr ? sr : (sr = GetComponent<SpriteRenderer>());

        void Awake() { sr = GetComponent<SpriteRenderer>(); }
        void Update() { t += Time.deltaTime; Apply(); }

        void Apply()
        {
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            if (frames == null || frames.Length == 0) { if (sr) sr.sprite = null; return; }
            float tt = t - delay + timeOffset;
            if (tt < 0) { sr.sprite = null; return; }
            int i = (int)(tt * fps);
            if (i >= frames.Length)
            {
                if (loop) i %= frames.Length;
                else
                {
                    if (destroyOnEnd) { Destroy(gameObject); return; }
                    i = frames.Length - 1;
                }
            }
            sr.sprite = frames[i];
        }
    }
}
