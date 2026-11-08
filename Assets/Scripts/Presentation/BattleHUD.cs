using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Bare-bones IMGUI readout for test scenes: health/strain bars over each unit, a combat log and the outcome.
    /// Driven only by dispatched timeline events (never the simulation, which runs ahead of playback),
    /// so it stays in sync during slow motion and replays. Replace with real UI later.
    /// </summary>
    public class BattleHUD : MonoBehaviour
    {
        public BattleController Controller;
        public PlaybackDirector Director;
        public Camera Camera;
        [Tooltip("Whose unit is under the mouse (for the details panel). Found in the scene if empty.")]
        public CombatVfx Vfx;
        [Tooltip("Battle log lines shown while the mouse isn't over it; hovering it opens the rest of the history.")]
        public int CollapsedLogLines = 5;
        [Tooltip("Battle log lines kept (shown when hovered, as many as fit on screen).")]
        public int LogHistory = 60;
        public float BarHeightAboveUnit = 2.3f;

        private class UnitReadout
        {
            public string Name;
            public Team Team;
            public float Health, MaxHealth, Strain, MaxStrain, Energy, MaxEnergy;
            public readonly List<string> Statuses = new List<string>();
            public List<string> Passives = new List<string>();   // passive ids (PassiveDisplay: their names and what they do)
            public readonly Dictionary<string, int> Stacks = new Dictionary<string, int>();   // stack counts above 1 (Flowing Death's chain)
            // Timed statuses (Trance, Focus under way, a slow on it...): when each runs out, and how long it ran from (for its bar).
            public readonly Dictionary<string, StatusTimerInfo> Timers = new Dictionary<string, StatusTimerInfo>();
            public bool Dead;
            public string Activity = "";
            // Windup in progress (empty name = none): drawn as a filling bar with the ability name and time left.
            public string WindupName, WindupId;
            public float WindupEnd, WindupFull;
            public bool WindupHeavy;
            public WindupKind WindupAttack;   // what the bar's icon shows: an attack's reach, or nothing
            public bool Charging;   // the bar is a charge building toward full power (WindupEnd = full)
            public bool Channeling; // the bar is a channel running out (WindupEnd = when it ends at most)
            public string Order;   // out-of-range order in progress: "Slash > Brawler (chasing)" / "Snipe > Gunner (queued)"
            public string Holding, HeldBy;   // Subjugate in progress: whom this unit holds / who holds it (names)
            public bool Suspended;           // lifted into the air (Amblivate) until it falls
            public bool Airborne;            // hovering by its own leap (Skyward Demise) until it comes down
        }

        private readonly Dictionary<string, UnitReadout> units = new Dictionary<string, UnitReadout>();
        private readonly Dictionary<string, ActionDefinition> defs = new Dictionary<string, ActionDefinition>();
        private readonly Dictionary<string, StatusSpec> statusSpecs = new Dictionary<string, StatusSpec>();
        private readonly List<string> log = new List<string>();
        private BattleLogText logText;
        private bool battleOver;

        private void OnEnable()
        {
            if (Director == null) return;
            Director.OnPlaybackReset += Rebuild;
            Director.OnEventDispatched += OnEvent;
        }

        private void OnDisable()
        {
            if (Director == null) return;
            Director.OnPlaybackReset -= Rebuild;
            Director.OnEventDispatched -= OnEvent;
        }

        private void Rebuild()
        {
            units.Clear();
            defs.Clear();
            log.Clear();
            battleOver = false;
            LogText();
            if (Director.Timeline == null) return;
            foreach (var u in Director.Timeline.InitialState.Units)
            {
                units[u.Id] = new UnitReadout
                {
                    Name = u.DisplayName, Team = u.Team,
                    Health = u.Health, MaxHealth = u.MaxHealth, Strain = u.Strain, MaxStrain = u.MaxStrain,
                    Energy = u.Energy, MaxEnergy = u.MaxEnergy, Passives = u.Passives != null ? new List<string>(u.Passives) : new List<string>(),
                };
                foreach (var a in u.Actions)
                    for (var d = a; d != null && !defs.ContainsKey(d.Id); d = d.LinkedAction) defs[d.Id] = d;
            }
            // What each status does, from the abilities that give it (for the details panel).
            statusSpecs.Clear();
            foreach (var d in defs.Values)
            {
                foreach (var list in new[] { d.SelfStatuses, d.TargetStatusesOnHit, d.TargetStatusesDuringWindup, d.SelfStatusesDuringWindup, d.AuraStatuses })
                    if (list != null) foreach (var s in list) if (s != null && !string.IsNullOrEmpty(s.Id) && !statusSpecs.ContainsKey(s.Id)) statusSpecs[s.Id] = s;
            }
        }

        /// <summary>The log's wording, naming units and abilities from what this HUD knows.</summary>
        private void LogText()
        {
            if (logText != null) return;
            logText = new BattleLogText
            {
                UnitName = id => { var r = Get(id); return r != null ? r.Name : null; },
                Action = id => { ActionDefinition d; return id != null && defs.TryGetValue(id, out d) ? d : null; },
                Suspended = id => { var r = Get(id); return r != null && r.Suspended; },
            };
        }

        /// <summary>An ability's display name for the log (its id if it isn't a known action).</summary>
        private string ActionName(string id)
        {
            ActionDefinition def;
            return id != null && defs.TryGetValue(id, out def) && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : id;
        }

        private string NameOf(string id)
        {
            UnitReadout r;
            return id != null && units.TryGetValue(id, out r) ? r.Name : "?";
        }

        private UnitReadout Get(string id)
        {
            UnitReadout r;
            return id != null && units.TryGetValue(id, out r) ? r : null;
        }

        private void OnEvent(TimelineEvent evt)
        {
            LogText();
            string line = logText.Line(evt);   // before the readout changes (a fall asks whether the unit hung in the air)
            if (line != null) Log(line);
            var actor = Get(evt.ActorId);
            var target = Get(evt.TargetId);
            switch (evt.Type)
            {
                case TacticsGame.Core.EventType.ActionStarted:
                {
                    var a = (ActionStartedInfo)evt.Payload;
                    if (actor == null) break;
                    if (!a.Free) { actor.Charging = actor.Channeling = false; actor.WindupName = null; }   // a released charge (or anything new) ends the charge bar
                    if (a.ActionId == "wait") { actor.Activity = a.Transcended ? "focusing (transcended)" : "focusing"; break; }
                    if (a.ActionId == "idle") { actor.Activity = ""; break; }   // an AI pausing (not a Focus)
                    actor.Activity = ActionName(a.ActionId) + " (windup)";
                    ActionDefinition def;
                    if (a.Windup > 0f && defs.TryGetValue(a.ActionId, out def))
                    {
                        // A retry after a dodge keeps the original length, so the bar carries on from where it was.
                        bool resumed = actor.WindupId == a.ActionId && actor.WindupName == null && actor.WindupFull >= a.Windup;
                        actor.WindupFull = resumed ? actor.WindupFull : a.Windup;
                        actor.WindupEnd = evt.SimTime + a.Windup;
                        actor.WindupId = a.ActionId;
                        actor.WindupName = def.DisplayName;
                        actor.WindupHeavy = def.Impact == ImpactType.Heavy;
                        actor.WindupAttack = KindOf(def);
                    }
                    break;
                }
                case TacticsGame.Core.EventType.ActionResolved:
                    if (actor == null) break;
                    actor.Activity = "recovering";
                    actor.WindupName = actor.WindupId = null;
                    break;
                case TacticsGame.Core.EventType.ActionInterrupted:
                    if (actor == null) break;
                    actor.Activity = "";
                    actor.Charging = false;
                    actor.WindupName = null;
                    if (((InterruptInfo)evt.Payload).Reason != "dodge") actor.WindupId = null;   // a dodge retries it: remember which
                    break;
                case TacticsGame.Core.EventType.MoveStarted:
                    if (actor != null) actor.Activity = "moving";
                    break;
                case TacticsGame.Core.EventType.UnitReady:
                    if (actor != null) actor.Activity = "";
                    break;
                case TacticsGame.Core.EventType.Damage:
                {
                    var d = (DamageInfo)evt.Payload;
                    if (target != null) target.Health = d.RemainingHealth;
                    break;
                }
                case TacticsGame.Core.EventType.Carry:
                {
                    var c = (CarryInfo)evt.Payload;
                    if (actor != null) actor.Holding = c.Carrying ? NameOf(evt.TargetId) : null;
                    if (target != null) target.HeldBy = c.Carrying ? NameOf(evt.ActorId) : null;
                    break;
                }
                case TacticsGame.Core.EventType.ChannelStarted:
                {
                    var c = (ChannelInfo)evt.Payload;
                    if (actor == null) break;
                    actor.Activity = "channeling";
                    actor.WindupName = ActionName(c.ActionId);
                    actor.WindupId = c.ActionId;
                    actor.WindupFull = Mathf.Max(0.01f, c.Seconds);
                    actor.WindupEnd = evt.SimTime + c.Seconds;
                    actor.WindupHeavy = false;
                    actor.WindupAttack = WindupKind.None;
                    actor.Channeling = true;
                    break;
                }
                case TacticsGame.Core.EventType.ChannelEnded:
                {
                    var c = (ChannelInfo)evt.Payload;
                    if (actor == null) break;
                    actor.Channeling = false;
                    actor.WindupName = actor.WindupId = null;
                    actor.Activity = c.Broken ? "" : "recovering";
                    break;
                }
                case TacticsGame.Core.EventType.AerialSlam:
                    if (actor != null) actor.Holding = NameOf(evt.TargetId);
                    if (target != null) target.HeldBy = NameOf(evt.ActorId);
                    break;
                case TacticsGame.Core.EventType.AerialSlamEnded:
                    if (actor != null) actor.Holding = null;
                    if (target != null) target.HeldBy = null;
                    break;
                case TacticsGame.Core.EventType.DropStarted:
                    if (target != null) target.Activity = "dropping in";
                    break;
                case TacticsGame.Core.EventType.Lift:
                {
                    bool hover = ((LiftInfo)evt.Payload).Hover;
                    if (target != null) { if (hover) target.Airborne = true; else target.Suspended = true; }
                    break;
                }
                case TacticsGame.Core.EventType.Dash:
                {
                    var dash = (DashInfo)evt.Payload;
                    if (actor != null && dash.Fall) actor.Airborne = false;   // coming down from a hover (or knocked out of it)
                    if (actor == null || !dash.Teleport) break;
                    actor.Suspended = actor.Airborne = false;   // teleported out of the air: it lands on its feet
                    break;
                }
                case TacticsGame.Core.EventType.Fall:
                {
                    if (target != null) { target.Activity = "falling"; target.Suspended = false; }
                    break;
                }
                case TacticsGame.Core.EventType.Stun:
                    if (target != null) target.Activity = "stunned";
                    break;
                case TacticsGame.Core.EventType.StrainChanged:
                    if (actor != null) actor.Strain = ((StrainInfo)evt.Payload).Total;
                    break;
                case TacticsGame.Core.EventType.Heal:
                    if (target != null) target.Health = Mathf.Min(target.MaxHealth, target.Health + (float)evt.Payload);
                    break;
                case TacticsGame.Core.EventType.EnergyChanged:
                    if (actor != null) actor.Energy = ((EnergyInfo)evt.Payload).Total;
                    break;
                case TacticsGame.Core.EventType.StatusApplied:
                {
                    var id = (string)evt.Payload;
                    if (target != null && !target.Statuses.Contains(id)) target.Statuses.Add(id);
                    break;
                }
                case TacticsGame.Core.EventType.StatusExpired:
                    if (actor != null) { actor.Statuses.Remove((string)evt.Payload); actor.Timers.Remove((string)evt.Payload); actor.Stacks.Remove((string)evt.Payload); }
                    break;
                case TacticsGame.Core.EventType.StatusStacks:
                {
                    var st = (StatusStacksInfo)evt.Payload;
                    if (actor != null) actor.Stacks[st.Id] = st.Stacks;
                    break;
                }
                case TacticsGame.Core.EventType.StatusTimer:
                {
                    var t = (StatusTimerInfo)evt.Payload;
                    if (target != null) target.Timers[t.Id] = t;
                    break;
                }
                case TacticsGame.Core.EventType.ChargeStarted:
                {
                    var c = (ChargeInfo)evt.Payload;
                    ActionDefinition cd;
                    if (actor == null) break;
                    actor.Activity = "charging";
                    actor.WindupName = defs.TryGetValue(c.ActionId, out cd) ? cd.DisplayName : c.ActionId;
                    actor.WindupId = c.ActionId;
                    actor.WindupFull = Mathf.Max(0.01f, c.MaxSeconds);
                    actor.WindupEnd = evt.SimTime + c.MaxSeconds;
                    actor.WindupHeavy = false;
                    actor.WindupAttack = cd != null ? KindOf(cd) : WindupKind.None;
                    actor.Charging = true;
                    break;
                }
                case TacticsGame.Core.EventType.QueuedActionChanged:
                {
                    if (actor == null) break;
                    var q = (QueuedActionInfo)evt.Payload;
                    ActionDefinition qd;
                    if (evt.TargetId == null) { actor.Order = null; break; }
                    string name = defs.TryGetValue(q.ActionId ?? "", out qd) ? qd.DisplayName : q.ActionId;
                    actor.Order = string.Format("{0} > {1} ({2})", name, NameOf(evt.TargetId), q.Chase ? "chasing" : "queued");
                    break;
                }
                case TacticsGame.Core.EventType.UnitSpawned:
                {
                    // A summon joins mid-battle: its bars, its abilities for the details panel, and a line in the log.
                    var s = (SpawnInfo)evt.Payload;
                    var u = s.Unit;
                    if (u == null) break;
                    units[u.Id] = new UnitReadout { Name = u.DisplayName, Team = u.Team, Health = u.Health, MaxHealth = u.MaxHealth, Strain = u.Strain, MaxStrain = u.MaxStrain, Energy = u.Energy, MaxEnergy = u.MaxEnergy, Passives = u.Passives != null ? new List<string>(u.Passives) : new List<string>() };
                    foreach (var a in u.Actions)
                        for (var d = a; d != null && !defs.ContainsKey(d.Id); d = d.LinkedAction) defs[d.Id] = d;
                    break;
                }
                case TacticsGame.Core.EventType.Death:
                    if (target != null) { target.Dead = true; target.Health = 0f; target.Timers.Clear(); target.Stacks.Clear(); target.Activity = ""; target.WindupName = null; target.Order = null; target.Holding = target.HeldBy = null; target.Suspended = target.Airborne = false; }
                    break;
                case TacticsGame.Core.EventType.BattleEnd:
                    battleOver = true;
                    break;
            }
        }

        /// <summary>A status's name, with its stack count when it has more than one ("flow x3").</summary>
        private static string StatusLabel(UnitReadout r, string id)
        {
            int n;
            return StatusDisplay.NameOf(id) + (r.Stacks.TryGetValue(id, out n) && n > 1 ? " x" + n : "");
        }

        private void Log(string line)
        {
            log.Add(line);
            if (log.Count > Mathf.Max(1, LogHistory)) log.RemoveAt(0);
        }

        private void OnGUI()
        {
            GUI.depth = 5;   // behind the combat text and the orders panel (its ability popup covers these bars)
            UiScale.Begin();
            if (Director == null || Director.Timeline == null) return;
            if (units.Count == 0) Rebuild();
            var cam = Camera != null ? Camera : Camera.main;

            if (cam != null && Controller != null && Controller.Context != null && Controller.Context.Registry != null)
            {
                foreach (var pair in Controller.Context.Registry)
                {
                    var r = Get(pair.Key);
                    if (r == null || pair.Value == null) continue;
                    Vector2 at;
                    if (!UiScale.WorldToGui(cam, pair.Value.transform.position + Vector3.up * BarHeightAboveUnit, out at)) continue;
                    DrawUnit(r, at);
                }
            }
            if (UiDeclutter.On) { UiDeclutter.DrawHint(); return; }   // Alt: just the bars, and how to bring the rest back
            DrawDetails();

            DrawLog();

            if (battleOver && Director.ReachedTarget && Controller != null && Controller.Sim != null)
            {
                // A slim banner at the top (under the timeline strip), so the middle of the screen is free to watch the replay.
                var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true };
                GUI.Box(new Rect(UiScale.Width / 2f - 220, 30, 440, 42), Controller.Sim.Outcome + "   <size=14>R: replay</size>", style);
            }
        }

        private bool logExpanded;

        /// <summary>Where the battle log is on screen (GUI space): the tutorial's side popups keep clear of it.</summary>
        public static Rect LogRect { get; private set; }

        /// <summary>
        /// The battle log, bottom left: on top of the unit panel (the unit giving orders, its Etchings) while there is one,
        /// else down in the corner over the order bar. Like the timeline it stays small, the last CollapsedLogLines entries,
        /// until the mouse is over it; then it opens upward to the rest of the history (newest at the bottom, as many as
        /// fit). Fixed rows rather than GUILayout: the log can change mid-frame (a battle starting clears it), which would
        /// break a layout measured earlier. Lines are rich text (BattleLogText colours the impacts).
        /// </summary>
        private void DrawLog()
        {
            const float line = 20f, head = 26f, width = 460f;
            float bottom = (UnitPanel.ScreenRect.height > 0f ? UnitPanel.ScreenRect.y : UiScale.Height - PlayerOrdersUI.BottomReserved) - 6f;
            int fit = Mathf.Max(1, Mathf.FloorToInt((bottom - 10f - head - 4f) / line));
            int shown = Mathf.Min(log.Count, logExpanded ? Mathf.Min(fit, LogHistory) : Mathf.Min(fit, Mathf.Max(1, CollapsedLogLines)));
            float height = head + 4f + Mathf.Max(1, shown) * line;
            var box = new Rect(10, bottom - height, width, height);
            LogRect = box;
            if (Event.current.type == UnityEngine.EventType.Repaint) logExpanded = box.Contains(Event.current.mousePosition);
            GUI.Box(box, GUIContent.none);
            if (logExpanded) GUI.Box(box, GUIContent.none);   // more opaque while open: it covers the battle
            if (logStyle == null) logStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false, clipping = TextClipping.Clip };
            string more = !logExpanded && log.Count > shown ? "   (hover for more)" : "";
            GUI.Label(new Rect(box.x + 6, box.y + 4, width - 12, 20), string.Format("t = {0:0.000}s   playback x{1:0.00}{2}", Director.PlaybackClock, Director.playbackSpeed, more), logStyle);
            for (int i = 0; i < shown; i++) GUI.Label(new Rect(box.x + 6, box.y + head + i * line, width - 12, line), log[log.Count - shown + i], logStyle);
        }

        private GUIStyle logStyle;

        private void DrawUnit(UnitReadout r, Vector2 at)
        {
            const float width = 90f;
            float x = at.x - width / 2f;
            var label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 13 };
            label.normal.textColor = r.Team == Team.Player ? new Color(0.6f, 0.8f, 1f) : new Color(1f, 0.6f, 0.6f);
            string title = r.Dead ? r.Name + " (down)" : r.Name;
            bool winding = !r.Dead && r.WindupName != null;
            bool declutter = UiDeclutter.On;   // Alt: only the windup, health, strain and important-status bars
            if (!declutter) CombatVfx.OutlinedLabel(Fitted(at.x, at.y - (winding ? 54 : 36), 18, title, label, width + 80f), title, label, label.normal.textColor, 1f);
            if (winding) DrawWindup(r, x, at.y, width);
            else if (!r.Dead && !declutter)
            {
                // Held or holding (and hanging in the air) says more than the moment-to-moment activity.
                string activity = r.HeldBy != null ? "held by " + r.HeldBy : r.Suspended ? "suspended" : r.Airborne && (r.Activity == "" || r.Activity == "recovering" || r.Activity == "moving") ? "airborne" : r.Holding != null && (r.Activity == "" || r.Activity == "recovering") ? "holding " + r.Holding : r.Activity;
                if (!string.IsNullOrEmpty(activity)) CombatVfx.OutlinedLabel(new Rect(x - 40, at.y - 20, width + 80, 18), activity, label, label.normal.textColor, 1f);
            }

            Bar(new Rect(x, at.y, width, 7), r.MaxHealth > 0f ? r.Health / r.MaxHealth : 0f, new Color(0.3f, 0.9f, 0.3f), r.MaxHealth);
            Bar(new Rect(x, at.y + 9, width, 4), r.MaxStrain > 0f ? r.Strain / r.MaxStrain : 0f, new Color(1f, 0.7f, 0.2f), r.MaxStrain);
            float below = at.y + 15f;
            if (r.MaxEnergy > 0f)
            {
                Bar(new Rect(x, below, width, 4), r.Energy / r.MaxEnergy, new Color(0.35f, 0.6f, 1f));
                below += 6f;
            }
            if (!r.Dead) below = DrawImportantStatuses(r, x, below, width);
            if (declutter) return;
            if (r.Statuses.Count > 0 && !r.Dead)
            {
                // Lasting statuses (stances, holds) as one line of names; timed ones are in the details panel (hover the unit).
                var small = new GUIStyle(label) { alignment = TextAnchor.UpperCenter, fontSize = 12 };
                var shown = r.Statuses.FindAll(s => !StatusDisplay.IsHidden(s) && !r.Timers.ContainsKey(s)).ConvertAll(s => StatusLabel(r, s));
                if (shown.Count > 0)
                {
                    CombatVfx.OutlinedLabel(new Rect(x - 40, below, width + 80, 16), string.Join(" · ", shown.ToArray()), small, small.normal.textColor, 1f);
                    below += 12f;
                }
            }
            if (r.Order != null && !r.Dead)
            {
                var order = new GUIStyle(label) { alignment = TextAnchor.UpperCenter, fontSize = 12, fontStyle = FontStyle.Bold };
                order.normal.textColor = new Color(1f, 0.85f, 0.4f);
                CombatVfx.OutlinedLabel(new Rect(x - 60, below, width + 120, 16), r.Order, order, order.normal.textColor, 1f);
            }
        }

        // ------------------------------------------------------------------ details panel (hover a unit)

        private const float PanelWidth = 300f, PanelPad = 8f;
        private GUIStyle panelTitle, panelText, panelSmall;

        /// <summary>
        /// The unit under the mouse in detail, beside the cursor: its health and strain, what an enemy is winding up (the
        /// ability's name, time left and description), and every status on it with what it does and, if timed, how long is left.
        /// An enemy's passives are always listed, with what they do (PassiveDisplay).
        /// </summary>
        private void DrawDetails()
        {
            if (Vfx == null) Vfx = FindAnyObjectByType<CombatVfx>();
            var r = Vfx != null ? Get(Vfx.HoveredUnitId) : null;
            if (r == null || r.Dead) return;
            if (panelTitle == null)
            {
                panelTitle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = true };
                panelText = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                panelSmall = new GUIStyle(panelText) { fontSize = 11 };
                panelSmall.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            }
            float inner = PanelWidth - 2f * PanelPad;

            // Measure first (rows of label + optional note), then draw.
            var rows = new List<KeyValuePair<string, string>>();   // (heading, note) per row
            var colors = new List<Color>();
            string windup = null, windupNote = null;
            if (r.Team != Team.Player && r.WindupName != null)
            {
                ActionDefinition def;
                defs.TryGetValue(r.WindupId ?? "", out def);
                float left = Mathf.Max(0f, r.WindupEnd - Director.PlaybackClock);
                windup = string.Format("{0}: {1}  {2:0.00}s", r.Channeling ? "Channeling" : r.Charging ? "Charging" : "Winding up", r.WindupName, left);
                windupNote = def != null && !string.IsNullOrEmpty(def.Description) ? def.Description : null;
            }
            foreach (var id in r.Statuses)
            {
                if (StatusDisplay.IsHidden(id)) continue;
                StatusTimerInfo t;
                bool timed = r.Timers.TryGetValue(id, out t);
                string head = StatusLabel(r, id) + (timed ? string.Format("  {0:0.00}s", Mathf.Max(0f, t.ExpiresAt - Director.PlaybackClock)) : "");
                rows.Add(new KeyValuePair<string, string>(head, StatusEffects(id)));
                colors.Add(StatusDisplay.Get(id).Color);
            }
            // An enemy's passives, always: what it will do that no windup warns of.
            if (r.Team != Team.Player)
                foreach (var id in r.Passives)
                {
                    rows.Add(new KeyValuePair<string, string>("Passive: " + PassiveDisplay.NameOf(id), PassiveDisplay.DescriptionOf(id)));
                    colors.Add(PassiveColor);
                }

            string title = string.Format("{0}   health {1:0}/{2:0}   strain {3:0}/{4:0}", r.Name, r.Health, r.MaxHealth, r.Strain, r.MaxStrain);
            float h = PanelPad + panelTitle.CalcHeight(new GUIContent(title), inner);
            if (windup != null) h += 4f + panelText.CalcHeight(new GUIContent(windup), inner) + (windupNote != null ? panelSmall.CalcHeight(new GUIContent(windupNote), inner) : 0f);
            if (rows.Count > 0) h += 6f;
            foreach (var row in rows) h += panelText.CalcHeight(new GUIContent(row.Key), inner) + (row.Value != null ? panelSmall.CalcHeight(new GUIContent(row.Value), inner) : 0f) + 2f;
            h += PanelPad;

            Vector2 m = Event.current.mousePosition;
            float x = Mathf.Clamp(m.x + 18f, 4f, UiScale.Width - PanelWidth - 4f), y = Mathf.Clamp(m.y + 16f, 4f, UiScale.Height - h - 4f);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(x, y, PanelWidth, h), Texture2D.whiteTexture);
            GUI.color = old;

            float cy = y + PanelPad;
            panelTitle.normal.textColor = r.Team == Team.Player ? new Color(0.6f, 0.8f, 1f) : new Color(1f, 0.6f, 0.6f);
            cy += Line(x, cy, inner, title, panelTitle);
            if (windup != null)
            {
                cy += 4f;
                panelText.normal.textColor = r.WindupHeavy ? HeavyWindup : r.Channeling ? ChannelWindup : r.Charging ? ChargeWindup : LightWindup;
                cy += Line(x, cy, inner, windup, panelText);
                if (windupNote != null) cy += Line(x, cy, inner, windupNote, panelSmall);
            }
            if (rows.Count > 0) cy += 6f;
            for (int i = 0; i < rows.Count; i++)
            {
                panelText.normal.textColor = colors[i];
                cy += Line(x, cy, inner, rows[i].Key, panelText);
                if (rows[i].Value != null) cy += Line(x, cy, inner, rows[i].Value, panelSmall);
                cy += 2f;
            }
        }

        private static float Line(float x, float y, float width, string text, GUIStyle style)
        {
            float h = style.CalcHeight(new GUIContent(text), width);
            GUI.Label(new Rect(x + PanelPad, y, width, h), text, style);
            return h;
        }

        /// <summary>What a status does, from the ability data that gives it ("Dodge +30%, stun immune"), or null if unknown.</summary>
        private string StatusEffects(string id)
        {
            StatusSpec s;
            if (!statusSpecs.TryGetValue(id, out s)) return null;
            var parts = new List<string>();
            if (s.Modifiers != null) foreach (var m in s.Modifiers) parts.Add(StatText.Name(m.Stat) + " " + StatText.Change(m));
            if (s.ActionModifiers != null) foreach (var m in s.ActionModifiers) parts.Add(StatText.Label(m, ActionName) + " " + StatText.Change(m));
            var cells = new List<KeyValuePair<string, string>>();
            var notes = new List<string>();
            StatText.Describe(s.Flags, cells, notes);
            foreach (var c in cells) parts.Add(c.Key + " " + c.Value);
            parts.AddRange(notes);
            if (s.ForbidsActions != null && s.ForbidsActions.Count > 0) parts.Add("can't use " + string.Join(", ", s.ForbidsActions.ConvertAll(ActionName).ToArray()));
            return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : null;
        }

        /// <summary>A label rect centred on 'centerX', at least 'minWidth' wide and wide enough for all of 'text' (never clipped).</summary>
        private static Rect Fitted(float centerX, float y, float height, string text, GUIStyle style, float minWidth)
        {
            float w = Mathf.Max(minWidth, style.CalcSize(new GUIContent(text)).x + 12f);
            return new Rect(centerX - w / 2f, y, w, height);
        }

        public enum WindupKind { None, Melee, Ranged }

        /// <summary>The icon for an ability's windup bar: melee or ranged for anything that hits enemies, none otherwise.</summary>
        private static WindupKind KindOf(ActionDefinition def)
        {
            bool attack = def.Damage > 0f || def.StunDuration > 0f;
            bool atEnemies = def.Targeting == TargetType.Enemy || def.Targeting == TargetType.Position || def.Targeting == TargetType.AnyUnit ||
                             (def.Targeting == TargetType.Self && def.Shape != AreaShape.None);
            if (!attack || !atEnemies) return WindupKind.None;
            return def.IsRanged ? WindupKind.Ranged : WindupKind.Melee;
        }

        private static readonly Color PassiveColor = new Color(0.85f, 0.75f, 1f);
        private static readonly Color HeavyWindup = new Color(1f, 0.32f, 0.1f), LightWindup = new Color(0.45f, 0.85f, 1f), ChargeWindup = new Color(0.8f, 0.45f, 1f),
                                      ChannelWindup = new Color(0.4f, 1f, 0.75f);

        /// <summary>
        /// Windup in progress: the ability name and time left over a bar that fills to the moment it lands, with an icon on
        /// its left for an attack (crosshair: ranged, sword: melee). A Light one is a slim blue bar. A Heavy one (it will break
        /// windups) is unmistakable: bigger bold red-orange text and a thick bar in a pulsing frame.
        /// Both blink for the last 20% so an incoming hit is easy to spot.
        /// </summary>
        private void DrawWindup(UnitReadout r, float x, float y, float width)
        {
            float left = Mathf.Max(0f, r.WindupEnd - Director.PlaybackClock);
            float fill = r.WindupFull > 0f ? Mathf.Clamp01(1f - left / r.WindupFull) : 1f;
            bool heavy = r.WindupHeavy && !r.Charging && !r.Channeling && r.WindupAttack != WindupKind.None;
            Color color = r.Channeling ? ChannelWindup : r.Charging ? ChargeWindup : heavy ? HeavyWindup : LightWindup;
            if (r.Channeling) fill = 1f - fill;   // a channel drains as it runs out
            else if (fill > 0.8f && Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.5f) color = Color.Lerp(color, Color.white, 0.7f);

            var text = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = heavy ? 14 : 11, fontStyle = heavy ? FontStyle.Bold : FontStyle.Normal, wordWrap = false, clipping = TextClipping.Overflow };
            text.normal.textColor = color;
            string name = r.WindupName;
            string label = r.Charging ? string.Format("{0}  {1:0}%", name, fill * 100f) : r.Channeling ? string.Format("{0} (channel)  {1:0.00}s", name, left) : string.Format("{0}  {1:0.00}s", name, left);
            CombatVfx.OutlinedLabel(Fitted(x + width / 2f, y - 39, 18, label, text, width + 100f), label, text, text.normal.textColor, heavy ? 2f : 1f);

            float h = heavy ? 10f : 5f;
            var bar = new Rect(x - 5f, y - 7f - h, width + 10f, h);
            var old = GUI.color;
            if (heavy)
            {
                // A frame that pulses between the bar's colour and white.
                GUI.color = Color.Lerp(HeavyWindup, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f));
                GUI.DrawTexture(new Rect(bar.x - 3f, bar.y - 3f, bar.width + 6f, bar.height + 6f), Texture2D.whiteTexture);
            }
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(bar.x - 1f, bar.y - 1f, bar.width + 2f, bar.height + 2f), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * fill, bar.height), Texture2D.whiteTexture);
            if (r.WindupAttack != WindupKind.None)
            {
                const float icon = 16f;
                var at = new Rect(bar.x - icon - 5f, bar.y + bar.height / 2f - icon / 2f, icon, icon);
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.DrawTexture(new Rect(at.x - 1f, at.y - 1f, at.width + 2f, at.height + 2f), Texture2D.whiteTexture);
                GUI.color = color;
                GUI.DrawTexture(at, r.WindupAttack == WindupKind.Ranged ? HudIcons.Ranged : HudIcons.Melee);
            }
            GUI.color = old;
        }

        /// <summary>Important statuses (StatusLook.Important: Trance, Wrath, Unstoppable) as duration bars in their colour, under
        /// the health and strain bars; their names are in the details panel. One with no timer (Unstoppable lasts a windup)
        /// runs out with the windup, or stays full. Returns where the next line goes.</summary>
        private float DrawImportantStatuses(UnitReadout r, float x, float y, float width)
        {
            foreach (var id in r.Statuses)
            {
                if (StatusDisplay.IsHidden(id)) continue;
                var look = StatusDisplay.Get(id);
                if (!look.Important) continue;
                Bar(new Rect(x, y, width, 4), ImportantFill(r, id, look), look.Color);
                y += 6f;
            }
            return y;
        }

        /// <summary>How full an important status's bar is: its time left, the windup it lasts for, or full.</summary>
        private float ImportantFill(UnitReadout r, string id, StatusDisplay.Look look)
        {
            StatusTimerInfo t;
            float left;
            if (r.Timers.TryGetValue(id, out t) && t.Duration > 0f) left = Mathf.Clamp01((t.ExpiresAt - Director.PlaybackClock) / t.Duration);
            else if (r.WindupName != null && !r.Charging && r.WindupFull > 0f) left = Mathf.Clamp01((r.WindupEnd - Director.PlaybackClock) / r.WindupFull);
            else left = 1f;
            return look.CountsUp ? 1f - left : left;
        }

        /// <summary>A bar filled to 'fill'; with 'max' above 0, cut into segments of SegmentSize (health and strain: one per 10).</summary>
        private static void Bar(Rect rect, float fill, Color color, float max = 0f)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), Texture2D.whiteTexture);
            GUI.color = old;
            Segments(rect, max);
        }

        /// <summary>How much health or strain each segment of a bar stands for.</summary>
        public const float SegmentSize = 10f;

        /// <summary>
        /// Thin dark lines across a bar of 'max' every SegmentSize (the last segment may be shorter). Too many to tell apart
        /// (closer than MinSegmentWidth pixels) and none are drawn.
        /// </summary>
        public static void Segments(Rect rect, float max)
        {
            if (max <= SegmentSize + 1e-3f) return;
            float step = rect.width * SegmentSize / max;
            if (step < MinSegmentWidth) return;
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            for (float v = SegmentSize; v < max - 1e-3f; v += SegmentSize)
                GUI.DrawTexture(new Rect(rect.x + rect.width * v / max - 0.5f, rect.y, 1f, rect.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private const float MinSegmentWidth = 2.5f;
    }
}
