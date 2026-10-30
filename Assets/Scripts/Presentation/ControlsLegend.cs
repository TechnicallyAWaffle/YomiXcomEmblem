using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Top-right panel listing the battle's keyboard and mouse shortcuts. H shows or hides it (it starts open).
    /// Keep the lists in step with TacticsCamera, PlayerOrdersUI, InteractionCamera and DevInput.
    /// </summary>
    public class ControlsLegend : MonoBehaviour
    {
        public KeyCode ToggleKey = KeyCode.H;
        public bool StartOpen = true;

        private static readonly string[,] CameraKeys =
        {
            { "WASD / Arrows", "Pan" },
            { "Middle-drag", "Pan" },
            { "Right-drag", "Rotate / tilt" },
            { "Q / E", "Rotate" },
            { "Scroll", "Zoom" },
            { "F", "Center on the unit giving orders" },
            { "Hover timeline", "Center on that unit" },
            { "C", "Cinematic camera: key moments / all / off" },
        };

        private static readonly string[,] OrderKeys =
        {
            { "1 - 0", "Use ability (again: repeat a follow-up)" },
            { "M", "Move" },
            { "Hold Space", "Wait, with time running slowly" },
            { "Left-click", "Pick target / point / dash spot" },
            { "Shift + order", "Queue several orders" },
            { "Enter", "Confirm the queued plan" },
            { "Backspace", "Undo last queued order" },
            { "Right-click / Esc", "Back" },
            { "R", "Replay (after the battle)" },
        };

        private bool open;
        private GUIStyle keyStyle, textStyle, headStyle;

        private void Awake() { open = StartOpen; }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey)) open = !open;
        }

        private void OnGUI()
        {
            UiScale.Begin();
            if (keyStyle == null)
            {
                keyStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };
                keyStyle.normal.textColor = new Color(1f, 0.9f, 0.45f);
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                headStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };
                headStyle.normal.textColor = new Color(0.7f, 0.85f, 1f);
            }

            const float width = 360f, line = 19f, keyWidth = 130f;
            float x = UiScale.Width - width - 10f, y = 10f;
            if (!open)
            {
                GUI.Box(new Rect(x + width - 130f, y, 130f, 24f), GUIContent.none);
                GUI.Label(new Rect(x + width - 122f, y + 3f, 120f, line), ToggleKey + ": show controls", textStyle);
                return;
            }

            float height = 16f + (CameraKeys.GetLength(0) + OrderKeys.GetLength(0) + 3) * line;
            GUI.Box(new Rect(x, y, width, height), GUIContent.none);
            float cy = y + 6f;
            GUI.Label(new Rect(x + 8f, cy, width - 16f, line), "Camera", headStyle); cy += line;
            cy = Rows(CameraKeys, x + 8f, cy, keyWidth, width - 16f, line);
            GUI.Label(new Rect(x + 8f, cy, width - 16f, line), "Orders", headStyle); cy += line;
            cy = Rows(OrderKeys, x + 8f, cy, keyWidth, width - 16f, line);
            GUI.Label(new Rect(x + 8f, cy, width - 16f, line), ToggleKey + ": hide this panel", textStyle);
        }

        private float Rows(string[,] rows, float x, float y, float keyWidth, float width, float line)
        {
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                GUI.Label(new Rect(x, y, keyWidth, line), rows[i, 0], keyStyle);
                GUI.Label(new Rect(x + keyWidth, y, width - keyWidth, line), rows[i, 1], textStyle);
                y += line;
            }
            return y;
        }
    }
}
