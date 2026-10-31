using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// One scale for all the IMGUI panels, so text and boxes stay readable at any resolution. Each OnGUI calls Begin()
    /// first and then lays out in "GUI units": UiScale.Width x UiScale.Height instead of Screen.width x Screen.height.
    /// Mouse positions from Event.current are already in GUI units; world points and raycasts convert with
    /// WorldToGui / GuiToScreen.
    /// </summary>
    public static class UiScale
    {
        /// <summary>Overall UI size multiplier on top of the resolution scaling.</summary>
        public static float UserScale = 1.2f;
        /// <summary>Screen height the layout was designed for; taller screens scale up from it.</summary>
        public const float ReferenceHeight = 900f;

        public static float Factor { get { return Mathf.Max(1f, Screen.height / ReferenceHeight) * UserScale; } }
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
    }
}
