using System;
using System.IO;
using UnityEngine;

namespace OreSeas
{
    /// <summary>
    /// "Upload your own image": opens the Android photo picker (Plugins/Android/NugshipPicker.androidlib, no storage
    /// permission needed), which returns a 128x128 centre-cropped PNG; we pixelate it to 20x20. Editor: file dialog.
    /// </summary>
    public sealed class AvatarPicker : MonoBehaviour
    {
        Action<Texture2D, string> done; float nextPoll;
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass picker;
#endif
        public static bool Supported => Application.platform == RuntimePlatform.Android || Application.isEditor;

        /// <summary>done(photo, error): photo is a readable Texture2D (any size) or null (cancelled / error text).</summary>
        public static void Pick(Action<Texture2D, string> done)
        {
#if UNITY_EDITOR
            var path = UnityEditor.EditorUtility.OpenFilePanel("Captain portrait", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path)) { done(null, null); return; }
            done(Load(path), null);
#elif UNITY_ANDROID
            var go = new GameObject("AvatarPicker"); DontDestroyOnLoad(go);
            var p = go.AddComponent<AvatarPicker>(); p.done = done;
            try
            {
                p.picker = new AndroidJavaClass("com.nugship.picker.PickerActivity");
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                    p.picker.CallStatic("pick", act);
            }
            catch (Exception e) { Debug.LogWarning("[OreSeas] picker: " + e.Message); p.Finish(null, "PHOTO PICKER UNAVAILABLE"); }
#else
            done(null, "PHOTO UPLOAD WORKS ON THE PHONE");
#endif
        }

        public static Texture2D Load(string path)
        {
            try
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                return t.LoadImage(File.ReadAllBytes(path)) ? t : null;
            }
            catch (Exception e) { Debug.LogWarning("[OreSeas] load photo: " + e.Message); return null; }
        }

        void Finish(Texture2D t, string err) { var d = done; done = null; Destroy(gameObject); d?.Invoke(t, err); }

#if UNITY_ANDROID && !UNITY_EDITOR
        void Update()
        {
            if (done == null || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 0.2f;
            string r = picker.CallStatic<string>("poll");   // null = still open, "" = cancelled, "!msg" = error, else PNG path
            if (r == null) return;
            if (r.Length == 0) { Finish(null, null); return; }
            if (r[0] == '!') { Finish(null, "COULD NOT READ PHOTO"); Debug.LogWarning("[OreSeas] picker: " + r); return; }
            var t = Load(r);
            Finish(t, t == null ? "COULD NOT READ PHOTO" : null);
        }
        void OnDestroy() { picker?.Dispose(); }
#endif
    }
}
