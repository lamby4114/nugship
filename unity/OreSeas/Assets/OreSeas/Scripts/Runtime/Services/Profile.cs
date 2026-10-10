using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using OreSeas.Net;
using UnityEngine;

namespace OreSeas.Services
{
    /// <summary>
    /// The captain profile: display name (unique on the server, case/punctuation-insensitive), avatar ref and the
    /// private guest key that proves this phone owns its public guest id. Saved locally first (works offline, marked
    /// dirty) and pushed to the server on the next welcome. The wallet is only shown when connected; SIWS pairing of
    /// wallet &lt;-&gt; guest id is behind ServerConfig.SIWS_PAIRING (false: nothing is ever signed).
    /// </summary>
    public static class Profile
    {
        public const string DefaultName = "Captain";
        public static event Action Changed;

        public static string Name => ServerConfig.PlayerName;
        public static string Avatar { get { var a = PlayerPrefs.GetString("os.avatar", Avatars.Default); return Avatars.IsValid(a) ? a : Avatars.Default; } }
        public static bool Claimed => PlayerPrefs.GetInt("os.claimed", 0) == 1;
        public static bool Dirty => PlayerPrefs.GetInt("os.pdirty", 0) == 1;
        public static bool IsSet => Claimed || (Name != DefaultName && Name.ToUpperInvariant() != "CAPTAIN");
        public static string PairedWallet => PlayerPrefs.GetString("os.paired", "");

        /// <summary>Private 32-byte hex secret, never shown; the server stores only its sha256.</summary>
        public static string GuestKey
        {
            get
            {
                var k = PlayerPrefs.GetString("os.gkey", "");
                if (k.Length != 64) { var b = new byte[32]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); k = Hex(b); PlayerPrefs.SetString("os.gkey", k); PlayerPrefs.Save(); }
                return k;
            }
        }

        static string CustomPath => Path.Combine(Application.persistentDataPath, "avatar_custom.png");
        /// <summary>The last uploaded/picked custom portrait (20x20 PNG) or null.</summary>
        public static byte[] CustomPng => File.Exists(CustomPath) ? File.ReadAllBytes(CustomPath) : null;
        public static string CustomRef { get { var p = CustomPng; return p == null ? null : "u:" + Hash16(p); } }

        // ---------------- name rules (mirror of server profile.ts) ----------------
        static readonly HashSet<string> Reserved = new HashSet<string> { "captain", "guest", "admin", "administrator", "moderator", "mod", "nugship", "nuggy", "ore", "server", "system", "support",
            "official", "dev", "developer", "staff", "kraken", "captain barnacle", "barnacle", "admiral brine", "brine", "mad molly", "molly", "the ghost captain",
            "ghost captain", "high priest of the kraken cult", "kraken cult", "you", "rival", "anonymous", "null", "undefined" };
        static readonly string[] Block = { "fuck", "shit", "cunt", "nigg", "fag", "rape", "hitler", "nazi", "whore", "slut", "bitch", "dick", "pussy", "retard" };

