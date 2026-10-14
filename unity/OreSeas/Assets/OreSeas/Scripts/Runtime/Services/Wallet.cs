using System;
using System.Threading.Tasks;
using UnityEngine;
#if ORESEAS_SOLANA
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using Solana.Unity.Rpc;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;
using Solana.Unity.Wallet.Utilities;
#endif

namespace OreSeas.Services
{
    public struct WalletResult
    {
        public bool ok; public string message;
        public static WalletResult Ok(string m = "") => new WalletResult { ok = true, message = m };
        public static WalletResult Fail(string m) => new WalletResult { ok = false, message = m };
    }

    /// <summary>
    /// Wallet seam. Seeker: Mobile Wallet Adapter -> Seed Vault, MAINNET.
    /// v1.1.0: with ServerConfig.LIVE_ORE + the ORESEAS_LIVE_ORE define, the PLAYER's wallet signs (and submits) each ORE
    /// transaction and the SIWS sign-in message. The game never holds a key or any SOL (non-custodial).
    /// </summary>
    public interface IWalletService
    {
        bool IsAvailable { get; }
        bool IsConnected { get; }
        string PublicKey { get; }
        string Cluster { get; }
        string Status { get; }
        event Action Changed;
        Task<WalletResult> Connect();
        void Disconnect();
        /// <summary>Legacy seam: always refuses (live builds use MwaWalletService.SignAndSend with a typed Transaction).</summary>
        Task<WalletResult> SignTransactionStub(byte[] unsignedTx, string reason);
        /// <summary>Sign a UTF-8 text message (SIWS sign-in). ok -> message = base58 signature.</summary>
        Task<WalletResult> SignText(string message);
    }

    public static class WalletFactory
    {
        public static IWalletService Create()
        {
#if ORESEAS_SOLANA && UNITY_ANDROID && !UNITY_EDITOR
            return new MwaWalletService();
#else
            return new OfflineWalletService();
#endif
        }
        public static string Short(string pk) => string.IsNullOrEmpty(pk) || pk.Length < 10 ? pk ?? "" : pk.Substring(0, 4) + ".." + pk.Substring(pk.Length - 4);
        public const string SigningDisabled = "SIGNING DISABLED IN DEV BUILD";
    }

    /// <summary>Editor / desktop: no MWA. Game stays fully playable offline.</summary>
    public sealed class OfflineWalletService : IWalletService
    {
        public bool IsAvailable => false;
        public bool IsConnected => false;
        public string PublicKey => null;
        public string Cluster => "OFFLINE";
        public string Status => "MWA ONLY ON ANDROID / SEEKER";
        public event Action Changed { add { } remove { } }
        public Task<WalletResult> Connect() => Task.FromResult(WalletResult.Fail("WALLET NEEDS SEEKER / ANDROID"));
        public void Disconnect() { }
        public Task<WalletResult> SignTransactionStub(byte[] tx, string reason) => Task.FromResult(WalletResult.Fail(WalletFactory.SigningDisabled));
        public Task<WalletResult> SignText(string message) => Task.FromResult(WalletResult.Fail("NO WALLET"));
    }

#if ORESEAS_SOLANA
    /// <summary>Mobile Wallet Adapter via Solana.Unity-SDK on MAINNET. Signing only in live builds (ORESEAS_LIVE_ORE).</summary>
    public sealed class MwaWalletService : IWalletService
    {
        SolanaWalletAdapter adapter; Account account; string status = "NOT CONNECTED"; bool busy;
        public bool IsAvailable => Application.platform == RuntimePlatform.Android;
        public bool IsConnected => account != null;
        public string PublicKey => account?.PublicKey?.Key;
        public string Cluster => "MAINNET";
        public string Status => status;
        public event Action Changed;

        SolanaWalletAdapter Adapter()
        {
            if (adapter != null) return adapter;
            var opts = new SolanaWalletAdapterOptions
            {
                solanaMobileWalletAdapterOptions = new SolanaMobileWalletAdapterOptions
                {
                    identityUri = Net.ServerConfig.DappIdentityUri,   // https://lamby4114.github.io/nugship/ (the listed website)
                    iconUri = Net.ServerConfig.DappIconPath,           // relative to identityUri
                    name = "NUGSHIP",
                    keepConnectionAlive = true
                }
            };
            adapter = new SolanaWalletAdapter(opts, RpcCluster.MainNet, Net.ServerConfig.RpcUrl);
            return adapter;
        }

