using System.Collections.Generic;
using UnityEngine;

namespace OreSeas
{
    /// <summary>SFX pool + music with crossfade and a synced "tension" layer for battle (spec: calm aiming layer + tension layer near lock).</summary>
    public sealed class AudioManager : MonoBehaviour
    {
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        AudioSource[] sfx; int next;
        AudioSource musicA, musicB, tension; bool aIsMain = true;
        string currentMusic; float fade = 1f, fadeSpeed = 1.2f, tensionTarget, tensionLevel;
        public float MusicVolume = 0.55f, SfxVolume = 0.9f;

        public static AudioManager Create(Transform parent)
        {
            var go = new GameObject("Audio"); go.transform.SetParent(parent, false);
            var am = go.AddComponent<AudioManager>();
            am.sfx = new AudioSource[12];
            for (int i = 0; i < am.sfx.Length; i++) { am.sfx[i] = go.AddComponent<AudioSource>(); am.sfx[i].playOnAwake = false; }
            am.musicA = am.MakeMusic(); am.musicB = am.MakeMusic(); am.tension = am.MakeMusic();
            return am;
        }

        AudioSource MakeMusic() { var s = gameObject.AddComponent<AudioSource>(); s.loop = true; s.playOnAwake = false; s.volume = 0; return s; }

        AudioClip Clip(string name)
        {
            if (clips.TryGetValue(name, out var c)) return c;
            c = Resources.Load<AudioClip>("Audio/" + name);
            if (c == null) Debug.LogWarning("[OreSeas] missing audio " + name);
            clips[name] = c; return c;
        }

        public void Sfx(string name, float delay = 0f, float vol = 1f, float pitch = 1f)
        {
            if (!Settings.Sfx) return;
            var c = Clip("sfx_" + name); if (c == null) return;
            var s = sfx[next]; next = (next + 1) % sfx.Length;
            s.Stop(); s.clip = c; s.volume = SfxVolume * vol; s.pitch = pitch;
            if (delay > 0) s.PlayDelayed(delay); else s.Play();
        }

        public void Sting(string name, float vol = 1f)
        {
            if (!Settings.Music) return;
            var c = Clip(name); if (c == null) return;
            var s = sfx[next]; next = (next + 1) % sfx.Length;
            s.Stop(); s.clip = c; s.volume = MusicVolume * 1.2f * vol; s.pitch = 1; s.Play();
        }

        /// <summary>Crossfade to a music loop. tensionLayer (optional) is started in sample-sync with the main loop.</summary>
        public void Music(string name, string tensionLayer = null)
        {
            if (name == currentMusic) return;
            currentMusic = name;
            var incoming = aIsMain ? musicB : musicA;
            aIsMain = !aIsMain;
            incoming.clip = name == null ? null : Clip(name);
            incoming.volume = 0;
            if (incoming.clip != null) incoming.Play();
            fade = 0;
            tension.Stop(); tensionLevel = 0; tensionTarget = 0;
            if (tensionLayer != null)
            {
                tension.clip = Clip(tensionLayer);
                if (tension.clip != null) { tension.volume = 0; tension.timeSamples = 0; tension.Play(); }
            }
        }

        /// <summary>0..1, raises the battle tension layer (e.g. as the lock approaches).</summary>
        public void SetTension(float v) { tensionTarget = Mathf.Clamp01(v); }
        public void DuckMusic(float seconds) { duckUntil = Time.unscaledTime + seconds; }
        float duckUntil;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            fade = Mathf.MoveTowards(fade, 1, dt * fadeSpeed);
            var main = aIsMain ? musicA : musicB; var old = aIsMain ? musicB : musicA;
            float mv = Settings.Music ? MusicVolume : 0;
            if (Time.unscaledTime < duckUntil) mv *= 0.35f;
            main.volume = mv * fade; old.volume = mv * (1 - fade);
            if (fade >= 1 && old.isPlaying) old.Stop();
            tensionLevel = Mathf.MoveTowards(tensionLevel, tensionTarget, dt * 0.8f);
            if (tension.clip != null)
            {
                tension.volume = mv * tensionLevel * fade;
                if (main.isPlaying && tension.isPlaying && main.clip != null && tension.clip.samples == main.clip.samples && Mathf.Abs(tension.timeSamples - main.timeSamples) > 2048)
                    tension.timeSamples = main.timeSamples; // keep layers locked
            }
        }
    }

    public static class Haptics
    {
        public static void Buzz(int ms)
        {
            if (!Settings.Vibrate) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var vib = act.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    if (vib == null) return;
                    using (var ver = new AndroidJavaClass("android.os.Build$VERSION"))
                    {
                        if (ver.GetStatic<int>("SDK_INT") >= 26)
                        {
                            using (var ve = new AndroidJavaClass("android.os.VibrationEffect"))
                            using (var eff = ve.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, -1))
                                vib.Call("vibrate", eff);
                        }
                        else vib.Call("vibrate", (long)ms);
                    }
                }
            }
            catch (System.Exception) { if (ms >= 60) Handheld.Vibrate(); }
#elif UNITY_ANDROID || UNITY_IOS
            if (ms >= 60) Handheld.Vibrate();
#endif
        }
    }
}
