using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OreSeas.EditorTools
{
    /// <summary>
    /// Batchmode entry points:
    ///   Unity.exe -batchmode -quit -projectPath C:\Users\vdifu\Desktop\OreSeas -executeMethod OreSeas.EditorTools.BuildScript.CompileCheck -logFile OreSeasLogs\compile.log
    ///   Unity.exe -batchmode -quit -projectPath C:\Users\vdifu\Desktop\OreSeas -buildTarget Android -executeMethod OreSeas.EditorTools.BuildScript.BuildAndroidDev -logFile OreSeasLogs\build.log
    ///   Unity.exe ... -executeMethod OreSeas.EditorTools.BuildScript.BuildAndroidRelease   (needs NUGSHIP_KEYSTORE* env vars, see below)
    /// </summary>
    public static class BuildScript
    {
        public const string PackageId = "com.nugship.game";
        public const string MainScene = "Assets/Scenes/Main.unity";
        public const string ApkPath = "Builds/Nugship-dev.apk";
        public const string ReleaseApkPath = "Builds/Nugship-release.apk";
        /// <summary>Scripting define for store builds: ServerConfig.DefaultServer becomes wss://nugship-server.fly.dev/ws.</summary>
        public const string ReleaseDefine = "NUGSHIP_RELEASE";
        /// <summary>v1.1.0+: release builds compile live mining (player-signed ORE deploys). Set NUGSHIP_LIVE_ORE=0 to build without it.</summary>
        public const string LiveDefine = "ORESEAS_LIVE_ORE";
        /// <summary>dApp Store release identity for this build when NUGSHIP_VERSION_CODE / _NAME are not set.</summary>
        public const int ReleaseVersionCode = 3;
        public const string ReleaseVersionName = "1.1.0";

        [MenuItem("NUGSHIP/Configure Player Settings")]
        public static void Configure() => Configure(false);

        /// <summary>release: TLS only (insecureHttpOption NotAllowed). Dev: cleartext to the adb-reversed PC server, development builds only.</summary>
        public static void Configure(bool release)
        {
            PlayerSettings.companyName = "Nugship";
            PlayerSettings.productName = "Nugship";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, PackageId);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.defaultScreenWidth = 1200;   // Seeker: 1200x2670
            PlayerSettings.defaultScreenHeight = 2670;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.forceInternetPermission = true; // devnet RPC for MWA + NUGSHIP server
            // Dev: UnityWebRequest REST to the PC server is plain http://127.0.0.1:8787 (adb reverse) - allowed in development builds only.
            // Release: https/wss to nugship-server.fly.dev only.
            PlayerSettings.insecureHttpOption = release ? InsecureHttpOption.NotAllowed : InsecureHttpOption.DevelopmentOnly;
            PlayerSettings.stripEngineCode = false; // keep System.Net.WebSockets intact (see Assets/OreSeas/link.xml)
            PlayerSettings.SplashScreen.backgroundColor = new Color32(0x1a, 0x12, 0x10, 255);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[OreSeas] player settings configured (" + PackageId + ", portrait, IL2CPP ARM64, minSdk 26, " + (release ? "RELEASE: https only" : "dev: cleartext in development builds") + ")");
        }

        /// <summary>Scripts compile before -executeMethod runs; reaching this method means the project compiled.</summary>
        [MenuItem("NUGSHIP/Compile Check + Self Test")]
        public static void CompileCheck()
        {
            Debug.Log("[OreSeas] COMPILE OK - " + DateTime.Now.ToString("u"));
            bool ok = SelfTest();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 2);
        }

        /// <summary>Offline checks of the online layer (no network): commit vector, JSON round-trip, layout codes, LIVE_ORE gate.</summary>
        public static bool SelfTest()
        {
            bool ok = true;
            void Check(string name, bool pass) { Debug.Log("[OreSeas] SELFTEST " + (pass ? "PASS " : "FAIL ") + name); ok &= pass; }
            Check("fleet commit vector (PROTOCOL.md)", OreSeas.Net.OnlineSession.SelfTest());
            var parsed = OreSeas.Net.Json.ParseObject("{\"t\":\"turn_start\",\"roundId\":431893,\"alreadyFired\":[1,7],\"ok\":true,\"s\":\"a\\\"b\"}");
            Check("json parse", OreSeas.Net.J.Str(parsed, "t") == "turn_start" && OreSeas.Net.J.ULong(parsed, "roundId") == 431893UL
                                && OreSeas.Net.J.Ints(parsed, "alreadyFired").Count == 2 && OreSeas.Net.J.Bool(parsed, "ok") && OreSeas.Net.J.Str(parsed, "s") == "a\"b");
            var msg = OreSeas.Net.Json.Serialize(OreSeas.Net.J.Msg("aim", "matchId", "m1", "squares", new System.Collections.Generic.List<int> { 3, 4 }, "fireNow", false));
            Check("json write", msg == "{\"t\":\"aim\",\"matchId\":\"m1\",\"squares\":[3,4],\"fireNow\":false}");
#if ORESEAS_LIVE_ORE
            Check("LIVE_ORE on with the ORESEAS_LIVE_ORE define", OreSeas.Net.ServerConfig.LIVE_ORE && OreSeas.Net.ServerConfig.SIWS_PAIRING);
#else
            Check("LIVE_ORE off without the ORESEAS_LIVE_ORE define (editor/dev default)", !OreSeas.Net.ServerConfig.LIVE_ORE);
#endif
            Check("live mining is opt-in (default OFF until the consent sheet)", UnityEngine.PlayerPrefs.GetInt("os.livemining", 0) == 0 || OreSeas.Settings.LiveConsent);
            Check("MWA identity is the listed website (https)", OreSeas.Net.ServerConfig.DappIdentityUri == "https://lamby4114.github.io/nugship/");
#if ORESEAS_SOLANA && ORESEAS_LIVE_ORE
            Check("LiveTx caps: per-square ceiling, match cap, balance", OreSeas.Ore.Chain.LiveTx.Validate(20_000_000, 1, 0, 1_000_000_000, 10_000_000_000) != null
                && OreSeas.Ore.Chain.LiveTx.Validate(1_000_000, 7, 48_000_000, 50_000_000, 1_000_000_000) == "MATCH SPEND CAP REACHED"
                && OreSeas.Ore.Chain.LiveTx.Validate(1_000_000, 7, 0, 50_000_000, 1_000_000_000) == null);
#endif
            Check("http base from ws url", OreSeas.Net.ServerConfig.HttpBase.StartsWith("http"));
            Check("release server is TLS (wss://), dev default is the adb-reverse PC", OreSeas.Net.ServerConfig.ReleaseServer.StartsWith("wss://") && OreSeas.Net.ServerConfig.DevServer == "ws://127.0.0.1:8787/ws");
            Check("no secrets in the project (x_kit / credentials / keys)", SecretsScan(out var leak) || Fail(leak));
#if !ORESEAS_LIVE_ORE
#pragma warning disable CS0162
            Check("SIWS wallet pairing gate is OFF (non-live build)", !OreSeas.Net.ServerConfig.SIWS_PAIRING);
#pragma warning restore CS0162
            bool pairRefused = false; OreSeas.Services.Profile.PairWallet((ok, why) => pairRefused = !ok && why.Contains("OFF"));
            Check("SIWS pairing refuses while gated", pairRefused);
#endif
            Check("centring parity rule (every string, whole + half anchors, x1 + x2)", OreSeas.CenterAudit.ParitySelfTest(out var pfail) || Fail(pfail));
            // name rules mirror server/src/profile/profile.ts
            string V(string n) => OreSeas.Services.Profile.Validate(OreSeas.Services.Profile.Normalize(n));
            Check("name rules", V("Salty Nugget") == null && V("  salty   nugget ") == null && V("ab") == "too_short" && V("A_VERY_LONG_NAME_X") == "too_long"
                                 && V("1234") == "needs_letter" && V("Bad!Name") == "bad_chars" && V("-EDGE") == "bad_chars" && V("A..B") == "bad_chars"
                                 && V("Captain") == "reserved" && V("Nug Ship") == "reserved" && V("xxfuckxx") == "not_allowed");
            Check("name normalise + key", OreSeas.Services.Profile.Normalize(" salty  nugget ") == "SALTY NUGGET" && OreSeas.Services.Profile.Key("SALTY_NUGGET") == "saltynugget");
            Check("avatar refs", OreSeas.Avatars.IsValid("a:nuggy") && OreSeas.Avatars.IsValid("u:0123456789abcdef") && !OreSeas.Avatars.IsValid("a:nope") && !OreSeas.Avatars.IsValid("u:12") && OreSeas.Avatars.Ids.Length == 18);
            Check("avatar atlas has every portrait + mini", AvatarAtlasOk(out var amiss) || Fail(amiss));
            Check("photo pixelates to 20x20 palette portrait", PixelateOk());
            Debug.Log("[OreSeas] SELFTEST " + (ok ? "ALL PASS" : "FAILED"));
            return ok;
        }

        static bool Fail(string why) { Debug.Log("[OreSeas] SELFTEST detail: " + why); return false; }

        static bool AvatarAtlasOk(out string missing)
        {
            missing = null;
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/OreSeas/Resources/Atlases/avatars_0_frames.json");
            if (ta == null) { missing = "avatars_0_frames.json"; return false; }
            foreach (var id in OreSeas.Avatars.Ids)
                foreach (var n in new[] { "\"av_" + id + "\"", "\"avm_" + id + "\"" })
                    if (!ta.text.Contains(n)) { missing = n; return false; }
            var bg = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/OreSeas/Resources/Atlases/bg_0_frames.json");
            if (bg == null || !bg.text.Contains("\"title_signore\"") || !bg.text.Contains("\"title_chunk_l\"") || bg.text.Contains("\"title_peek_0\"")) { missing = "bg_0 sign ore / peek removed"; return false; }
            return true;
        }

        static bool PixelateOk()
        {
            var src = new Texture2D(64, 48, TextureFormat.RGBA32, false);
            var px = new Color32[64 * 48];
            for (int y = 0; y < 48; y++) for (int x = 0; x < 64; x++) px[y * 64 + x] = new Color32((byte)(x * 4), (byte)(y * 5), (byte)((x + y) * 2), 255);
            src.SetPixels32(px); src.Apply();
            var t = OreSeas.AvatarPixelate.FromPhoto(src);
            var png = t.EncodeToPNG();
            bool ok = t.width == 20 && t.height == 20 && png.Length < 6144 && OreSeas.AvatarPixelate.Mini(t).width == 10;
            UnityEngine.Object.DestroyImmediate(src); UnityEngine.Object.DestroyImmediate(t);
            return ok;
        }

        /// <summary>
        /// Fails if anything secret-looking is inside the Unity project: the X/Twitter kit (x_kit, *credentials*),
        /// keystores, private keys, .env files, or token-shaped strings in text assets. Runs in CompileCheck and before
        /// every build; the repo's .gitignore and pre-commit hook (tools/hooks/pre-commit) block the same paths.
        /// </summary>
        public static bool SecretsScan(out string hit)
        {
            hit = null;
            string root = Path.GetFullPath(".");
            string[] badNames = { "x_kit", "credentials", ".env", ".keystore", ".jks", ".p12", ".pem", "id_rsa", "secrets.json", "service-account" };
            string[] badText = { "BEGIN PRIVATE KEY", "BEGIN RSA PRIVATE KEY", "BEGIN OPENSSH PRIVATE KEY", "consumer_secret", "access_token_secret", "bearer_token", "api_secret", "x-api-key" };
            foreach (var dir in new[] { "Assets", "ProjectSettings", "Packages" })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(f).ToLowerInvariant(); var rel = f.Replace('\\', '/');
                    if (rel.Contains("/x_kit") || badNames.Any(b => name.Contains(b))) { hit = rel; return false; }
                    var ext = Path.GetExtension(name);
                    if (ext != ".cs" && ext != ".json" && ext != ".txt" && ext != ".asset" && ext != ".xml" && ext != ".gradle" && ext != ".properties" && ext != ".md") continue;
                    if (new FileInfo(f).Length > 2_000_000) continue;
                    var text = File.ReadAllText(f);
                    if (rel.EndsWith("Editor/BuildScript.cs")) continue; // this list itself
                    foreach (var b in badText) if (text.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) { hit = rel + " contains '" + b + "'"; return false; }
                }
            }
            return true;
        }

        /// <summary>
        /// Store / Solana dApp Store build: non-development, NUGSHIP_RELEASE define (wss://nugship-server.fly.dev/ws),
        /// insecureHttpOption NotAllowed, signed with a release keystore that is NOT in the repo. Needs env vars
        /// NUGSHIP_KEYSTORE (path), NUGSHIP_KEYSTORE_PASS, NUGSHIP_KEY_ALIAS, NUGSHIP_KEY_PASS and optionally
        /// NUGSHIP_VERSION_CODE / NUGSHIP_VERSION_NAME. Refuses to build without them (no keystore exists yet), and never
        /// writes passwords into ProjectSettings. The dApp Store wants its own signing key (not the Google Play one).
        /// </summary>
        [MenuItem("NUGSHIP/Build Android RELEASE APK (needs keystore env)")]
        public static void BuildAndroidRelease()
        {
            string ks = Environment.GetEnvironmentVariable("NUGSHIP_KEYSTORE"), ksPass = Environment.GetEnvironmentVariable("NUGSHIP_KEYSTORE_PASS"),
                   alias = Environment.GetEnvironmentVariable("NUGSHIP_KEY_ALIAS"), keyPass = Environment.GetEnvironmentVariable("NUGSHIP_KEY_PASS");
            if (string.IsNullOrEmpty(ks) || !File.Exists(ks) || string.IsNullOrEmpty(ksPass) || string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(keyPass))
            {
                Debug.LogError("[OreSeas] RELEASE BUILD REFUSED: set NUGSHIP_KEYSTORE (existing .keystore outside the repo), NUGSHIP_KEYSTORE_PASS, NUGSHIP_KEY_ALIAS, NUGSHIP_KEY_PASS");
                if (Application.isBatchMode) EditorApplication.Exit(3);
                return;
            }
            if (Path.GetFullPath(ks).StartsWith(Path.GetFullPath("."), StringComparison.OrdinalIgnoreCase)) { Debug.LogError("[OreSeas] RELEASE BUILD REFUSED: keystore must live outside the Unity project"); if (Application.isBatchMode) EditorApplication.Exit(3); return; }
            if (!SecretsScan(out var leak)) { Debug.LogError("[OreSeas] RELEASE BUILD REFUSED: secret in project: " + leak); if (Application.isBatchMode) EditorApplication.Exit(4); return; }
            Configure(true);
            PlayerSettings.Android.bundleVersionCode = int.TryParse(Environment.GetEnvironmentVariable("NUGSHIP_VERSION_CODE"), out var vc) && vc > 0 ? vc : ReleaseVersionCode;
            var vn = Environment.GetEnvironmentVariable("NUGSHIP_VERSION_NAME"); PlayerSettings.bundleVersion = string.IsNullOrEmpty(vn) ? ReleaseVersionName : vn;
            if (PlayerSettings.Android.bundleVersionCode <= 2) { Debug.LogError("[OreSeas] RELEASE BUILD REFUSED: versionCode must be > 2 (1.0.0 shipped as 2)"); if (Application.isBatchMode) EditorApplication.Exit(5); return; }
            bool live = Environment.GetEnvironmentVariable("NUGSHIP_LIVE_ORE") != "0";
            Debug.Log("[OreSeas] release " + PlayerSettings.bundleVersion + " (" + PlayerSettings.Android.bundleVersionCode + ") live mining compiled in: " + live);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false;   // the dApp Store takes an APK
            bool prevCustom = PlayerSettings.Android.useCustomKeystore; string prevKs = PlayerSettings.Android.keystoreName, prevAlias = PlayerSettings.Android.keyaliasName;
            BuildReport report = null;
            try
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = ks; PlayerSettings.Android.keystorePass = ksPass;
                PlayerSettings.Android.keyaliasName = alias; PlayerSettings.Android.keyaliasPass = keyPass;
                Directory.CreateDirectory("Builds");
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { MainScene }, locationPathName = ReleaseApkPath, target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None, extraScriptingDefines = live ? new[] { ReleaseDefine, LiveDefine } : new[] { ReleaseDefine }
                });
            }
            finally
            {
                // never leave the release key configured (or its passwords in memory) for the next dev build
                PlayerSettings.Android.keystorePass = ""; PlayerSettings.Android.keyaliasPass = "";
                PlayerSettings.Android.useCustomKeystore = prevCustom; PlayerSettings.Android.keystoreName = prevKs; PlayerSettings.Android.keyaliasName = prevAlias;
                Configure(false);
            }
            var s = report.summary;
            Debug.Log("[OreSeas] RELEASE BUILD " + s.result + " -> " + Path.GetFullPath(ReleaseApkPath) + " size=" + s.totalSize + " versionCode=" + PlayerSettings.Android.bundleVersionCode + " errors=" + s.totalErrors);
            if (s.result != BuildResult.Succeeded)
                foreach (var step in report.steps) foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception)) Debug.LogError("[OreSeas] " + m.content);
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        public const string WinShotPath = "Builds/win/Nugship.exe";

        /// <summary>Desktop player used only to render screenshots (-nugshot). Development build, Mono.</summary>
        [MenuItem("NUGSHIP/Build Windows Screenshot Player")]
        public static void BuildWindowsShots()
        {
            Configure();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 600; PlayerSettings.defaultScreenHeight = 1335;   // Seeker portrait at 1/2 (the play-test renders 1200x2670 off-screen)
            PlayerSettings.resizableWindow = true;
            Directory.CreateDirectory("Builds/win");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { MainScene }, locationPathName = WinShotPath,
                target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.Development
            });
            var s = report.summary;
            Debug.Log("[OreSeas] WIN BUILD " + s.result + " -> " + Path.GetFullPath(WinShotPath) + " errors=" + s.totalErrors);
            if (s.result == BuildResult.Succeeded)
            {
                // second captain for PvP play-tests: a different productName = its own PlayerPrefs + persistentDataPath
                string pn = PlayerSettings.productName;
                PlayerSettings.productName = pn + "B";
                Directory.CreateDirectory("Builds/winB");
                var rb = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { MainScene }, locationPathName = "Builds/winB/Nugship.exe",
                    target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.Development
                });
                PlayerSettings.productName = pn;
                Debug.Log("[OreSeas] WIN BUILD B " + rb.summary.result + " errors=" + rb.summary.totalErrors);
            }
            if (s.result != BuildResult.Succeeded)
                foreach (var step in report.steps) foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception)) Debug.LogError("[OreSeas] " + m.content);
            PlayerSettings.defaultScreenWidth = 1200; PlayerSettings.defaultScreenHeight = 2670; // restore phone defaults
            AssetDatabase.SaveAssets();
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        [MenuItem("NUGSHIP/Build Android Dev APK")]
        public static void BuildAndroidDev()
        {
            if (!SecretsScan(out var leak)) { Debug.LogError("[OreSeas] BUILD REFUSED: secret in project: " + leak); if (Application.isBatchMode) EditorApplication.Exit(4); return; }
            Configure(false);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false;
            Directory.CreateDirectory("Builds");
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log("[OreSeas] BUILD " + s.result + " -> " + Path.GetFullPath(ApkPath) + " size=" + s.totalSize + " errors=" + s.totalErrors + " time=" + s.totalTime);
            if (s.result != BuildResult.Succeeded)
                foreach (var step in report.steps) foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception)) Debug.LogError("[OreSeas] " + m.content);
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
