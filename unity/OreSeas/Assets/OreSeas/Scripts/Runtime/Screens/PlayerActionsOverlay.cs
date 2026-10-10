using System;
using OreSeas.Net;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>
    /// Store policy: report a captain's name or uploaded portrait, and block / unblock them. Opened from a leaderboard
    /// row or the PvP results. Reports go to the server (report_player); a block hides the captain on this phone at once
    /// and the server never pairs you again. AI captains and your own row never open it.
    /// </summary>
    public static class PlayerActionsOverlay
    {
        public const float ToastY = 150;   // above the 150-tall parchment (y 180..330)

        public static void Open(string playerId, string name, string avatar, int rating, Action changed = null)
        {
            var net = GameApp.Net;
            if (string.IsNullOrEmpty(playerId) || playerId.StartsWith("ai:") || (net != null && playerId == net.PlayerId)) return;
            var root = Gfx.Node(GameApp.OverlayRoot, "PlayerActions");
            int prevLayer = UiButton.TopLayer, L = prevLayer + 1;
            UiButton.TopLayer = L;
            var dim = Gfx.Rect(root, 0, 0, Px.W, Px.H, Pal.Ink, 350); dim.color = new Color(0.06f, 0.04f, 0.04f, 0.7f);
            UiButton.Create(root, 0, 0, Px.W, Px.H, null, L, "blocker").sfx = null;
            Gfx.Sprite(root, "parch_224x150", 8, 180, 351); Ui.Nails(root, 8, 180, 224, 150, 352);
            Ui.Ribbon(root, "ribbon_red", 120, 170, "CAPTAIN", 353);
            bool blocked = net != null && net.IsBlocked(playerId);
            Avatars.Draw(root, blocked ? Avatars.Default : avatar, 22, 196, 353);
            PixelText.Create(root, Lay.Fit(blocked ? "BLOCKED CAPTAIN" : (name ?? "?").ToUpperInvariant(), 150), 56, 200, TextStyle.Of(Pal.MapD), 354);
            PixelText.Create(root, rating > 0 ? "RATING " + rating : "", 56, 211, TextStyle.Of(Pal.MapB), 354);

            Action close = null;
            close = () => { Lay.PopBack(close); UiButton.TopLayer = prevLayer; if (root) UnityEngine.Object.Destroy(root.gameObject); };
            Lay.PushBack(close);

            void Report(string kind)
            {
                if (net == null || !net.Ready) { GameApp.Toast("OFFLINE - REPORT WHEN ONLINE", 2.2f, ToastY); return; }
                net.Report(playerId, kind);
                GameApp.Toast(kind == "avatar" ? "PORTRAIT REPORTED - THANK YOU" : "NAME REPORTED - THANK YOU", 2.4f, ToastY);
                GameApp.Audio.Sfx("lock");
                close();
            }
            var rn = Ui.Plank(root, 14, 236, 104, 18, "REPORT NAME", null, () => Report("name"), 355, L);
            var rp = Ui.Plank(root, 122, 236, 104, 18, "REPORT PORTRAIT", null, () => Report("avatar"), 355, L);
            // built-in portraits are ours; only uploaded pictures can be reported
            if (!Avatars.IsUploaded(avatar)) { rp.SetEnabled(false, false); rp.label.SetColor(Pal.Sand3); }
            Ui.Plank(root, 14, 260, 104, 18, blocked ? "UNBLOCK" : "BLOCK", null, () =>
            {
                net?.SetBlock(playerId, !blocked);
                GameApp.Toast(blocked ? "UNBLOCKED" : "BLOCKED - YOU WON'T BE PAIRED AGAIN", 2.4f, ToastY);
                GameApp.Audio.Sfx("lock");
                close(); changed?.Invoke();
            }, 355, L);
            Ui.Plank(root, 122, 260, 104, 18, "CLOSE", null, close, 355, L);
            PixelText.Create(root, "REPORTS ARE REVIEWED; REPEATED", 120, 290, TextStyle.Of(Pal.MapB, TextAlign.Center), 354);
            PixelText.Create(root, "REPORTS RESET THE NAME OR PICTURE.", 120, 301, TextStyle.Of(Pal.MapB, TextAlign.Center), 354);
            PixelText.Create(root, "BLOCKED CAPTAINS ARE NEVER PAIRED.", 120, 312, TextStyle.Of(Pal.MapB, TextAlign.Center), 354);
        }
    }

    /// <summary>Small yes/no parchment (account deletion confirm). Android back = no.</summary>
    public static class ConfirmOverlay
    {
        public static void Open(string title, string[] lines, string yes, Action onYes, string no = "KEEP")
        {
            var root = Gfx.Node(GameApp.OverlayRoot, "Confirm");
            int prevLayer = UiButton.TopLayer, L = prevLayer + 1;
            UiButton.TopLayer = L;
            var dim = Gfx.Rect(root, 0, 0, Px.W, Px.H, Pal.Ink, 350); dim.color = new Color(0.06f, 0.04f, 0.04f, 0.75f);
            UiButton.Create(root, 0, 0, Px.W, Px.H, null, L, "blocker").sfx = null;
            Gfx.Sprite(root, "parch_224x150", 8, 180, 351); Ui.Nails(root, 8, 180, 224, 150, 352);
            Ui.Ribbon(root, "ribbon_red_wide", 120, 170, title, 353);
            for (int i = 0; i < lines.Length && i < 7; i++) PixelText.Create(root, lines[i], 120, 198 + i * 11, TextStyle.Of(i == 0 ? Pal.Red1 : Pal.MapD, TextAlign.Center), 354);
            Action close = null;
            close = () => { Lay.PopBack(close); UiButton.TopLayer = prevLayer; if (root) UnityEngine.Object.Destroy(root.gameObject); };
            Lay.PushBack(close);
            Ui.Plank(root, 14, 300, 104, 20, yes, null, () => { close(); onYes?.Invoke(); }, 355, L).label.SetColor(Pal.Gold4);
            Ui.Plank(root, 122, 300, 104, 20, no, null, close, 355, L);
        }
    }
}
