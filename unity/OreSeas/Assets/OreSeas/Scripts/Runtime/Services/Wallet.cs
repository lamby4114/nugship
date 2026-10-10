using System;
using System.Threading.Tasks;
using UnityEngine;
#if ORESEAS_SOLANA
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
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
    /// Wallet seam. Seeker: Mobile Wallet Adapter -> Seed Vault. This build only CONNECTS (reads the public key).
    /// Nothing is ever signed or sent: signing is a stub that refuses until Victor explicitly OKs real transactions.
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
        /// <summary>Dev build: always refuses. Real signing needs Victor's explicit in-chat OK (SPEC hard rule).</summary>
        Task<WalletResult> SignTransactionStub(byte[] unsignedTx, string reason);
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
    }

#if ORESEAS_SOLANA
    /// <summary>Mobile Wallet Adapter via Solana.Unity-SDK (devnet). Connect = authorize + read pubkey only.</summary>
    public sealed class MwaWalletService : IWalletService
    {
        SolanaWalletAdapter adapter; Account account; string status = "NOT CONNECTED"; bool busy;
        public bool IsAvailable => Application.platform == RuntimePlatform.Android;
        public bool IsConnected => account != null;
        public string PublicKey => account?.PublicKey?.Key;
        public string Cluster => "DEVNET";
        public string Status => status;
        public event Action Changed;

        SolanaWalletAdapter Adapter()
        {
            if (adapter != null) return adapter;
            var opts = new SolanaWalletAdapterOptions
            {
                solanaMobileWalletAdapterOptions = new SolanaMobileWalletAdapterOptions
                {
                    identityUri = "https://nugship.app",   // TODO: final dApp identity URI before dApp Store submission
                    iconUri = "/favicon.ico",
                    name = "NUGSHIP",
                    keepConnectionAlive = true
                }
            };
            adapter = new SolanaWalletAdapter(opts, RpcCluster.DevNet);
            return adapter;
        }

        public async Task<WalletResult> Connect()
        {
            if (busy) return WalletResult.Fail("CONNECTING...");
            busy = true; status = "OPENING WALLET..."; Changed?.Invoke();
            try
            {
                account = await Adapter().Login();
                status = account != null ? "CONNECTED (DEVNET)" : "CANCELLED";
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

        // Deliberately never calls adapter.SignTransaction / SignAndSendTransaction / SignMessage.
        public Task<WalletResult> SignTransactionStub(byte[] tx, string reason)
        {
            Debug.Log("[OreSeas] sign request refused (dev build): " + reason);
            return Task.FromResult(WalletResult.Fail(WalletFactory.SigningDisabled));
        }
    }
#endif
}
