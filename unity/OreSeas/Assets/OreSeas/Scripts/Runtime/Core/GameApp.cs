using System;
using System.Collections;
using OreSeas.Game;
using OreSeas.Net;
using OreSeas.Services;
using UnityEngine;

namespace OreSeas
{
    /// <summary>Parameters handed from menus -> fleet -> battle.</summary>
    public sealed class MatchSetup
    {
        public string mode = "solo"; // solo | daily | ranked
        public CaptainDef rival; public BiomeDef biome; public int campaignIndex = -1; public int seed;
        public Fleet fleet; public byte[] salt; public string commit;
        public MatchTicket ticket;
        /// <summary>Set when the match is run by the NUGSHIP server (PvP, private code, solo vs server AI).</summary>
        public OnlineSession online;
    }

    /// <summary>Owns the camera, services, screen stack, input routing and transitions.</summary>
    public sealed class GameApp : MonoBehaviour
    {
        public static GameApp I { get; private set; }
        public static AudioManager Audio { get; private set; }
        public static IWalletService Wallet { get; private set; }
        public static IMatchService Match { get; private set; }
        public static ILeaderboardService Leaderboard { get; private set; }
        public static ServerClient Net { get; private set; }
        public static MatchSetup Setup = new MatchSetup();

        Transform screensRoot, overlayRoot; ScreenBase current; bool transitioning;
        PixelCanvas[] dither; SpriteRenderer fadeSr;

        public static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("OreSeasApp");
            DontDestroyOnLoad(go);
            go.AddComponent<GameApp>();
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.Portrait;
            SetupCamera();
            Atlas.LoadAll();
            Audio = AudioManager.Create(transform);
            Wallet = WalletFactory.Create();
            Match = new LocalSimMatchService();
            Leaderboard = new ServerLeaderboardService(new LocalLeaderboardService());
            Net = ServerClient.Create(transform);   // connects lazily (title / matchmaking / ranks call EnsureConnected)
            screensRoot = new GameObject("Screens").transform; screensRoot.SetParent(transform, false);
            overlayRoot = new GameObject("Overlay").transform; overlayRoot.SetParent(transform, false);
            BuildFader();
            PixelFont.LegacyCenter = ServerConfig.HasFlag("-legacycenter");   // desktop "before" screenshots only
            if (ServerConfig.HasFlag("-nugshot")) gameObject.AddComponent<ShotMode>();
            else if (ServerConfig.HasFlag("-nugplay")) gameObject.AddComponent<PlayTest>();   // desktop end-to-end play-test harness only
        }

        void Start() { Go<Screens.TitleScreen>(fade: false); }

        void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) { var cgo = new GameObject("Main Camera") { tag = "MainCamera" }; cam = cgo.AddComponent<Camera>(); }
            cam.orthographic = true;
            cam.orthographicSize = Px.H / 2f;
            cam.transform.position = new Vector3(Px.W / 2f, -Px.H / 2f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0x10, 0x0c, 0x0a, 255);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
#if ORESEAS_URP
            var ppc = cam.GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
            if (ppc == null) ppc = cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
            ppc.assetsPPU = 1;
            ppc.refResolutionX = Px.W; ppc.refResolutionY = Px.H;
            ppc.gridSnapping = UnityEngine.Rendering.Universal.PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            ppc.cropFrame = UnityEngine.Rendering.Universal.PixelPerfectCamera.CropFrame.Windowbox; // integer scale; Seeker 1200x2670 = exactly 5x
#else
            float sw = Screen.width, sh = Screen.height, s = Mathf.Min(sw / Px.W, sh / Px.H);
            float w = Px.W * s / sw, h = Px.H * s / sh; cam.rect = new Rect((1 - w) / 2, (1 - h) / 2, w, h);
