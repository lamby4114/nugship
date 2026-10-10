using UnityEngine;
#if ORESEAS_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace OreSeas
{
    /// <summary>Single-pointer input in logical mockup coordinates (touch or mouse).</summary>
    public static class InputRouter
    {
        public static bool Down, Held, Up;
        public static Vector2 Pos;
        public static bool BackPressed;

        // Desktop play-test harness only (-nugplay): synthetic single pointer in logical px, fed through the same
        // Down/Held/Up path as touch so buttons, boards and drags see exactly what a finger would produce.
        public static bool Synthetic, SynthPressed, SynthBack; public static Vector2 SynthPos;

        public static void Poll()
        {
            Down = Up = false; BackPressed = false;
            if (Synthetic)
            {
                Pos = SynthPos; BackPressed = SynthBack; SynthBack = false;
                Down = SynthPressed && !Held; Up = !SynthPressed && Held; Held = SynthPressed;
                return;
            }
            bool pressed = false; Vector2 sp = Vector2.zero; bool any = false;
#if ORESEAS_INPUTSYSTEM
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.isPressed) { pressed = true; sp = ts.primaryTouch.position.ReadValue(); any = true; }
            else if (ts != null && ts.primaryTouch.press.wasReleasedThisFrame) { sp = ts.primaryTouch.position.ReadValue(); any = true; }
            else if (Mouse.current != null) { pressed = Mouse.current.leftButton.isPressed; sp = Mouse.current.position.ReadValue(); any = true; }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) BackPressed = true;
#else
            if (Input.touchCount > 0) { var t = Input.GetTouch(0); pressed = t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled; sp = t.position; any = true; }
            else { pressed = Input.GetMouseButton(0); sp = Input.mousePosition; any = true; }
            if (Input.GetKeyDown(KeyCode.Escape)) BackPressed = true;
#endif
            if (any) Pos = Px.ScreenToLogical(sp);
            Down = pressed && !Held;
            Up = !pressed && Held;
            Held = pressed;
            if ((Down || Up) && Debug.isDebugBuild) Debug.Log("[OreSeas] input " + (Down ? "down" : "up") + " screen=" + sp + " logical=" + Pos + " scr=" + Screen.width + "x" + Screen.height + (Px.Cam != null ? " cam=" + Px.Cam.pixelRect : "") + " safe=" + Screen.safeArea);
        }
    }
}
