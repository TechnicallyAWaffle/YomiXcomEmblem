using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Top-right panel listing the battle's keyboard and mouse shortcuts. H shows or hides it (it starts open). Whether it is
    /// open is kept for the rest of the session: the scene reloads between screens (the upgrade phase), and a panel hidden
    /// once stays hidden.
    /// Keep the lists in step with TacticsCamera, PlayerOrdersUI, InteractionCamera and DevInput.
    /// </summary>
    public class ControlsLegend : MonoBehaviour
    {
        public KeyCode ToggleKey = KeyCode.H;
        public bool StartOpen = true;

        // Each row: a key and what it does (GameText "controls.*": an empty one hides the row). Read each time, so edits show at once.
        private static string[,] CameraKeys()
        {
            return new string[,]
            {
                { GameText.Get("controls.camera.pan.key", "WASD / Arrows"), GameText.Get("controls.camera.pan", "Pan") },
                { GameText.Get("controls.camera.pan_drag.key", "Middle-drag"), GameText.Get("controls.camera.pan_drag", "Pan") },
                { GameText.Get("controls.camera.rotate_tilt.key", "Right-drag"), GameText.Get("controls.camera.rotate_tilt", "Rotate / tilt") },
                { GameText.Get("controls.camera.rotate.key", "Q / E"), GameText.Get("controls.camera.rotate", "Rotate") },
                { GameText.Get("controls.camera.zoom.key", "Scroll"), GameText.Get("controls.camera.zoom", "Zoom") },
                { GameText.Get("controls.camera.center_on_orders.key", "Z"), GameText.Get("controls.camera.center_on_orders", "Center on the unit giving orders") },
                { GameText.Get("controls.camera.center_on_hovered.key", "Hover timeline"), GameText.Get("controls.camera.center_on_hovered", "Center on that unit") },
                { GameText.Get("controls.camera.replay_camera.key", "C"), GameText.Get("controls.camera.replay_camera", "Replay camera: cinematic / none (no cinematic effects)") },
            };
        }

        private static string[,] OrderKeys()
        {
            return new string[,]
            {
                { GameText.Get("controls.orders.ability.key", "1 - 0, -, =, [, ]"), GameText.Get("controls.orders.ability", "Use ability (again: repeat a follow-up)") },
                { GameText.Get("controls.orders.move.key", "F"), GameText.Get("controls.orders.move", "Move") },
                { GameText.Get("controls.orders.focus.key", "Hold Space"), GameText.Get("controls.orders.focus", "Focus or keep charging (time slows)") },
                { GameText.Get("controls.orders.pick.key", "Left-click"), GameText.Get("controls.orders.pick", "Pick target / point / dash spot") },
                { GameText.Get("controls.orders.free_aim.key", "Hold left-click 1s"), GameText.Get("controls.orders.free_aim", "Aim a single-target ability that way (hits the closest in a 90° cone)") },
                { GameText.Get("controls.orders.queue.key", "Shift + order"), GameText.Get("controls.orders.queue", "Queue several orders") },
                { GameText.Get("controls.orders.confirm.key", "Enter"), GameText.Get("controls.orders.confirm", "Confirm the queued plan") },
                { GameText.Get("controls.orders.undo.key", "Backspace"), GameText.Get("controls.orders.undo", "Undo last queued order") },
                { GameText.Get("controls.orders.back.key", "Right-click / Esc"), GameText.Get("controls.orders.back", "Back") },
                { GameText.Get("controls.orders.replay.key", "R"), GameText.Get("controls.orders.replay", "Replay (after the battle)") },
                { GameText.Get("controls.orders.replay_play.key", "Space / Home"), GameText.Get("controls.orders.replay_play", "Replay: pause or play / back to the start") },
                { GameText.Get("controls.orders.replay_seek.key", ", / ."), GameText.Get("controls.orders.replay_seek", "Replay: 5s back / ahead (or drag the bar)") },
                { GameText.Get("controls.orders.replay_speed.key", "[ / ]"), GameText.Get("controls.orders.replay_speed", "Replay: slower / faster") },
                { GameText.Get("controls.orders.dev_tools.key", "~"), GameText.Get("controls.orders.dev_tools", "Dev tools (win, heal, god mode...)") },
            };
        }

        private bool open;
        private static bool? remembered;   // open or not, across scene loads (null: not yet decided this session)
        private GUIStyle keyStyle, textStyle, headStyle;

        private void Awake() { open = remembered ?? StartOpen; }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey)) { open = !open; remembered = open; }
        }

        private void OnGUI()
        {
            if (UiDeclutter.On) return;   // the tactical view (Alt) hides the UI, this panel too
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
                GUI.Label(new Rect(x + width - 122f, y + 3f, 120f, line), GameText.Format("controls.show", "{0}: show controls", ToggleKey), textStyle);
                return;
            }

            var cameraKeys = CameraKeys(); var orderKeys = OrderKeys();
            float height = 16f + (Shown(cameraKeys) + Shown(orderKeys) + 3) * line;
            GUI.Box(new Rect(x, y, width, height), GUIContent.none);
            float cy = y + 6f;
            GUI.Label(new Rect(x + 8f, cy, width - 16f, line), GameText.Get("controls.camera", "Camera"), headStyle); cy += line;
            cy = Rows(cameraKeys, x + 8f, cy, keyWidth, width - 16f, line);
            GUI.Label(new Rect(x + 8f, cy, width - 16f, line), GameText.Get("controls.orders", "Orders"), headStyle); cy += line;
            cy = Rows(orderKeys, x + 8f, cy, keyWidth, width - 16f, line);
            GUI.Label(new Rect(x + 8f, cy, width - 16f, line), GameText.Format("controls.hide", "{0}: hide this panel", ToggleKey), textStyle);
        }

        private static int Shown(string[,] rows)
        {
            int n = 0;
            for (int i = 0; i < rows.GetLength(0); i++) if (GameText.Shown(rows[i, 1])) n++;
            return n;
        }

        private float Rows(string[,] rows, float x, float y, float keyWidth, float width, float line)
        {
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                if (!GameText.Shown(rows[i, 1])) continue;
                GUI.Label(new Rect(x, y, keyWidth, line), rows[i, 0], keyStyle);
                GUI.Label(new Rect(x + keyWidth, y, width - keyWidth, line), rows[i, 1], textStyle);
                y += line;
            }
            return y;
        }
    }
}
