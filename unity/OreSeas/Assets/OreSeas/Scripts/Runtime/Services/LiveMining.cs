using System;
using System.Threading.Tasks;
using UnityEngine;
#if ORESEAS_SOLANA && ORESEAS_LIVE_ORE
using OreSeas.Ore.Chain;
using Solana.Unity.Rpc.Types;
using Solana.Unity.Wallet;
#endif

namespace OreSeas.Services
{
    /// <summary>
    /// v1.1.0 live ORE mining, MANUAL mode: every deploy is a transaction the player approves in their own wallet
    /// (MWA / Seed Vault). Non-custodial: SOL goes wallet -> ORE program. Spend caps: LiveTx per-square ceiling +
    /// the player's per-match cap (Settings.LiveCapSol). Off unless the player opted in (18+ consent sheet).
    /// </summary>
    public static class LiveMining
    {
        public static bool Built => Net.ServerConfig.LIVE_ORE;
        /// <summary>Player opted in, consent current, wallet connected.</summary>
        public static bool Active => Built && Settings.LiveMining && Settings.LiveConsent && GameApp.Wallet != null && GameApp.Wallet.IsConnected;

        public static string MatchId { get; private set; }
        public static ulong SpentLamports { get; private set; }
        public static string LastSignature { get; private set; }
        public static ulong CapLamports => (ulong)Math.Round(Settings.LiveCapSol * 1_000_000_000d);
        public static event Action<string> Status;

        public static void BeginMatch(string matchId) { if (MatchId != matchId) { MatchId = matchId; SpentLamports = 0; LastSignature = null; } }
        public static string Solscan(string sig) => "https://solscan.io/tx/" + sig;
        static void Say(string s) { Debug.Log("[OreSeas.Live] " + s); Status?.Invoke(s); }

        /// <summary>Server sent lock{fire:true}: build Checkpoint?+Deploy for exactly the locked squares and ask the wallet to approve.</summary>
        public static async Task<WalletResult> Fire(string matchId, ulong roundId, uint mask, ulong lamportsPerSquare)
        {
#if ORESEAS_SOLANA && ORESEAS_LIVE_ORE
            if (!Active) return WalletResult.Fail("LIVE MINING OFF");
            BeginMatch(matchId);
            var w = GameApp.Wallet as MwaWalletService; if (w == null) return WalletResult.Fail("NO MWA WALLET");
            try
            {
                var me = new PublicKey(w.PublicKey);
                var rpc = w.Rpc;
                var bal = await rpc.GetBalanceAsync(me.Key, Commitment.Confirmed);
                if (bal == null || !bal.WasSuccessful) return WalletResult.Fail("RPC BUSY - NO SHOT");
                var why = LiveTx.Validate(lamportsPerSquare, mask, SpentLamports, CapLamports, bal.Result.Value);
                if (why != null) { Say(why); return WalletResult.Fail(why); }
                var miner = await ReadMiner(rpc, me);
                var bh = await rpc.GetLatestBlockHashAsync(Commitment.Confirmed);
                if (bh == null || !bh.WasSuccessful) return WalletResult.Fail("RPC BUSY - NO SHOT");
                var tx = LiveTx.BuildUnsigned(me, bh.Result.Value.Blockhash, LiveTx.ManualFire(me, roundId, mask, lamportsPerSquare, miner));
                ulong cost = lamportsPerSquare * (ulong)LiveTx.Count(mask);
                Say("APPROVE " + (cost / 1e9).ToString("0.000") + " SOL IN WALLET");
                var r = await w.SignAndSend(tx, "ORE deploy round " + roundId + " mask " + mask);
                if (!r.ok) { Say(r.message); return r; }
                SpentLamports += cost; LastSignature = r.message;
                Say("DEPLOYED " + (cost / 1e9).ToString("0.000") + " SOL");
                return r;
            }
            catch (Exception e) { Debug.LogWarning("[OreSeas.Live] fire: " + e); Say("DEPLOY FAILED"); return WalletResult.Fail("DEPLOY FAILED"); }
#else
            await Task.Yield(); return WalletResult.Fail(WalletFactory.SigningDisabled);
#endif
        }