#endif
            Px.Cam = cam;
        }

        // ---------- screens ----------
        public static void Go<T>(Action<T> init = null, bool fade = true) where T : ScreenBase
        {
            if (I == null || I.transitioning) return;
            I.StartCoroutine(I.SwitchTo(init, fade));
        }

        IEnumerator SwitchTo<T>(Action<T> init, bool fade) where T : ScreenBase
        {
            transitioning = true;
            if (fade) yield return Fade(0, 4, 0.22f);
            UiButton.TopLayer = 0; Lay.ClearBack();
            if (current != null) Destroy(current.gameObject);
            foreach (Transform c in overlayRoot) Destroy(c.gameObject);
            yield return null;
            var go = new GameObject(typeof(T).Name); go.transform.SetParent(screensRoot, false);
            var s = go.AddComponent<T>();
            init?.Invoke(s);
            current = s;
            s.Build();
            CenterAudit.RunDeferred(this, typeof(T).Name);
            if (fade) yield return Fade(4, 0, 0.25f);
            transitioning = false;
        }

        void BuildFader()
        {
            dither = new PixelCanvas[5];
            var go = new GameObject("Fader"); go.transform.SetParent(transform, false);
            go.transform.localPosition = Px.P(0, 0);
            fadeSr = go.AddComponent<SpriteRenderer>(); fadeSr.sortingOrder = 1000;
            for (int lv = 1; lv <= 4; lv++)
            {
                var c = new PixelCanvas(Px.W, Px.H, "dither" + lv);
                for (int y = 0; y < Px.H; y++) for (int x = 0; x < Px.W; x++)
                    {
                        int b = Bayer[(y & 1) * 2 + (x & 1)];
                        c.Buf[(Px.H - 1 - y) * Px.W + x] = b < lv ? (Color32)Pal.Ink : Pal.Clear;
                    }
                c.Apply(); dither[lv] = c;
            }
            fadeSr.enabled = false;
        }
        static readonly int[] Bayer = { 0, 2, 3, 1 };

        IEnumerator Fade(int from, int to, float dur)
        {
            int steps = Mathf.Abs(to - from); int dir = to > from ? 1 : -1;
            for (int i = 0; i <= steps; i++)
            {
                int lv = from + i * dir;
                fadeSr.enabled = lv > 0; if (lv > 0) fadeSr.sprite = dither[lv].Sprite;
                yield return new WaitForSeconds(dur / Mathf.Max(1, steps));
            }
        }

        public static Transform OverlayRoot => I.overlayRoot;
        public static ScreenBase Current => I != null ? I.current : null;
        public static bool Transitioning => I != null && I.transitioning;

        // ---------- toast ----------
        GameObject toast;
        public static void Toast(string msg, float seconds = 2.2f, float y = 300)
        {
            if (I == null) return;
            if (I.toast) Destroy(I.toast);
            var n = Gfx.Node(I.overlayRoot, "Toast"); I.toast = n.gameObject;
            msg = Lay.Fit(msg, 216);
            int w = Mathf.Min(232, PixelFont.CenteredWidth(msg, Px.W / 2f) + 14);
            float x = Px.W / 2f - w / 2f;
            Gfx.Rect(n, x, y, w, 15, Pal.Ink, 400); Gfx.Rect(n, x + 1, y + 1, w - 2, 13, Pal.Plum, 401); Gfx.Rect(n, x + 1, y + 1, w - 2, 1, Pal.Sky1, 402);
            PixelText.Create(n, msg, Px.W / 2f, y + 4, TextStyle.Of(Pal.Gold4, TextAlign.Center), 403);
            Destroy(n.gameObject, seconds);
        }

        void Update()
        {
            InputRouter.Poll();
            if (transitioning || current == null) return;
            bool consumed = UiButton.Route();
            if (!consumed) current.Pointer();
            if (InputRouter.BackPressed && !Lay.HandleBack()) current.Back();
        }

        void OnApplicationPause(bool paused) { if (!paused) Audio?.SetTension(0); }
    }

    /// <summary>Base for full screens. Children build their scene under transform in Build().</summary>
    public abstract class ScreenBase : MonoBehaviour
    {
        public Transform Root => transform;
        public abstract void Build();
        /// <summary>Pointer input not consumed by a button (drag/tap on boards).</summary>
        public virtual void Pointer() { }
        public virtual void Back() { }
    }
}
