using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Battle timeline across the top of the screen: one chip per unit, placed at the moment it will be free to act again
    /// ("now" at the left, WindowSeconds ahead at the right). Player chips are blue, enemy chips red. Ready times come from
    /// dispatched events only (windup + recovery, stuns, walks, dashes), never from the simulation's look-ahead.
    ///
    /// While you aim an ability, a gold span runs from now to the moment it would land, with a faint tail to when your unit
    /// would be free again; enemies that will be free before it lands (and so can react) get a red frame. Aiming a move
    /// shows a cyan span to when the walk ends (the unit is free again), with the same red frames. Hovering a chip
    /// centres the camera on that unit and highlights it and its target like hovering the unit does (CombatVfx.HoverOverride).
    /// It stays minimized to a slim strip of ticks until the mouse comes up near it.
    /// </summary>
    public class TimelineBar : MonoBehaviour
    {
        public PlaybackDirector Director;
        public CombatVfx Vfx;
        public PlayerOrdersUI Orders;
        public TacticsCamera CameraRig;
        [Tooltip("Seconds of future the bar spans.")]
        public float WindowSeconds = 5f;
        [Tooltip("GUI units kept clear on the left (combat log) and right (controls panel) of the top edge.")]
        public float ReserveLeft = 480f, ReserveRight = 380f;

        private const float ChipWidth = 92f, ChipHeight = 22f, RowGap = 3f, AxisHeight = 18f, MaxWidth = 680f;
        private const float CollapsedHeight = 14f, HoverMargin = 30f;
        private static readonly Color PlayerColor = new Color(0.3f, 0.55f, 1f), EnemyColor = new Color(0.95f, 0.35f, 0.3f);
        private static readonly Color PreviewColor = new Color(1f, 0.85f, 0.3f), ReactFrame = new Color(1f, 0.15f, 0.1f);
        private static readonly Color MoveColor = new Color(0.45f, 0.9f, 1f);

        private struct Chip { public string UnitId, Label; public float At; public bool Player, Beyond, CanReact, Ordering; }

        private readonly Dictionary<string, float> readyAt = new Dictionary<string, float>();   // unit -> sim time it is free
        private readonly HashSet<string> dead = new HashSet<string>();
        private readonly List<Chip> chips = new List<Chip>();
        private readonly List<float> rowEnds = new List<float>();
        private readonly List<int> rowOf = new List<int>();   // row of chips[i]
        private string hoveredUnit, focusedUnit;
        private bool expanded;
        private GUIStyle chipStyle, axisStyle, spanStyle;

        private void OnEnable()
        {
            if (Director == null) return;
            Director.OnEventDispatched += OnEvent;
            Director.OnPlaybackReset += ResetTimes;
        }

        private void OnDisable()
        {
            if (Director != null)
            {
                Director.OnEventDispatched -= OnEvent;
                Director.OnPlaybackReset -= ResetTimes;
            }
            if (Vfx != null && hoveredUnit != null) Vfx.HoverOverride = null;
        }

        private void ResetTimes() { readyAt.Clear(); dead.Clear(); }

        private void OnEvent(TimelineEvent e)
        {
            switch (e.Type)
            {
                case TacticsGame.Core.EventType.ActionStarted:
                {
                    var a = (ActionStartedInfo)e.Payload;
                    if (!a.Free) readyAt[e.ActorId] = e.SimTime + a.Windup + a.Recovery;   // reactions run alongside: no change
                    break;
                }
                case TacticsGame.Core.EventType.ActionResolved:
                    readyAt[e.ActorId] = e.SimTime + ((ActionResolvedInfo)e.Payload).Recovery;
                    break;
                case TacticsGame.Core.EventType.ActionInterrupted:
                    // A dodge retries the action once the dash lands; anything else frees the unit (a stun follows separately).
                    readyAt[e.ActorId] = e.SimTime + (((InterruptInfo)e.Payload).Reason == "dodge" ? BattleSimulation.DashDuration : 0f);
                    break;
                case TacticsGame.Core.EventType.Stun:
                    readyAt[e.TargetId] = e.SimTime + ((StunInfo)e.Payload).Duration;
                    break;
                case TacticsGame.Core.EventType.MoveStarted:
                {
                    var m = (MoveInfo)e.Payload;
                    float length = 0f;
                    for (int i = 1; i < m.Path.Count; i++)
                    {
                        Vector3 leg = m.Path[i] - m.Path[i - 1]; leg.y = 0f;   // walking speed is over the ground; jumps add their own time
                        length += leg.magnitude;
                    }
                    var terrain = Director.Timeline != null ? Director.Timeline.InitialState.Terrain : null;
                    readyAt[e.ActorId] = e.SimTime + length / Mathf.Max(0.01f, m.Speed) + (terrain != null ? terrain.JumpSecondsAlong(m.Path) : 0f);
                    break;
                }
                case TacticsGame.Core.EventType.ChargeStarted:   // a charging unit decides (keep charging or release) all the time
                case TacticsGame.Core.EventType.MoveEnded:
                case TacticsGame.Core.EventType.UnitReady:
                    readyAt[e.ActorId] = e.SimTime;
                    break;
                case TacticsGame.Core.EventType.Dash:
                {
                    float until = e.SimTime + ((DashInfo)e.Payload).Duration, current;
                    readyAt[e.ActorId] = readyAt.TryGetValue(e.ActorId, out current) ? Mathf.Max(current, until) : until;
                    break;
                }
                case TacticsGame.Core.EventType.Death:
                    readyAt.Remove(e.TargetId);
                    dead.Add(e.TargetId);
                    break;
            }
        }

        private void OnGUI()
        {
            if (Director == null || Director.Timeline == null) return;
            UiScale.Begin();
            GUI.depth = -10;   // in front of the orders panel, so clicks on the bar don't reach the world
            if (chipStyle == null)
            {
                chipStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
                chipStyle.normal.textColor = Color.white;
                axisStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.UpperCenter };
                axisStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
                spanStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                spanStyle.normal.textColor = PreviewColor;
            }

            float now = Director.PlaybackClock;
            Unit actor = null, target; ActionDefinition def = null; float landAt = 0f;
            bool preview = Orders != null && Orders.TryGetTargetingPreview(out actor, out def, out target, out landAt);
            // Aiming a move instead: the span runs to when the walk ends and the unit is free to act again.
            Unit mover = null; float moveReady = 0f;
            bool movePreview = !preview && Orders != null && Orders.TryGetMovePreview(out mover, out moveReady);
            BuildChips(now, preview ? landAt : movePreview ? moveReady : float.NegativeInfinity, preview ? actor : mover);

            // Between the log and the controls panel if there's room, otherwise under the log.
            float space = UiScale.Width - ReserveLeft - ReserveRight;
            Rect bar;
            if (space >= 360f) { float w = Mathf.Min(MaxWidth, space); bar = new Rect(ReserveLeft + (space - w) / 2f, 10f, w, 0f); }
            else bar = new Rect(10f, 260f, Mathf.Min(MaxWidth, UiScale.Width - 20f), 0f);

            int rows = Mathf.Max(1, LayoutRows(bar));
            float spanRow = preview || movePreview ? ChipHeight : 0f;
            bar.height = 8f + rows * (ChipHeight + RowGap) + spanRow + AxisHeight + 6f;

            // Minimized to a slim strip unless the mouse is up near it (top-middle); once open it stays open while hovered.
            Vector2 mouse = Event.current.mousePosition;
            var zone = new Rect(bar.x - HoverMargin, 0f, bar.width + HoverMargin * 2f, (expanded ? bar.yMax : bar.y + CollapsedHeight) + HoverMargin);
            if (Event.current.type == UnityEngine.EventType.Repaint) expanded = zone.Contains(mouse);
            if (!expanded)
            {
                DrawCollapsed(new Rect(bar.x, bar.y, bar.width, CollapsedHeight), now, preview ? landAt : movePreview ? moveReady : -1f);
                if (Event.current.type == UnityEngine.EventType.Repaint) SetHover(null);
                return;
            }
            Fill(bar, new Color(0f, 0f, 0f, 0.55f));

            float axisY = bar.yMax - AxisHeight - 2f;
            Fill(new Rect(bar.x + 6f, axisY, bar.width - 12f, 1f), new Color(1f, 1f, 1f, 0.35f));
            for (int s = 0; s <= Mathf.FloorToInt(WindowSeconds); s++)
            {
                float x = XFor(bar, s / WindowSeconds);
                Fill(new Rect(x, axisY - 3f, 1f, 7f), new Color(1f, 1f, 1f, 0.5f));
                GUI.Label(new Rect(x - 20f, axisY + 2f, 40f, AxisHeight), s == 0 ? "now" : "+" + s + "s", axisStyle);
            }

            if (preview)
            {
                // The aimed ability: windup as a gold span to where it lands, recovery as a faint tail to when you're free.
                float land = Mathf.Clamp01((landAt - now) / WindowSeconds);
                float free = Mathf.Clamp01((landAt + def.Recovery / Mathf.Max(0.01f, actor.EffectiveActionSpeed()) - now) / WindowSeconds);
                float y = axisY - spanRow - 2f;
                float x0 = XFor(bar, 0f), x1 = XFor(bar, land), x2 = XFor(bar, free);
                Fill(new Rect(x1, y + 7f, Mathf.Max(0f, x2 - x1), 6f), new Color(PreviewColor.r, PreviewColor.g, PreviewColor.b, 0.3f));
                Fill(new Rect(x0, y + 5f, Mathf.Max(2f, x1 - x0), 10f), PreviewColor);
                Fill(new Rect(x1 - 1f, y, 3f, axisY - y), PreviewColor);
                string text = string.Format("{0} lands +{1:0.0}s", def.DisplayName, landAt - now);
                float tw = spanStyle.CalcSize(new GUIContent(text)).x;
                GUI.Label(new Rect(Mathf.Min(x1 + 4f, bar.xMax - tw - 4f), y, tw + 4f, ChipHeight), text, spanStyle);
            }
            else if (movePreview)
            {
                // The aimed move: walking time as a span to where the unit is free again.
                float free = Mathf.Clamp01((moveReady - now) / WindowSeconds);
                float y = axisY - spanRow - 2f;
                float x0 = XFor(bar, 0f), x1 = XFor(bar, free);
                Fill(new Rect(x0, y + 5f, Mathf.Max(2f, x1 - x0), 10f), MoveColor);
                Fill(new Rect(x1 - 1f, y, 3f, axisY - y), MoveColor);
                string text = string.Format("Walk: free +{0:0.0}s{1}", moveReady - now, moveReady - now > WindowSeconds ? " »" : "");
                float tw = spanStyle.CalcSize(new GUIContent(text)).x;
                var old = spanStyle.normal.textColor;
                spanStyle.normal.textColor = MoveColor;
                GUI.Label(new Rect(Mathf.Min(x1 + 4f, bar.xMax - tw - 4f), y, tw + 4f, ChipHeight), text, spanStyle);
                spanStyle.normal.textColor = old;
            }

            string hoverNow = null;
            for (int i = 0; i < chips.Count; i++)
            {
                var c = chips[i];
                var r = ChipRect(bar, i);
                bool hover = r.Contains(mouse);
                if (hover) hoverNow = c.UnitId;
                DrawChip(r, c, hover || c.UnitId == hoveredUnit);
                Fill(new Rect(r.center.x, r.yMax, 1f, axisY - r.yMax), new Color(1f, 1f, 1f, 0.3f));   // drop line to its time
            }
            if (Event.current.type == UnityEngine.EventType.Repaint) SetHover(hoverNow);

            if (bar.Contains(mouse) && Event.current.isMouse) Event.current.Use();   // the bar eats its own clicks
        }

        /// <summary>The minimized bar: a tick per unit at its ready time (gold for an aimed ability's landing).</summary>
        private void DrawCollapsed(Rect strip, float now, float landAt)
        {
            Fill(strip, new Color(0f, 0f, 0f, 0.45f));
            foreach (var c in chips)
                Fill(new Rect(XFor(strip, c.At) - 2f, strip.y + 2f, 4f, strip.height - 4f), c.Player ? PlayerColor : EnemyColor);
            if (landAt >= 0f)
                Fill(new Rect(XFor(strip, Mathf.Clamp01((landAt - now) / WindowSeconds)) - 1.5f, strip.y, 3f, strip.height), PreviewColor);
            GUI.Label(new Rect(strip.x, strip.yMax, strip.width, 16f), "timeline", axisStyle);
        }

        private void BuildChips(float now, float landAt, Unit ordering)
        {
            chips.Clear();
            foreach (var u in Director.Timeline.InitialState.Units)
            {
                float t;
                if (dead.Contains(u.Id)) continue;
                if (!readyAt.TryGetValue(u.Id, out t)) t = 0f;   // nothing yet: ready from the start
                float left = Mathf.Max(0f, t - now);
                chips.Add(new Chip
                {
                    UnitId = u.Id, Label = Short(u.DisplayName), At = Mathf.Clamp01(left / WindowSeconds), Beyond = left > WindowSeconds,
                    Player = u.Team == Team.Player,
                    CanReact = u.Team != Team.Player && t < landAt,   // free to act before the aimed ability lands
                    Ordering = ordering != null && ordering.Id == u.Id,
                });
            }
            chips.Sort((a, b) => a.At.CompareTo(b.At));
        }

        /// <summary>Stacks chips into rows so overlapping times don't cover each other. Returns the row count.</summary>
        private int LayoutRows(Rect bar)
        {
            rowEnds.Clear();
            rowOf.Clear();
            foreach (var c in chips)
            {
                float left = Mathf.Clamp(XFor(bar, c.At) - ChipWidth / 2f, bar.x + 2f, bar.xMax - ChipWidth - 2f);
                int row = rowEnds.FindIndex(end => end + 2f <= left);
                if (row < 0) { row = rowEnds.Count; rowEnds.Add(0f); }
                rowEnds[row] = left + ChipWidth;
                rowOf.Add(row);
            }
            return rowEnds.Count;
        }

        private Rect ChipRect(Rect bar, int i)
        {
            float x = Mathf.Clamp(XFor(bar, chips[i].At) - ChipWidth / 2f, bar.x + 2f, bar.xMax - ChipWidth - 2f);
            return new Rect(x, bar.y + 6f + rowOf[i] * (ChipHeight + RowGap), ChipWidth, ChipHeight);
        }

        private static float XFor(Rect bar, float t) { return bar.x + 6f + t * (bar.width - 12f); }

        private void DrawChip(Rect r, Chip c, bool highlighted)
        {
            Color fill = c.Player ? PlayerColor : EnemyColor;
            if (highlighted) fill = Color.Lerp(fill, Color.white, 0.35f);
            Color frame = c.CanReact ? ReactFrame : c.Ordering ? PreviewColor : new Color(0f, 0f, 0f, 0.8f);
            float border = c.CanReact || c.Ordering ? 3f : 2f;
            Fill(new Rect(r.x - border, r.y - border, r.width + border * 2f, r.height + border * 2f), frame);
            Fill(r, fill);
            GUI.Label(r, c.Beyond ? c.Label + " »" : c.Label, chipStyle);
        }

        /// <summary>Hovering a chip highlights its unit and target (via CombatVfx) and glides the camera to it once.</summary>
        private void SetHover(string unitId)
        {
            if (unitId == hoveredUnit) return;
            hoveredUnit = unitId;
            if (Vfx != null) Vfx.HoverOverride = unitId;
            if (unitId == null) { focusedUnit = null; return; }
            if (unitId == focusedUnit) return;
            focusedUnit = unitId;
            var view = Director.FindView(unitId);
            if (view != null && CameraRig != null) CameraRig.FocusOn(view.transform.position, false);
        }

        private static string Short(string name) { return name.Length <= 10 ? name : name.Substring(0, 9) + "."; }

        private static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
