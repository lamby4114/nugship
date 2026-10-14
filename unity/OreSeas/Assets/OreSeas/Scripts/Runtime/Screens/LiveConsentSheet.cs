using System;
using OreSeas.Services;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>
    /// v1.1.0 live-mining opt-in: 18+ and risk disclosure, wallet connect, then the player taps I AGREE.
    /// Shown every time live mining is switched on until the consent version is accepted; nothing is signed here.
    /// </summary>
    public static class LiveConsentSheet
    {
        static readonly string[] Lines =
        {
            "LIVE MINING USES REAL SOL.",
            "EACH VOLLEY DEPLOYS SOL INTO THE",
            "ORE MINING ROUND ON SOLANA MAINNET.",
            "YOU APPROVE EVERY DEPLOY IN YOUR",
            "OWN WALLET. NUGSHIP NEVER HOLDS",
            "YOUR SOL OR KEYS.",
            "MOST DEPLOYS LOSE SOME OR ALL SOL.",
            "WINNINGS ARE NOT GUARANTEED.",
            "NO REFUNDS. TX FEES APPLY.",
            "18+ ONLY. NOT WHERE ILLEGAL.",
            "NOT AFFILIATED WITH ORE OR",
            "SOLANA MOBILE. NOT FINANCIAL ADVICE.",
        };

        public static void Open(Action onAccept)
        {
            var root = Gfx.Node(GameApp.OverlayRoot, "LiveConsent");
            int prevLayer = UiButton.TopLayer;
            UiButton.TopLayer = 2;
            var dim = Gfx.Rect(root, 0, 0, Px.W, Px.H, Pal.Ink, 400); dim.color = new Color(0.06f, 0.04f, 0.04f, 0.85f);
            UiButton.Create(root, 0, 0, Px.W, Px.H, null, 2, "blocker").sfx = null;
            Gfx.Sprite(root, "parch_216x300", 12, 104, 401); Ui.Nails(root, 12, 104, 216, 300, 402);
            Ui.Ribbon(root, "ribbon_red", 120, 94, "LIVE MINING", 403);
            for (int i = 0; i < Lines.Length; i++)
                PixelText.Create(root, Lines[i], 120, 122 + i * 14, TextStyle.Of(i == 0 || i == 6 || i == 9 ? Pal.Red1 : Pal.MapD, TextAlign.Center), 403);
            PixelText.Create(root, "MATCH CAP " + Settings.LiveCapSol.ToString("0.00") + " SOL", 120, 296, TextStyle.Of(Pal.MapB, TextAlign.Center), 403);
            var walletLine = PixelText.Create(root, WalletLine(), 120, 312, TextStyle.Of(Pal.MapB, TextAlign.Center), 403);

            Action close = null;
            close = () => { Lay.PopBack(close); UiButton.TopLayer = prevLayer; if (root) UnityEngine.Object.Destroy(root.gameObject); };
            Lay.PushBack(close);
            Ui.Small(root, 18, 356, 98, 34, "CANCEL", close, 410, 2, "red");
            Ui.Small(root, 124, 356, 98, 34, "I AGREE (18+)", async () =>
            {
                if (GameApp.Wallet == null || !GameApp.Wallet.IsConnected)
                {
                    walletLine.Set("OPENING WALLET...");
                    var r = await GameApp.Wallet.Connect();
                    if (walletLine == null) return;
                    walletLine.Set(WalletLine());
                    if (!r.ok) { GameApp.Toast("CONNECT A WALLET FIRST"); return; }
                }
                Settings.LiveConsent = true;
                onAccept?.Invoke();
                GameApp.Toast("LIVE MINING ON - APPROVE EACH DEPLOY IN YOUR WALLET", 3f);
                close();
            }, 410, 2, "green");
        }

        static string WalletLine() => GameApp.Wallet != null && GameApp.Wallet.IsConnected
            ? "WALLET " + WalletFactory.Short(GameApp.Wallet.PublicKey) + " (MAINNET)"
            : "NO WALLET CONNECTED YET";
    }
}
