using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using OreSeas.Net;
using UnityEngine;
using UnityEngine.Networking;

namespace OreSeas.Ore
{
    /// <summary>
    /// LIVE ORE rounds, READ-ONLY. Polls a Solana JSON-RPC (getSlot + getAccountInfo of the ORE Board / Round accounts)
    /// and exposes them through IOreRoundSource, so the battle clock / winning square follow real mainnet ORE.
    /// It never builds, signs or sends a transaction (there is no code path for it here; see ServerConfig.LIVE_ORE).
    /// Decoding + Round PDA come from the imported C# ORE layer (OreSeas.Ore.Chain) when the Solana SDK is present;
    /// without it, a minimal Board decoder keeps the round clock working and settlements are reported as unavailable.
    /// </summary>
    public sealed class LiveOreRoundSource : IOreRoundSource
    {
        public const string BoardAddress = "BrcSxdp1nXFzou1YyDnQJcPNBNHgoypZmTsyKBSLLXzi"; // OrePda.Board() (ORE v3)

        static LiveOreRoundSource shared;
        /// <summary>One poller per app (public RPCs rate-limit hard).</summary>
        public static LiveOreRoundSource Shared => shared ?? (shared = new LiveOreRoundSource());

        OreRoundInfo cur; bool haveBoard; double lastSlot; float lastSlotAt; float nextSlotPoll, nextBoardPoll; bool slotBusy, boardBusy;
        ulong boardRound, boardStart, boardEnd; readonly HashSet<ulong> settled = new HashSet<ulong>();
        public string Status { get; private set; } = "CONNECTING TO ORE...";
        public bool HasData => haveBoard;
        public OreRoundResult? LastResult { get; private set; }

        public bool IsSimulated => false;
        public bool PlayerPaced => false;
        public OreRoundInfo Current => cur;
        public float SlotSeconds => OreConstants.SlotSeconds;
        public event Action<OreRoundInfo> RoundOpened;
        public event Action<OreRoundResult> RoundSettled;

        public void CloseRoundNow() { }   // real ORE decides when a round ends

        public float SecondsUntilLock(int marginSlots)
        {
            if (!haveBoard || cur.phase != OrePhase.Deploying) return 0;
            return (float)Math.Max(0, (cur.endSlot - (double)marginSlots - cur.currentSlot) * SlotSeconds);
        }

        /// <summary>Idempotent (wall-clock based): safe to call from several screens per frame.</summary>
        public void Tick(float dt)
        {
            float now = Time.realtimeSinceStartup;
            if (!slotBusy && now >= nextSlotPoll) { slotBusy = true; nextSlotPoll = now + 3f; OreRpc.Call("getSlot", "[{\"commitment\":\"processed\"}]", OnSlot); }
            if (!boardBusy && now >= nextBoardPoll) { boardBusy = true; nextBoardPoll = now + 5f; GetAccount(BoardAddress, OnBoard); }
            if (!haveBoard) return;
            cur.currentSlot = lastSlot + (now - lastSlotAt) / SlotSeconds;
            cur.phase = cur.currentSlot < cur.endSlot ? OrePhase.Deploying : OrePhase.Intermission;
        }

        void OnSlot(Dictionary<string, object> res, string err)
        {
            slotBusy = false;
            if (res == null) { Status = "ORE RPC: " + (err ?? "NO SLOT"); return; }
            lastSlot = J.Num(res, "result"); lastSlotAt = Time.realtimeSinceStartup;
        }

        void OnBoard(byte[] data, string err)
        {
            boardBusy = false;
            if (data == null) { Status = "ORE RPC: " + (err ?? "NO BOARD"); return; }
            try
            {
#if ORESEAS_SOLANA
                var b = Chain.OreBoard.Decode(data);
                ulong id = b.RoundId, start = b.StartSlot, end = b.WaitingForFirstDeploy ? b.StartSlot + OreConstants.RoundSlots : b.EndSlot;
#else
                if (data.Length < 40 || data[0] != 105) throw new FormatException("not an ORE Board");
                ulong id = U64(data, 8), start = U64(data, 16), end = U64(data, 24);
                if (end == ulong.MaxValue) end = start + OreConstants.RoundSlots;
#endif
                bool newRound = haveBoard && id != boardRound;
                ulong prev = boardRound;
                boardRound = id; boardStart = start; boardEnd = end;
                if (lastSlotAt <= 0) { lastSlot = start; lastSlotAt = Time.realtimeSinceStartup; }
                cur = new OreRoundInfo { roundId = id, startSlot = start, endSlot = end, currentSlot = cur.currentSlot > 0 ? cur.currentSlot : start, phase = OrePhase.Deploying };
                haveBoard = true; Status = "LIVE ORE ROUND #" + id;
                if (newRound)
                {
                    FetchSettlement(prev);
                    RoundOpened?.Invoke(cur);
                }
            }
            catch (Exception e) { Status = "ORE DECODE: " + e.Message; }
        }

