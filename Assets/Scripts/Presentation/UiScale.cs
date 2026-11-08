using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// One scale for all the IMGUI panels, so text and boxes stay readable at any resolution. Each OnGUI calls Begin()
    /// first and then lays out in "GUI units": UiScale.Width x UiScale.Height instead of Screen.width x Screen.height.
    /// Mouse positions from Event.current are already in GUI units; world points and raycasts convert with
    /// WorldToGui / GuiToScreen.
    /// The scale fits the reference canvas (ReferenceWidth x ReferenceHeight, divided by UserScale) inside the screen, so
    /// the GUI area is never smaller than it in either direction: at 16:9 it is exactly that canvas; a narrower screen
    /// (16:10, 4:3, a laptop) gets more height, a wider one more width, and nothing laid out for 16:9 overlaps or cuts off.
    /// </summary>
    public static class UiScale
    {
        /// <summary>Overall UI size multiplier on top of the resolution scaling.</summary>
        public static float UserScale = 1.2f;
        /// <summary>The screen size the layout was designed for (16:9).</summary>
        public const float ReferenceWidth = 1600f, ReferenceHeight = 900f;
        /// <summary>The smallest scale used, so text stays legible on very small or very narrow screens.</summary>
        public const float MinFactor = 0.6f;

        public static float Factor
        {
            get { return Mathf.Max(MinFactor, Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight) * UserScale); }
        }
        public static float Width { get { return Screen.width / Factor; } }
        public static float Height { get { return Screen.height / Factor; } }

        public static void Begin()
        {
            float f = Factor;
            GUI.matrix = Matrix4x4.Scale(new Vector3(f, f, 1f));
        }

        /// <summary>A world point in GUI units (top-left origin). False if it is behind the camera.</summary>
        public static bool WorldToGui(Camera cam, Vector3 world, out Vector2 gui)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            float f = Factor;
            gui = new Vector2(s.x / f, (Screen.height - s.y) / f);
            return s.z > 0f;
        }

        /// <summary>A GUI-unit point (e.g. Event.current.mousePosition) as a screen pixel position for raycasts.</summary>
        public static Vector2 GuiToScreen(Vector2 gui)
        {
            float f = Factor;
            return new Vector2(gui.x * f, Screen.height - gui.y * f);
        }

        /// <summary>The OS cursor in GUI units (top-left origin), read from the input system rather than the GUI event.</summary>
        public static Vector2 CursorGui
        {
            get { Vector3 p = Input.mousePosition; float f = Factor; return new Vector2(p.x / f, (Screen.height - p.y) / f); }
        }

        /// <summary>
        /// Whether hover effects should show: the window has focus, the cursor is on it, and the GUI event's mouse position
        /// matches the real cursor. With a touchpad (taps and gestures arrive as touches) or once the cursor has left the
        /// window, the GUI event can keep an old position, leaving whatever was under it "hovered". Call it outside any
        /// GUI.BeginGroup / BeginArea / scroll view, where Event.current.mousePosition is in plain GUI units.
        /// </summary>
        public static bool HoverIsReal()
        {
            if (!Application.isFocused) return false;
            Vector3 p = Input.mousePosition;
            if (p.x < 0f || p.y < 0f || p.x > Screen.width || p.y > Screen.height) return false;
            if (Event.current == null) return true;
            return (Event.current.mousePosition - CursorGui).sqrMagnitude <= 4f * 4f;
        }
    }
}
