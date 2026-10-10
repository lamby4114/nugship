using OreSeas.Game;
using UnityEngine;

namespace OreSeas.View
{
    /// <summary>Nuggy the mascot (character only - no token). Poses exported per scale: s085, s115, s250.
    /// Battle avatar is reactive: idle (tail sway + blink), Hop/Happy on a hit, Dance (+ gold sparkle) on a crit,
    /// Wince (ears flat, squeezed eyes, sweat drop) on a miss or when your ship is hit, Sleepy (half-closed eyes, Zz) while locked.</summary>
    public sealed class NuggyView : MonoBehaviour
    {
        public enum Pose { Idle, Hop, Happy, Dance, Wince, Sleepy }
        string scale; SpriteRenderer sr; Sprite[] idle, happy, d1, d2, sleepy; Sprite blink, hop, wince;
        Pose pose = Pose.Idle; float t, poseUntil, nextBlink = 2.5f, blinkUntil; Vector3 rest;

        public static NuggyView Create(Transform parent, string scale, float x, float y, int order)
        {
            var sr = Gfx.Sprite(parent, (Sprite)null, x, y, order, "Nuggy");
            var n = sr.gameObject.AddComponent<NuggyView>(); n.sr = sr; n.scale = scale; n.rest = sr.transform.localPosition;
            string p = "nug_" + scale + "_";
            n.idle = Atlas.Seq(p + "idle"); n.happy = Atlas.Seq(p + "happy"); n.d1 = Atlas.Seq(p + "dance1"); n.d2 = Atlas.Seq(p + "dance2");
            n.blink = Atlas.Get(p + "blink_0"); n.hop = Atlas.Get(p + "hop_0");
            n.wince = Atlas.Has(p + "wince_0") ? Atlas.Get(p + "wince_0") : null; n.sleepy = Atlas.Has(p + "sleepy_0") ? Atlas.Seq(p + "sleepy") : null;
            n.t = Random.value * 6f;
            return n;
        }

        public void Play(Pose p, float seconds) { pose = p; poseUntil = Time.time + seconds; }
        /// <summary>Like Play, but never interrupts a stronger reaction that is still running (Dance > Happy/Hop > Wince > Sleepy).</summary>
        public void React(Pose p, float seconds) { if (pose != Pose.Idle && Time.time < poseUntil && Rank(pose) > Rank(p)) return; Play(p, seconds); }
        static int Rank(Pose p) => p == Pose.Dance ? 4 : p == Pose.Happy || p == Pose.Hop ? 3 : p == Pose.Wince ? 2 : p == Pose.Sleepy ? 1 : 0;
        public Pose Current => pose;
        public int Order { set { sr.sortingOrder = value; } }

        void Update()
        {
            t += Time.deltaTime;
            if (pose != Pose.Idle && Time.time > poseUntil) pose = Pose.Idle;
            Vector3 off = Vector3.zero; Sprite s;
            switch (pose)
            {
                case Pose.Hop: s = hop ?? Frame(idle, 7.6f); off.y = Mathf.Round(Mathf.Abs(Mathf.Sin((poseUntil - Time.time) * 12f)) * 4); break;
                case Pose.Happy: s = Frame(happy, 7.6f); off.y = Mathf.Round(Mathf.Abs(Mathf.Sin(t * 7f)) * 2); break;
                case Pose.Wince: s = wince ?? Frame(idle, 7.6f); off.y = (poseUntil - Time.time) > 0.55f ? -1 : 0; break;   // tiny flinch down, then hold
                case Pose.Sleepy: s = sleepy != null && sleepy.Length > 0 ? Frame(sleepy, 2.8f) : (blink ?? Frame(idle, 7.6f)); break;
                case Pose.Dance: s = ((int)(t * 2.5f) % 2 == 0) ? Frame(d1, 7.6f) : Frame(d2, 7.6f); off.y = Mathf.Round(Mathf.Abs(Mathf.Sin(t * 9f)) * 3); break;
                default:
                    s = Frame(idle, 48f / (Mathf.PI * 2));
                    nextBlink -= Time.deltaTime;
                    if (nextBlink <= 0) { blinkUntil = Time.time + 0.12f; nextBlink = 2.5f + Random.value * 3f; }
                    if (Time.time < blinkUntil && blink) s = blink;
                    break;
            }
            sr.sprite = s; transform.localPosition = rest + off;
        }

        Sprite Frame(Sprite[] f, float fps) => f == null || f.Length == 0 ? null : f[(int)(t * fps) % f.Length];
    }
}
