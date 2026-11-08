using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Lower left, just above the order panel, while a unit is up for orders: its icon (UnitDefinition.Icon, or its initials
    /// on a team-coloured tile until there is art) with its health, strain and important statuses (StatusLook.Important)
    /// above. Hovering the icon lists every Etching the unit has and what each does.
    /// </summary>
    public class UnitPanel : MonoBehaviour
    {
        public PlayerOrdersUI Orders;
        public BattleContext Context;

        private const float Width = 210f, IconSize = 64f, Pad = 8f, Row = 16f;
        private static readonly Color PlayerTile = new Color(0.22f, 0.36f, 0.6f), HealthColor = new Color(0.3f, 0.9f, 0.3f), StrainColor = new Color(1f, 0.7f, 0.2f);
        private GUIStyle label, initials, popupTitle, popupName, popupText;
        // The longest each important status has had left, per unit, so its bar can drain from full.
        private readonly Dictionary<string, float> longest = new Dictionary<string, float>();

        /// <summary>Where the panel is on screen (GUI space; empty when hidden): the order panel lets clicks there be.</summary>
        public static Rect ScreenRect { get; private set; }

        private void Awake()
        {
            if (Orders == null) Orders = GetComponent<PlayerOrdersUI>();
            if (Orders == null) Orders = FindFirstObjectByType<PlayerOrdersUI>();
            if (Context == null) Context = FindFirstObjectByType<BattleContext>();
        }

        private void OnGUI()
        {
            ScreenRect = new Rect();
            if (Orders == null || UiDeclutter.On) return;
            var unit = Orders.OrderingUnit;
            var sim = Orders.Sim;
            if (unit == null || sim == null) return;
            UiScale.Begin();
            GUI.depth = -4;   // under the order panel's ability popup and the timeline, over the HUD
            Styles();

            var important = new List<StatusEffect>();
            foreach (var s in unit.Statuses) if (!StatusDisplay.IsHidden(s.Id) && StatusDisplay.Get(s.Id).Important) important.Add(s);

            float height = Pad * 2f + IconSize + 2f * (Row + 6f) + important.Count * (Row + 6f);
            var rect = new Rect(10f, UiScale.Height - PlayerOrdersUI.BottomReserved - height - 6f, Width, height);
            ScreenRect = rect;
            GUI.Box(rect, GUIContent.none);
            GUI.Box(rect, GUIContent.none);   // doubled for a more opaque background with the default skin

            float x = rect.x + Pad, inner = Width - 2f * Pad, y = rect.y + Pad;
            foreach (var s in important)
            {
                var look = StatusDisplay.Get(s.Id);
                bool timed = s.ExpiresAt < BattleSimulation.UntimedStatus;
                float left = timed ? Mathf.Max(0f, s.ExpiresAt - sim.Now) : 0f;
                y = Meter(x, y, inner, StatusDisplay.NameOf(s.Id) + (timed ? string.Format("  {0:0.00}s", left) : ""), timed ? Fill(unit.Id + "/" + s.Id, left) : 1f, look.Color);
            }
            y = Meter(x, y, inner, string.Format("Health  {0:0} / {1:0}", unit.Health, unit.MaxHealth), unit.MaxHealth > 0f ? unit.Health / unit.MaxHealth : 0f, HealthColor, unit.MaxHealth);
            y = Meter(x, y, inner, string.Format("Strain  {0:0} / {1:0}", unit.Strain, unit.MaxStrain), unit.MaxStrain > 0f ? unit.Strain / unit.MaxStrain : 0f, StrainColor, unit.MaxStrain);

            var icon = new Rect(x, y, IconSize, IconSize);
            DrawIcon(icon, unit);
            GUI.Label(new Rect(icon.xMax + 8f, y, inner - IconSize - 8f, IconSize), unit.DisplayName + "\n<size=11>" + EtchingCount(unit) + "</size>", label);

            if (icon.Contains(Event.current.mousePosition)) DrawEtchings(unit, icon);
            var e = Event.current;
            if (e.isMouse && rect.Contains(e.mousePosition)) e.Use();
        }

        /// <summary>A labelled bar; returns the y under it.</summary>
        private float Meter(float x, float y, float width, string text, float fill, Color color, float max = 0f)
        {
            GUI.Label(new Rect(x, y - 2f, width, Row), text, label);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x, y + Row - 2f, width, 5f), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y + Row - 2f, width * Mathf.Clamp01(fill), 5f), Texture2D.whiteTexture);
            GUI.color = old;
            BattleHUD.Segments(new Rect(x, y + Row - 2f, width, 5f), max);   // health and strain: one segment per 10
            return y + Row + 6f;
        }

        private float Fill(string key, float left)
        {
            float most;
            if (!longest.TryGetValue(key, out most) || left > most + 1e-3f) longest[key] = most = left;   // refreshed or new: full again
            return most > 0f ? left / most : 0f;
        }

        private void DrawIcon(Rect r, Unit unit)
        {
            var def = DefinitionOf(unit);
            if (def != null && def.Icon != null) { GUI.DrawTexture(r, def.Icon, ScaleMode.ScaleToFit); return; }
            var old = GUI.color;
            GUI.color = PlayerTile;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(r, Initials(unit.DisplayName), initials);
        }

        private static string Initials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var words = name.Split(new[] { ' ', '-' }, System.StringSplitOptions.RemoveEmptyEntries);
            return words.Length >= 2 ? (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpper() : name.Substring(0, Mathf.Min(2, name.Length)).ToUpper();
        }

        private static string EtchingCount(Unit unit)
        {
            int n = unit.Etchings != null ? unit.Etchings.Count : 0;
            return n == 0 ? "no Etchings" : string.Format("{0} Etching{1} (hover the icon)", n, n == 1 ? "" : "s");
        }

        private UnitDefinition DefinitionOf(Unit unit)
        {
            if (Context == null || Context.Placements == null) return null;
            foreach (var p in Context.Placements) if (p.View != null && p.View.UnitId == unit.Id) return p.Definition;
            return null;
        }

        /// <summary>The popup over the icon: every Etching, its effect, and the ability it is on (if one).</summary>
        private void DrawEtchings(Unit unit, Rect icon)
        {
            const float width = 320f;
            float inner = width - 2f * Pad;
            var rows = new List<KeyValuePair<string, string>>();
            if (unit.Etchings != null)
                foreach (var n in unit.Etchings)
                {
                    string on = null;
                    if (!string.IsNullOrEmpty(n.ActionId)) { var a = unit.Actions.Find(x => x.Id == n.ActionId); on = a != null ? a.DisplayName : n.ActionId; }
                    rows.Add(new KeyValuePair<string, string>(n.Name + (on != null ? "  on " + on : ""), n.Description));
                }
            string head = unit.DisplayName + ": Etchings";
            float h = Pad * 2f + popupTitle.CalcHeight(new GUIContent(head), inner) + 4f;
            if (rows.Count == 0) h += popupText.CalcHeight(new GUIContent("None yet."), inner);
            foreach (var r in rows) h += popupName.CalcHeight(new GUIContent(r.Key), inner) + popupText.CalcHeight(new GUIContent(r.Value), inner) + 4f;

            float x = icon.xMax + 12f, y = Mathf.Clamp(icon.yMax - h, 4f, UiScale.Height - h - 4f);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.88f);
            GUI.DrawTexture(new Rect(x, y, width, h), Texture2D.whiteTexture);
            GUI.color = old;
            float cy = y + Pad;
            cy += Line(x + Pad, cy, inner, head, popupTitle) + 4f;
            if (rows.Count == 0) Line(x + Pad, cy, inner, "None yet.", popupText);
            foreach (var r in rows)
            {
                cy += Line(x + Pad, cy, inner, r.Key, popupName);
                cy += Line(x + Pad, cy, inner, r.Value, popupText) + 4f;
            }
        }

        private static float Line(float x, float y, float width, string text, GUIStyle style)
        {
            float h = style.CalcHeight(new GUIContent(text), width);
            GUI.Label(new Rect(x, y, width, h), text, style);
            return h;
        }

        private void Styles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
            initials = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            initials.normal.textColor = Color.white;
            popupTitle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = true };
            popupTitle.normal.textColor = new Color(0.6f, 0.8f, 1f);
            popupName = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = true };
            popupName.normal.textColor = new Color(0.5f, 0.9f, 0.85f);
            popupText = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            popupText.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        }
    }
}
