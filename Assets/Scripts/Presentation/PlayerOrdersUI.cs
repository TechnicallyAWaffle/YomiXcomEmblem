using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Bare-bones IMGUI order panel. While the world is held, it walks the pending decisions one unit at a time:
    ///   Action decision: pick an action (then click a target / point), Move (click the ground), Wait or Auto.
    ///                    Hold Shift while giving an order to queue it and keep planning; later orders are planned
    ///                    from where the earlier ones leave the unit. Confirm (or an order without Shift) sends the plan.
    ///                    Player units still walking can also be stopped here, for a strain cost.
    ///   Dodge dash:      click any point inside the dash ring (or a compass button for a full-range dash).
    /// Keys: 1-0 abilities, M move, Space wait, Enter confirm, Backspace undo, F re-center, Esc / right-click back
    /// (a right-drag rotates the camera instead). The camera glides to each unit as it comes up for orders. Ground markers show the unit, its plan, ranges and targets.
    /// Replace with real UI later.
    /// </summary>
    public class PlayerOrdersUI : PlayerInputProvider
    {
        public BattleContext Context;
        public Camera Camera;
        [Tooltip("Skip the panel and let the placeholder AI give the player team's orders too.")]
        public bool AutoPlay;
        public float WaitSeconds = 0.5f;
        public float RaycastDistance = 500f;
        [Tooltip("The tactical camera rig; found automatically if left empty.")]
        public TacticsCamera CameraRig;
        [Tooltip("Center the camera on each unit (and reset its rotation) when it is ready for orders.")]
        public bool FocusOnReadyUnit = true;

        [Header("Hold Space to wait")]
        public PlaybackDirector Director;
        [Tooltip("Battle seconds each step of a held wait lasts; the unit is re-ordered to wait again while Space stays down.")]
        public float HoldWaitStep = 0.1f;
        [Tooltip("Playback speed while Space is held.")]
        [Range(0.05f, 1f)] public float HoldPlaybackSpeed = 0.35f;

        [Header("Markers")]
        public Color SelectionColor = new Color(1f, 0.9f, 0.3f, 0.9f);
        public Color RangeColor = new Color(1f, 1f, 1f, 0.35f);
        public Color PlanColor = new Color(0.4f, 0.85f, 1f, 0.9f);
        public Color ValidTargetColor = new Color(0.35f, 1f, 0.45f, 0.9f);
        public Color InvalidTargetColor = new Color(1f, 0.35f, 0.3f, 0.6f);
        public Color ComboColor = new Color(1f, 0.75f, 0.15f, 0.95f);
        public Color HoldColor = new Color(1f, 0.85f, 0.4f, 0.75f);   // out of range: ordering it chases (melee) or queues (ranged)
        public Color FollowupColor = new Color(1f, 0.8f, 0.25f, 1f);   // the follow-up its own key re-casts

        private enum Mode { ChooseAction, PickTarget, PickMove, PickDash }

        /// <summary>One order in the plan being built, plus where it leaves the unit (for planning the next one).</summary>
        private class PlannedOrder
        {
            public UnitCommand Command;
            public string Label;
            public Vector3 EndPosition;
            public List<Vector3> Path;   // moves only
        }

        private static readonly string[] DashNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        // Compass layout in a 3x3 grid (row-major); -1 is the empty centre.
        private static readonly int[] DashGrid = { 7, 0, 1, 6, -1, 2, 5, 4, 3 };

        private readonly List<DecisionRequest> pending = new List<DecisionRequest>();
        private readonly List<PlannedOrder> plan = new List<PlannedOrder>();
        private readonly BasicEnemyAI ai = new BasicEnemyAI();
        private readonly List<Vector3> pathScratch = new List<Vector3>();
        private BattleSimulation sim;
        private System.Action onComplete;
        private Mode mode;
        private ActionDefinition selected;
        private string message;
        private Rect panelRect;
        private WorldMarkers markers;
        private Unit hovered;
        private bool hoverOnGround;
        private Vector3 hoverPoint;

        private bool Active { get { return onComplete != null && pending.Count > 0; } }
        private Camera Cam { get { return Camera != null ? Camera : Camera.main; } }
        private Unit Current { get { return Active ? sim.GetUnit(pending[0].UnitId) : null; } }

        private void Awake() { markers = new WorldMarkers(transform); }

        public override void RequestDecisions(BattleSimulation sim, IReadOnlyList<DecisionRequest> requests, System.Action onComplete)
        {
            this.sim = sim;
            this.onComplete = onComplete;
            pending.Clear();
            // Dashes are reactions to an attack that just happened: answer those first.
            pending.AddRange(requests.OrderBy(r => r.Kind == DecisionKind.DodgeDash ? 0 : 1));

            if (AutoPlay)
            {
                foreach (var r in pending) AutoDecide(r);
                pending.Clear();
                Finish();
                return;
            }
            BeginCurrent();
        }

        // ------------------------------------------------------------------ flow

        private void BeginCurrent()
        {
            plan.Clear();
            while (pending.Count > 0)
            {
                var u = sim.GetUnit(pending[0].UnitId);
                if (u != null && u.IsAlive) break;
                pending.RemoveAt(0);
            }
            if (pending.Count == 0) { Finish(); return; }

            mode = pending[0].Kind == DecisionKind.DodgeDash ? Mode.PickDash : Mode.ChooseAction;
            selected = null;
            message = pending[0].Reason != null ? "Last order failed: " + pending[0].Reason : null;
            if (FocusOnReadyUnit) FocusCurrent();
        }

        private TacticsCamera Rig { get { if (CameraRig == null) CameraRig = FindFirstObjectByType<TacticsCamera>(); return CameraRig; } }

        private void FocusCurrent()
        {
            var u = Current;
            if (u != null && Rig != null) Rig.FocusOn(u.Position);
        }

        private void Next()
        {
            if (pending.Count > 0) pending.RemoveAt(0);
            BeginCurrent();
        }

        private void Finish()
        {
            var cb = onComplete;
            onComplete = null;
            if (cb != null) cb();
        }

        /// <summary>Number row 1-0 picks the matching ability button; M move, Space wait, Backspace undo, F re-center.</summary>
        private bool HandleHotkey(Unit unit, KeyCode key)
        {
            if (key == KeyCode.F) { FocusCurrent(); return true; }
            if (mode == Mode.PickDash) return false;

            int slot = AbilitySlot(key);
            if (slot >= 0)
            {
                if (slot >= unit.Actions.Count) return true;
                var def = unit.Actions[slot];
                bool usable; string why;
                ActionLabel(unit, def, out usable, out why);
                if (!usable) { message = def.DisplayName + ": can't use now (" + why + ")"; return true; }
                // A highlighted follow-up's own key repeats it on the same target; otherwise pick a target as usual.
                if (def == Followup(unit) && (mode == Mode.ChooseAction || mode == Mode.PickTarget)) { CastFollowup(unit, def); return true; }
                mode = Mode.ChooseAction;   // switching abilities mid-targeting is fine
                ChooseAction(unit, def);
                return true;
            }
            switch (key)
            {
                case KeyCode.M:
                    if (plan.Count == 0 && unit.IsRooted) { message = "Can't move: immobilized"; return true; }
                    mode = Mode.PickMove; selected = null; message = null;
                    return true;
                case KeyCode.Backspace:
                    if (plan.Count == 0) return false;
                    plan.RemoveAt(plan.Count - 1);
                    return true;
            }
            return false;
        }

        /// <summary>
        /// While aiming an ability (targeting mode, nothing queued before it): who would use it, the target under the cursor
        /// (if any) and the sim time it would land if ordered now. The battle timeline draws it as a ghost entry.
        /// </summary>
        public bool TryGetTargetingPreview(out Unit unit, out ActionDefinition def, out Unit target, out float resolveAt)
        {
            unit = Current; def = selected; target = hovered; resolveAt = 0f;
            if (unit == null || def == null || mode != Mode.PickTarget || plan.Count > 0) return false;
            resolveAt = sim.Now + def.Windup / Mathf.Max(0.01f, unit.EffectiveActionSpeed());
            return true;
        }

        /// <summary>
        /// The follow-up Space would re-cast: the unit's last ability, if it is flagged IsFollowup and could be ordered again
        /// right now (affordable, and its last target still valid). Null otherwise. Its button is highlighted meanwhile.
        /// </summary>
        private ActionDefinition Followup(Unit unit)
        {
            if (plan.Count > 0 || string.IsNullOrEmpty(unit.LastActionId)) return null;
            var def = unit.Actions.FirstOrDefault(a => a.Id == unit.LastActionId && a.IsFollowup);
            if (def == null) return null;
            bool usable; string why;
            ActionLabel(unit, def, out usable, out why);
            if (!usable) return null;
            if (def.Targeting == TargetType.Enemy || def.Targeting == TargetType.Ally)
            {
                var t = sim.GetUnit(unit.LastActionTargetId);
                if (t == null || !t.IsAlive || !sim.IsTargetableBy(t, unit)) return null;
            }
            return def;
        }

        private void CastFollowup(Unit unit, ActionDefinition def)
        {
            mode = Mode.ChooseAction;
            selected = null;
            if (def.Targeting == TargetType.Enemy || def.Targeting == TargetType.Ally) TryAddAction(unit, def, sim.GetUnit(unit.LastActionTargetId));
            else if (def.Targeting == TargetType.Position) AddOrder(UnitCommand.UseActionAt(unit.Id, def, unit.LastActionPos), def.DisplayName + " (point)", PlannedPosition(unit));
            else ChooseAction(unit, def);
        }

        /// <summary>0-based ability slot for the number row (and keypad): 1..9 then 0 for the tenth. -1 if not a number key.</summary>
        private static int AbilitySlot(KeyCode key)
        {
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9) return key - KeyCode.Alpha1;
            if (key == KeyCode.Alpha0) return 9;
            if (key >= KeyCode.Keypad1 && key <= KeyCode.Keypad9) return key - KeyCode.Keypad1;
            if (key == KeyCode.Keypad0) return 9;
            return -1;
        }

        private static string SlotKey(int slot) { return slot < 9 ? (slot + 1).ToString() : slot == 9 ? "0" : null; }

        private static string FollowupKeyName(Unit unit, ActionDefinition def)
        {
            int slot = unit.Actions.IndexOf(def);
            string key = slot >= 0 ? SlotKey(slot) : null;
            return key ?? def.DisplayName;
        }

        private void Back()
        {
            if (mode == Mode.PickTarget || mode == Mode.PickMove) { mode = Mode.ChooseAction; selected = null; message = null; }
        }

        /// <summary>Adds an order to the plan. Without Shift held the plan is sent right away.</summary>
        private void AddOrder(UnitCommand cmd, string label, Vector3 endPosition, List<Vector3> path = null)
        {
            plan.Add(new PlannedOrder { Command = cmd, Label = label, EndPosition = endPosition, Path = path });
            mode = Mode.ChooseAction;
            selected = null;
            message = null;
            bool queueMore = Event.current != null ? Event.current.shift : Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!queueMore) ConfirmPlan();
        }

        private void ConfirmPlan()
        {
            foreach (var p in plan) sim.SubmitCommand(p.Command);
            Next();
        }

        private void SubmitDash(Unit unit, int direction8)
        {
            sim.SubmitDodgeDash(unit.Id, direction8);
            Next();
        }

        private void AutoDecide(DecisionRequest r)
        {
            var unit = sim.GetUnit(r.UnitId);
            if (unit == null || !unit.IsAlive) return;
            if (r.Kind == DecisionKind.DodgeDash)
            {
                var attacker = sim.GetUnit(r.AttackerId);
                sim.SubmitDodgeDash(unit.Id, attacker != null ? sim.BestDashDirection(unit, attacker) : 0);
            }
            else sim.SubmitCommand(ai.Decide(sim, unit));
        }

        // ------------------------------------------------------------------ planning from the end of the plan

        private Vector3 PlannedPosition(Unit unit) { return plan.Count > 0 ? plan[plan.Count - 1].EndPosition : unit.Position; }

        /// <summary>The unit as it will stand once the current plan has played out (position only), for range/LOS checks.</summary>
        private Unit PlannedUnit(Unit unit)
        {
            if (plan.Count == 0) return unit;
            var u = unit.Snapshot();   // same Id, so visibility still ignores the real unit's own body
            u.Position = PlannedPosition(unit);
            u.Statuses = unit.Statuses;
            u.CooldownReadyAt = unit.CooldownReadyAt;
            return u;
        }

        private bool CanTarget(Unit planned, ActionDefinition def, Unit target, out string reason)
        {
            reason = null;
            if (target == null || !target.IsAlive) { reason = "no target"; return false; }
            if (def.Targeting == TargetType.Enemy && target.Team == planned.Team) { reason = "not an enemy"; return false; }
            if (def.Targeting == TargetType.Ally && target.Team != planned.Team) { reason = "not an ally"; return false; }
            // Cost/cooldown of a later queued order are only known when it starts; the sim re-checks then.
            return sim.CanUse(planned, def, target, out reason, plan.Count == 0);
        }

        private void ChooseAction(Unit unit, ActionDefinition def)
        {
            message = null;
            var planned = PlannedUnit(unit);
            switch (def.Targeting)
            {
                case TargetType.Self:
                    TryAddAction(unit, def, unit);
                    break;
                case TargetType.None:
                    AddOrder(UnitCommand.UseAction(unit.Id, def), def.DisplayName, planned.Position);
                    break;
                default:
                    selected = def;
                    mode = Mode.PickTarget;
                    break;
            }
        }

        private void TryAddAction(Unit unit, ActionDefinition def, Unit target)
        {
            var planned = PlannedUnit(unit);
            string reason;
            string label = target.Id == unit.Id ? def.DisplayName : def.DisplayName + " > " + target.DisplayName;
            if (!CanTarget(planned, def, target, out reason))
            {
                // Out of reach is fine. Melee: the unit chases the target and strikes once in range. Ranged: it is queued and fires
                // when the target is in range, while the unit stays free to move or wait.
                if (reason != "out of range") { message = (plan.Count > 0 ? "Can't from there: " : "Can't: ") + reason; return; }
                bool chase = !def.IsRanged;
                AddOrder(UnitCommand.UseActionWhenInRange(unit.Id, def, target.Id), label + (chase ? " (chase)" : " (queued)"), chase ? target.Position : planned.Position);
                return;
            }
            AddOrder(UnitCommand.UseActionWhenInRange(unit.Id, def, target.Id), label, planned.Position);
        }

        // ------------------------------------------------------------------ world picking

        private Unit UnitUnder(RaycastHit hit)
        {
            var view = hit.collider != null ? hit.collider.GetComponentInParent<UnitView>() : null;
            return view != null ? sim.GetUnit(view.UnitId) : null;
        }

        /// <summary>While aiming an enemy-only (or ally-only) ability, units on the wrong side can't be picked.</summary>
        private bool Pickable(Unit unit, Unit other)
        {
            if (mode != Mode.PickTarget || selected == null) return true;
            if (selected.Targeting == TargetType.Enemy) return other.Team != unit.Team;
            if (selected.Targeting == TargetType.Ally) return other.Team == unit.Team;
            return true;
        }

        /// <summary>
        /// The world point under the cursor and the unit there, if any. Units that can't be picked right now (yourself and
        /// allies for an enemy-only ability) are see-through, so the ray goes on to the enemy or ground behind them.
        /// </summary>
        private bool PickWorld(Unit unit, Vector2 screenPos, out RaycastHit hit, out Unit picked)
        {
            hit = default(RaycastHit);
            picked = null;
            var cam = Cam;
            if (cam == null) return false;
            Physics.SyncTransforms();   // views were moved by script; colliders must follow before we pick (physics may be frozen)
            var hits = Physics.RaycastAll(cam.ScreenPointToRay(screenPos), RaycastDistance);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var u = UnitUnder(h);
                if (u != null && (!u.IsAlive || !Pickable(unit, u))) continue;
                hit = h;
                picked = u;
                return true;
            }
            return false;
        }

        private static Vector2 GuiToScreen(Vector2 gui) { return UiScale.GuiToScreen(gui); }

        private void HandleWorldClick(Unit unit, Vector2 screenPos)
        {
            RaycastHit hit;
            Unit clickedUnit;
            if (!PickWorld(unit, screenPos, out hit, out clickedUnit)) return;
            Vector3 from = PlannedPosition(unit);

            switch (mode)
            {
                case Mode.PickTarget:
                    if (selected.Targeting == TargetType.Position)
                    {
                        if (selected.Shape != AreaShape.Line && Vector3.Distance(from, hit.point) > selected.Range) { message = "Out of range."; return; }
                        bool dash = selected.BehaviorId == "dash";
                        AddOrder(UnitCommand.UseActionAt(unit.Id, selected, hit.point), selected.DisplayName + (dash ? "" : " (point)"),
                            dash ? sim.DashDestination(PlannedUnit(unit), hit.point) : from);
                    }
                    else if (clickedUnit != null) TryAddAction(unit, selected, clickedUnit);
                    break;

                case Mode.PickMove:
                {
                    if (clickedUnit != null) { message = "Click open ground."; return; }
                    var path = new List<Vector3>();
                    if (!sim.PlanPath(unit, from, sim.FreeSpotNear(unit, hit.point, from, true), path)) { message = "Can't get there."; return; }
                    AddOrder(UnitCommand.MoveTo(unit.Id, hit.point), "Move", path[path.Count - 1], path);
                    break;
                }

                case Mode.PickDash:
                    sim.SubmitDodgeDashTo(unit.Id, clickedUnit != null && clickedUnit.Id == unit.Id ? unit.Position : hit.point);
                    Next();
                    break;
            }
        }

        /// <summary>
        /// Combo timing: seconds the target stays stunned after this ability would land (negative = the stun wears off
        /// that long before). Null when the target isn't stunned, the ability isn't offensive, or the timing isn't known
        /// (an order queued behind others starts at an unknown time).
        /// </summary>
        private float? ComboMargin(Unit planned, ActionDefinition def, Unit target)
        {
            if (plan.Count > 0 || target == null || target.Phase != UnitPhase.Stunned || !IsOffensive(def)) return null;
            float landsAt = sim.Now + def.Windup / Mathf.Max(0.01f, planned.EffectiveActionSpeed());
            return target.PhaseEndsAt - landsAt;
        }

        private static bool IsOffensive(ActionDefinition def)
        {
            return def.Damage > 0f && (def.Targeting == TargetType.Enemy || def.BehaviorId == "area_attack");
        }

        /// <summary>What an attack on this unit would look like right now. Mirrors the sim's hit roll; it's a preview, not the roll.</summary>
        private string TargetPreview(Unit planned, ActionDefinition def, Unit target)
        {
            string reason;
            if (!CanTarget(planned, def, target, out reason))
                return reason == "out of range"
                    ? (def.IsRanged
                        ? string.Format("{0}: out of range\nClick to queue it: fires once in range while you keep moving (up to {1:0}s)", target.DisplayName, sim.MaxRangeWait)
                        : string.Format("{0}: out of reach\nClick to chase: walks at them and strikes once in range", target.DisplayName))
                    : target.DisplayName + ": " + reason;
            if (def.Targeting != TargetType.Enemy) return target.DisplayName;

            float exposure = (def.IsRanged && !def.PiercesTerrain) ? sim.Visibility.GetExposure(planned, target, false) : 1f;
            float bonus = 0f;
            foreach (var s in target.Statuses) bonus += s.IncomingAccuracyBonus;
            float hitChance = Mathf.Clamp01(def.BaseAccuracy * exposure + bonus);
            // Dodge at the moment it lands: none if the target is still stunned then (a combo).
            float? combo = ComboMargin(planned, def, target);
            float dodge = combo.HasValue && combo.Value >= 0f ? 0f : target.UnstunnedDodge();
            string text = string.Format("{0}: {1:0}% to land\nhit {2:0}%  (exposure {3:0}%)  dodge {4:0}%",
                target.DisplayName, hitChance * (1f - dodge) * 100f, hitChance * 100f, exposure * 100f, dodge * 100f);
            if (combo.HasValue)
                text += combo.Value >= 0f
                    ? string.Format("\nCOMBO: still stunned when it lands ({0:0.0}s to spare), can't dodge", combo.Value)
                    : string.Format("\nNo combo: the stun wears off {0:0.0}s before it lands", -combo.Value);
            return text;
        }

        // ------------------------------------------------------------------ hover + ground markers

        // Hold-to-wait: units that were up for orders when Space went down keep waiting in short steps while it is held,
        // with playback slowed. Any other unit that comes up meanwhile is asked for orders as usual.
        private readonly HashSet<string> holdUnits = new HashSet<string>();
        private bool holding;
        private float holdStartClock, holdRestoreSpeed;

        private PlaybackDirector Dir { get { if (Director == null) Director = FindFirstObjectByType<PlaybackDirector>(); return Director; } }

        private void UpdateHoldWait()
        {
            if (!Input.GetKey(KeyCode.Space)) { if (holding) EndHoldWait(); return; }
            bool canWait = Active && mode == Mode.ChooseAction && plan.Count == 0 && Current != null;
            if (Input.GetKeyDown(KeyCode.Space) && canWait)
            {
                holdUnits.Add(Current.Id);
                if (!holding) StartHoldWait();
            }
            if (holding && canWait && holdUnits.Contains(Current.Id))
            {
                sim.SubmitCommand(UnitCommand.Wait(Current.Id, HoldWaitStep));
                Next();
            }
        }

        private void StartHoldWait()
        {
            holding = true;
            if (Dir == null) return;
            holdStartClock = Dir.PlaybackClock;
            holdRestoreSpeed = Dir.playbackSpeed;
            Dir.SetPlaybackSpeed(HoldPlaybackSpeed);
        }

        private void EndHoldWait()
        {
            holding = false;
            holdUnits.Clear();
            if (Dir != null) Dir.SetPlaybackSpeed(holdRestoreSpeed);
        }

        /// <summary>While Space is held: how long the battle has run since, top-centre.</summary>
        private void DrawHoldTimer()
        {
            if (!holding || Dir == null) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = new Color(1f, 0.9f, 0.5f);
            string text = string.Format("Waiting (Space)   +{0:0.0}s", Dir.PlaybackClock - holdStartClock);
            GUI.Box(new Rect(UiScale.Width / 2f - 150f, 110f, 300f, 34f), text, style);
        }

        private void Update()
        {
            UpdateHoldWait();
            markers.Begin();
            var unit = Current;
            if (unit != null) UpdateHover();
            if (unit != null) DrawMarkers(unit);
            markers.End();
        }

        private void UpdateHover()
        {
            hovered = null;
            hoverOnGround = false;
            RaycastHit hit;
            if (!PickWorld(Current, Input.mousePosition, out hit, out hovered)) return;
            hoverOnGround = hovered == null;
            hoverPoint = hit.point;
        }

        private void DrawMarkers(Unit unit)
        {
            Vector3 from = PlannedPosition(unit);
            markers.Circle(unit.Position, unit.BodyRadius + 0.35f, SelectionColor, 0.07f);

            // The plan so far: routes for queued moves and a ring where the unit will end up.
            foreach (var p in plan) if (p.Path != null) markers.Polyline(p.Path, PlanColor);
            if (plan.Count > 0) markers.Circle(from, unit.BodyRadius + 0.2f, PlanColor, 0.05f);

            switch (mode)
            {
                case Mode.ChooseAction:
                    foreach (var def in unit.Actions)
                    {
                        if (def.Targeting == TargetType.Self && def.Shape == AreaShape.Circle)
                            markers.Circle(from, def.AreaRadius, InvalidTargetColor, 0.03f, 64);   // self-centred area (Shockwave)
                        else if (def.Targeting != TargetType.Self && def.Targeting != TargetType.None)
                            markers.Circle(from, def.Range, RangeColor, 0.03f, 96);
                    }
                    break;

                case Mode.PickTarget:
                {
                    if (selected.BehaviorId == "dash")
                    {
                        // Dash: its reach, and where it would land from where the unit will be.
                        var dasher = PlannedUnit(unit);
                        markers.Circle(from, dasher.DashDistanceNow(), RangeColor, 0.05f, 64);
                        if (hoverOnGround)
                        {
                            Vector3 to = sim.DashDestination(dasher, hoverPoint);
                            markers.Segment(from, to, SelectionColor, 0.08f);
                            markers.Circle(to, unit.BodyRadius, SelectionColor, 0.05f);
                        }
                        break;
                    }
                    if (selected.Shape != AreaShape.Line) markers.Circle(from, selected.Range, RangeColor, 0.06f, 96);
                    if (selected.Targeting == TargetType.Position)
                    {
                        if (hoverOnGround) DrawAreaPreview(unit, from);
                        break;
                    }
                    var planned = PlannedUnit(unit);
                    foreach (var other in sim.State.Units)
                    {
                        if (!other.IsAlive) continue;
                        if (selected.Targeting == TargetType.Enemy && other.Team == unit.Team) continue;
                        if (selected.Targeting == TargetType.Ally && other.Team != unit.Team) continue;
                        string reason;
                        bool ok = CanTarget(planned, selected, other, out reason);
                        bool hold = !ok && reason == "out of range";
                        bool isHovered = hovered != null && hovered.Id == other.Id;
                        markers.Circle(other.Position, other.BodyRadius + 0.3f, ok ? ValidTargetColor : hold ? HoldColor : InvalidTargetColor, isHovered ? 0.12f : 0.05f);
                        if (isHovered && hold) markers.Segment(from, other.Position, HoldColor, 0.03f);
                        float? combo = ok ? ComboMargin(planned, selected, other) : null;
                        if (combo.HasValue && combo.Value >= 0f) markers.Circle(other.Position, other.BodyRadius + 0.55f, ComboColor, 0.08f);
                        if (isHovered && ok) markers.Segment(from, other.Position, ValidTargetColor, 0.04f);
                    }
                    break;
                }

                case Mode.PickMove:
                    if (hoverOnGround && sim.PlanPath(unit, from, sim.FreeSpotNear(unit, hoverPoint, from, true), pathScratch))
                    {
                        markers.Polyline(pathScratch, SelectionColor);
                        markers.Circle(pathScratch[pathScratch.Count - 1], 0.35f, SelectionColor, 0.05f);
                    }
                    break;

                case Mode.PickDash:
                {
                    float dist = unit.DashDistanceNow();
                    markers.Circle(unit.Position, dist, RangeColor, 0.03f, 64);
                    if (hoverOnGround)
                    {
                        Vector3 to = sim.DashDestination(unit, hoverPoint);
                        markers.Segment(unit.Position, to, SelectionColor, 0.08f);
                        markers.Circle(to, unit.BodyRadius, SelectionColor, 0.05f);
                    }
                    var attacker = sim.GetUnit(pending[0].AttackerId);
                    if (attacker != null) markers.Circle(attacker.Position, attacker.BodyRadius + 0.3f, InvalidTargetColor, 0.08f);
                    break;
                }
            }
        }

        /// <summary>Point-targeted preview: the line or circle the action would cover, with the enemies it would catch ringed.</summary>
        private void DrawAreaPreview(Unit unit, Vector3 from)
        {
            if (selected.Shape == AreaShape.Line)
            {
                Vector3 dir = hoverPoint - from; dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f) return;
                dir.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, dir) * (selected.LineWidth / 2f);
                Vector3 end = from + dir * selected.Range;
                markers.Polyline(new[] { from + side, end + side, end - side, from - side, from + side }, SelectionColor, 0.05f);
            }
            else
            {
                bool inRange = Vector3.Distance(from, hoverPoint) <= selected.Range;
                float radius = selected.AreaRadius > 0f ? selected.AreaRadius : 0.5f;
                markers.Circle(hoverPoint, radius, inRange ? SelectionColor : InvalidTargetColor, 0.06f, 64);
                if (!inRange || selected.Shape != AreaShape.Circle) return;
            }
            var planned = PlannedUnit(unit);
            foreach (var hit in sim.UnitsInArea(planned, selected, hoverPoint))
            {
                markers.Circle(hit.Position, hit.BodyRadius + 0.3f, ValidTargetColor, 0.08f);
                float? combo = ComboMargin(planned, selected, hit);
                if (combo.HasValue && combo.Value >= 0f) markers.Circle(hit.Position, hit.BodyRadius + 0.55f, ComboColor, 0.08f);
            }
        }

        // ------------------------------------------------------------------ panel

        private void OnGUI()
        {
            UiScale.Begin();
            DrawHoldTimer();
            if (!Active) return;
            var unit = Current;
            if (unit == null) return;
            var e = Event.current;

            DrawMarker(unit);
            if (mode == Mode.PickTarget && selected != null) DrawComboLabels(unit);

            if (e.type == UnityEngine.EventType.KeyDown && e.keyCode == KeyCode.Escape) { Back(); e.Use(); return; }
            if (e.type == UnityEngine.EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) &&
                mode == Mode.ChooseAction && plan.Count > 0) { e.Use(); ConfirmPlan(); return; }
            if (e.type == UnityEngine.EventType.KeyDown && e.keyCode != KeyCode.None && HandleHotkey(unit, e.keyCode)) { e.Use(); return; }

            var stoppable = new List<Unit>();
            if (mode == Mode.ChooseAction)
            {
                foreach (var other in sim.State.Units)
                {
                    float cost; string reason;
                    if (other.Team == Team.Player && sim.CanCancelMove(other.Id, out cost, out reason)) stoppable.Add(other);
                }
            }

            float width = PanelWidth;
            float height = 82f;
            if (mode == Mode.PickDash) height += 90f;
            else if (mode == Mode.ChooseAction) height += ButtonRows(unit) * (ButtonHeight + ButtonGap);
            else height += 34f;
            if (plan.Count > 0) height += 52f;
            if (stoppable.Count > 0) height += 28f;
            panelRect = new Rect((UiScale.Width - width) / 2f, UiScale.Height - height - 10f, width, height);
            GUI.Box(panelRect, GUIContent.none);

            var title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            float x = panelRect.x + 10f, y = panelRect.y + 6f;
            GUI.Label(new Rect(x, y, width - 20f, 20f), Header(unit), title);
            y += 20f;
            var hint = new GUIStyle(GUI.skin.label);
            if (!string.IsNullOrEmpty(message)) hint.normal.textColor = new Color(1f, 0.75f, 0.4f);
            GUI.Label(new Rect(x, y, width - 20f, 20f), !string.IsNullOrEmpty(message) ? message : Hint(), hint);
            y += 24f;

            ClearTip();
            bool changed;
            switch (mode)
            {
                case Mode.ChooseAction: changed = DrawChooseAction(unit, x, ref y, stoppable); break;
                case Mode.PickDash: changed = DrawDash(unit, x, y); break;
                default: changed = DrawPicking(unit, x, ref y); break;
            }
            if (changed || !Active) return;

            // While picking a target, keep the chosen ability's description up so its rules are in view.
            if (mode == Mode.PickTarget && selected != null)
            {
                tipTitle = selected.DisplayName; tipBody = selected.Description; tipStats = ActionStats(selected); tipWarning = null;
                tipAnchor = new Rect(panelRect.x, panelRect.y, 0f, 0f);
            }
            DrawTip();   // last, so it draws over the panel

            if (mode == Mode.PickTarget) DrawHoverPreview(unit, e.mousePosition);

            if (e.type == UnityEngine.EventType.MouseDown && !panelRect.Contains(e.mousePosition))
            {
                if (e.button == 0 && mode != Mode.ChooseAction) HandleWorldClick(unit, GuiToScreen(e.mousePosition));
                if (e.button == 0) e.Use();   // middle and right mouse belong to the camera (pan, rotate)
            }
            // Right-click backs out, but only a click: a right-drag rotates the camera.
            if (e.type == UnityEngine.EventType.MouseUp && e.button == 1 && !panelRect.Contains(e.mousePosition) &&
                (Rig == null || !Rig.RightDragged)) { Back(); e.Use(); }
        }

        private string Header(Unit unit)
        {
            switch (mode)
            {
                case Mode.PickTarget:
                    return selected.Targeting == TargetType.Position
                        ? string.Format("{0}: click a point for {1} (range {2:0}m)", unit.DisplayName, selected.DisplayName, selected.Range)
                        : string.Format("{0}: click a target for {1} (range {2:0}m)", unit.DisplayName, selected.DisplayName, selected.Range);
                case Mode.PickMove: return unit.DisplayName + ": click the ground to move there";
                case Mode.PickDash:
                {
                    var attacker = sim.GetUnit(pending[0].AttackerId);
                    return string.Format("{0} dodged {1}! Pick where to dash", unit.DisplayName, attacker != null ? attacker.DisplayName : "an attack");
                }
                default:
                    return string.Format("Orders for {0}    HP {1:0}/{2:0}   Strain {3:0}/{4:0}{5}",
                        unit.DisplayName, unit.Health, unit.MaxHealth, unit.Strain, unit.MaxStrain,
                        unit.MaxEnergy > 0f ? string.Format("   Energy {0:0}/{1:0}", unit.Energy, unit.MaxEnergy) : "");
            }
        }

        private string Hint()
        {
            switch (mode)
            {
                case Mode.PickDash: return "Or click any point inside the ring to dash exactly there (click the unit to stand firm). Buttons dash full range; N is up the field (+Z).";
                case Mode.ChooseAction:
                {
                    if (plan.Count > 0) return "Keep adding orders (Shift), or Confirm / Enter to send the plan.";
                    var followup = Current != null ? Followup(Current) : null;
                    if (followup != null) return "Press " + FollowupKeyName(Current, followup) + " again: " + followup.DisplayName + " on the same target. Hold Space to wait.";
                    return "Keys 1-0 pick abilities (hover for details). Shift queues orders. Hold Space to wait while time runs slowly.";
                }
                default: return "Shift-click to queue and keep planning. Right-click or Esc to go back.";
            }
        }

        /// <summary>Returns true if the panel's state changed (order sent or mode switched): stop drawing this frame's stale panel.</summary>
        private const float PanelWidth = 700f;
        private const float ButtonWidth = 128f;
        private const float ButtonHeight = 28f;
        private const float ButtonGap = 6f;

        private static int ButtonsPerRow { get { return Mathf.Max(1, Mathf.FloorToInt((PanelWidth - 20f + ButtonGap) / (ButtonWidth + ButtonGap))); } }
        private static int ButtonRows(Unit unit) { return Mathf.CeilToInt((unit.Actions.Count + 3) / (float)ButtonsPerRow); }   // + Move, Wait, Auto

        private Rect ButtonRect(int index, float x, float y)
        {
            int row = index / ButtonsPerRow, col = index % ButtonsPerRow;
            return new Rect(x + col * (ButtonWidth + ButtonGap), y + row * (ButtonHeight + ButtonGap), ButtonWidth, ButtonHeight);
        }

        private string ActionLabel(Unit unit, ActionDefinition def, out bool usable, out string why)
        {
            usable = true; why = null;
            string label = def.DisplayName;
            float cost = sim.EnergyCostOf(unit, def);
            if (cost > 0f) label += string.Format(" ({0:0}E)", cost);
            float strain = sim.StrainCostOf(unit, def, unit.LastActionTargetId);   // follow-up discounts are against the last target
            if (Mathf.Abs(strain) > 0.5f) label += string.Format(" ({0:+0;-0}S)", strain);
            if (plan.Count > 0) return label;   // a later order's resources are only known when it starts
            usable = sim.CanAfford(unit, def, out why);
            float readyAt;
            if (!usable && why == "cooldown" && unit.CooldownReadyAt.TryGetValue(def.Id, out readyAt))
                label = string.Format("{0} ({1:0.0}s)", def.DisplayName, readyAt - sim.Now);
            return label;
        }

        /// <summary>One line of numbers for the tooltip: targeting, timing, damage, cost, cooldown and special rules.</summary>
        private static string ActionStats(ActionDefinition def)
        {
            var parts = new List<string>();
            switch (def.Targeting)
            {
                case TargetType.Self: parts.Add(def.Shape == AreaShape.Circle ? string.Format("around you, {0:0.#}m", def.AreaRadius) : "self"); break;
                case TargetType.Ally: parts.Add(string.Format("ally, range {0:0}m", def.Range)); break;
                case TargetType.Position:
                    parts.Add(def.Shape == AreaShape.Line ? string.Format("line {0:0}m x {1:0.#}m", def.Range, def.LineWidth)
                        : def.Shape == AreaShape.Circle ? string.Format("area {0:0.#}m, range {1:0}m", def.AreaRadius, def.Range)
                        : string.Format("point, range {0:0}m", def.Range));
                    break;
                case TargetType.Enemy: parts.Add(string.Format("{0}, range {1:0}m", def.IsRanged ? "ranged" : "melee", def.Range)); break;
            }
            parts.Add(string.Format("windup {0:0.##}s / recovery {1:0.##}s", def.Windup, def.Recovery));
            bool attacks = def.Targeting == TargetType.Enemy || def.BehaviorId == "area_attack";
            if (attacks && def.Damage > 0f)
            {
                parts.Add(string.Format("{0:0} dmg ({1})", def.Damage, def.Impact));
                if (def.BaseAccuracy < 0.995f) parts.Add(string.Format("{0:0}% acc", def.BaseAccuracy * 100f));
                if (def.StunDuration > 0f) parts.Add(string.Format("stun {0:0.##}s", def.StunDuration));
            }
            if (def.Heal > 0f) parts.Add(string.Format("heals {0:0}", def.Heal));
            if (def.EnergyCost > 0f) parts.Add(string.Format("{0:0} energy", def.EnergyCost));
            if (def.StrainOnSelf > 0f) parts.Add(string.Format("costs {0:0} strain", def.StrainOnSelf));
            if (def.StrainOnSelf < 0f) parts.Add(string.Format("sheds {0:0} strain", -def.StrainOnSelf));
            if (attacks && def.StrainOnTarget > 0f) parts.Add(string.Format("+{0:0} strain to target", def.StrainOnTarget));
            if (def.IsFollowup) parts.Add("follow-up (Space)");
            if (def.EnergyGain > 0f) parts.Add(string.Format("+{0:0} energy", def.EnergyGain));
            if (def.Cooldown > 0f) parts.Add(string.Format("cooldown {0:0}s", def.Cooldown));
            if (def.InterruptedByDamage || def.InterruptedByStun)
                parts.Add(def.InterruptedByDamage ? "broken by any hit" : "broken by stun");
            return string.Join("  ·  ", parts.ToArray());
        }

        // Hover tooltip for the button under the mouse, rebuilt every OnGUI event.
        private string tipTitle, tipBody, tipStats, tipWarning;
        private Rect tipAnchor;

        private void SetTip(Rect button, string title, string body, string stats = null, string warning = null)
        {
            if (!button.Contains(Event.current.mousePosition)) return;
            tipTitle = title; tipBody = body; tipStats = stats; tipWarning = warning; tipAnchor = button;
        }

        private void SetActionTip(Rect button, ActionDefinition def, string warning)
        {
            SetTip(button, def.DisplayName, string.IsNullOrEmpty(def.Description) ? null : def.Description, ActionStats(def), warning);
        }

        private void ClearTip() { tipTitle = null; }

        /// <summary>Floating box just above the hovered button (or above the panel), kept on screen.</summary>
        private void DrawTip()
        {
            if (string.IsNullOrEmpty(tipTitle)) return;
            const float width = 360f, pad = 8f;
            var titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            var bodyStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            var statsStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12 };
            statsStyle.normal.textColor = new Color(0.75f, 0.85f, 1f);
            var warnStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            warnStyle.normal.textColor = new Color(1f, 0.75f, 0.4f);

            float inner = width - pad * 2f;
            float hTitle = titleStyle.CalcHeight(new GUIContent(tipTitle), inner);
            float hBody = string.IsNullOrEmpty(tipBody) ? 0f : bodyStyle.CalcHeight(new GUIContent(tipBody), inner);
            float hStats = string.IsNullOrEmpty(tipStats) ? 0f : statsStyle.CalcHeight(new GUIContent(tipStats), inner);
            float hWarn = string.IsNullOrEmpty(tipWarning) ? 0f : warnStyle.CalcHeight(new GUIContent(tipWarning), inner);
            float height = pad * 2f + hTitle + hBody + hStats + hWarn + 4f;

            float x = Mathf.Clamp(tipAnchor.x, 4f, UiScale.Width - width - 4f);
            float y = Mathf.Max(4f, Mathf.Min(tipAnchor.y, panelRect.y) - height - 6f);
            var box = new Rect(x, y, width, height);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.97f);
            GUI.Box(box, GUIContent.none);
            GUI.Box(box, GUIContent.none);   // doubled for a more opaque background with the default skin
            GUI.color = old;

            float cy = y + pad;
            GUI.Label(new Rect(x + pad, cy, inner, hTitle), tipTitle, titleStyle); cy += hTitle;
            if (hBody > 0f) { GUI.Label(new Rect(x + pad, cy, inner, hBody), tipBody, bodyStyle); cy += hBody + 2f; }
            if (hStats > 0f) { GUI.Label(new Rect(x + pad, cy, inner, hStats), tipStats, statsStyle); cy += hStats + 2f; }
            if (hWarn > 0f) GUI.Label(new Rect(x + pad, cy, inner, hWarn), tipWarning, warnStyle);
        }

        private bool DrawChooseAction(Unit unit, float x, ref float y, List<Unit> stoppable)
        {
            const float bw = ButtonWidth, gap = ButtonGap;
            int index = 0;
            var followup = Followup(unit);
            foreach (var def in unit.Actions)
            {
                bool usable; string why;
                string label = ActionLabel(unit, def, out usable, out why);
                string slotKey = SlotKey(index);
                if (slotKey != null) label = slotKey + "  " + label;
                var rect = ButtonRect(index++, x, y);
                SetActionTip(rect, def, usable ? null : "Can't use now: " + why);
                GUI.enabled = usable;
                bool isFollowup = def == followup;
                if (isFollowup)
                {
                    label += "  (again)";
                    // The follow-up Space would re-cast: a gold frame and tint so it stands out.
                    var old = GUI.color;
                    GUI.color = FollowupColor;
                    GUI.DrawTexture(new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f), Texture2D.whiteTexture);
                    GUI.color = old;
                    GUI.backgroundColor = FollowupColor;
                }
                bool clicked = GUI.Button(rect, label);
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
                if (clicked) { ChooseAction(unit, def); return true; }   // the button still lets you pick a new target; its key repeats the last
            }
            bool rooted = plan.Count == 0 && unit.IsRooted;
            var moveRect = ButtonRect(index++, x, y);
            SetTip(moveRect, "Move", "Walk to any point on the navmesh. Shift-click to queue more orders after it.",
                string.Format("speed {0:0.#} m/s", unit.EffectiveMoveSpeed()), rooted ? "Can't move: immobilized" : null);
            GUI.enabled = !rooted;
            if (GUI.Button(moveRect, rooted ? "M  Move (rooted)" : "M  Move"))
            {
                GUI.enabled = true; mode = Mode.PickMove; message = null; return true;
            }
            GUI.enabled = true;
            var waitRect = ButtonRect(index++, x, y);
            SetTip(waitRect, "Wait", string.Format("Do nothing for {0:0.0}s, then ask for orders again. Or hold Space to keep waiting, with time running slowly, until you let go.", WaitSeconds));
            if (GUI.Button(waitRect, string.Format("Wait {0:0.0}s", WaitSeconds)))
            {
                AddOrder(UnitCommand.Wait(unit.Id, WaitSeconds), string.Format("Wait {0:0.0}s", WaitSeconds), PlannedPosition(unit));
                return true;
            }
            var autoRect = ButtonRect(index++, x, y);
            SetTip(autoRect, "Auto", "Let the AI pick this unit's order: its first usable attack on the nearest enemy, or close the distance.",
                null, plan.Count > 0 ? "Clear the plan to use Auto." : null);
            GUI.enabled = plan.Count == 0;
            if (GUI.Button(autoRect, "Auto")) { GUI.enabled = true; AutoDecide(pending[0]); Next(); return true; }
            GUI.enabled = true;
            y += ButtonRows(unit) * (ButtonHeight + gap);

            if (plan.Count > 0)
            {
                string steps = string.Join("   ", plan.Select((p, i) => (i + 1) + ". " + p.Label).ToArray());
                GUI.Label(new Rect(x, y, panelRect.width - 20f, 20f), "Plan: " + steps);
                y += 20f;
                if (GUI.Button(new Rect(x, y, bw, 24f), "Confirm (Enter)")) { ConfirmPlan(); return true; }
                if (GUI.Button(new Rect(x + bw + gap, y, bw, 24f), "Undo (Bksp)")) { plan.RemoveAt(plan.Count - 1); return true; }
                if (GUI.Button(new Rect(x + 2f * (bw + gap), y, bw, 24f), "Clear")) { plan.Clear(); return true; }
                y += 24f + gap;
            }

            // Walks in progress can be cut short while it's a friendly unit's turn, at a strain cost.
            float sx = x;
            foreach (var other in stoppable)
            {
                float cost; string reason;
                if (!sim.CanCancelMove(other.Id, out cost, out reason)) continue;
                if (GUI.Button(new Rect(sx, y, 180f, 22f), string.Format("Stop {0} (+{1:0} strain)", other.DisplayName, cost)))
                {
                    sim.CancelMove(other.Id);
                    message = other.DisplayName + " stopped; it will ask for orders next.";
                    return true;
                }
                sx += 186f;
            }
            return false;
        }

        private bool DrawPicking(Unit unit, float x, ref float y)
        {
            if (GUI.Button(new Rect(x, y, 100f, 28f), "Back")) { Back(); return true; }
            y += 34f;
            if (plan.Count > 0)
                GUI.Label(new Rect(x, y, panelRect.width - 20f, 20f),
                    "Plan: " + string.Join("   ", plan.Select((p, i) => (i + 1) + ". " + p.Label).ToArray()));
            return false;
        }

        private bool DrawDash(Unit unit, float x, float y)
        {
            const float bw = 46f, bh = 26f;
            for (int i = 0; i < DashGrid.Length; i++)
            {
                int dir = DashGrid[i];
                if (dir < 0) continue;
                var r = new Rect(x + (i % 3) * (bw + 4f), y + (i / 3) * (bh + 4f), bw, bh);
                if (GUI.Button(r, DashNames[dir])) { SubmitDash(unit, dir); return true; }
            }
            if (GUI.Button(new Rect(x + 3f * (bw + 4f) + 16f, y, 140f, 28f), "Auto (away)"))
            {
                var attacker = sim.GetUnit(pending[0].AttackerId);
                SubmitDash(unit, attacker != null ? sim.BestDashDirection(unit, attacker) : 0);
                return true;
            }
            return false;
        }

        private void DrawHoverPreview(Unit unit, Vector2 mouse)
        {
            if (selected == null || selected.Targeting == TargetType.Position || hovered == null) return;
            string preview = TargetPreview(PlannedUnit(unit), selected, hovered);
            int lines = preview.Split('\n').Length;
            GUI.Box(new Rect(mouse.x + 18f, mouse.y + 4f, 330f, 8f + lines * 17f), preview);
        }

        /// <summary>"COMBO" over every target this ability would still catch stunned (dodge 0) when it lands.</summary>
        private void DrawComboLabels(Unit unit)
        {
            var cam = Cam;
            if (cam == null || Context == null || Context.Registry == null || !IsOffensive(selected)) return;
            var planned = PlannedUnit(unit);
            IEnumerable<Unit> candidates = selected.Targeting == TargetType.Position
                ? (hoverOnGround ? sim.UnitsInArea(planned, selected, hoverPoint) : Enumerable.Empty<Unit>())
                : sim.State.Units.Where(o => o.IsAlive && Pickable(unit, o));
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            foreach (var other in candidates)
            {
                string reason;
                if (selected.Targeting != TargetType.Position && !CanTarget(planned, selected, other, out reason)) continue;
                float? combo = ComboMargin(planned, selected, other);
                UnitView view;
                if (!combo.HasValue || combo.Value < 0f || !Context.Registry.TryGetValue(other.Id, out view)) continue;
                Vector2 at;
                if (!UiScale.WorldToGui(cam, view.transform.position + Vector3.up * 2.6f, out at)) continue;
                var r = new Rect(at.x - 50f, at.y - 12f, 100f, 24f);
                style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), "COMBO", style);
                style.normal.textColor = ComboColor;
                GUI.Label(r, "COMBO", style);
            }
        }

        private void DrawMarker(Unit unit)
        {
            var cam = Cam;
            UnitView view;
            if (cam == null || Context == null || Context.Registry == null || !Context.Registry.TryGetValue(unit.Id, out view)) return;
            Vector2 at;
            if (!UiScale.WorldToGui(cam, view.transform.position + Vector3.up * 3f, out at)) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            style.normal.textColor = SelectionColor;
            GUI.Label(new Rect(at.x - 20f, at.y - 20f, 40f, 40f), "▼", style);
        }
    }
}
