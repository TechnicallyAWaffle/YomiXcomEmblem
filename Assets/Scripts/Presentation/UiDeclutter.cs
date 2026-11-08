using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Press Alt to see the battle itself (press it again to bring everything back): every panel, label and popup hides
    /// except the windup bars and the health and strain bars. Each UI script checks On before drawing what should go.
    /// </summary>
    public static class UiDeclutter
    {
        private static bool on;
        private static int checkedFrame = -1;

        public static bool On
        {
            get
            {
                // Read the key once a frame, however many scripts ask.
                if (checkedFrame != Time.frameCount)
                {
                    checkedFrame = Time.frameCount;
                    if (Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt)) on = !on;
                }
                return on;
            }
        }

        private static GUIStyle hintStyle;

        /// <summary>While the UI is hidden: a small prompt in the bottom-left corner saying Alt brings it back (BattleHUD draws it).</summary>
        public static void DrawHint()
        {
            if (hintStyle == null)
            {
                hintStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 4, 4) };
                hintStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            }
            var text = new GUIContent("UI hidden. Press ALT to show it again.");
            Vector2 size = hintStyle.CalcSize(text);
            GUI.Box(new Rect(12f, UiScale.Height - size.y - 12f, size.x, size.y), text, hintStyle);
        }
    }
}