        /// <summary>Winning square of a finished round = Round.slot_hash rng % 25 (round.rs), via the Round PDA.</summary>
        void FetchSettlement(ulong roundId)
        {
            if (!settled.Add(roundId)) return;
#if ORESEAS_SOLANA
            string addr;
            try { addr = Chain.OrePda.Round(roundId).Key; } catch (Exception e) { Debug.LogWarning("[OreSeas] round PDA: " + e.Message); return; }
            GetAccount(addr, (data, err) =>
            {
                if (data == null) return;
                try
                {
                    var r = Chain.OreRound.Decode(data);
                    var sq = r.WinningSquare();
                    if (!sq.HasValue) return;  // entropy not revealed yet; the next round's poll won't retry (fine for display)
                    var res = new OreRoundResult { roundId = roundId, winningSquare = sq.Value, motherlode = r.HitMotherlode(), totalDeployedSol = r.TotalDeployed() / 1e9, simulated = false };
                    LastResult = res; RoundSettled?.Invoke(res);
                }
                catch (Exception e) { Debug.LogWarning("[OreSeas] round decode: " + e.Message); }
            });
#else
            Debug.Log("[OreSeas] live ORE settlement needs the Solana SDK (ORESEAS_SOLANA) for the Round PDA");
#endif
        }

        static ulong U64(byte[] d, int o) { ulong v = 0; for (int i = 7; i >= 0; i--) v = (v << 8) | d[o + i]; return v; }

        static void GetAccount(string address, Action<byte[], string> done)
        {
            OreRpc.Call("getAccountInfo", "[\"" + address + "\",{\"encoding\":\"base64\",\"commitment\":\"confirmed\"}]", (res, err) =>
            {
                var value = J.Obj(J.Obj(res, "result"), "value");
                var data = J.Arr(value, "data");
                if (data == null || data.Count < 1 || !(data[0] is string b64)) { done(null, err ?? "account not found"); return; }
                try { done(Convert.FromBase64String(b64), null); } catch (Exception e) { done(null, e.Message); }
            });
        }
    }

    /// <summary>Minimal JSON-RPC POST runner (read methods only: getSlot, getAccountInfo).</summary>
    public sealed class OreRpc : MonoBehaviour
    {
        static OreRpc inst; static int nextId = 1;
        static readonly HashSet<string> Allowed = new HashSet<string> { "getSlot", "getAccountInfo", "getMultipleAccounts", "getEpochInfo" };

        static OreRpc Inst()
        {
            if (inst) return inst;
            var go = new GameObject("OreRpc"); DontDestroyOnLoad(go); go.hideFlags = HideFlags.HideInHierarchy;
            return inst = go.AddComponent<OreRpc>();
        }

        public static void Call(string method, string paramsJson, Action<Dictionary<string, object>, string> done)
        {
            if (!Allowed.Contains(method)) { done?.Invoke(null, "method not allowed (read-only client)"); return; }
            Inst().StartCoroutine(Inst().Post(method, paramsJson, done));
        }

        IEnumerator Post(string method, string paramsJson, Action<Dictionary<string, object>, string> done)
        {
            string body = "{\"jsonrpc\":\"2.0\",\"id\":" + (nextId++) + ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}";
            using (var req = new UnityWebRequest(ServerConfig.RpcUrl, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { done?.Invoke(null, req.responseCode == 429 ? "RATE LIMITED" : req.error); yield break; }
                Dictionary<string, object> d = null; string err = null;
                try { d = Json.ParseObject(req.downloadHandler.text); if (J.Obj(d, "error") != null) { err = J.Str(J.Obj(d, "error"), "message"); d = null; } }
                catch (Exception e) { err = e.Message; }
                done?.Invoke(d, err);
            }
        }
    }
}
