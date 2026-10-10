using System;
using UnityEngine;

namespace OreSeas.Screens
{
    /// <summary>
    /// Battle pause menu (HUD pause button or Android back): RESUME / HOW TO PLAY / SETTINGS / RESIGN.
    /// The ORE clock is real time, so pausing only hides the board - rounds keep settling (noted on the panel).
    /// </summary>
    public static class PauseOverlay
    {
        static Transform open;
        public static bool IsOpen => open != null;

        /// <summary>resignLabel "RESIGN" (online: second tap confirms) or "ABANDON" (local).</summary>
        public static void Toggle(string resignLabel, Action resign, bool clockRuns = true)
        {
            if (open != null) { Close(); return; }
            int prevLayer = UiButton.TopLayer, layer = prevLayer + 1; const int o = 320;
            UiButton.TopLayer = layer;
            var root = Gfx.Node(GameApp.OverlayRoot, "Pause"); open = root;
            var dim = Gfx.Rect(root, 0, 0, Px.W, Px.H, Pal.Ink, o); dim.color = new Color(0.06f, 0.04f, 0.04f, 0.7f);
            UiButton.Create(root, 0, 0, Px.W, Px.H, null, layer, "blocker").sfx = null;
            Gfx.Sprite(root, "parch_224x150", 8, 170, o + 1); Ui.Nails(root, 8, 170, 224, 150, o + 2);
            Ui.Ribbon(root, "ribbon_red", 120, 162, "PAUSED", o + 3);
            Action close = null;
            close = () => { Lay.PopBack(close); UiButton.TopLayer = prevLayer; if (root) UnityEngine.Object.Destroy(root.gameObject); if (open == root) open = null; };
            closeAction = close;
            Ui.Small(root, 10, 192, 108, 34, "RESUME", close, o + 5, layer, "green");
            Ui.Small(root, 122, 192, 108, 34, "HOW TO PLAY", () => HelpOverlay.Open(), o + 5, layer, "gold");
            Ui.Small(root, 10, 232, 108, 34, "SETTINGS", () => { close(); SettingsOverlay.Open(); }, o + 5, layer);
            Ui.Btn rb = null; bool armed = false;
            rb = Ui.Small(root, 122, 232, 108, 34, resignLabel, () =>
            {
                if (!armed) { armed = true; rb.SetLabel("TAP TO CONFIRM"); GameApp.Audio?.Sfx("error"); return; }
                close(); resign?.Invoke();
            }, o + 5, layer, "red");
            // online matches ride the ORE clock (it can't stop); local practice really pauses
            PixelText.Create(root, clockRuns ? "THE ORE CLOCK KEEPS TICKING" : "PRACTICE CLOCK PAUSED", 120, 280, TextStyle.Of(Pal.MapB, TextAlign.Center), o + 4, "note");
            PixelText.Create(root, clockRuns ? "ROUNDS SETTLE WHILE PAUSED" : "TAKE YER TIME, CAPTAIN", 120, 292, TextStyle.Of(Pal.MapB, TextAlign.Center), o + 4, "note2");
            Lay.PushBack(close);
            CenterAudit.RunDeferred(GameApp.I, "PauseOverlay");
        }

        static Action closeAction;
        public static void Close() { var c = closeAction; closeAction = null; c?.Invoke(); }
    }
}
