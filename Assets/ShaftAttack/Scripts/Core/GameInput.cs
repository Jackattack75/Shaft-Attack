using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShaftAttack
{
    /// <summary>
    /// One place that knows how to read the keyboard and mouse, whichever input backend
    /// the project is set to. Everything else asks this instead of touching input directly.
    ///
    /// All polled on demand, so script execution order never matters.
    ///
    /// Bindings: Shift dash, Ctrl slide (ground-slam in the air), Space jump,
    /// left mouse pickaxe, right mouse bomb. No sprint key - you are always running.
    /// </summary>
    public static class GameInput
    {
        // The new Input System reports mouse movement in pixels per frame, which is a much
        // bigger number than the old Input Manager's axes. Scaling here means the
        // sensitivity value on PlayerLook means roughly the same thing either way.
        private const float NewInputLookScale = 0.05f;

        /// <summary>WASD as (strafe, forward). Not normalised - the motor handles that.</summary>
        public static Vector2 Move
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                if (kb == null) return Vector2.zero;
                float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                return new Vector2(x, y);
#else
                return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
            }
        }

        /// <summary>Mouse movement since last frame, already scaled to a consistent range.</summary>
        public static Vector2 LookDelta
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Mouse m = Mouse.current;
                if (m == null) return Vector2.zero;
                return m.delta.ReadValue() * NewInputLookScale;
#else
                return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
            }
        }

        public static bool JumpPressedThisFrame
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                return kb != null && kb.spaceKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.Space);
#endif
            }
        }

        /// <summary>Shift. Costs a stamina bar.</summary>
        public static bool DashPressedThisFrame
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                return kb != null && (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame);
#else
                return Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
#endif
            }
        }

        /// <summary>Ctrl held. Slide on the ground.</summary>
        public static bool SlideHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                return kb != null && (kb.leftCtrlKey.isPressed || kb.cKey.isPressed);
#else
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
#endif
            }
        }

        /// <summary>Ctrl tapped. Starts a ground slam when airborne.</summary>
        public static bool SlidePressedThisFrame
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                return kb != null && (kb.leftCtrlKey.wasPressedThisFrame || kb.cKey.wasPressedThisFrame);
#else
                return Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.C);
#endif
            }
        }

        /// <summary>Left mouse held. Pickaxe.</summary>
        public static bool PrimaryHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Mouse m = Mouse.current;
                return m != null && m.leftButton.isPressed;
#else
                return Input.GetMouseButton(0);
#endif
            }
        }

        /// <summary>Right mouse (or Q) tapped. Throw a bomb.</summary>
        public static bool SecondaryPressedThisFrame
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Mouse m = Mouse.current;
                Keyboard kb = Keyboard.current;
                return (m != null && m.rightButton.wasPressedThisFrame) ||
                       (kb != null && kb.qKey.wasPressedThisFrame);
#else
                return Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Q);
#endif
            }
        }

        public static bool CancelPressedThisFrame
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.Escape);
#endif
            }
        }

        public static bool ClickPressedThisFrame
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Mouse m = Mouse.current;
                return m != null && m.leftButton.wasPressedThisFrame;
#else
                return Input.GetMouseButtonDown(0);
#endif
            }
        }
    }
}
