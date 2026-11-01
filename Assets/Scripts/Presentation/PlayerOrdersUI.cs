using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Bare-bones IMGUI order panel. While the world is held, it walks the pending decisions one unit at a time:
    ///   Action decision: pick an action (then click a target / point), Move (click the ground), Dash, Focus (a wait that sheds strain) or Auto.
    ///                    Unit targets must be in line of sight from where the unit will be; aiming a move highlights
    ///                    the units in sight from the destination.
    ///                    Hold Shift while giving an order to queue it and keep planning; later orders are planned
    ///                    from where the earlier ones leave the unit. Confirm (or an order without Shift) sends the plan.
    ///                    Player units still walking can also be stopped here, for a strain cost.
    ///   Dodge dash:      click any point inside the dash ring (or a compass button for a full-range dash).
    /// Keys: 1-0 abilities, F move, G dash, hold Space focus, Enter confirm, Backspace undo, Z re-center, Esc / right-click back
    /// (a right-drag rotates the camera instead). The camera glides to each unit as it comes up for orders. Ground markers show the unit, its plan, ranges and targets.
    /// Transcend: holding an ability that can be Transcended (its key or its button) for TranscendHoldSeconds picks its
    /// Transcended version (extra strain) instead; a quick press is the normal one. The screen darkens as the hold builds
    /// and stays dark while the Transcended ability is aimed.
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

        [Header("Hold Space to focus")]
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
        [Tooltip("Picking a move: enemies (and allies) the unit would have line of sight to from the destination.")]
        public Color SightEnemyColor = new Color(1f, 0.45f, 0.35f, 0.95f);
        public Color SightAllyColor = new Color(0.4f, 0.75f, 1f, 0.8f);

        [Header("Transcend")]
        [Tooltip("Seconds an ability's key or button must be held to Transcend it.")]
        public float TranscendHoldSeconds = 0.4f;
        public Color TranscendColor = new Color(0.85f, 0.6f, 1f, 1f);

        // Mesmer: strike again or stop a Mesmerize counter. Grapple: holding someone with Subjugate (keep headbutting, throw,
        // or move carrying them). PickDestination: the point an ability with a DestinationRange sends its target to, after the unit.
        private enum Mode { ChooseAction, PickTarget, PickMove, PickDash, Mesmer, Grapple, PickDestination }
        private Unit destinationFor;   // PickDestination: the unit picked first
        private bool releasing;   // PickTarget for a charging ability's release: Space keeps charging, no going back

        /// <summary>One order in the plan being built, plus where it leaves the unit (for planning the next one).</summary>
        private class PlannedOrder
        {
            public UnitCommand Command;
            public string Label;
            public Vector3 EndPosition;
            public List<Vector3> Path;   // moves only
        }

        private readonly List<DecisionRequest> pending = new List<DecisionRequest>();
        private readonly List<PlannedOrder> plan = new List<PlannedOrder>();
        private readonly IUnitAI ai = new AIRouter();   // Auto plays a unit the way its AI profile would
        private readonly List<Vector3> pathScratch = new List<Vector3>();
        private BattleSimulation sim;
        private System.Action onComplete;
        private Mode mode;
        private ActionDefinition selected;
        private bool selectedTranscend;   // 'selected' is being aimed Transcended
        // A held ability button/key that may become a Transcend: what, which key (None = the mouse), since when.
        private ActionDefinition holdDef;
        private KeyCode holdKey;
        private float holdStart, transcendFlashUntil;
        private bool holdFired;
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

            var current = sim.GetUnit(pending[0].UnitId);
            releasing = pending[0].Kind == DecisionKind.Charge && current.ActiveAction != null;
            mode = pending[0].Kind == DecisionKind.DodgeDash ? Mode.PickDash : releasing ? Mode.PickTarget : current.Grapple != null ? Mode.Grapple
                 : current.MesmerStrikesLeft > 0 ? Mode.Mesmer : Mode.ChooseAction;
            destinationFor = null;
            selected = releasing ? current.ActiveAction.Definition : null;
            selectedTranscend = false;
            holdDef = null; holdFocus = false;
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

        /// <summary>Number row 1-0 picks the matching ability button; F move, Backspace undo, Z re-center.</summary>
        private bool HandleHotkey(Unit unit, KeyCode key)
        {
            if (key == KeyCode.Z) { FocusCurrent(); return true; }
            if (mode == Mode.PickDash) return false;
            if (releasing) return AbilitySlot(key) >= 0 || key == KeyCode.F;   // a charge can only be released or kept going
            if (mode == Mode.Mesmer)
            {
                if (key == KeyCode.Alpha1 || key == KeyCode.Keypad1) { MesmerStrike(unit); return true; }
                if (key == KeyCode.Alpha2 || key == KeyCode.Keypad2) { MesmerStop(unit); return true; }
                return false;
            }
            if (unit.Grapple != null)
            {
                // Holding someone: keep headbutting, throw, or move (carrying them). Nothing else.
                bool awaiting = sim.AwaitingThrow(unit);
                if (mode == Mode.Grapple && !awaiting && (key == KeyCode.Alpha1 || key == KeyCode.Keypad1)) { GrappleHold(unit); return true; }
                if (mode == Mode.Grapple && (key == KeyCode.Alpha2 || key == KeyCode.Keypad2)) { GrappleThrow(unit); return true; }
                if (key == KeyCode.F && !awaiting) { mode = Mode.PickMove; selected = null; message = null; return true; }
                return false;
            }

            int slot = AbilitySlot(key);
            var slots = Slots(unit);
            if (slot >= 0 || key == KeyCode.G)
            {
                var def = key == KeyCode.G ? DashOf(unit) : slot < slots.Count ? slots[slot] : null;
                if (def == null) return slot >= 0;
                if (holdDef == def && holdKey == key) return true;   // the key repeating while it is held
                bool usable; string why;
                ActionDetail(unit, def, out usable, out why);
                if (!usable) { message = def.DisplayName + ": can't use now (" + why + ")"; return true; }
                if (def.CanTranscend) { BeginHold(def, key); return true; }   // acts on release, or Transcends if held
                PressAbility(unit, def);
                return true;
            }
            switch (key)
            {
                case KeyCode.F:
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

        /// <summary>A quick press of an ability: a highlighted follow-up's own key repeats it on the same target; otherwise
        /// pick a target as usual (switching abilities mid-targeting is fine).</summary>
        private void PressAbility(Unit unit, ActionDefinition def)
        {
            if (def == Followup(unit) && (mode == Mode.ChooseAction || mode == Mode.PickTarget)) { CastFollowup(unit, def); return; }
            mode = Mode.ChooseAction;
            ChooseAction(unit, def);
        }

        /// <summary>The numbered ability buttons: every action but Dash, which has its own key (G).</summary>
        private static List<ActionDefinition> Slots(Unit unit) { return unit.Actions.Where(a => a.BehaviorId != "dash").ToList(); }
        private static ActionDefinition DashOf(Unit unit) { return unit.Actions.FirstOrDefault(a => a.BehaviorId == "dash"); }

        // ------------------------------------------------------------------ Transcend (hold to)

        private void BeginHold(ActionDefinition def, KeyCode key)
        {
            holdDef = def;
            holdKey = key;
            holdStart = Time.unscaledTime;
            holdFired = false;
        }

        private float HoldProgress { get { return (holdDef != null || holdFocus) && !holdFired ? Mathf.Clamp01((Time.unscaledTime - holdStart) / Mathf.Max(0.05f, TranscendHoldSeconds)) : 0f; } }

        /// <summary>
        /// Every frame while an ability is held: held long enough, it is Transcended; let go before that, it is a normal
        /// press. Reads the key/mouse directly, so a release the GUI never saw still counts.
        /// </summary>
        private void UpdateHold(Unit unit)
        {
            if (holdFocus) { UpdateFocusHold(unit); return; }
            if (holdDef == null) return;
            if (unit == null || !unit.Actions.Contains(holdDef)) { holdDef = null; return; }
            bool down = holdKey != KeyCode.None ? Input.GetKey(holdKey) : Input.GetMouseButton(0);
            if (!holdFired && HoldProgress >= 1f)
            {
                holdFired = true;
                Transcend(unit, holdDef);
            }
            else if (!down)
            {
                var def = holdDef;
                bool fired = holdFired;
                holdDef = null;
                if (!fired) PressAbility(unit, def);
            }
        }

        // The Focus button held with the mouse: Transcended Focus once held long enough (Focus isn't an ActionDefinition).
        private bool holdFocus;

        private void UpdateFocusHold(Unit unit)
        {
            if (unit == null || mode != Mode.ChooseAction) { holdFocus = false; return; }
            if (HoldProgress >= 1f)
            {
                holdFocus = false;
                transcendFlashUntil = Time.unscaledTime + 0.3f;
                OrderFocus(unit, true);
            }
            else if (!Input.GetMouseButton(0)) { holdFocus = false; OrderFocus(unit, false); }
        }

        /// <summary>A Focus order; Transcended, it lasts until the unit is Focused (BattleSimulation.StartWait).</summary>
        private void OrderFocus(Unit unit, bool transcend)
        {
            AddOrder(UnitCommand.Wait(unit.Id, WaitSeconds).Transcended(transcend),
                transcend ? "Focus (until focused)" : string.Format("Focus {0:0.0}s", WaitSeconds), PlannedPosition(unit));
        }

        private void Transcend(Unit unit, ActionDefinition def)
        {
            string why;
            if (plan.Count == 0 && !sim.CanAfford(unit, def, out why, null, true)) { message = "Can't Transcend " + def.DisplayName + ": " + why; return; }
            transcendFlashUntil = Time.unscaledTime + 0.3f;
            mode = Mode.ChooseAction;
            ChooseAction(unit, def, true);
        }

        /// <summary>
        /// While aiming an ability (targeting mode, nothing queued before it): who would use it, the target under the cursor
        /// (if any) and the sim time it would land if ordered now. The battle timeline draws it as a ghost entry.
        /// </summary>
        public bool TryGetTargetingPreview(out Unit unit, out ActionDefinition def, out Unit target, out float resolveAt)
        {
            unit = Current; def = selected; target = hovered; resolveAt = 0f;
            if (unit == null || def == null || (mode != Mode.PickTarget && mode != Mode.PickDestination) || plan.Count > 0) return false;
            if (mode == Mode.PickDestination) target = destinationFor;
            resolveAt = sim.Now + def.Windup / Mathf.Max(0.01f, unit.EffectiveActionSpeed());
            return true;
        }

        /// <summary>While aiming a move (nothing queued before it) at a reachable point: who and when it would be free to act
        /// again (the walk's end). The battle timeline draws it like an aimed ability.</summary>
        public bool TryGetMovePreview(out Unit unit, out float readyAt)
        {
            unit = Current; readyAt = 0f;
            if (unit == null || mode != Mode.PickMove || plan.Count > 0 || moveSeconds < 0f) return false;
            readyAt = sim.Now + moveSeconds;
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
            ActionDetail(unit, def, out usable, out why);
            if (!usable) return null;
            if (TargetsUnit(def))
            {
                var t = sim.GetUnit(unit.LastActionTargetId);
                if (t == null || !t.IsAlive || !sim.IsTargetableBy(t, unit) || !sim.HasSightOf(unit, def, t)) return null;
            }
            return def;
        }

        /// <summary>Aimed at a unit (an enemy, an ally, or either), rather than a point or the user.</summary>
        private static bool TargetsUnit(ActionDefinition def)
        {
            return def.Targeting == TargetType.Enemy || def.Targeting == TargetType.Ally || def.Targeting == TargetType.AnyUnit;
        }

        private void CastFollowup(Unit unit, ActionDefinition def)
        {
            mode = Mode.ChooseAction;
            selected = null;
            if (TargetsUnit(def) && def.DestinationRange <= 0f) TryAddAction(unit, def, sim.GetUnit(unit.LastActionTargetId));
            else if (def.Targeting == TargetType.Position) AddOrder(UnitCommand.UseActionAt(unit.Id, def, unit.LastActionPos), def.DisplayName + " (point)", PlannedPosition(unit));
            else ChooseAction(unit, def);
        }

        /// <summary>0-based ability slot for the number row (and keypad): 1..9, 0 for the tenth, then -, =, [, ] and \. -1 if none.</summary>
        private static int AbilitySlot(KeyCode key)
        {
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9) return key - KeyCode.Alpha1;
            if (key == KeyCode.Alpha0) return 9;
            if (key == KeyCode.Minus) return 10;
            if (key == KeyCode.Equals) return 11;
            if (key == KeyCode.LeftBracket) return 12;
            if (key == KeyCode.RightBracket) return 13;
            if (key == KeyCode.Backslash) return 14;
            if (key >= KeyCode.Keypad1 && key <= KeyCode.Keypad9) return key - KeyCode.Keypad1;
            if (key == KeyCode.Keypad0) return 9;
            return -1;
        }

        private static string SlotKey(int slot) { return slot < 9 ? (slot + 1).ToString() : slot == 9 ? "0" : slot == 10 ? "-" : slot == 11 ? "=" : slot == 12 ? "[" : slot == 13 ? "]" : slot == 14 ? "\\" : null; }

        private static string FollowupKeyName(Unit unit, ActionDefinition def)
        {
            if (def.BehaviorId == "dash") return "G";
            int slot = Slots(unit).IndexOf(def);
            string key = slot >= 0 ? SlotKey(slot) : null;
            return key ?? def.DisplayName;
        }

        private void Back()
        {
            if (releasing || mode == Mode.Mesmer || mode == Mode.Grapple) return;   // charges can't be cancelled; a counter or a hold has its own buttons
            if (mode == Mode.PickMove && Current != null && Current.Grapple != null) { mode = Mode.Grapple; message = null; return; }
            if (mode == Mode.PickDestination) { mode = Mode.PickTarget; destinationFor = null; message = null; return; }
            if (mode == Mode.PickTarget || mode == Mode.PickMove) { mode = Mode.ChooseAction; selected = null; selectedTranscend = false; message = null; }
        }

        /// <summary>Adds an order to the plan. Without Shift held the plan is sent right away.</summary>
        private void AddOrder(UnitCommand cmd, string label, Vector3 endPosition, List<Vector3> path = null)
        {
            if (cmd.Transcend) label = "Transcended " + label;
            plan.Add(new PlannedOrder { Command = cmd, Label = label, EndPosition = endPosition, Path = path });
            mode = Mode.ChooseAction;
            selected = null;
            selectedTranscend = false;
            message = null;
            // Either source: orders also come from Update (a released Transcend hold), where Event.current isn't this frame's.
            bool queueMore = (Event.current != null && Event.current.shift) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Current != null && Current.Grapple != null) queueMore = false;   // holding someone: one order at a time
            if (!queueMore) ConfirmPlan();
        }

        private void ConfirmPlan()
        {
            foreach (var p in plan) sim.SubmitCommand(p.Command);
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

        /// <summary>CanUse for the aimed ability, Transcended if that is how it is being aimed.</summary>
        private bool CanTarget(Unit planned, ActionDefinition def, Unit target, out string reason)
        {
            reason = null;
            if (target == null || !target.IsAlive) { reason = "no target"; return false; }
            if (def.Targeting == TargetType.Enemy && target.Team == planned.Team) { reason = "not an enemy"; return false; }
            if (def.Targeting == TargetType.Ally && target.Team != planned.Team) { reason = "not an ally"; return false; }
            // Cost/cooldown of a later queued order are only known when it starts; the sim re-checks then.
            bool ok = sim.CanUse(planned, def, target, out reason, plan.Count == 0, def == selected && selectedTranscend);
            // Ranged abilities only pick targets the unit can see (from where the plan leaves it), out-of-range ones too: no
            // queueing a shot at a target behind a wall. Melee can still chase around it.
            if (def.IsRanged && (ok || reason == "out of range") && !sim.HasSightOf(planned, def, target)) { reason = "no line of sight"; return false; }
            return ok;
        }

        /// <summary>Order 'def' now (self/untargeted) or start aiming it; 'transcend' picks its Transcended version.</summary>
        private void ChooseAction(Unit unit, ActionDefinition def, bool transcend = false)
        {
            message = null;
            transcend &= def.CanTranscend;
            var planned = PlannedUnit(unit);
            if (def.IsCharge)
            {
                // Charges start at once; the target is picked when it is released.
                AddOrder(UnitCommand.UseAction(unit.Id, def).Transcended(transcend), def.DisplayName + " (charge)", planned.Position);
                return;
            }
            switch (def.Targeting)
            {
                case TargetType.Self:
                    selected = def; selectedTranscend = transcend;   // so CanTarget prices the Transcend in
                    TryAddAction(unit, def, unit, transcend);
                    if (mode == Mode.ChooseAction) { selected = null; selectedTranscend = false; }
                    break;
                case TargetType.None:
                    AddOrder(UnitCommand.UseAction(unit.Id, def).Transcended(transcend), def.DisplayName, planned.Position);
                    break;
                default:
                    selected = def;
                    selectedTranscend = transcend;
                    mode = Mode.PickTarget;
                    break;
            }
        }

        private void TryAddAction(Unit unit, ActionDefinition def, Unit target, bool transcend = false)
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
                AddOrder(UnitCommand.UseActionWhenInRange(unit.Id, def, target.Id).Transcended(transcend), label + (chase ? " (chase)" : " (queued)"), chase ? target.Position : planned.Position);
                return;
            }
            AddOrder(UnitCommand.UseActionWhenInRange(unit.Id, def, target.Id).Transcended(transcend), label, planned.Position);
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
            if (mode == Mode.PickDestination) return false;   // a destination is ground: see through everyone
            if (mode != Mode.PickTarget || selected == null) return true;
            if (selected.Targeting == TargetType.Enemy) return other.Team != unit.Team;
            if (selected.Targeting == TargetType.Ally) return other.Team == unit.Team && !(selected.CannotTargetSelf && other.Id == unit.Id);
            if (selected.Targeting == TargetType.AnyUnit) return !(selected.CannotTargetSelf && other.Id == unit.Id);
            if (AimingPoint) return other.Team != unit.Team;   // aim through your own side at the enemy behind
            return true;
        }

        /// <summary>Picking the point for a point-targeted ability (not Dash, which goes wherever the ground is).</summary>
        private bool AimingPoint
        {
            get { return mode == Mode.PickTarget && selected != null && selected.Targeting == TargetType.Position && selected.BehaviorId != "dash"; }
        }

        /// <summary>How close (screen pixels at 1080p) the cursor must pass an enemy for a line to lock onto it.</summary>
        public float AimSnapPixels = 40f;

        /// <summary>
        /// Where a point-targeted ability aims, at the elevation the player means: an enemy under the cursor (or, for a line,
        /// one the cursor passes close to) is aimed at its feet, so a line climbs onto the ledge it stands on; otherwise the
        /// ground under the cursor, with a cliff face counting as the top of the block it belongs to.
        /// </summary>
        private Vector3 AimPoint(Unit unit, Vector2 screenPos, RaycastHit hit, Unit picked)
        {
            if (picked != null && picked.Team != unit.Team) return picked.Position;
            if (selected.Shape == AreaShape.Line)
            {
                var snap = EnemyNearCursor(unit, screenPos);
                if (snap != null) return snap.Position;
            }
            return sim.Terrain.OnGround(hit.point - hit.normal * 0.05f);
        }

        /// <summary>The visible enemy whose body (feet to head, on screen) passes closest to the cursor, within AimSnapPixels.</summary>
        private Unit EnemyNearCursor(Unit unit, Vector2 screenPos)
        {
            var cam = Cam;
            if (cam == null) return null;
            float best = AimSnapPixels * Screen.height / 1080f;
            Unit found = null;
            foreach (var other in sim.State.Units)
            {
                if (!other.IsAlive || other.Team == unit.Team || !sim.IsTargetableBy(other, unit)) continue;
                Vector3 feet = cam.WorldToScreenPoint(other.Position), head = cam.WorldToScreenPoint(other.Position + Vector3.up * other.BodyHeight);
                if (feet.z <= 0f || head.z <= 0f) continue;
                Vector2 a = feet, b = head, ab = b - a;
                float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector2.Dot(screenPos - a, ab) / ab.sqrMagnitude) : 0f;
                float d = Vector2.Distance(screenPos, a + ab * t);
                if (d < best) { best = d; found = other; }
            }
            return found;
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

        private void Highlight(Unit u, Color color, bool pulse = false)
        {
            UnitView view;
            if (Context != null && Context.Registry != null && Context.Registry.TryGetValue(u.Id, out view)) UnitHighlighter.Request(view, color, pulse);
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
                    if (releasing)
                    {
                        if (selected.Targeting == TargetType.Position) sim.SubmitCommand(UnitCommand.ReleaseCharge(unit.Id, null, AimPoint(unit, screenPos, hit, clickedUnit)));
                        else if (clickedUnit != null)
                        {
                            if (!sim.HasSightOf(unit, selected, clickedUnit)) { message = "Can't: no line of sight"; return; }
                            sim.SubmitCommand(UnitCommand.ReleaseCharge(unit.Id, clickedUnit.Id, clickedUnit.Position));
                        }
                        else return;
                        Next();
                        return;
                    }
                    if (selected.Targeting == TargetType.Position)
                    {
                        bool dash = selected.BehaviorId == "dash";
                        Vector3 point = dash ? hit.point : AimPoint(unit, screenPos, hit, clickedUnit);
                        if (selected.Shape != AreaShape.Line && Vector3.Distance(from, point) > unit.ActionStat(selected, ActionStat.Range)) { message = "Out of range."; return; }
                        AddOrder(UnitCommand.UseActionAt(unit.Id, selected, point).Transcended(selectedTranscend), selected.DisplayName + (dash ? "" : " (point)"),
                            dash ? sim.DashDestination(PlannedUnit(unit), point, selectedTranscend) : from);
                    }
                    else if (clickedUnit != null && selected.DestinationRange > 0f) BeginDestination(unit, clickedUnit);
                    else if (clickedUnit != null) TryAddAction(unit, selected, clickedUnit, selectedTranscend);
                    break;

                case Mode.PickDestination:
                {
                    var who = destinationFor;
                    if (who == null || !who.IsAlive) { mode = Mode.PickTarget; destinationFor = null; return; }
                    Vector3 point = DestinationPoint(hit);
                    if (FlatDistance(who.Position, point) > selected.DestinationRange) { message = string.Format("Too far: pick a point within {0:0}m of {1}.", selected.DestinationRange, who.DisplayName); return; }
                    string label = selected.DisplayName + " > " + (who.Id == unit.Id ? "self" : who.DisplayName);
                    destinationFor = null;
                    AddOrder(UnitCommand.UseActionOn(unit.Id, selected, who.Id, point).Transcended(selectedTranscend), label, who.Id == unit.Id ? point : from);
                    break;
                }

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

                case Mode.Grapple:
                    // Holding someone: a click throws them that way.
                    if (unit.Grapple == null) return;
                    sim.SubmitCommand(UnitCommand.ReleaseGrapple(unit.Id, clickedUnit != null ? clickedUnit.Position : hit.point));
                    Next();
                    break;
            }
        }

        /// <summary>An ability that sends its target somewhere (DestinationRange): the unit is picked, now the point.</summary>
        private void BeginDestination(Unit unit, Unit target)
        {
            string reason;
            if (!CanTarget(PlannedUnit(unit), selected, target, out reason) && reason != "out of range")
            {
                message = (plan.Count > 0 ? "Can't from there: " : "Can't: ") + reason;
                return;
            }
            destinationFor = target;
            mode = Mode.PickDestination;
            message = null;
        }

        /// <summary>The walkable point under the cursor for a destination (a cliff face counts as the top of its block).</summary>
        private Vector3 DestinationPoint(RaycastHit hit)
        {
            return sim.WalkablePointNear(sim.Terrain.OnGround(hit.point - hit.normal * 0.05f));
        }

        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        private void GrappleHold(Unit unit) { sim.SubmitCommand(UnitCommand.HoldGrapple(unit.Id)); Next(); }
        /// <summary>Throw now: toward the point under the cursor if there is one, else the way the unit faces.</summary>
        private void GrappleThrow(Unit unit)
        {
            Vector3? at = ThrowAim();
            sim.SubmitCommand(at.HasValue ? UnitCommand.ReleaseGrapple(unit.Id, at.Value) : UnitCommand.ReleaseGrapple(unit.Id));
            Next();
        }

        /// <summary>Where a throw would be aimed: the unit or ground under the cursor (null: none, throw the way it faces).</summary>
        private Vector3? ThrowAim() { return hovered != null ? hovered.Position : hoverOnGround ? hoverPoint : (Vector3?)null; }

        /// <summary>The way a throw from 'unit' would go now: toward the cursor, else its facing.</summary>
        private Vector3 ThrowDirection(Unit unit)
        {
            Vector3? at = ThrowAim();
            if (at.HasValue)
            {
                Vector3 d = at.Value - unit.Position; d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) return d.normalized;
            }
            return sim.GrappleFacing(unit);
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
            return def.Damage > 0f && ActionBehaviors.Get(def.BehaviorId).RollsToHit(def);
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
            float height = sim.HeightAccuracyBonus(planned, target, def);
            float bonus = height;
            bonus += ActionBehaviors.Get(def.BehaviorId).PreviewAccuracyBonus(def, def == selected && selectedTranscend);
            bonus += target.IncomingAccuracyBonus();
            float hitChance = Mathf.Clamp01(planned.ActionStat(def, ActionStat.Accuracy) * exposure + bonus);
            // Dodge at the moment it lands: none if the target is still stunned then (a combo).
            float? combo = ComboMargin(planned, def, target);
            float dodge = combo.HasValue && combo.Value >= 0f ? 0f : target.UnstunnedDodge();
            string text = string.Format("{0}: {1:0}% to land\nhit {2:0}%  (exposure {3:0}%)  dodge {4:0}%",
                target.DisplayName, hitChance * (1f - dodge) * 100f, hitChance * 100f, exposure * 100f, dodge * 100f);
            if (height > 0f) text += string.Format("\nHIGH GROUND: +{0:0}% hit", height * 100f);
            if (combo.HasValue)
                text += combo.Value >= 0f
                    ? string.Format("\nCOMBO: still stunned when it lands ({0:0.0}s to spare), can't dodge", combo.Value)
                    : string.Format("\nNo combo: the stun wears off {0:0.0}s before it lands", -combo.Value);
            return text;
        }

        // ------------------------------------------------------------------ hover + ground markers

        // Hold-to-focus: units that were up for orders when Space went down keep focusing (waiting) in short steps while it is held,
        // with playback slowed. Any other unit that comes up meanwhile is asked for orders as usual.
        private readonly HashSet<string> holdUnits = new HashSet<string>();
        private bool holding;
        private bool holdBlocked;   // a hold was cut short by another unit coming up: ignore Space until it is released
        private float holdStartClock, holdRestoreSpeed;

        private PlaybackDirector Dir { get { if (Director == null) Director = FindFirstObjectByType<PlaybackDirector>(); return Director; } }

        private void UpdateHoldWait()
        {
            if (!Input.GetKey(KeyCode.Space)) { if (holding) EndHoldWait(); holdBlocked = false; return; }
            // Another unit came up for orders mid-hold: stop, and wait for Space to be let go and pressed again.
            if (holding && Active && Current != null && !holdUnits.Contains(Current.Id))
            {
                EndHoldWait();
                holdBlocked = true;
                message = "Stopped focusing: " + Current.DisplayName + " is ready. Release and hold Space again to keep focusing.";
            }
            if (holdBlocked) return;
            // A charging unit keeps charging for as long as Space is held.
            if (Active && releasing && Current != null)
            {
                if (!holding) StartHoldWait();
                holdUnits.Add(Current.Id);
                sim.SubmitCommand(UnitCommand.ContinueCharge(Current.Id, HoldWaitStep));
                Next();
                return;
            }
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
            string text = string.Format("Focusing (Space)   +{0:0.0}s", Dir.PlaybackClock - holdStartClock);
            GUI.Box(new Rect(UiScale.Width / 2f - 150f, 110f, 300f, 34f), text, style);
        }

        private void Update()
        {
            UpdateHoldWait();
            markers.Begin();
            sightEnemies = -1;
            moveSeconds = -1f;
            var unit = Current;
            UpdateHold(unit);
            if (unit != null) UpdateHover();
            if (unit != null) Highlight(unit, SelectionColor, true);   // the unit being commanded pulses
            if (unit != null) DrawMarkers(unit);
            // The unit glows violet while an ability is held toward a Transcend, and while a Transcended one is aimed.
            if (unit != null && (HoldProgress > 0f || Transcending)) Highlight(unit, TranscendColor, true);
            markers.End();
        }

        private bool Transcending { get { return Active && (mode == Mode.PickTarget || mode == Mode.PickDestination) && selected != null && selectedTranscend; } }

        // The hovered move's walking time (-1 = none) and where along the route its label goes (refreshed every Update).
        private float moveSeconds = -1f;
        private Vector3 moveLabelAt;

        private void UpdateHover()
        {
            hovered = null;
            hoverOnGround = false;
            RaycastHit hit;
            if (!PickWorld(Current, Input.mousePosition, out hit, out hovered)) return;
            hoverOnGround = hovered == null;
            hoverPoint = hit.point;
            if (AimingPoint)
            {
                hoverPoint = AimPoint(Current, Input.mousePosition, hit, hovered);   // an enemy under the cursor is aimed at, not blocking the preview
                hoverOnGround = true;
            }
            if (mode == Mode.PickDestination) hoverPoint = DestinationPoint(hit);
        }

        /// <summary>
        /// A range ring of 'range' around 'from', drawn at the height of 'at' (the point or unit being aimed at), so it stays
        /// visible on raised or lower ground and shows the real reach there. Range is a 3D distance, so at a different height the
        /// ring is narrower (sqrt(range^2 - height^2)); 'flat' reaches (dashes, destinations) are measured across the ground
        /// and keep their radius. The ring at the unit's own height stays, faint, for reference.
        /// </summary>
        private void RangeRing(Vector3 from, float range, Vector3? at, bool flat, Color color, float width)
        {
            float dy = at.HasValue ? at.Value.y - from.y : 0f;
            if (Mathf.Abs(dy) < 0.1f) { markers.Circle(from, range, color, width, 96); return; }
            float r = flat ? range : Mathf.Sqrt(Mathf.Max(0f, range * range - dy * dy));
            var faint = color; faint.a *= 0.3f;
            markers.Circle(from, range, faint, width * 0.6f, 96);
            markers.Circle(new Vector3(from.x, at.Value.y, from.z), r, color, width, 96);
        }

        private void DrawMarkers(Unit unit)
        {
            Vector3 from = PlannedPosition(unit);
            markers.Circle(unit.Position, unit.BodyRadius + 0.35f, SelectionColor, 0.07f);

            // The plan so far: routes for queued moves and a ring where the unit will end up.
            foreach (var p in plan) if (p.Path != null) markers.Polyline(sim.Terrain.Drape(p.Path), PlanColor);
            if (plan.Count > 0) markers.Circle(from, unit.BodyRadius + 0.2f, PlanColor, 0.05f);
            // Enemies already picked as targets in the plan stay lit.
            foreach (var p in plan)
            {
                var t = p.Command.TargetUnitId != null && p.Command.TargetUnitId != unit.Id ? sim.GetUnit(p.Command.TargetUnitId) : null;
                if (t != null && t.IsAlive) Highlight(t, PlanColor);
            }

            switch (mode)
            {
                case Mode.ChooseAction:
                    break;   // no range rings while waiting for orders: they show once an ability is picked

                case Mode.PickTarget:
                {
                    Color aim = selectedTranscend ? TranscendColor : SelectionColor;
                    if (selected.BehaviorId == "dash")
                    {
                        // Dash: its reach, and where it would land from where the unit will be.
                        var dasher = PlannedUnit(unit);
                        Vector3? dashAt = hoverOnGround ? hoverPoint : (Vector3?)null;
                        if (selectedTranscend) RangeRing(from, sim.DashReach(dasher), dashAt, true, RangeColor, 0.03f);   // the plain reach, for comparison
                        RangeRing(from, sim.DashReach(dasher, selectedTranscend), dashAt, true, selectedTranscend ? TranscendColor : RangeColor, 0.05f);
                        if (hoverOnGround)
                        {
                            Vector3 to = sim.DashDestination(dasher, hoverPoint, selectedTranscend);
                            markers.Segment(from, to, aim, 0.08f);
                            markers.Circle(to, unit.BodyRadius, aim, 0.05f);
                        }
                        break;
                    }
                    // Reach is measured in 3D, so the ring follows the height of whatever is being aimed at (see RangeRing).
                    Vector3? aimedAt = selected.Targeting == TargetType.Position ? (hoverOnGround ? hoverPoint : (Vector3?)null) : hovered != null ? hovered.Position : hoverOnGround ? hoverPoint : (Vector3?)null;
                    if (selected.Shape != AreaShape.Line) RangeRing(from, unit.ActionStat(selected, ActionStat.Range), aimedAt, false, selectedTranscend ? TranscendColor : RangeColor, 0.06f);
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
                        if (selected.CannotTargetSelf && other.Id == unit.Id) continue;
                        string reason;
                        bool ok = CanTarget(planned, selected, other, out reason);
                        bool hold = !ok && reason == "out of range";
                        bool isHovered = hovered != null && hovered.Id == other.Id;
                        markers.Circle(other.Position, other.BodyRadius + 0.3f, ok ? ValidTargetColor : hold ? HoldColor : InvalidTargetColor, isHovered ? 0.12f : 0.05f);
                        if (isHovered && hold) markers.Segment(from, other.Position, HoldColor, 0.03f);
                        float? combo = ok ? ComboMargin(planned, selected, other) : null;
                        if (combo.HasValue && combo.Value >= 0f) markers.Circle(other.Position, other.BodyRadius + 0.55f, ComboColor, 0.08f);
                        bool isCombo = combo.HasValue && combo.Value >= 0f;
                        // The unit under the cursor lights up in its verdict's colour (and pulses); combo targets stand out, even behind cover.
                        if (isHovered) Highlight(other, ok ? (isCombo ? ComboColor : ValidTargetColor) : hold ? HoldColor : InvalidTargetColor, true);
                        else if (isCombo) Highlight(other, ComboColor);
                        if (isHovered && ok) markers.Segment(from, other.Position, ValidTargetColor, 0.04f);
                    }
                    break;
                }

                case Mode.PickDestination:
                {
                    // Where the picked unit would be sent: the ring it can go within, and the spot under the cursor.
                    var who = destinationFor;
                    if (who == null) break;
                    Color aim = selectedTranscend ? TranscendColor : SelectionColor;
                    RangeRing(who.Position, selected.DestinationRange, hoverOnGround ? hoverPoint : (Vector3?)null, true, RangeColor, 0.05f);
                    markers.Circle(who.Position, who.BodyRadius + 0.3f, ValidTargetColor, 0.1f);
                    Highlight(who, ValidTargetColor, true);
                    if (!hoverOnGround) break;
                    bool ok = FlatDistance(who.Position, hoverPoint) <= selected.DestinationRange;
                    markers.Segment(who.Position, hoverPoint, ok ? aim : InvalidTargetColor, 0.06f);
                    markers.Circle(hoverPoint, who.BodyRadius, ok ? aim : InvalidTargetColor, 0.06f);
                    break;
                }

                case Mode.Grapple:
                {
                    // The held unit, and where the throw would send it.
                    var held = unit.Grapple != null ? sim.GetUnit(unit.Grapple.TargetId) : null;
                    if (held == null) break;
                    Highlight(held, InvalidTargetColor, true);
                    // It swings round to the side it is thrown toward (the cursor), then flies GrappleThrowDistance that way.
                    Vector3 dir = ThrowDirection(unit);
                    Vector3 throwFrom = unit.Position + dir * BattleSimulation.CarryOffset(unit, held);
                    Vector3 throwTo = sim.Paths.ClampDash(throwFrom, throwFrom + dir * sim.GrappleThrowDistance);
                    markers.Segment(throwFrom, throwTo, InvalidTargetColor, 0.06f);
                    markers.Circle(throwTo, held.BodyRadius, InvalidTargetColor, 0.05f);
                    break;
                }

                case Mode.PickMove:
                    if (hoverOnGround && sim.PlanPath(unit, from, sim.FreeSpotNear(unit, hoverPoint, from, true), pathScratch))
                    {
                        Vector3 dest = pathScratch[pathScratch.Count - 1];
                        markers.Polyline(sim.Terrain.Drape(pathScratch), SelectionColor);   // rises and drops where it jumps
                        markers.Circle(dest, 0.35f, SelectionColor, 0.05f);
                        moveSeconds = sim.MoveSeconds(unit, pathScratch);
                        moveLabelAt = Midway(pathScratch);
                        DrawSightFrom(unit, dest);
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

        /// <summary>The point halfway along a path (by ground distance), on the terrain.</summary>
        private Vector3 Midway(List<Vector3> path)
        {
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
            float half = total / 2f;
            for (int i = 1; i < path.Count; i++)
            {
                float leg = Vector3.Distance(path[i - 1], path[i]);
                if (leg >= half) return sim.Terrain.OnGround(Vector3.Lerp(path[i - 1], path[i], leg > 1e-4f ? half / leg : 0f));
                half -= leg;
            }
            return path[path.Count - 1];
        }

        /// <summary>Picking a move: the walking time, over the middle of the route (and when the unit is free again).</summary>
        private void DrawMoveLabel()
        {
            var cam = Cam;
            Vector2 at;
            if (moveSeconds < 0f || cam == null || !UiScale.WorldToGui(cam, moveLabelAt + Vector3.up * 0.6f, out at)) return;
            string text = string.Format("{0:0.0}s walk", moveSeconds);
            var style = new GUIStyle(GUI.skin.box) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = SelectionColor;
            float w = style.CalcSize(new GUIContent(text)).x + 12f;
            GUI.Box(new Rect(at.x - w / 2f, at.y - 12f, w, 24f), text, style);
        }

        // Units in sight of the hovered move destination, counted for the cursor label (refreshed every Update).
        private int sightEnemies = -1, sightAllies;

        /// <summary>
        /// Move preview: highlights (through walls too) every unit the mover would have line of sight to standing at 'dest',
        /// with everyone else where they are now. Enemies hidden from the player's side aren't shown.
        /// </summary>
        private void DrawSightFrom(Unit unit, Vector3 dest)
        {
            var mover = unit.Snapshot();   // same Id, so its own body doesn't block
            mover.Position = dest;
            sightEnemies = 0; sightAllies = 0;
            foreach (var other in sim.State.Units)
            {
                if (!other.IsAlive || other.Id == unit.Id || !sim.IsTargetableBy(other, unit)) continue;
                if (!sim.Visibility.HasLineOfSight(mover, other)) continue;
                bool enemy = other.Team != unit.Team;
                if (enemy) sightEnemies++; else sightAllies++;
                Color c = enemy ? SightEnemyColor : SightAllyColor;
                Highlight(other, c);
                markers.Circle(other.Position, other.BodyRadius + 0.3f, c, enemy ? 0.07f : 0.04f);
            }
        }

        /// <summary>Picking a move: how many units are in sight of the hovered destination, next to the cursor.</summary>
        private void DrawSightLabel(Vector2 mouse)
        {
            if (sightEnemies < 0) return;
            string text = string.Format("In sight: {0} enem{1}, {2} all{3}", sightEnemies, sightEnemies == 1 ? "y" : "ies", sightAllies, sightAllies == 1 ? "y" : "ies");
            GUI.Box(new Rect(mouse.x + 18f, mouse.y + 4f, 220f, 24f), text);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>A point 'along' metres down the hovered line, at the line's height there.</summary>
        private Vector3 LinePoint(Vector3 from, Vector3 dir, float along)
        {
            Vector3 p = from + dir * along;
            p.y = selected.IsRanged ? BattleSimulation.LineHeightAt(from, hoverPoint, along) : from.y;   // melee sweeps stay level
            return p;
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
                // Ramps from the caster to the aimed elevation, then level (see BattleSimulation.LineHeightAt).
                float aimDist = Mathf.Min(Vector3.Distance(Flat(from), Flat(hoverPoint)), unit.ActionStat(selected, ActionStat.Range));
                Vector3 bend = LinePoint(from, dir, aimDist), end = LinePoint(from, dir, unit.ActionStat(selected, ActionStat.Range));
                markers.Polyline(new[] { from + side, bend + side, end + side, end - side, bend - side, from - side, from + side }, SelectionColor, 0.05f);
                markers.Circle(hoverPoint, 0.3f, SelectionColor, 0.05f, 24);
            }
            else
            {
                bool inRange = Vector3.Distance(from, hoverPoint) <= unit.ActionStat(selected, ActionStat.Range);
                float radius = selected.AreaRadius > 0f ? unit.ActionStat(selected, ActionStat.AreaRadius) : 0.5f;
                markers.Circle(hoverPoint, radius, inRange ? SelectionColor : InvalidTargetColor, 0.06f, 64);
                if (!inRange || selected.Shape != AreaShape.Circle) return;
            }
            var planned = PlannedUnit(unit);
            foreach (var hit in sim.UnitsInArea(planned, selected, hoverPoint))
            {
                markers.Circle(hit.Position, hit.BodyRadius + 0.3f, ValidTargetColor, 0.08f);
                float? combo = ComboMargin(planned, selected, hit);
                if (combo.HasValue && combo.Value >= 0f) markers.Circle(hit.Position, hit.BodyRadius + 0.55f, ComboColor, 0.08f);
                if (combo.HasValue && combo.Value >= 0f) Highlight(hit, ComboColor);
            }
        }

        // ------------------------------------------------------------------ panel

        private void OnGUI()
        {
            GUI.depth = -5;   // in front of the HUD's health and strain bars and the combat text, so the ability popup is never covered
            UiScale.Begin();
            if (UiDeclutter.On) return;   // Alt: only the battle and its bars (release to give orders)
            DrawHoldTimer();
            var unit = Active ? Current : null;
            if (unit == null) { BottomReserved = 0f; return; }
            var e = Event.current;

            DrawTranscendOverlay(unit);   // first: everything else draws over the darkened screen
            DrawMarker(unit);
            if (mode == Mode.PickTarget && selected != null) DrawComboLabels(unit);
            if (mode == Mode.PickMove) DrawMoveLabel();

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
            float height = 56f;
            LayoutTiles(unit);
            if (mode == Mode.PickDash) height += 4f;   // header and hint only: the dash is picked in the world
            else if (mode == Mode.ChooseAction) height += ButtonRows(unit) * (ButtonHeight + ButtonGap);
            else height += 34f;
            if (plan.Count > 0) height += 52f;
            if (stoppable.Count > 0) height += 28f;
            panelRect = new Rect((UiScale.Width - width) / 2f, UiScale.Height - height - 4f, width, height);
            BottomReserved = height + 4f;
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
            tipUnit = unit;   // the popup shows this unit's numbers (upgrades and statuses included)
            bool changed;
            switch (mode)
            {
                case Mode.ChooseAction: changed = DrawChooseAction(unit, x, ref y, stoppable); break;
                case Mode.PickDash: changed = false; break;
                case Mode.Mesmer: changed = DrawMesmer(unit, x, ref y); break;
                case Mode.Grapple: changed = DrawGrapple(unit, x, ref y); break;
                default: changed = DrawPicking(unit, x, ref y); break;
            }
            if (changed || !Active) return;

            // While picking a target, keep the chosen ability's description up so its rules are in view.
            if ((mode == Mode.PickTarget || mode == Mode.PickDestination) && selected != null)
            {
                tipTitle = selected.DisplayName; tipBody = selected.Description; tipStats = null; tipSections = ActionStats(selected, tipUnit); tipWarning = null;
                tipAnchor = new Rect(panelRect.x, panelRect.y, 0f, 0f);
            }
            DrawTip();   // last, so it draws over the panel

            if (mode == Mode.PickTarget) DrawHoverPreview(unit, e.mousePosition);
            if (mode == Mode.PickMove) DrawSightLabel(e.mousePosition);

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
                    if (releasing)
                        return string.Format("{0}: charging {1}  {2:0.0}s ({3:0}%)", unit.DisplayName, selected.DisplayName,
                            unit.ActiveAction != null ? sim.Now - unit.ActiveAction.StartedAt : 0f, sim.ChargeLevel(unit) * 100f);
                {
                    string name = (selectedTranscend ? "TRANSCENDED " : "") + selected.DisplayName;
                    float range = selected.BehaviorId == "dash" ? sim.DashReach(PlannedUnit(unit), selectedTranscend) : unit.ActionStat(selected, ActionStat.Range);
                    return selected.Targeting == TargetType.Position
                        ? string.Format("{0}: click a point for {1} (range {2:0.#}m)", unit.DisplayName, name, range)
                        : string.Format("{0}: click a target for {1} (range {2:0.#}m)", unit.DisplayName, name, range);
                }
                case Mode.PickMove:
                    return unit.Grapple != null ? unit.DisplayName + ": click the ground to move there, carrying " + HeldName(unit)
                                                : unit.DisplayName + ": click the ground to move there (lit units: in sight from there)";
                case Mode.PickDestination:
                    return string.Format("{0}: click where {1} {2} for {3}", unit.DisplayName, destinationFor != null && destinationFor.Id == unit.Id ? "you" : destinationFor != null ? destinationFor.DisplayName : "it",
                        destinationFor != null && destinationFor.Id == unit.Id ? "go" : "goes", (selectedTranscend ? "TRANSCENDED " : "") + selected.DisplayName);
                case Mode.Grapple:
                {
                    var g = unit.Grapple;
                    if (g != null && g.AwaitingThrow) return string.Format("{0}: the grab is up! Click where to throw {1} (2: toward the cursor, or the way you face)", unit.DisplayName, HeldName(unit));
                    return g == null ? unit.DisplayName : string.Format("{0}: holding {1}!  next headbutt in {2:0.0}s, thrown in {3:0.0}s (click to aim it)", unit.DisplayName, HeldName(unit),
                        Mathf.Max(0f, g.NextHeadbuttAt - sim.Now), Mathf.Max(0f, g.EndsAt - sim.Now));
                }
                case Mode.Mesmer:
                    return string.Format("{0}: Mesmerize counter on {1}! {2} strike{3} left", unit.DisplayName,
                        sim.GetUnit(unit.MesmerTargetId) != null ? sim.GetUnit(unit.MesmerTargetId).DisplayName : "?", unit.MesmerStrikesLeft, unit.MesmerStrikesLeft == 1 ? "" : "s");
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
                case Mode.PickDash: return "Click a point inside the ring to dash there, or click the unit to stand firm.";
                case Mode.ChooseAction:
                {
                    if (plan.Count > 0) return "Keep adding orders (Shift), or Confirm / Enter to send the plan.";
                    var followup = Current != null ? Followup(Current) : null;
                    if (followup != null) return "Press " + FollowupKeyName(Current, followup) + " again: " + followup.DisplayName + " on the same target. Hold Space to focus.";
                    return "Keys 1-0, - = [ ] abilities, G dash; hold one to Transcend it. Shift queues orders. Hold Space to focus (sheds strain).";
                }
                case Mode.Mesmer: return "Keep striking, or stop and give a normal order. Any other order ends the counter.";
                case Mode.Grapple: return "1 keep headbutting, click or 2 throw now (toward the cursor), F move while carrying them.";
                case Mode.PickDestination: return "Click a point inside the ring. Right-click or Esc to pick another unit.";
                default:
                    return releasing ? "Hold Space to keep charging (time runs slowly). Click to release it there."
                                     : "Shift-click to queue and keep planning. Right-click or Esc to go back.";
            }
        }

        /// <summary>Returns true if the panel's state changed (order sent or mode switched): stop drawing this frame's stale panel.</summary>
        // The orders panel spans the bottom of the screen; its buttons are tiles sized so one row usually holds them all.
        private const float PanelMaxWidth = 1500f;
        private const float ButtonWidth = 128f;    // the plan's Confirm / Undo / Clear
        private const float TileMinWidth = 112f, TileMaxWidth = 172f;
        private const float ButtonHeight = 48f;
        private const float ButtonGap = 6f;

        private static float PanelWidth { get { return Mathf.Min(UiScale.Width - 12f, PanelMaxWidth); } }

        /// <summary>GUI height the orders panel takes at the bottom of the screen (0 when hidden), for other overlays to stay clear of.</summary>
        public static float BottomReserved { get; private set; }

        // This frame's tile layout (see LayoutTiles).
        private float tileWidth = TileMinWidth, tileRowOffset;
        private int tilesPerRow = 1, tileCount;

        private static int TileCount(Unit unit) { return unit.Actions.Count + 3; }   // abilities and Dash, + Move, Focus, Auto

        /// <summary>Tile width and columns for this unit's buttons: as wide as fits (within bounds), rows centred.</summary>
        private void LayoutTiles(Unit unit)
        {
            float inner = PanelWidth - 20f;
            tileCount = TileCount(unit);
            tileWidth = Mathf.Clamp((inner - ButtonGap * (tileCount - 1)) / tileCount, TileMinWidth, TileMaxWidth);
            tilesPerRow = Mathf.Max(1, Mathf.FloorToInt((inner + ButtonGap) / (tileWidth + ButtonGap)));
            int cols = Mathf.Min(tilesPerRow, tileCount);
            tileRowOffset = (inner - (cols * tileWidth + (cols - 1) * ButtonGap)) / 2f;
        }

        /// <summary>Width of the widest single word (word wrap can't split one).</summary>
        private static float LongestWord(GUIStyle style, string text)
        {
            float w = 0f;
            foreach (var word in text.Split(' ')) w = Mathf.Max(w, style.CalcSize(new GUIContent(word)).x);
            return w;
        }

        private int ButtonRows(Unit unit) { return Mathf.CeilToInt(TileCount(unit) / (float)tilesPerRow); }

        private static GUIStyle tileName, tileDetail, tileKey;

        /// <summary>
        /// A tile button: its key in the top-left corner, the name (wrapped, shrunk if need be, never cut off) in the middle and a
        /// small line of costs or cooldown along the bottom. Returns whether it was clicked.
        /// </summary>
        private static bool Tile(Rect rect, string key, string name, string detail)
        {
            if (tileName == null)
            {
                tileName = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
                tileDetail = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 10, clipping = TextClipping.Overflow };
                tileKey = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontSize = 11, fontStyle = FontStyle.Bold };
            }
            bool clicked = GUI.Button(rect, GUIContent.none);
            bool on = GUI.enabled;
            var text = new Color(1f, 1f, 1f, on ? 1f : 0.45f);
            tileName.normal.textColor = text;
            tileKey.normal.textColor = new Color(1f, 0.85f, 0.45f, on ? 1f : 0.45f);
            tileDetail.normal.textColor = new Color(0.75f, 0.85f, 1f, on ? 0.95f : 0.45f);

            bool hasDetail = !string.IsNullOrEmpty(detail);
            var nameRect = new Rect(rect.x + 4f, rect.y + (key != null ? 9f : 2f), rect.width - 8f, rect.height - (key != null ? 9f : 2f) - (hasDetail ? 13f : 2f));
            var content = new GUIContent(name);
            int size = 13;
            tileName.fontSize = size;
            while (size > 9 && (tileName.CalcHeight(content, nameRect.width) > nameRect.height + 1f || LongestWord(tileName, name) > nameRect.width))
                tileName.fontSize = --size;
            GUI.Label(nameRect, content, tileName);
            if (key != null) GUI.Label(new Rect(rect.x + 5f, rect.y + 1f, 30f, 14f), key, tileKey);
            if (hasDetail) GUI.Label(new Rect(rect.x + 2f, rect.yMax - 15f, rect.width - 4f, 13f), detail, tileDetail);
            return clicked;
        }

        /// <summary>
        /// One ability button. Abilities that can be Transcended act on release (or Transcend when held, see UpdateHold), so
        /// their press is caught on mouse-down instead of the button's own click; a violet stripe marks them, and a bar
        /// fills along the bottom while one is held. Returns true if the panel changed.
        /// </summary>
        private bool AbilityButton(Unit unit, ActionDefinition def, Rect rect, string key, ActionDefinition followup)
        {
            bool usable; string why;
            string detail = ActionDetail(unit, def, out usable, out why);
            string name = def.DisplayName;
            SetActionTip(rect, def, usable ? null : "Can't use now: " + why);
            var e = Event.current;
            if (usable && def.CanTranscend && e.type == UnityEngine.EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                BeginHold(def, KeyCode.None);
                e.Use();
            }
            GUI.enabled = usable;
            if (def == followup)
            {
                detail = string.IsNullOrEmpty(detail) ? "again" : "again · " + detail;
                // The follow-up its key would re-cast: a gold frame and tint so it stands out.
                Fill(new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f), FollowupColor);
                GUI.backgroundColor = FollowupColor;
            }
            if (def == holdDef) GUI.backgroundColor = Color.Lerp(Color.white, TranscendColor, 0.4f + 0.6f * HoldProgress);
            bool clicked = Tile(rect, key, name, detail);
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            if (def.CanTranscend)
            {
                Fill(new Rect(rect.x + 1f, rect.y + 3f, 3f, rect.height - 6f), new Color(TranscendColor.r, TranscendColor.g, TranscendColor.b, usable ? 0.9f : 0.35f));
                if (def == holdDef && !holdFired) Fill(new Rect(rect.x + 2f, rect.yMax - 5f, (rect.width - 4f) * HoldProgress, 3f), TranscendColor);
                return false;   // pressed through the hold
            }
            if (clicked) { ChooseAction(unit, def); return true; }   // the button still lets you pick a new target; its key repeats the last
            return false;
        }

        private static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        // ------------------------------------------------------------------ Transcend overlay

        private static Texture2D vignette;

        /// <summary>A soft black vignette: clear in the middle, dark at the edges.</summary>
        private static Texture2D Vignette()
        {
            if (vignette != null) return vignette;
            const int n = 128;
            vignette = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.41421f;
                    px[y * n + x] = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1f, d)));
                }
            vignette.SetPixels(px);
            vignette.Apply();
            return vignette;
        }

        /// <summary>
        /// Transcend feedback: the screen darkens (a violet-tinted veil plus a vignette) as an ability is held, flashes when
        /// the Transcend takes, and stays dark with a banner while the Transcended ability is being aimed.
        /// </summary>
        private void DrawTranscendOverlay(Unit unit)
        {
            if (Event.current.type != UnityEngine.EventType.Repaint) return;
            float hold = HoldProgress;
            float k = Transcending ? 1f : hold * hold;
            float flash = Mathf.Clamp01((transcendFlashUntil - Time.unscaledTime) / 0.3f);
            if (k <= 0f && flash <= 0f) return;
            var screen = new Rect(0f, 0f, UiScale.Width, UiScale.Height);
            Fill(screen, new Color(0.08f, 0.02f, 0.15f, 0.3f * k));
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.85f * k);
            GUI.DrawTexture(screen, Vignette(), ScaleMode.StretchToFill);
            GUI.color = old;
            if (flash > 0f) Fill(screen, new Color(TranscendColor.r, TranscendColor.g, TranscendColor.b, 0.35f * flash));

            var def = Transcending ? selected : holdDef;
            if (def == null && !holdFocus) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            string text = Transcending
                ? string.Format("TRANSCENDED {0}   (+{1:0} strain)", def.DisplayName.ToUpperInvariant(), def.TranscendStrain)
                : string.Format("Transcending {0}...", def != null ? def.DisplayName : "Focus");
            var r = new Rect(0f, 150f, UiScale.Width, 34f);
            style.normal.textColor = new Color(0f, 0f, 0f, 0.8f * Mathf.Max(k, 0.3f));
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            style.normal.textColor = new Color(TranscendColor.r, TranscendColor.g, TranscendColor.b, Mathf.Max(k, 0.3f));
            GUI.Label(r, text, style);
        }

        private Rect ButtonRect(int index, float x, float y)
        {
            int row = index / tilesPerRow, col = index % tilesPerRow;
            return new Rect(x + tileRowOffset + col * (tileWidth + ButtonGap), y + row * (ButtonHeight + ButtonGap), tileWidth, ButtonHeight);
        }

        /// <summary>A tile's small line: energy and strain costs, or the time left on cooldown.</summary>
        private string ActionDetail(Unit unit, ActionDefinition def, out bool usable, out string why)
        {
            usable = true; why = null;
            var parts = new List<string>();
            float cost = sim.EnergyCostOf(unit, def);
            if (cost > 0f) parts.Add(string.Format("{0:0} energy", cost));
            float strain = sim.StrainCostOf(unit, def, unit.LastActionTargetId);   // follow-up discounts are against the last target
            if (Mathf.Abs(strain) > 0.5f) parts.Add(string.Format("{0:+0;-0} strain", strain));
            string detail = string.Join(" · ", parts.ToArray());
            if (plan.Count > 0) return detail;   // a later order's resources are only known when it starts
            usable = sim.CanAfford(unit, def, out why);
            float readyAt;
            if (!usable && why == "cooldown" && unit.CooldownReadyAt.TryGetValue(def.Id, out readyAt))
                detail = string.Format("cooldown {0:0.0}s", readyAt - sim.Now);
            return detail;
        }

        // ------------------------------------------------------------------ ability popup

        /// <summary>One boxed group of the ability popup: a heading over a row (or rows) of label / value cells, plus notes.</summary>
        private class TipSection
        {
            public string Title;
            public readonly List<KeyValuePair<string, string>> Cells = new List<KeyValuePair<string, string>>();
            public readonly List<string> Notes = new List<string>();
            public void Add(string label, string value) { Cells.Add(new KeyValuePair<string, string>(label, value)); }
        }

        private static string Secs(float s) { return s <= 0f ? "instant" : string.Format("{0:0.##}s", s); }

        /// <summary>What the ability is aimed at, in a word or two (no range: the targeting rings show that).</summary>
        private static string TargetKind(ActionDefinition def)
        {
            switch (def.Targeting)
            {
                case TargetType.Self: return def.Shape == AreaShape.Circle ? "Around you" : "Self";
                case TargetType.Enemy: return def.IsRanged ? "Ranged enemy" : "Melee enemy";
                case TargetType.Ally: return def.CannotTargetSelf ? "Another ally" : "Ally";
                case TargetType.AnyUnit: return def.CannotTargetSelf ? "Ally or enemy" : "Any unit";
                case TargetType.Position: return def.BehaviorId == "dash" ? "Point" : def.Shape == AreaShape.Line ? "Line" : def.Shape == AreaShape.Circle ? "Area" : "Point";
                default: return "None";
            }
        }

        private static string Mult(float m) { return "x" + m.ToString("0.##"); }

        /// <summary>A status the ability grants or inflicts, as its own section: how long, and what it changes.</summary>
        /// <param name="lasts">Overrides the "Lasts" cell (a dome's statuses last while inside it).</param>
        private static TipSection StatusSection(string who, StatusSpec s, bool duringWindup = false, string lasts = null)
        {
            string whom = s.AppliesTo == StatusTarget.Allies ? " (ally)" : s.AppliesTo == StatusTarget.Enemies ? " (enemy)" : "";
            var sec = new TipSection { Title = string.Format("{0}{1}: {2}", who, whom, StatusDisplay.NameOf(s.Id).ToUpperInvariant()) };
            sec.Add("Lasts", lasts ?? (duringWindup ? "the windup" : Secs(s.Duration)));
            if (s.Modifiers != null) foreach (var m in s.Modifiers) sec.Add(StatText.Name(m.Stat), StatText.Change(m));
            if (s.ActionModifiers != null) foreach (var m in s.ActionModifiers) sec.Add(StatText.Label(m, id => id.Replace('_', ' ')), StatText.Change(m));
            var flagCells = new List<KeyValuePair<string, string>>();
            StatText.Describe(s.Flags, flagCells, sec.Notes);
            foreach (var c in flagCells) sec.Add(c.Key, c.Value);
            if (s.ForbidsActions != null && s.ForbidsActions.Count > 0) sec.Notes.Add("Can't use meanwhile: " + string.Join(", ", s.ForbidsActions.Select(id => id.Replace('_', ' ')).ToArray()) + ".");
            return sec;
        }

        /// <summary>
        /// The ability popup's numbers, grouped: how it is used (aim, timing, cooldown), what it does, what it costs, then a
        /// section per status it grants or inflicts. Ranges are left out: the targeting rings show them. With 'who', its numbers are that unit's.
        /// </summary>
        private List<TipSection> ActionStats(ActionDefinition def, Unit who = null)
        {
            // The numbers of the unit being ordered (its upgrades and statuses in), or the ability's own without one.
            System.Func<ActionStat, float> V = s => who != null ? who.ActionStat(def, s) : Stats.Base(def, s);
            var sections = new List<TipSection>();
            var use = new TipSection { Title = "USE" };
            use.Add("Target", TargetKind(def));
            if (def.Shape == AreaShape.Circle && V(ActionStat.AreaRadius) > 0f) use.Add("Radius", string.Format("{0:0.#}m", V(ActionStat.AreaRadius)));
            if (def.Shape == AreaShape.Line && def.Targeting == TargetType.Position) use.Add("Width", string.Format("{0:0.#}m", def.LineWidth));
            if (def.Shape == AreaShape.Circle && def.AreaHeight > 0f) use.Add("Dome height", string.Format("{0:0.#}m", def.AreaHeight));
            if (def.IsCharge) use.Add("Charge", "up to " + Secs(def.MaxChargeSeconds));
            if (def.ChannelSeconds > 0f) use.Add("Channel", "up to " + Secs(def.ChannelSeconds));
            else if (def.AuraSeconds > 0f) use.Add("Dome lasts", Secs(def.AuraSeconds));
            use.Add("Windup", Secs(V(ActionStat.Windup)));
            use.Add("Recovery", Secs(V(ActionStat.Recovery)));
            if (V(ActionStat.Cooldown) > 0f) use.Add("Cooldown", Secs(V(ActionStat.Cooldown)));
            sections.Add(use);

            // What it does: its behavior words it (ActionBehavior.Describe), so special rules need no case here.
            var behavior = ActionBehaviors.Get(def.BehaviorId);
            var tip = new AbilityTip(def, who, sim);
            behavior.Describe(tip);
            var effect = new TipSection { Title = "EFFECT" };
            foreach (var c in tip.Effect) effect.Add(c.Key, c.Value);
            if (effect.Cells.Count > 0) sections.Add(effect);

            var cost = new TipSection { Title = "COST" };
            if (V(ActionStat.StrainOnSelf) > 0f) cost.Add("Strain", "+" + V(ActionStat.StrainOnSelf).ToString("0"));
            if (V(ActionStat.StrainOnSelf) < 0f) cost.Add("Strain", "sheds " + (-V(ActionStat.StrainOnSelf)).ToString("0"));
            if (V(ActionStat.EnergyCost) > 0f) cost.Add("Energy", V(ActionStat.EnergyCost).ToString("0"));
            if (def.EnergyGain > 0f) cost.Add("Energy gain", "+" + def.EnergyGain.ToString("0"));
            if (def.IsCharge && def.ChargeStrainPerSecond > 0f) cost.Add("Charging", string.Format("+{0:0} strain/s", def.ChargeStrainPerSecond));
            if (V(ActionStat.HealthCost) > 0f) cost.Add("Health", "-" + V(ActionStat.HealthCost).ToString("0"));
            if (def.CanTranscend) cost.Add("Transcend", string.Format("+{0:0} strain", def.TranscendStrain));
            if (cost.Cells.Count > 0) sections.Add(cost);

            foreach (var s in def.SelfStatuses) if (s != null) sections.Add(StatusSection("YOU", s));
            foreach (var s in def.TargetStatusesOnHit) if (s != null) sections.Add(StatusSection(tip.TargetStatusesTitle, s));
            if (def.TargetStatusesDuringWindup != null)
                foreach (var s in def.TargetStatusesDuringWindup) if (s != null) sections.Add(StatusSection("TARGET", s, true));
            if (def.SelfStatusesDuringWindup != null)
                foreach (var s in def.SelfStatusesDuringWindup) if (s != null) sections.Add(StatusSection("YOU", s, true));
            if (def.AuraStatuses != null)
                foreach (var s in def.AuraStatuses) if (s != null) sections.Add(StatusSection("IN DOME", s, false, "while inside"));
            return sections;
        }

        // Hover tooltip for the button under the mouse, rebuilt every OnGUI event. Abilities fill tipSections; the plain
        // buttons (Move, Focus...) give a line of tipStats instead.
        private string tipTitle, tipBody, tipStats, tipWarning;
        private Unit tipUnit;
        private List<TipSection> tipSections;
        private Rect tipAnchor;

        private void SetTip(Rect button, string title, string body, string stats = null, string warning = null)
        {
            if (!button.Contains(Event.current.mousePosition)) return;
            tipTitle = title; tipBody = body; tipStats = stats; tipSections = null; tipWarning = warning; tipAnchor = button;
        }

        private void SetActionTip(Rect button, ActionDefinition def, string warning)
        {
            if (!button.Contains(Event.current.mousePosition)) return;
            SetTip(button, def.DisplayName, string.IsNullOrEmpty(def.Description) ? null : def.Description, null, warning);
            tipSections = ActionStats(def, tipUnit);
        }

        private void ClearTip() { tipTitle = null; }

        private const float TipWidth = 420f, TipPad = 9f, CellHeight = 34f, CellGap = 4f, SectionGap = 7f, SectionTitleHeight = 15f;
        private const int CellsPerRow = 3;
        private static GUIStyle tipTitleStyle, tipBodyStyle, tipStatsStyle, tipWarnStyle, tipSectionStyle, tipCellLabel, tipCellValue, tipNoteStyle;

        private static void TipStyles()
        {
            if (tipTitleStyle != null) return;
            tipTitleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true, fontSize = 15 };
            tipBodyStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            tipStatsStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12 };
            tipStatsStyle.normal.textColor = new Color(0.75f, 0.85f, 1f);
            tipWarnStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            tipWarnStyle.normal.textColor = new Color(1f, 0.75f, 0.4f);
            tipSectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold };
            tipSectionStyle.normal.textColor = new Color(1f, 0.85f, 0.45f, 0.9f);
            tipCellLabel = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip };
            tipCellLabel.normal.textColor = new Color(0.7f, 0.75f, 0.85f);
            tipCellValue = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerLeft, clipping = TextClipping.Clip };
            tipCellValue.normal.textColor = Color.white;
            tipNoteStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12 };
            tipNoteStyle.normal.textColor = new Color(0.85f, 0.88f, 0.95f);
        }

        private static string Bullets(List<string> notes) { return string.Join("\n", notes.Select(n => "• " + n).ToArray()); }

        private static float SectionHeight(TipSection s, float inner)
        {
            int rows = Mathf.CeilToInt(s.Cells.Count / (float)CellsPerRow);
            float h = SectionTitleHeight + rows * (CellHeight + CellGap);
            if (s.Notes.Count > 0) h += tipNoteStyle.CalcHeight(new GUIContent(Bullets(s.Notes)), inner - 8f) + 2f;
            return h;
        }

        /// <summary>Floating box just above the hovered button (or above the panel), kept on screen.</summary>
        private void DrawTip()
        {
            if (string.IsNullOrEmpty(tipTitle)) return;
            TipStyles();
            float inner = TipWidth - TipPad * 2f;
            float hTitle = tipTitleStyle.CalcHeight(new GUIContent(tipTitle), inner);
            float hBody = string.IsNullOrEmpty(tipBody) ? 0f : tipBodyStyle.CalcHeight(new GUIContent(tipBody), inner) + 4f;
            float hStats = string.IsNullOrEmpty(tipStats) ? 0f : tipStatsStyle.CalcHeight(new GUIContent(tipStats), inner) + 2f;
            float hWarn = string.IsNullOrEmpty(tipWarning) ? 0f : tipWarnStyle.CalcHeight(new GUIContent(tipWarning), inner) + 2f;
            float hSections = 0f;
            if (tipSections != null) foreach (var s in tipSections) hSections += SectionHeight(s, inner) + SectionGap;
            float height = TipPad * 2f + hTitle + hBody + hStats + hWarn + hSections + 2f;

            float x = Mathf.Clamp(tipAnchor.x, 4f, UiScale.Width - TipWidth - 4f);
            float y = Mathf.Max(4f, Mathf.Min(tipAnchor.y, panelRect.y) - height - 6f);
            var box = new Rect(x, y, TipWidth, height);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.97f);
            GUI.Box(box, GUIContent.none);
            GUI.Box(box, GUIContent.none);   // doubled for a more opaque background with the default skin
            GUI.color = old;

            float left = x + TipPad, cy = y + TipPad;
            GUI.Label(new Rect(left, cy, inner, hTitle), tipTitle, tipTitleStyle); cy += hTitle;
            if (hBody > 0f) { GUI.Label(new Rect(left, cy, inner, hBody), tipBody, tipBodyStyle); cy += hBody; }
            if (tipSections != null)
            {
                float cellWidth = (inner - CellGap * (CellsPerRow - 1)) / CellsPerRow;
                foreach (var s in tipSections)
                {
                    // Each section: a faint panel with its heading, then its cells in rows of CellsPerRow, then any notes.
                    float sh = SectionHeight(s, inner);
                    Fill(new Rect(left - 3f, cy - 1f, inner + 6f, sh + 2f), new Color(1f, 1f, 1f, 0.04f));
                    Fill(new Rect(left - 3f, cy - 1f, 2f, sh + 2f), new Color(1f, 0.85f, 0.45f, 0.6f));
                    GUI.Label(new Rect(left + 2f, cy, inner, SectionTitleHeight), s.Title, tipSectionStyle);
                    float ry = cy + SectionTitleHeight;
                    for (int i = 0; i < s.Cells.Count; i++)
                    {
                        var cell = new Rect(left + (i % CellsPerRow) * (cellWidth + CellGap), ry + (i / CellsPerRow) * (CellHeight + CellGap), cellWidth, CellHeight);
                        Fill(cell, new Color(0f, 0f, 0f, 0.35f));
                        GUI.Label(new Rect(cell.x + 5f, cell.y + 2f, cell.width - 8f, 14f), s.Cells[i].Key, tipCellLabel);
                        GUI.Label(new Rect(cell.x + 5f, cell.y + 12f, cell.width - 8f, CellHeight - 13f), s.Cells[i].Value, tipCellValue);
                    }
                    if (s.Notes.Count > 0)
                    {
                        float ny = ry + Mathf.CeilToInt(s.Cells.Count / (float)CellsPerRow) * (CellHeight + CellGap);
                        string notes = Bullets(s.Notes);
                        GUI.Label(new Rect(left + 4f, ny, inner - 8f, tipNoteStyle.CalcHeight(new GUIContent(notes), inner - 8f)), notes, tipNoteStyle);
                    }
                    cy += sh + SectionGap;
                }
            }
            if (hStats > 0f) { GUI.Label(new Rect(left, cy, inner, hStats), tipStats, tipStatsStyle); cy += hStats; }
            if (hWarn > 0f) GUI.Label(new Rect(left, cy, inner, hWarn), tipWarning, tipWarnStyle);
        }

        private bool DrawChooseAction(Unit unit, float x, ref float y, List<Unit> stoppable)
        {
            const float bw = ButtonWidth, gap = ButtonGap;
            int index = 0;
            var followup = Followup(unit);
            foreach (var def in Slots(unit))
            {
                string slotKey = SlotKey(index);
                if (AbilityButton(unit, def, ButtonRect(index++, x, y), slotKey, followup)) return true;
            }
            bool rooted = plan.Count == 0 && unit.IsRooted;
            var moveRect = ButtonRect(index++, x, y);
            SetTip(moveRect, "Move", "Walk to any point on the navmesh. Shift-click to queue more orders after it.",
                string.Format("speed {0:0.#} m/s", unit.EffectiveMoveSpeed()), rooted ? "Can't move: immobilized" : null);
            GUI.enabled = !rooted;
            if (Tile(moveRect, "F", "Move", rooted ? "rooted" : string.Format("{0:0.#} m/s", unit.EffectiveMoveSpeed())))
            {
                GUI.enabled = true; mode = Mode.PickMove; message = null; return true;
            }
            GUI.enabled = true;
            var dash = DashOf(unit);
            if (dash != null && AbilityButton(unit, dash, ButtonRect(index++, x, y), "G", followup)) return true;
            var waitRect = ButtonRect(index++, x, y);
            SetTip(waitRect, "Focus", string.Format("Stand still and focus for {0:0.0}s, then ask for orders again. After {1:0.##}s of focus in a row " +
                "(back-to-back Focus orders, or hold Space to keep focusing with time running slowly) strain drains fast until you do anything else. A hit restarts the count. " +
                "Transcend (hold the button): focus in one order until strain starts draining, however many hits restart the count.",
                WaitSeconds, sim.FocusDelay), string.Format("sheds {0:0} strain/s once focused  ·  hold to Transcend{1}", sim.FocusStrainPerSecond,
                sim.TranscendFocusStrain > 0f ? string.Format(" (+{0:0} strain)", sim.TranscendFocusStrain) : ""));
            // Pressed through the hold, like a Transcend-able ability (UpdateHold): a quick click is a plain Focus.
            var fe = Event.current;
            if (fe.type == UnityEngine.EventType.MouseDown && fe.button == 0 && waitRect.Contains(fe.mousePosition))
            {
                holdDef = null; holdFocus = true; holdStart = Time.unscaledTime; holdFired = false;
                fe.Use();
            }
            if (holdFocus) GUI.backgroundColor = Color.Lerp(Color.white, TranscendColor, 0.4f + 0.6f * HoldProgress);
            Tile(waitRect, "Space", "Focus", string.Format("{0:0.0}s · hold: until focused", WaitSeconds));
            GUI.backgroundColor = Color.white;
            Fill(new Rect(waitRect.x + 1f, waitRect.y + 3f, 3f, waitRect.height - 6f), TranscendColor);
            if (holdFocus) Fill(new Rect(waitRect.x + 2f, waitRect.yMax - 5f, (waitRect.width - 4f) * HoldProgress, 3f), TranscendColor);
            var autoRect = ButtonRect(index++, x, y);
            SetTip(autoRect, "Auto", "Let the AI pick this unit's order: its first usable attack on the nearest enemy, or close the distance.",
                null, plan.Count > 0 ? "Clear the plan to use Auto." : null);
            GUI.enabled = plan.Count == 0;
            if (Tile(autoRect, null, "Auto", "AI picks")) { GUI.enabled = true; AutoDecide(pending[0]); Next(); return true; }
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

        /// <summary>Mesmerize counter: another rapid strike at the attacker, or stop and give a normal order.</summary>
        private bool DrawMesmer(Unit unit, float x, ref float y)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = FollowupColor;
            bool strike = GUI.Button(new Rect(x, y, 220f, 28f), string.Format("1  Strike ({0} left)", unit.MesmerStrikesLeft));
            GUI.backgroundColor = old;
            bool stop = GUI.Button(new Rect(x + 230f, y, 160f, 28f), "2  Stop");
            y += 34f;
            if (strike) { MesmerStrike(unit); return true; }
            if (stop) { MesmerStop(unit); return true; }
            return false;
        }

        private void MesmerStrike(Unit unit)
        {
            sim.SubmitCommand(UnitCommand.UseAction(unit.Id, unit.MesmerStrike, unit.MesmerTargetId));
            Next();
        }

        private void MesmerStop(Unit unit)
        {
            sim.CancelMesmer(unit.Id);
            mode = Mode.ChooseAction;
            message = "Counter stopped. Orders?";
        }

        private string HeldName(Unit unit)
        {
            var held = unit.Grapple != null ? sim.GetUnit(unit.Grapple.TargetId) : null;
            return held != null ? held.DisplayName : "?";
        }

        /// <summary>Holding someone (Subjugate): keep headbutting, throw now, or move while carrying them.</summary>
        private bool DrawGrapple(Unit unit, float x, ref float y)
        {
            var holdRect = new Rect(x, y, 220f, 28f);
            var throwRect = new Rect(x + 230f, y, 170f, 28f);
            var moveRect = new Rect(x + 410f, y, 200f, 28f);
            SetTip(holdRect, "Keep headbutting", "Stand and keep hold: headbutt again, then choose again.");
            bool awaiting = sim.AwaitingThrow(unit);
            SetTip(throwRect, "Throw now", "Wind up the throw and hurl them toward the cursor (or click the ground to throw that way).");
            SetTip(moveRect, "Move", "Walk somewhere, carrying them. You keep headbutting on the way.");
            var old = GUI.backgroundColor;
            GUI.backgroundColor = FollowupColor;
            GUI.enabled = !awaiting;   // the grab is up: throwing is all that's left
            bool hold = GUI.Button(holdRect, "1  Keep headbutting");
            GUI.backgroundColor = old;
            GUI.enabled = true;
            bool toss = GUI.Button(throwRect, "2  Throw");
            GUI.enabled = !awaiting;
            bool move = GUI.Button(moveRect, "F  Move (carrying)");
            GUI.enabled = true;
            y += 34f;
            if (hold) { GrappleHold(unit); return true; }
            if (toss) { GrappleThrow(unit); return true; }
            if (move) { mode = Mode.PickMove; message = null; return true; }
            return false;
        }

        private bool DrawPicking(Unit unit, float x, ref float y)
        {
            if (releasing) GUI.Label(new Rect(x, y, panelRect.width - 20f, 28f), "Can't be cancelled. Any damage breaks the charge.");
            else if (GUI.Button(new Rect(x, y, 100f, 28f), "Back")) { Back(); return true; }
            y += 34f;
            if (plan.Count > 0)
                GUI.Label(new Rect(x, y, panelRect.width - 20f, 20f),
                    "Plan: " + string.Join("   ", plan.Select((p, i) => (i + 1) + ". " + p.Label).ToArray()));
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