        public async Task<WalletResult> Connect()
        {
            if (busy) return WalletResult.Fail("CONNECTING...");
            busy = true; status = "OPENING WALLET..."; Changed?.Invoke();
            try
            {
                account = await Adapter().Login();
                status = account != null ? "CONNECTED (MAINNET)" : "CANCELLED";
                return account != null ? WalletResult.Ok(PublicKey) : WalletResult.Fail("CANCELLED");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OreSeas] MWA connect failed: " + e.Message);
                account = null; status = "NO WALLET FOUND";
                return WalletResult.Fail("NO MWA WALLET FOUND");
            }
            finally { busy = false; Changed?.Invoke(); }
        }

        public void Disconnect()
        {
            try { adapter?.Logout(); } catch (Exception e) { Debug.LogWarning("[OreSeas] MWA logout: " + e.Message); }
            account = null; status = "NOT CONNECTED"; Changed?.Invoke();
        }

        public Task<WalletResult> SignTransactionStub(byte[] tx, string reason) => Task.FromResult(WalletResult.Fail(WalletFactory.SigningDisabled));

        /// <summary>RPC used for blockhash / balance / account reads (same endpoint the wallet adapter uses).</summary>
        public IRpcClient Rpc => Adapter().ActiveRpcClient;

        public async Task<WalletResult> SignText(string message)
        {
#if ORESEAS_LIVE_ORE
            if (!Net.ServerConfig.SIWS_PAIRING || account == null) return WalletResult.Fail("SIGN-IN OFF");
            try
            {
                var sig = await Adapter().SignMessage(System.Text.Encoding.UTF8.GetBytes(message));
                return sig != null && sig.Length == 64 ? WalletResult.Ok(Encoders.Base58.EncodeData(sig)) : WalletResult.Fail("CANCELLED");
            }
            catch (Exception e) { Debug.LogWarning("[OreSeas] signMessage failed: " + e.Message); return WalletResult.Fail("SIGN-IN CANCELLED"); }
#else
            await Task.Yield(); return WalletResult.Fail(WalletFactory.SigningDisabled);
#endif
        }

        /// <summary>The player approves in their wallet; the wallet signs, then we submit via RPC. ok -> message = tx signature.</summary>
        public async Task<WalletResult> SignAndSend(Transaction tx, string reason)
        {
#if ORESEAS_LIVE_ORE
            if (!Net.ServerConfig.LIVE_ORE || account == null) return WalletResult.Fail("LIVE MINING OFF");
            if (busy) return WalletResult.Fail("WALLET BUSY");
            busy = true;
            try
            {
                Debug.Log("[OreSeas] wallet sign+send: " + reason);
                var r = await Adapter().SignAndSendTransaction(tx, false, Commitment.Confirmed);
                if (r != null && r.WasSuccessful && !string.IsNullOrEmpty(r.Result)) return WalletResult.Ok(r.Result);
                var why = r?.Reason ?? "NO RESPONSE";
                Debug.LogWarning("[OreSeas] send failed: " + why);
                return WalletResult.Fail(Short(why));
            }
            catch (Exception e) { Debug.LogWarning("[OreSeas] sign+send: " + e.Message); return WalletResult.Fail(e.Message.IndexOf("declin", StringComparison.OrdinalIgnoreCase) >= 0 || e.Message.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0 ? "DECLINED IN WALLET" : "WALLET ERROR"); }
            finally { busy = false; }
#else
            await Task.Yield(); return WalletResult.Fail(WalletFactory.SigningDisabled);
#endif
        }

        static string Short(string s)
        {
            if (s.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0) return "NOT ENOUGH SOL";
            if (s.IndexOf("blockhash", StringComparison.OrdinalIgnoreCase) >= 0) return "EXPIRED - TRY AGAIN";
            if (s.IndexOf("429", StringComparison.Ordinal) >= 0) return "RPC BUSY - TRY AGAIN";
            return "TX FAILED";
        }
    }
#endif
}
