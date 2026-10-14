using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace OreSeas.Net
{
    /// <summary>
    /// NUGSHIP server connection (PROTOCOL.md v1): WebSocket session (challenge -> hello -> welcome, resume on reconnect),
    /// matchmaking requests, per-match message queue (OnlineSession), and REST helpers for leaderboards/profile.
    /// Lives on the GameApp object. Everything runs on the main thread except WsClient's raw receive loop.
    /// </summary>
    public sealed class ServerClient : MonoBehaviour
    {
        public enum NetState { Offline, Connecting, Ready }
        public static ServerClient I { get; private set; }

        public NetState State { get; private set; } = NetState.Offline;
        public bool Ready => State == NetState.Ready;
        public string Status { get; private set; } = "OFFLINE";
        public string PlayerId { get; private set; }
        public string Name { get; private set; }
        public string Avatar { get; private set; } = Avatars.Default;
        public bool NameClaimed { get; private set; }
        public bool WalletPairingEnabled { get; private set; }
        public int Rating { get; private set; } = 1000;
        public bool Verified { get; private set; }
        public string[] StakeOptions { get; private set; } = new string[0];
        public Dictionary<string, object> OreStatus { get; private set; }
        public OnlineSession Session { get; private set; }
        /// <summary>Captains this player blocked (server list from welcome, merged with blocks made offline). Store policy.</summary>
        public readonly HashSet<string> Blocked = new HashSet<string>();
        /// <summary>True between a server_restart notice / close 1012 and the next welcome.</summary>
        public bool Restarting { get; private set; }

        /// <summary>Every non-match message (queued, private_created, error, ...) and match_found.</summary>
        public event Action<Dictionary<string, object>> Message;
        public event Action<OnlineSession> MatchFound;
        public event Action StateChanged;

        WsClient ws; string token; float nextPing, nextRetry; int retries; bool wantOnline;
        int restartTries; float restartAfter = 5f;
        readonly List<Action<bool>> readyWaiters = new List<Action<bool>>();

        static readonly HashSet<string> MatchTypes = new HashSet<string>
        {
            "fleet_ack", "opponent_ready", "fleets_committed", "turn_start", "aim_ack", "opponent_aiming", "clock", "lock", "fired_ack",
            "round_closed", "reveal", "turn_void", "opponent_disconnected", "opponent_reconnected", "match_state", "match_end", "rating_update"
        };

        public static ServerClient Create(Transform parent)
        {
            var go = new GameObject("ServerClient"); go.transform.SetParent(parent, false);
            return go.AddComponent<ServerClient>();
        }

        void Awake() { I = this; foreach (var b in PlayerPrefs.GetString(BlockedKey, "").Split(',')) if (b.Length > 0) Blocked.Add(b); }
        const string BlockedKey = "os.blocked";
        void OnDestroy() { ws?.Dispose(); if (I == this) I = null; }

        string GuestId
        {
            get => PlayerPrefs.GetString("os.guest", "");
            set { PlayerPrefs.SetString("os.guest", value); PlayerPrefs.Save(); }
        }

        // ---------------- connection ----------------
        /// <summary>Connect if needed; done(true) once welcomed. Safe to call repeatedly (screens call it on Build).</summary>
        public void EnsureConnected(Action<bool> done = null)
        {
            if (!ServerConfig.OnlineEnabled) { SetState(NetState.Offline, "ONLINE PLAY OFF (SETTINGS)"); done?.Invoke(false); return; }
            wantOnline = true;
            if (State == NetState.Ready) { done?.Invoke(true); return; }
            if (done != null) readyWaiters.Add(done);
            if (State == NetState.Connecting) return;
            StartConnect();
        }

        async void StartConnect()
        {
            SetState(NetState.Connecting, "CONNECTING...");
            ws?.Dispose();
            var mine = ws = new WsClient();
            bool ok = await mine.Connect(ServerConfig.ServerUrl);
            if (this == null || ws != mine) return;   // superseded (restart / settings change) while connecting
            if (!ok) { string why = mine.CloseReason; Fail("SERVER UNREACHABLE"); Debug.LogWarning("[OreSeas.Net] connect " + ServerConfig.ServerUrl + " failed: " + why); }
            // on success we wait for "challenge", then send hello
        }

        /// <summary>
        /// The server is restarting (deploy): sessions + resume tokens die with the process, so drop the token (the next
        /// hello is a fresh one with the same guestId/guestKey) and keep reconnecting with backoff until it is back.
        /// </summary>
        void ServerRestarting()
        {
            Restarting = true; token = null;
            ws?.Dispose(); ws = null;
            SetState(NetState.Offline, "SERVER RESTARTING");
            float wait = Mathf.Min(60f, restartAfter * (1 << Mathf.Min(restartTries++, 4)));
            nextRetry = Time.unscaledTime + wait;
            Debug.Log("[OreSeas.Net] server restarting; reconnect in " + wait + " s");
        }

        void Fail(string why)
        {
            if (Restarting) { ServerRestarting(); var w0 = readyWaiters.ToArray(); readyWaiters.Clear(); foreach (var cb in w0) cb?.Invoke(false); return; }
            SetState(NetState.Offline, why);
            var w = readyWaiters.ToArray(); readyWaiters.Clear();
            foreach (var cb in w) cb?.Invoke(false);
            nextRetry = Time.unscaledTime + Mathf.Min(30f, 3f * (1 + retries++));
        }

        void SetState(NetState s, string status) { State = s; Status = status; StateChanged?.Invoke(); }

        public void Disconnect() { wantOnline = false; ws?.Dispose(); ws = null; SetState(NetState.Offline, "OFFLINE"); }

        /// <summary>Desktop play-test harness only: drop the socket as if the network vanished (auto-reconnect + resume kick in).</summary>
        public void DebugDrop() { ws?.Dispose(); ws = null; Fail("CONNECTION LOST"); nextRetry = Time.unscaledTime + 4f; }

        /// <summary>Desktop play-test harness only: act as if the server announced a restart and closed with 1012.</summary>
        public void DebugServerRestart()
        {
            Handle("{\"t\":\"server_restart\",\"reason\":\"maintenance\",\"reconnectAfterMs\":2000,\"matchAbandoned\":false}");
            ws?.Dispose(); ws = null; ServerRestarting();
        }

        public void Send(Dictionary<string, object> msg)
        {
            if (ws == null || !ws.IsOpen) { Debug.LogWarning("[OreSeas.Net] not connected, dropped " + J.Str(msg, "t")); return; }
            ws.Send(Json.Serialize(msg));
        }

        /// <summary>Server challenge nonce (SIWS sign-in must include it).</summary>
        public string Nonce { get; private set; }
        /// <summary>fireMode for matches: "manual" (player signs each deploy) when live mining is on, else "virtual" (off-chain).</summary>
        public static string FireMode => Services.LiveMining.Active ? "manual" : "virtual";

        async void SendHello()
        {
            var hello = J.Msg("hello", "name", ServerConfig.PlayerName);
            // Live builds: present the wallet ONLY when the player opted in to live mining, and prove it with a SIWS signature
            // over the server nonce (otherwise anyone could claim someone else's wallet). Not opted in -> guest, off-chain.
            if (Services.LiveMining.Active && !string.IsNullOrEmpty(Nonce))
            {
                var wallet = GameApp.Wallet.PublicKey;
                var msg = Services.LiveSignIn.Message(wallet, Nonce);
                var sig = await GameApp.Wallet.SignText(msg);
                if (sig.ok)
                {
                    hello["wallet"] = wallet;
                    hello["signIn"] = new Dictionary<string, object> { ["message"] = msg, ["signature"] = sig.message };
                }
                else GameApp.Toast("WALLET SIGN-IN SKIPPED - PLAYING OFF-CHAIN");
            }
            if (!hello.ContainsKey("wallet"))
            {
                if (!string.IsNullOrEmpty(GuestId)) hello["guestId"] = GuestId;
                hello["guestKey"] = Services.Profile.GuestKey; // private; proves this phone owns the public guest id
            }
            if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(PlayerId)) hello["resume"] = new Dictionary<string, object> { ["playerId"] = PlayerId, ["token"] = token };
            Send(hello);
        }

        void Update()
        {
            if (ws != null)
            {
                int n = 0;
                // Handle can drop the socket (account_deleted, server_restart): re-check ws every message
                while (n++ < 64 && ws != null && ws.Inbox.TryDequeue(out var raw)) Handle(raw);
                if (ws != null && ws.Closed && State != NetState.Offline)
                {
                    // 1012 "Service Restart" (fly deploy) or a server_restart notice: reconnect with backoff 5 s, 10 s, 20 s ... 60 s
                    if (ws.CloseCode == 1012 || Restarting) ServerRestarting();
                    else Fail("CONNECTION LOST");
                }
            }
            if (State == NetState.Ready && Time.unscaledTime > nextPing) { nextPing = Time.unscaledTime + 15f; Send(J.Msg("ping", "echo", (long)(Time.unscaledTime * 1000))); }
            // auto-reconnect while a match is live (server keeps the seat for 90 s) or a screen asked to be online
            if (State == NetState.Offline && wantOnline && ServerConfig.OnlineEnabled && Time.unscaledTime > nextRetry && ((Session != null && !Session.ended) || Restarting))
                StartConnect();
        }

        void Handle(string raw)
        {
            Dictionary<string, object> m;
            try { m = Json.ParseObject(raw); } catch (Exception e) { Debug.LogWarning("[OreSeas.Net] bad json: " + e.Message); return; }
            if (m == null) return;
            string t = J.Str(m, "t", "");
            switch (t)
            {
                case "challenge": Nonce = J.Str(m, "nonce"); SendHello(); return;
                case "welcome":
                    PlayerId = J.Str(m, "playerId"); token = J.Str(m, "token"); Name = J.Str(m, "name"); Rating = J.Int(m, "rating", 1000);
                    Verified = J.Bool(m, "verified"); OreStatus = J.Obj(m, "ore"); StakeOptions = J.Strs(m, "stakeOptionsLamports").ToArray();
                    if (PlayerId != null && PlayerId.StartsWith("g_")) GuestId = PlayerId;
                    Avatar = J.Str(m, "avatar", Avatars.Default); NameClaimed = J.Bool(m, "nameClaimed"); WalletPairingEnabled = J.Bool(m, "walletPairing");
                    retries = 0; restartTries = 0; Restarting = false; SetState(NetState.Ready, "ONLINE");
                    SyncBlocked(J.Strs(m, "blocked"));
                    Services.Profile.OnWelcome(m);
                    if (string.IsNullOrEmpty(J.Str(m, "activeMatch")) && Session != null && !Session.ended) { Session.ended = true; Session.Inbox.Enqueue(J.Msg("session_lost")); }
                    { var w = readyWaiters.ToArray(); readyWaiters.Clear(); foreach (var cb in w) cb?.Invoke(true); }
                    return;
                case "pong": return;
                case "server_restart":
                    // tell the screens (they show SERVER RESTARTING), then let the 1012 close drive the backoff reconnect
                    Restarting = true; token = null;
                    restartAfter = Mathf.Clamp(J.Int(m, "reconnectAfterMs", 5000) / 1000f, 2f, 30f);
                    SetState(State, "SERVER RESTARTING");
                    Message?.Invoke(m);
                    return;
                case "blocked": SetBlocked(J.Strs(m, "blocked")); Message?.Invoke(m); return;
                case "account_deleted": OnAccountDeleted(); Message?.Invoke(m); return;
                case "profile": Name = J.Str(m, "name", Name); Avatar = J.Str(m, "avatar", Avatar); NameClaimed = true; Services.Profile.OnMessage(m); Message?.Invoke(m); return;
                case "avatar_uploaded": case "name_check": case "pairing_message": case "wallet_paired": Services.Profile.OnMessage(m); Message?.Invoke(m); return;
                case "match_found":
                    Session = OnlineSession.FromMatchFound(m);
                    MatchFound?.Invoke(Session); Message?.Invoke(m);
                    return;
                case "rating_update": Rating = J.Int(m, "rating", Rating); break;
                case "error":
                    Debug.LogWarning("[OreSeas.Net] server error " + J.Str(m, "code") + ": " + J.Str(m, "message"));
                    Services.Profile.OnMessage(m);
                    { string rf = J.Str(m, "ref", ""); if (Session != null && !Session.ended && (rf == "place_fleet" || rf == "aim" || rf == "get_state" || rf == "resign")) Session.Inbox.Enqueue(m); }
                    Message?.Invoke(m);
                    return;
            }
            if (MatchTypes.Contains(t))
            {
                if (Session == null) { Debug.LogWarning("[OreSeas.Net] " + t + " without a session"); return; }
                if (t == "fleets_committed") { Session.opponentCommit = J.Str(m, "opponentCommit"); Session.fleetsCommitted = true; }
                if (t == "fleet_ack") Session.fleetPlaced = true;
                Session.Inbox.Enqueue(m);
                if (t == "match_end") Session.ended = true;
                return;
            }
            Message?.Invoke(m);
        }

        // ---------------- matchmaking ----------------
        public void QueueRanked() => Send(J.Msg("queue_ranked", "stakeLamports", ServerConfig.StakeLamports, "fireMode", FireMode));
        public void CancelQueue() => Send(J.Msg("cancel_queue"));
        /// <summary>Invite code match. fireMode "virtual" = off-chain, no wallet (the only mode while LIVE_ORE=false).</summary>
        public void CreatePrivate() => Send(J.Msg("create_private", "stakeLamports", ServerConfig.StakeLamports, "fireMode", FireMode));
        public void JoinPrivate(string code) => Send(J.Msg("join_private", "code", (code ?? "").Trim().ToUpperInvariant(), "fireMode", FireMode));
        /// <summary>Solo vs a server AI captain. "practice" = synthetic fast rounds (fireNow allowed), no SOL.</summary>
        public void StartSolo(string serverCaptainId, bool practice = true) => Send(J.Msg("start_solo", "captain", serverCaptainId, "mode", practice ? "practice" : "live", "fireMode", practice ? "virtual" : FireMode));
        public void Resign() { if (Session != null && !Session.ended) Send(J.Msg("resign", "matchId", Session.matchId)); }
        public void ClearSession() { Session = null; }

        /// <summary>Unity captain ids -> server captain ids (ai.ts CAPTAINS).</summary>
        public static string ServerCaptainId(string unityId) => unityId == "cult" ? "kraken_cult" : unityId;
        public static string UnityCaptainId(string serverId) => serverId == "kraken_cult" ? "cult" : serverId;

        // ---------------- store policy: report / block / delete ----------------
        /// <summary>Report a captain's name or uploaded portrait (kind "name" | "avatar"). Reply: report_ack or error.</summary>
        public void Report(string playerId, string kind) { if (!string.IsNullOrEmpty(playerId)) Send(J.Msg("report_player", "playerId", playerId, "kind", kind)); }

        /// <summary>Block / unblock a captain: hidden on this phone at once, and the server never pairs you again.</summary>
        public void SetBlock(string playerId, bool block)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            if (block) Blocked.Add(playerId); else Blocked.Remove(playerId);
            SaveBlocked();
            if (Ready) Send(J.Msg(block ? "block_player" : "unblock_player", "playerId", playerId));
            StateChanged?.Invoke();
        }
        public bool IsBlocked(string playerId) => !string.IsNullOrEmpty(playerId) && Blocked.Contains(playerId);

        void SyncBlocked(List<string> server)
        {
            // blocks made while offline are pushed; the server list is adopted otherwise
            var srv = new HashSet<string>(server ?? new List<string>());
            foreach (var b in Blocked) if (!srv.Contains(b)) Send(J.Msg("block_player", "playerId", b));
            foreach (var b in srv) Blocked.Add(b);
            SaveBlocked();
        }
        void SetBlocked(List<string> list) { Blocked.Clear(); foreach (var b in list ?? new List<string>()) Blocked.Add(b); SaveBlocked(); }
        void SaveBlocked() { PlayerPrefs.SetString(BlockedKey, string.Join(",", Blocked)); PlayerPrefs.Save(); }

        Action<bool, string> deleteDone;
        /// <summary>
        /// In-app account deletion: the server wipes the profile, online stats, uploaded portrait, reports and blocks;
        /// then this phone forgets its captain (name, portrait, guest id + key, local log). done(ok, why).
        /// Never online (no guest id yet): nothing is on the server, so only the phone is wiped.
        /// </summary>
        public void DeleteAccount(Action<bool, string> done)
        {
            if (string.IsNullOrEmpty(GuestId) && string.IsNullOrEmpty(PlayerId)) { WipeLocal(); done?.Invoke(true, "local"); return; }
            if (!ServerConfig.OnlineEnabled) { done?.Invoke(false, "TURN ON ONLINE PLAY TO DELETE"); return; }
            EnsureConnected(ok =>
            {
                if (!ok) { done?.Invoke(false, "SERVER OFFLINE - TRY AGAIN"); return; }
                deleteDone = done;
                Send(J.Msg("delete_account", "confirm", "DELETE"));
            });
        }

        void OnAccountDeleted()
        {
            WipeLocal();
            ws?.Dispose(); ws = null; token = null; PlayerId = null; Session = null;
            SetState(NetState.Offline, "PROFILE DELETED");
            var d = deleteDone; deleteDone = null; d?.Invoke(true, "deleted");
        }

        static void WipeLocal()
        {
            foreach (var k in new[] { "os.name", "os.avatar", "os.claimed", "os.pdirty", "os.cname", "os.paired", "os.guest", "os.gkey", BlockedKey }) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
            Services.Profile.ForgetLocal();
            if (I != null) I.Blocked.Clear();
        }

        // ---------------- REST ----------------
        public void GetJson(string path, Action<Dictionary<string, object>, string> done) => StartCoroutine(GetRoutine(path, done));

        IEnumerator GetRoutine(string path, Action<Dictionary<string, object>, string> done)
        {
            using (var req = UnityWebRequest.Get(ServerConfig.HttpBase + path))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { done?.Invoke(null, req.error ?? "HTTP ERROR"); yield break; }
                Dictionary<string, object> d = null; string err = null;
                try { d = Json.ParseObject(req.downloadHandler.text); } catch (Exception e) { err = "BAD JSON: " + e.Message; }
                done?.Invoke(d, err);
            }
        }
    }
}
