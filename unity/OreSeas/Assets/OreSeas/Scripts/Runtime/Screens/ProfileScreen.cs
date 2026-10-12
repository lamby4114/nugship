using System;
using System.Collections.Generic;
using OreSeas.Game;
using OreSeas.Net;
using OreSeas.Services;
using OreSeas.View;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>
    /// PROFILE CREATOR: captain name (validated + unique on the server), portrait grid of built-in pixel captains,
    /// "upload your own" (gallery photo -> square crop -> 20x20 palette pixel art), live preview of how rivals see you.
    /// Profile is paired with this phone's guest id; the wallet pubkey only shows when a wallet is connected.
    /// SIWS wallet pairing exists but is switched off (ServerConfig.SIWS_PAIRING = false).
    /// </summary>
    public sealed class ProfileScreen : ScreenBase
    {
        string draftName, draftAvatar; byte[] draftPng; Sprite draftSprite;
        Ui.Btn namePlank, saveBtn; PixelText status, walletLine, pairLine, idLine, previewName, previewSub;
        Avatars.Frame bigFrame, previewFrame, customFrame; SpriteRenderer selRing; Transform previewRoot; PixelText plus;
        readonly List<Vector2> cells = new List<Vector2>();
        TouchScreenKeyboard kb; bool saving;
        const int GridX = 22, GridY = 166, Cell = 26, Gap = 8, Cols = 6;
        static readonly Vector2 CustomCell = new Vector2(GridX, 266);

        public override void Build()
        {
            var r = Root;
            Scenery.Build(r, Biomes.Get(BiomeId.Sunset), 534);
            PixelText.Create(r, "YOUR CAPTAIN", 120, 18, TextStyle.Of(Pal.GoldTxt, TextAlign.Center, 2).Outline(Pal.Ink, true), 40);
            draftName = Profile.IsSet ? Profile.Name : "";
            draftAvatar = Profile.Avatar;
            if (Avatars.IsUploaded(draftAvatar) && Profile.CustomRef != draftAvatar) draftAvatar = Avatars.Default;

            // ---- identity panel ----
            Gfx.Sprite(r, "parch_224x96", 8, 44, 10); Ui.Nails(r, 8, 44, 224, 96, 11);
            bigFrame = Avatars.Draw(r, draftAvatar, 18, 58, 14, 2);
            namePlank = Ui.Plank(r, 78, 58, 144, 20, "", null, EditName, 20);
            // tap your big portrait to upload a photo; portrait + name plank form one row centred on the screen (18..222)
            var bigBtn = UiButton.Create(r, 18, 58, 52, 52, Upload, 0, "Btn_portrait"); bigBtn.visual = bigFrame.root;
            AuditGroup.Add(bigBtn.transform, "identity row"); AuditGroup.Add(namePlank.root, "identity row");
            status = PixelText.Create(r, "", 80, 83, TextStyle.Of(Pal.MapB), 14, "status");
            walletLine = PixelText.Create(r, "", 80, 95, TextStyle.Of(Pal.MapD), 14, "wallet");
            pairLine = PixelText.Create(r, "", 80, 107, TextStyle.Of(Pal.MapD), 14, "pair");
            idLine = PixelText.Create(r, "", 80, 119, TextStyle.Of(Pal.MapB), 14, "id");

            // ---- portrait grid ----
            Gfx.Sprite(r, "parch_224x150", 8, 146, 10); Ui.Nails(r, 8, 146, 224, 150, 11);
            PixelText.Create(r, "PICK A PORTRAIT", 120, 153, TextStyle.Of(Pal.Red1, TextAlign.Center), 14, "hdr");
            selRing = Gfx.Rect(r, 0, 0, Cell + 4, Cell + 4, Pal.Gold3, 13, "selring");
            for (int i = 0; i < Avatars.Ids.Length; i++)
            {
                int c = i % Cols, row = i / Cols; float x = GridX + c * (Cell + Gap), y = GridY + row * (Cell + Gap);
                cells.Add(new Vector2(x, y));
                string id = "a:" + Avatars.Ids[i];
                Avatars.Draw(r, id, x, y, 14);
                UiButton.Create(r, x, y, Cell, Cell, () => Pick(id), 0, "cell_" + Avatars.Ids[i]).sfx = "select";
            }
            // custom portrait cell + upload plank (same row width as the grid: 22..218)
            customFrame = Avatars.Draw(r, Avatars.Default, CustomCell.x, CustomCell.y, 14);
            var custom = Profile.CustomPng;
            if (custom != null) { var cr = Profile.CustomRef; Avatars.Register(cr.Substring(2), custom); customFrame.Set(cr); }
            else customFrame.SetSprite(null);
            plus = PixelText.Create(r, "+", CustomCell.x + 13, CustomCell.y + 6, TextStyle.Of(Pal.Gold3, TextAlign.Center, 2), 19, "plus");
            plus.SetVisible(custom == null);
            var cc = UiButton.Create(r, CustomCell.x, CustomCell.y, Cell, Cell, PickCustom, 0, "cell_custom"); cc.sfx = "select";
            var up = Ui.Plank(r, 56, 269, 162, 20, "UPLOAD YOUR OWN", null, Upload, 20);
            AuditGroup.Add(cc.transform, "upload row"); AuditGroup.Add(up.root, "upload row");

            // ---- preview ----
            Gfx.Sprite(r, "parch_224x62", 8, 302, 10);
            PixelText.Create(r, "HOW RIVALS SEE YOU", 120, 309, TextStyle.Of(Pal.Red1, TextAlign.Center), 14, "hdr2");
            previewRoot = Gfx.Node(r, "Preview");

            // ---- deck ----
            var deck = Gfx.Node(r, "Deck", 0, 420 - 350); Gfx.Sprite(deck, "deck", 0, 0, 50);
            saveBtn = Ui.Small(r, 8, 440, 108, 40, "SAVE", Save, 70, 0, "green", 2);
            Ui.Small(r, 124, 440, 108, 40, "HARBOR", () => GameApp.Go<TitleScreen>(), 70, 0, "dim", 2);
            PixelText.Create(r, "NAMES ARE UNIQUE - CHECKED BY THE SERVER", 120, 494, TextStyle.Of(Pal.Sand1, TextAlign.Center).Shadow(Pal.Ink), 72, "note");
            // store policy: in-app deletion of the captain (server profile + online stats + uploaded portrait)
            if (Profile.IsSet || GameApp.Leaderboard.Mine.gamesPlayed > 0)   // nothing to delete on a fresh first launch
                Ui.Plank(r, 70, 388, 100, 18, "DELETE PROFILE", null, AskDelete, 60).label.SetColor(Pal.Gold4);

            var watch = r.gameObject.AddComponent<KbWatch>(); watch.screen = this;
            Profile.Changed += OnProfileChanged;
            if (GameApp.Wallet != null) GameApp.Wallet.Changed += RefreshLines;
            GameApp.Net.EnsureConnected(ok => { if (this != null) { RefreshLines(); if (draftName.Length > 0) Check(); } });
            Refresh();
        }

        void OnDestroy()
        {
            Profile.Changed -= OnProfileChanged;
            if (GameApp.Wallet != null) GameApp.Wallet.Changed -= RefreshLines;
        }

        void OnProfileChanged() { if (this != null && !saving) RefreshLines(); }

        // ---------------- state ----------------
        void Refresh()
        {
            namePlank.SetLabel(draftName.Length > 0 ? draftName : "TAP TO NAME YOURSELF");
            namePlank.label.SetColor(draftName.Length > 0 ? Pal.Gold4 : Pal.Sand2);
            if (draftAvatar == "custom" && draftSprite != null) bigFrame.SetSprite(draftSprite); else bigFrame.Set(draftAvatar);
            // selection ring
            int idx = draftAvatar.StartsWith("a:") ? Array.IndexOf(Avatars.Ids, draftAvatar.Substring(2)) : -1;
            Vector2 at = idx >= 0 ? cells[idx] : CustomCell;
            Gfx.SetRect(selRing, at.x - 2, at.y - 2, Cell + 4, Cell + 4);
            RefreshLines(); RefreshPreview();
        }

        void RefreshLines()
        {
            if (walletLine == null) return;
            var w = GameApp.Wallet;
            walletLine.Set(w != null && w.IsConnected ? "WALLET " + Profile.ShortKey(w.PublicKey) : "WALLET: NOT CONNECTED");
            var paired = Profile.PairedWallet;
            pairLine.Set(!string.IsNullOrEmpty(paired) ? "PAIRED " + Profile.ShortKey(paired) : "SIWS PAIRING: OFF");
            pairLine.SetColor(string.IsNullOrEmpty(paired) ? Pal.MapB : Pal.Grass4);
            var net = GameApp.Net;
            idLine.Set(net != null && net.Ready ? "GUEST " + net.PlayerId.ToUpperInvariant() : "OFFLINE - SAVES ON PHONE");
        }

        void RefreshPreview()
        {
            foreach (Transform c in previewRoot) Destroy(c.gameObject);
            string nm = draftName.Length > 0 ? draftName : "CAPTAIN";
            string sub = "RATING " + (GameApp.Net != null && GameApp.Net.Ready ? GameApp.Net.Rating : 1000);
            // [portrait] 8 px [name / rating] as one group centred on x=120
            int tw = Mathf.Max(PixelFont.Measure(nm), PixelFont.Measure(sub)); int gw = Avatars.Size + 8 + tw;
            float l = Mathf.Floor(120 - gw / 2f);
            previewFrame = Avatars.Draw(previewRoot, draftAvatar.StartsWith("a:") || Avatars.IsUploaded(draftAvatar) ? draftAvatar : Avatars.Default, l, 324, 14);
            if (draftAvatar == "custom" && draftSprite != null) previewFrame.SetSprite(draftSprite);
            previewName = PixelText.Create(previewRoot, nm, l + Avatars.Size + 8, 327, TextStyle.Of(Pal.MapD), 14, "pname");
            previewSub = PixelText.Create(previewRoot, sub, l + Avatars.Size + 8, 338, TextStyle.Of(Pal.MapB), 14, "psub");
        }

        // ---------------- actions ----------------
        void Pick(string id) { draftAvatar = id; Refresh(); }

        void PickCustom()
        {
            if (draftSprite != null) { draftAvatar = "custom"; Refresh(); return; }
            var cr = Profile.CustomRef; if (cr != null) { draftAvatar = cr; Refresh(); return; }
            Upload();
        }

        void Upload()
        {
            if (!AvatarPicker.Supported) { GameApp.Toast("PHOTO UPLOAD WORKS ON THE PHONE"); return; }
            AvatarPicker.Pick((photo, err) =>
            {
                if (this == null) return;
                if (photo == null) { if (err != null) GameApp.Toast(err); return; }
                UsePhoto(photo);
            });
        }

        /// <summary>Pixelates a photo into the custom cell (also used by the desktop screenshot player).</summary>
        public void UsePhoto(Texture2D photo)
        {
            var px = AvatarPixelate.FromPhoto(photo);
            draftPng = px.EncodeToPNG(); draftSprite = AvatarPixelate.ToSprite(px);
            customFrame.SetSprite(draftSprite); plus.SetVisible(false);
            draftAvatar = "custom"; Refresh();
            GameApp.Audio?.Sfx("chime");
        }

        void EditName()
        {
            if (TouchScreenKeyboard.isSupported) { kb = TouchScreenKeyboard.Open(draftName, TouchScreenKeyboardType.Default, false, false, false, false, "CAPTAIN NAME", 16); return; }
            GameApp.Toast("TYPE YOUR NAME ON THE PHONE");
        }

        /// <summary>Sets the draft name (keyboard result, or the screenshot player) and runs the availability check.</summary>
        // The pixel font is ASCII-only: fold accents (JOSÉ -> JOSE) and drop emoji/other symbols before display,
        // so a keyboard entry can never render as '?' glyphs.
        static string AsciiFold(string raw)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (raw ?? "").Normalize(System.Text.NormalizationForm.FormD))
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (c < 128 && (char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '.' || c == '-')) sb.Append(c);
            }
            return sb.ToString();
        }
        public void SetName(string raw) { draftName = Profile.Normalize(AsciiFold(raw)); if (draftName.Length > 16) draftName = draftName.Substring(0, 16); Refresh(); Check(); }

        void Check()
        {
            if (draftName.Length == 0) { status.Set(""); return; }
            status.Set("CHECKING..."); status.SetColor(Pal.MapB);
            string asked = draftName;
            Profile.CheckName(draftName, (ok, reason, norm) =>
            {
                if (this == null || asked != draftName) return;
                status.Set(Profile.Explain(ok && reason == null ? "available" : reason));
                status.SetColor(ok ? Pal.Grass4 : Pal.Red1);
            });
        }

        void Save()
        {
            if (saving) return;
            if (draftName.Length == 0) { GameApp.Toast("NAME YOUR CAPTAIN FIRST"); EditName(); return; }
            var bad = Profile.Validate(draftName);
            if (bad != null) { GameApp.Toast(Profile.Explain(bad)); return; }
            saving = true; saveBtn.SetLabel("...");
            Profile.Save(draftName, draftAvatar == "custom" ? null : draftAvatar, draftAvatar == "custom" ? draftPng : null, (ok, code) =>
            {
                saving = false;
                if (this == null) return;
                saveBtn.SetLabel("SAVE");
                if (ok && code == "saved") { GameApp.Toast("PROFILE SAVED"); GameApp.Audio?.Sfx("chime"); status.Set(Profile.Explain("yours")); status.SetColor(Pal.Grass4); }
                else if (ok) GameApp.Toast("SAVED ON PHONE - SYNCS ONLINE");
                else { GameApp.Toast(Profile.Explain(code)); status.Set(Profile.Explain(code)); status.SetColor(Pal.Red1); }
                if (draftAvatar == "custom" && Profile.CustomRef != null) { draftAvatar = Profile.Avatar; draftSprite = null; customFrame.Set(Profile.CustomRef); }
                Refresh();
            });
        }

        void AskDelete()
        {
            ConfirmOverlay.Open("DELETE PROFILE?", new[] { "THIS CANNOT BE UNDONE.", "THE SERVER ERASES YOUR NAME,", "PORTRAIT, RATING AND ONLINE", "STATS. CAMPAIGN PROGRESS ON", "THIS PHONE IS KEPT." }, "DELETE", () =>
            {
                var net = GameApp.Net;
                if (net == null) { GameApp.Toast("OFFLINE - TRY AGAIN"); return; }
                GameApp.Toast("DELETING...", 6f);
                net.DeleteAccount((ok, why) =>
                {
                    if (!ok) { GameApp.Toast(why, 3f); GameApp.Audio?.Sfx("error"); return; }
                    GameApp.Toast("PROFILE DELETED", 2.6f); GameApp.Audio?.Sfx("chime");
                    GameApp.Go<TitleScreen>();
                });
            });
        }

        public override void Back() => GameApp.Go<TitleScreen>();

        sealed class KbWatch : MonoBehaviour
        {
            public ProfileScreen screen;
            void Update()
            {
                var k = screen != null ? screen.kb : null;
                if (k == null || k.status == TouchScreenKeyboard.Status.Visible) return;
                screen.kb = null;
                if (k.status == TouchScreenKeyboard.Status.Done && !string.IsNullOrWhiteSpace(k.text)) screen.SetName(k.text);
            }
        }
    }
}