        /// <summary>Claim SOL + ORE rewards from the player's ORE miner (player signs).</summary>
        public static async Task<WalletResult> ClaimAll()
        {
#if ORESEAS_SOLANA && ORESEAS_LIVE_ORE
            if (!Built || GameApp.Wallet == null || !GameApp.Wallet.IsConnected) return WalletResult.Fail("CONNECT WALLET");
            var w = GameApp.Wallet as MwaWalletService; if (w == null) return WalletResult.Fail("NO MWA WALLET");
            try
            {
                var me = new PublicKey(w.PublicKey);
                var miner = await ReadMiner(w.Rpc, me);
                if (miner == null) return WalletResult.Fail("NOTHING TO CLAIM");
                bool sol = miner.RewardsSol > 0 || miner.NeedsCheckpoint, ore = miner.RewardsOre > 0 || miner.RefinedOre > 0 || miner.NeedsCheckpoint;
                if (!sol && !ore) return WalletResult.Fail("NOTHING TO CLAIM");
                var bh = await w.Rpc.GetLatestBlockHashAsync(Commitment.Confirmed);
                if (bh == null || !bh.WasSuccessful) return WalletResult.Fail("RPC BUSY");
                var tx = LiveTx.BuildUnsigned(me, bh.Result.Value.Blockhash, LiveTx.ClaimAll(me, miner, sol, ore));
                return await w.SignAndSend(tx, "ORE claim");
            }
            catch (Exception e) { Debug.LogWarning("[OreSeas.Live] claim: " + e); return WalletResult.Fail("CLAIM FAILED"); }
#else
            await Task.Yield(); return WalletResult.Fail(WalletFactory.SigningDisabled);
#endif
        }

        /// <summary>Text for the claim row: "0.012 SOL + 0.30 ORE" (read-only).</summary>
        public static async Task<string> RewardsLine()
        {
#if ORESEAS_SOLANA && ORESEAS_LIVE_ORE
            var w = GameApp.Wallet as MwaWalletService; if (w == null || !w.IsConnected) return "CONNECT WALLET";
            try
            {
                var m = await ReadMiner(w.Rpc, new PublicKey(w.PublicKey));
                if (m == null) return "NO ORE MINER YET";
                return (m.RewardsSol / 1e9).ToString("0.0000") + " SOL + " + ((m.RewardsOre + m.RefinedOre) / 1e11).ToString("0.000") + " ORE" + (m.NeedsCheckpoint ? " +PENDING" : "");
            }
            catch { return "RPC BUSY"; }
#else
            await Task.Yield(); return "LIVE MINING NOT IN THIS BUILD";
#endif
        }

#if ORESEAS_SOLANA && ORESEAS_LIVE_ORE
        static async Task<OreMiner> ReadMiner(Solana.Unity.Rpc.IRpcClient rpc, PublicKey me)
        {
            var a = await rpc.GetAccountInfoAsync(OrePda.Miner(me).Key, Commitment.Confirmed, BinaryEncoding.Base64);
            if (a == null || !a.WasSuccessful || a.Result?.Value?.Data == null || a.Result.Value.Data.Count == 0) return null;
            return OreMiner.Decode(Convert.FromBase64String(a.Result.Value.Data[0]));
        }
#endif
    }
}

namespace OreSeas.Services
{
    /// <summary>SIWS sign-in text (EIP-4361 style). The server only accepts it if it contains the connection's nonce.</summary>
    public static class LiveSignIn
    {
        public static string Message(string wallet, string nonce) =>
            Net.ServerConfig.SiwsDomain + " wants you to sign in with your Solana account:\n" + wallet +
            "\n\nSign in to NUGSHIP. This is not a transaction and costs nothing.\n\nNonce: " + nonce +
            "\nIssued At: " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }
}