        public static string Normalize(string raw)
        {
            var sb = new StringBuilder(); bool sp = false;
            foreach (char ch in (raw ?? "").Trim()) { if (char.IsWhiteSpace(ch)) { if (!sp) sb.Append(' '); sp = true; } else { sb.Append(char.ToUpperInvariant(ch)); sp = false; } }
            return sb.ToString();
        }
        public static string Key(string display) { var sb = new StringBuilder(); foreach (char ch in display.ToLowerInvariant()) if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch); return sb.ToString(); }
        static bool Punct(char c) => c == ' ' || c == '_' || c == '.' || c == '-';

        /// <summary>null when valid, else the server's error code.</summary>
        public static string Validate(string display)
        {
            if (display.Length < 3) return "too_short";
            if (display.Length > 16) return "too_long";
            bool letter = false;
            foreach (char c in display) { if (c >= 'A' && c <= 'Z') letter = true; else if (!(c >= '0' && c <= '9') && !Punct(c)) return "bad_chars"; }
            if (!letter) return "needs_letter";
            if (Punct(display[0]) || Punct(display[display.Length - 1])) return "bad_chars";
            for (int i = 1; i < display.Length; i++) if (display[i] != ' ' && Punct(display[i]) && display[i - 1] != ' ' && Punct(display[i - 1])) return "bad_chars";
            var key = Key(display);
            if (key.Length < 3) return "too_short";
            if (Reserved.Contains(display.ToLowerInvariant()) || Reserved.Contains(key)) return "reserved";
            foreach (var b in Block) if (key.Contains(b)) return "not_allowed";
            return null;
        }

        public static string Explain(string code)
        {
            switch (code)
            {
                case null: case "available": return "NAME AVAILABLE";
                case "yours": return "THIS NAME IS YOURS";
                case "too_short": return "AT LEAST 3 LETTERS";
                case "too_long": return "16 CHARACTERS MAX";
                case "bad_chars": return "A-Z 0-9 SPACE _ . - ONLY";
                case "needs_letter": return "NEEDS A LETTER";
                case "reserved": return "THAT NAME IS RESERVED";
                case "not_allowed": return "NOT ALLOWED";
                case "name_taken": return "TAKEN - TRY ANOTHER";
                case "bad_avatar": return "PORTRAIT REJECTED";
                case "offline": return "OFFLINE - SAVED ON PHONE";
                default: return code.ToUpperInvariant().Replace('_', ' ');
            }
        }

        // ---------------- local save ----------------
        /// <summary>Stores the profile on the phone (marks it dirty) and pushes it if online. customPng: a fresh 20x20 upload.</summary>
        public static void Save(string name, string avatar, byte[] customPng, Action<bool, string> done)
        {
            name = Normalize(name);
            if (customPng != null)
            {
                File.WriteAllBytes(CustomPath, customPng);
                avatar = "u:" + Hash16(customPng);
                Avatars.Register(avatar.Substring(2), customPng);
            }
            var bad = Validate(name);
            if (bad != null) { done?.Invoke(false, bad); return; }
            ServerConfig.PlayerName = name;
            PlayerPrefs.SetString("os.avatar", Avatars.IsValid(avatar) ? avatar : Avatars.Default);
            PlayerPrefs.SetInt("os.pdirty", 1); PlayerPrefs.Save();
            Changed?.Invoke();
            var sc = ServerClient.I;
            if (sc == null || !ServerConfig.OnlineEnabled) { done?.Invoke(true, "offline"); return; }
            sc.EnsureConnected(ok => { if (!ok) done?.Invoke(true, "offline"); else Push(done); });
        }

        // ---------------- server sync ----------------
        /// <summary>Last name the server accepted (a rejected save reverts to it).</summary>
        const string ConfirmedKey = "os.cname";
        static Action<bool, string> pushDone; static string pendingCheck; static Action<bool, string, string> checkDone;

        /// <summary>Uploads a custom portrait if needed, then set_profile. done(ok, reasonCode).</summary>
        public static void Push(Action<bool, string> done)
        {
            var sc = ServerClient.I;
            if (sc == null || !sc.Ready) { done?.Invoke(true, "offline"); return; }
            pushDone = done;
            var av = Avatar;
            if (Avatars.IsUploaded(av))
            {
                var png = CustomPng;
                if (png != null && "u:" + Hash16(png) == av) { sc.Send(J.Msg("upload_avatar", "png", Convert.ToBase64String(png))); return; } // set_profile follows avatar_uploaded
            }
            SendSetProfile();
        }

        static void SendSetProfile() => ServerClient.I?.Send(J.Msg("set_profile", "name", Name, "avatar", Avatar));

        /// <summary>Validation + server uniqueness. done(ok, reasonCode, normalizedName). Offline: local rules only.</summary>
        public static void CheckName(string raw, Action<bool, string, string> done)
        {
            var n = Normalize(raw); var bad = Validate(n);
            if (bad != null) { done(false, bad, n); return; }
            var sc = ServerClient.I;
            if (sc == null || !sc.Ready) { done(true, "offline", n); return; }
            pendingCheck = n; checkDone = done;
            sc.Send(J.Msg("check_name", "name", n));
        }

        /// <summary>From ServerClient: welcome adopts the server's claimed profile unless we have unsent local edits.</summary>
        internal static void OnWelcome(Dictionary<string, object> m)
        {
            bool claimed = J.Bool(m, "nameClaimed");
            PlayerPrefs.SetString("os.paired", J.Str(m, "pairedWallet", "") ?? "");
            if (Dirty) { Push(null); return; }
            if (claimed)
            {
                ServerConfig.PlayerName = J.Str(m, "name", Name); PlayerPrefs.SetString(ConfirmedKey, ServerConfig.PlayerName);
                var a = J.Str(m, "avatar", Avatars.Default);
                if (Avatars.IsValid(a)) PlayerPrefs.SetString("os.avatar", a);
                PlayerPrefs.SetInt("os.claimed", 1);
            }
            PlayerPrefs.Save(); Changed?.Invoke();
        }

        /// <summary>From ServerClient for profile / avatar_uploaded / name_check / pairing / errors with those refs.</summary>
        internal static bool OnMessage(Dictionary<string, object> m)
        {
            string t = J.Str(m, "t", "");
            switch (t)
            {
                case "profile":
                    ServerConfig.PlayerName = J.Str(m, "name", Name); PlayerPrefs.SetString(ConfirmedKey, ServerConfig.PlayerName);
                    { var a = J.Str(m, "avatar", Avatar); if (Avatars.IsValid(a)) PlayerPrefs.SetString("os.avatar", a); }
                    PlayerPrefs.SetInt("os.claimed", 1); PlayerPrefs.SetInt("os.pdirty", 0); PlayerPrefs.Save();
                    Changed?.Invoke(); Finish(true, "saved");
                    return true;
                case "avatar_uploaded": SendSetProfile(); return true;
                case "name_check":
                    if (checkDone != null && J.Str(m, "name") == pendingCheck) { var cb = checkDone; checkDone = null; cb(J.Bool(m, "ok"), J.Str(m, "reason"), J.Str(m, "name")); }
                    return true;
                case "wallet_paired": PlayerPrefs.SetString("os.paired", J.Str(m, "wallet", "")); PlayerPrefs.Save(); Changed?.Invoke(); return true;
                case "error":
                    {
                        string rf = J.Str(m, "ref", ""), code = J.Str(m, "code", "error");
                        if (rf == "set_profile" || rf == "upload_avatar")
                        {
                            if (code == "name_taken" || code == "reserved" || code == "not_allowed")
                            {
                                // don't retry a rejected name forever, and don't keep showing it on this phone while rivals see the
                                // server's name: fall back to the last name the server confirmed (or the default "Captain")
                                ServerConfig.PlayerName = PlayerPrefs.GetString(ConfirmedKey, "Captain");
                                PlayerPrefs.SetInt("os.pdirty", 0); PlayerPrefs.Save(); Changed?.Invoke();
                            }
                            Finish(false, code); return true;
                        }
                        if (rf == "check_name" && checkDone != null) { var cb = checkDone; checkDone = null; cb(false, code, pendingCheck); return true; }
                        return false;
                    }
            }
            return false;
        }

        /// <summary>After an account deletion: the custom portrait file goes too (prefs are cleared by ServerClient).</summary>
        internal static void ForgetLocal()
        {
            try { if (File.Exists(CustomPath)) File.Delete(CustomPath); } catch (Exception e) { Debug.LogWarning("[OreSeas] could not delete custom portrait: " + e.Message); }
            pushDone = null; checkDone = null;
            Changed?.Invoke();
        }

        static void Finish(bool ok, string code) { var d = pushDone; pushDone = null; d?.Invoke(ok, code); }

        // ---------------- SIWS wallet pairing (gated OFF) ----------------
        /// <summary>
        /// Wallet &lt;-&gt; guest pairing with a Sign-In-With-Solana message (server: pairing_message / pair_wallet, also gated by
        /// ALLOW_WALLET_PAIRING). With ServerConfig.SIWS_PAIRING=false this returns immediately and nothing is signed; even
        /// with it on, signing is a refusing stub until Victor approves message signing.
        /// </summary>
#pragma warning disable CS0162 // SIWS_PAIRING is a const gate
        public static void PairWallet(Action<bool, string> done)
        {
            if (!ServerConfig.SIWS_PAIRING) { done?.Invoke(false, "SIWS PAIRING IS OFF"); return; }
            var w = GameApp.Wallet;
            if (w == null || !w.IsConnected) { done?.Invoke(false, "CONNECT A WALLET FIRST"); return; }
            // Flow when enabled: send pairing_message{wallet} -> wallet signs the returned text (signMessage) ->
            // pair_wallet{wallet,message,signature}. The signing step is deliberately not wired in this build.
            done?.Invoke(false, "SIGNING NOT ENABLED IN THIS BUILD");
        }
#pragma warning restore CS0162

        // ---------------- utils ----------------
        public static string Hash16(byte[] png) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(png)).Substring(0, 16); }
        static string Hex(byte[] b) { var sb = new StringBuilder(b.Length * 2); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }

        /// <summary>Short display form of a pubkey: ABCD..WXYZ.</summary>
        public static string ShortKey(string k) => string.IsNullOrEmpty(k) ? "" : k.Length <= 10 ? k : k.Substring(0, 4) + ".." + k.Substring(k.Length - 4);
    }
}
