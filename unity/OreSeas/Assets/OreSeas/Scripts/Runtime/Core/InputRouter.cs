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
        static bool pendingUp; // a press+release inside one frame: emit Down now, Up next frame

        // Desktop play-test harness only (-nugplay): synthetic single pointer in logical px, fed through the same
        // Down/Held/Up path as touch so buttons, boards and drags see exactly what a finger would produce.
        public static bool Synthetic, SynthPressed, SynthBack; public static Vector2 SynthPos;

        public static void Poll()
        {
            Down = Up = false; BackPressed = false;
            if (pendingUp) { pendingUp = false; Up = true; Held = false; return; }
            if (Synthetic)
            {
                Pos = SynthPos; BackPressed = SynthBack; SynthBack = false;
                Down = SynthPressed && !Held; Up = !SynthPressed && Held; Held = SynthPressed;
                return;
            }
            bool pressed = false; Vector2 sp = Vector2.zero; bool any = false;
#if ORESEAS_INPUTSYSTEM
            if (!UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled) UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
            var at = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
            // EnhancedTouch buffers per event: a touch that begins and ends inside one frame is still reported as Began
            // this frame and Ended the next, so quick taps (and adb 'input tap') are never lost.
            if (at.Count > 0)
            {
                var t = at[0]; var ph = t.phase;
                pressed = ph != UnityEngine.InputSystem.TouchPhase.Ended && ph != UnityEngine.InputSystem.TouchPhase.Canceled;
                sp = t.screenPosition; any = true;
            }
            else if (Mouse.current != null && Touchscreen.current == null) { pressed = Mouse.current.leftButton.isPressed; sp = Mouse.current.position.ReadValue(); any = true; }
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
