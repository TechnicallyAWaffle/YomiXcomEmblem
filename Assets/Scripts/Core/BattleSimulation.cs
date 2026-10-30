using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Headless, deterministic, fixed-tick battle simulation. Pure C#.
    ///
    /// Loop: RunUntilDecision() advances ticks until a player unit needs input
    /// (or the battle ends), appending events to Timeline. The caller plays that
    /// stretch back (slow motion), collects commands, submits them, and calls
    /// RunUntilDecision() again. Replays just re-play the finished Timeline.
    ///
    /// Actions that come due on the same tick resolve simultaneously: all effects
    /// are computed against a snapshot of dodge/block taken before any of them apply.
    /// </summary>
    public class BattleSimulation
    {
        public const float TickLength = 0.025f;
        public const float UnravelStunSeconds = 4f;
        public const float DashDuration = 0.2f;
        public const float DisplaceDuration = 0.3f;
        public const float StandingGap = 0.2f;     // kept between bodies where units stop (see FreeSpotNear)
        public const float SettleDuration = 0.2f;  // shuffle out of an overlap (see SettleOutOfBodies)
        public const int StrainRecoveryTicks = 10;      // strain recovers in 0.25s steps (one StrainChanged event each)
        public const float ChaseRepathSeconds = 0.3f;   // a melee chase re-plans its path toward the target this often
        /// <summary>Longest a melee chase runs, or a queued ranged ability waits, for its out-of-range target before giving up.</summary>
        public float MaxRangeWait = 10f;
        public const float LightBlockBreak = 0.10f;
        public const float HeavyBlockBreak = 0.25f;
        public const float RangeTolerance = 1.15f;

        public readonly BattleState State;
        public readonly BattleTimeline Timeline;
        public readonly TriggerBus Triggers = new TriggerBus();
        public IVisibilityService Visibility;
        public IPathService Paths;
        public IUnitAI EnemyAI;

        public BattleOutcome Outcome { get; private set; }
        public float Now { get { return tick * TickLength; } }

        private long tick;
        private readonly System.Random rng;
        private readonly Dictionary<string, Queue<UnitCommand>> queues = new Dictionary<string, Queue<UnitCommand>>();
        private bool playerTurnOpen;   // true while the sim is paused on a player unit's turn (move-cancel window)
        private int deployCounter;
        private int trapCounter;

        /// <summary>Strain a unit gains for having its walk cancelled: base + perSecond * seconds of walking that were left.</summary>
        public float MoveCancelStrainBase = 2f;
        public float MoveCancelStrainPerSecond = 6f;
        private readonly List<DecisionRequest> pendingDashes = new List<DecisionRequest>();
        private readonly List<ActionInstance> freeActions = new List<ActionInstance>();

        private struct CombatSnapshot { public float Dodge; public float Block; }

        private static readonly Vector3[] Directions8 =
        {
            new Vector3(0, 0, 1), new Vector3(0.7071f, 0, 0.7071f), new Vector3(1, 0, 0), new Vector3(0.7071f, 0, -0.7071f),
            new Vector3(0, 0, -1), new Vector3(-0.7071f, 0, -0.7071f), new Vector3(-1, 0, 0), new Vector3(-0.7071f, 0, 0.7071f),
        };

        public BattleSimulation(BattleState start, int seed, IStaticOcclusion staticOcclusion = null,
                                IPathService paths = null, IUnitAI enemyAI = null)
        {
            Timeline = new BattleTimeline { InitialState = start.Clone() };
            State = start.Clone();
            rng = new System.Random(seed);
            Visibility = new SimVisibility(staticOcclusion ?? new NoStaticOcclusion(), State);
            Paths = paths ?? new StraightLinePathService();
            EnemyAI = enemyAI ?? new BasicEnemyAI();
            Outcome = BattleOutcome.InProgress;

            // Reactions, stances and passives subscribe to Triggers once per battle.
            var attached = new HashSet<ActionBehavior>();
            foreach (var u in State.Units)
                foreach (var def in u.Actions)
                {
                    AttachBehavior(def, attached);
                    AttachBehavior(def.LinkedAction, attached);   // Take Aim's Snipe, Retaliate's shot
                }
            Passives.Attach(this);
        }

        private void AttachBehavior(ActionDefinition def, HashSet<ActionBehavior> attached)
        {
            if (def == null) return;
            var b = ActionBehaviors.Get(def.BehaviorId);
            if (attached.Add(b)) b.Attach(this);
        }

        // ------------------------------------------------------------------ public API

        public Unit GetUnit(string id) { return id == null ? null : State.GetUnit(id); }

        /// <summary>Appends an order to the unit's queue. Works for busy units too: queued orders start, in order,
        /// the moment the unit is ready. A queued order that turns out invalid clears the rest of that queue.</summary>
        public void SubmitCommand(UnitCommand cmd)
        {
            Queue<UnitCommand> q;
            if (!queues.TryGetValue(cmd.UnitId, out q)) queues[cmd.UnitId] = q = new Queue<UnitCommand>();
            q.Enqueue(cmd);
        }

        public void ClearQueue(string unitId) { queues.Remove(unitId); }

        public int QueuedCount(string unitId)
        {
            Queue<UnitCommand> q;
            return queues.TryGetValue(unitId, out q) ? q.Count : 0;
        }

        /// <summary>
        /// Can this player unit's walk be cancelled right now, and what would it cost? Allowed only while the sim is
        /// paused on a friendly unit's turn. The cost is strain on the cancelled unit, growing with the walking time left.
        /// </summary>
        public bool CanCancelMove(string unitId, out float strainCost, out string reason)
        {
            strainCost = 0f; reason = null;
            var u = GetUnit(unitId);
            if (u == null || !u.IsAlive || u.Team != Team.Player) { reason = "invalid unit"; return false; }
            if (u.Phase != UnitPhase.Moving || u.Move == null) { reason = "not moving"; return false; }
            if (!playerTurnOpen) { reason = "only on a friendly unit's turn"; return false; }
            strainCost = MoveCancelStrainBase + MoveCancelStrainPerSecond * RemainingMoveSeconds(u);
            return true;
        }

        /// <summary>Stops the unit where it stands (it becomes ready for new orders) and charges the strain. Clears its queue by default.</summary>
        public bool CancelMove(string unitId, bool clearQueue = true)
        {
            float cost; string reason;
            if (!CanCancelMove(unitId, out cost, out reason)) return false;
            var u = GetUnit(unitId);
            AddStrain(u, cost);
            if (clearQueue) queues.Remove(unitId);
            EndMove(u);
            return true;
        }

        private float RemainingMoveSeconds(Unit u)
        {
            float length = 0f;
            Vector3 from = u.Position;
            for (int i = u.Move.NextIndex; i < u.Move.Path.Count; i++)
            {
                length += Vector3.Distance(from, u.Move.Path[i]);
                from = u.Move.Path[i];
            }
            return length / Mathf.Max(0.01f, u.Move.Speed);
        }

        public void SubmitDodgeDash(string unitId, int direction8)
        {
            int idx = pendingDashes.FindIndex(d => d.UnitId == unitId);
            if (idx < 0) return;
            pendingDashes.RemoveAt(idx);
            var unit = GetUnit(unitId);
            if (unit != null && unit.IsAlive) ExecuteDash(unit, direction8);
        }

        /// <summary>Dash toward any point: stops there if it is within dash range, otherwise goes the full range that way.</summary>
        public void SubmitDodgeDashTo(string unitId, Vector3 point)
        {
            int idx = pendingDashes.FindIndex(d => d.UnitId == unitId);
            if (idx < 0) return;
            pendingDashes.RemoveAt(idx);
            var unit = GetUnit(unitId);
            if (unit == null || !unit.IsAlive) return;
            DodgeTo(unit, DashDestination(unit, point));
        }

        /// <summary>The Dash ability: slide toward 'point' like a dodge (same speed and reach). Doesn't touch a windup.</summary>
        public void DashUnitTo(Unit unit, Vector3 point)
        {
            if (!unit.IsAlive || unit.IsRooted) return;
            Vector3 to = DashDestination(unit, point);
            if ((to - unit.Position).sqrMagnitude > 1e-4f) Relocate(unit, to, DashDuration, false);
        }

        /// <summary>Where a dash toward 'point' lands: capped at the unit's dash distance and clamped to walkable space.</summary>
        public Vector3 DashDestination(Unit unit, Vector3 point)
        {
            Vector3 d = point - unit.Position; d.y = 0f;
            float max = unit.DashDistanceNow();
            if (d.magnitude > max) d = d.normalized * max;
            return Paths.ClampDash(unit.Position, unit.Position + d);
        }

        public Unit NearestEnemy(Unit unit)
        {
            Unit best = null;
            float bestDist = float.MaxValue;
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u.Team == unit.Team || !IsTargetableBy(u, unit)) continue;
                float d = Vector3.Distance(unit.Position, u.Position);
                if (d < bestDist) { bestDist = d; best = u; }
            }
            return best;
        }

        /// <summary>Index 0-7 of the dash direction that moves 'unit' furthest from 'threat'.</summary>
        public int BestDashDirection(Unit unit, Unit threat)
        {
            Vector3 away = (unit.Position - threat.Position);
            away.y = 0f;
            away = away.normalized;
            int best = 0;
            float bestDot = float.MinValue;
            for (int i = 0; i < Directions8.Length; i++)
            {
                float dot = Vector3.Dot(away, Directions8[i]);
                if (dot > bestDot) { bestDot = dot; best = i; }
            }
            return best;
        }

        /// <summary>Resources (cooldown, energy, required status) plus target validity, range and (for ranged) line of sight.
        /// checkResources = false previews a later queued order, whose resources are only known when it starts.</summary>
        public bool CanUse(Unit actor, ActionDefinition def, Unit target, out string reason, bool checkResources = true)
        {
            reason = null;
            if (checkResources && !CanAfford(actor, def, out reason, target != null ? target.Id : null)) return false;
            if (def.Targeting == TargetType.Enemy)
            {
                if (target == null || !target.IsAlive || target.Team == actor.Team) { reason = "invalid target"; return false; }
                if (!IsTargetableBy(target, actor)) { reason = "target hidden"; return false; }
                if (Vector3.Distance(actor.Position, target.Position) > def.Range) { reason = "out of range"; return false; }
                if (def.IsRanged && !def.PiercesTerrain && !Visibility.HasLineOfSight(actor, target)) { reason = "no line of sight"; return false; }
            }
            else if (def.Targeting == TargetType.Ally)
            {
                if (target == null || !target.IsAlive || target.Team != actor.Team) { reason = "invalid target"; return false; }
                if (Vector3.Distance(actor.Position, target.Position) > def.Range) { reason = "out of range"; return false; }
            }
            return true;
        }

        /// <summary>Cooldown, energy and required-status checks only (no target). Drives greyed-out ability buttons.</summary>
        public bool CanAfford(Unit actor, ActionDefinition def, out string reason, string targetId = null)
        {
            reason = null;
            float readyAt;
            if (actor.CooldownReadyAt.TryGetValue(def.Id, out readyAt) && readyAt > Now) { reason = "cooldown"; return false; }
            if (!string.IsNullOrEmpty(def.RequiredStatus) && !actor.HasStatus(def.RequiredStatus)) { reason = "needs " + def.RequiredStatus; return false; }
            if (EnergyCostOf(actor, def) > actor.Energy + 1e-4f) { reason = "not enough energy"; return false; }
            float strain = StrainCostOf(actor, def, targetId);
            if (strain > 0f && actor.Strain + strain > actor.MaxStrain + 1e-4f && !ActionBehaviors.Get(def.BehaviorId).AllowsStrainOverflow) { reason = "too much strain"; return false; }
            return true;
        }

        public float EnergyCostOf(Unit actor, ActionDefinition def) { return ActionBehaviors.Get(def.BehaviorId).EnergyCost(this, actor, def); }
        public float StrainCostOf(Unit actor, ActionDefinition def, string targetId = null) { return ActionBehaviors.Get(def.BehaviorId).StrainCost(this, actor, def, targetId); }

        /// <summary>Hidden (untargetable) units can't be targeted or hit by the other team.</summary>
        public bool IsTargetableBy(Unit target, Unit actor) { return target.Team == actor.Team || !target.IsHidden; }

        /// <summary>Range, sight and targetability only: can this action land on that unit from where the actor stands?
        /// For reactions that skip cooldown/cost (Take Aim's snipe, Retaliate's shot, Final Cut's follow-up).</summary>
        public bool InReach(Unit actor, ActionDefinition def, Unit target)
        {
            string reason;
            return CanUse(actor, def, target, out reason, false);
        }

        public IEnumerable<Unit> EnemiesOf(Unit unit)
        {
            foreach (var u in State.Units)
                if (u.IsAlive && u.Team != unit.Team && IsTargetableBy(u, unit)) yield return u;
        }

        /// <summary>Enemies of the actor caught in the definition's area (see AreaAttackBehavior).</summary>
        public List<Unit> UnitsInArea(Unit actor, ActionDefinition def, Vector3 targetPos)
        {
            var hits = new List<Unit>();
            Vector3 origin = actor.Position;
            Vector3 dir = targetPos - origin; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 center = def.Targeting == TargetType.Position ? targetPos : origin;
            foreach (var u in EnemiesOf(actor))
            {
                Vector3 p = u.Position;
                if (def.Shape == AreaShape.Line)
                {
                    Vector3 v = p - origin; v.y = 0f;
                    float along = Vector3.Dot(v, dir);
                    float side = (v - dir * along).magnitude;
                    if (along >= -u.BodyRadius && along <= def.Range + u.BodyRadius && side <= def.LineWidth / 2f + u.BodyRadius) hits.Add(u);
                }
                else
                {
                    Vector3 v = p - center; v.y = 0f;
                    if (v.magnitude <= def.AreaRadius + u.BodyRadius) hits.Add(u);
                }
            }
            return hits;
        }

        /// <summary>Grant an immediate reaction action (Retaliate's free shot, a Snipe from Take Aim...).
        /// It runs alongside the actor's normal state: it does not require or change the actor's phase.</summary>
        public ActionInstance QueueFreeAction(Unit actor, ActionDefinition def, string targetId, float windupOverride = -1f, float damageMultiplier = 1f)
        {
            if (!actor.IsAlive) return null;
            var behavior = ActionBehaviors.Get(def.BehaviorId);
            float windup = windupOverride >= 0f ? windupOverride : def.Windup / actor.EffectiveActionSpeed();
            var inst = new ActionInstance
            {
                Actor = actor, Definition = def, Behavior = behavior, TargetUnitId = targetId,
                StartedAt = Now, ResolveAt = Now + windup, IsFree = true, DamageMultiplier = damageMultiplier,
            };
            freeActions.Add(inst);
            Emit(EventType.ActionStarted, actor.Id, targetId,
                new ActionStartedInfo { ActionId = def.Id, Windup = windup, Recovery = 0f, Free = true });
            return inst;
        }

        // ------------------------------------------------------------------ helpers for behaviors

        /// <summary>Apply (or refresh: same id replaces) a status. Rooting a walking unit stops it where it stands.</summary>
        public void ApplyStatus(Unit target, StatusEffect status, Unit source)
        {
            if (target == null || !target.IsAlive) return;
            float oldAction = target.EffectiveActionSpeed(), oldMove = target.EffectiveMoveSpeed();
            target.Statuses.RemoveAll(s => s.Id == status.Id);   // refresh instead of stacking
            target.Statuses.Add(status);
            Emit(EventType.StatusApplied, source != null ? source.Id : null, target.Id, status.Id);
            HandleSpeedChange(target, oldAction, oldMove);
            if (status.Rooted && target.Phase == UnitPhase.Moving) EndMove(target);
        }

        public bool RemoveStatus(Unit u, string id)
        {
            int idx = u.Statuses.FindIndex(s => s.Id == id);
            if (idx < 0) return false;
            float oldAction = u.EffectiveActionSpeed(), oldMove = u.EffectiveMoveSpeed();
            u.Statuses.RemoveAt(idx);
            Emit(EventType.StatusExpired, u.Id, null, id);
            HandleSpeedChange(u, oldAction, oldMove);
            return true;
        }

        public void AddEnergy(Unit u, float delta)
        {
            if (u.MaxEnergy <= 0f) return;
            float before = u.Energy;
            u.Energy = Mathf.Clamp(u.Energy + delta, 0f, u.MaxEnergy);
            if (Mathf.Abs(u.Energy - before) < 1e-4f) return;
            Emit(EventType.EnergyChanged, u.Id, null, new EnergyInfo { Amount = u.Energy - before, Total = u.Energy });
        }

        /// <summary>Shove a unit per def.Displace: away from its nearest enemy, or pulled toward 'source' (stopping at arm's length).</summary>
        public void DisplaceUnit(Unit target, Unit source, ActionDefinition def)
        {
            if (target == null || !target.IsAlive || def.DisplaceDistance <= 0f) return;
            Vector3 from = target.Position, to;
            if (def.Displace == DisplaceMode.TowardSource)
            {
                if (source == null) return;
                Vector3 d = from - source.Position; d.y = 0f;
                float stop = source.BodyRadius + target.BodyRadius + 0.5f;
                if (d.magnitude <= stop) return;
                to = source.Position + d.normalized * Mathf.Max(stop, d.magnitude - def.DisplaceDistance);
                to.y = from.y;
            }
            else if (def.Displace == DisplaceMode.AwayFromEnemies)
            {
                var threat = NearestEnemy(target);
                if (threat == null) return;
                Vector3 away = from - threat.Position; away.y = 0f;
                if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;
                to = from + away.normalized * def.DisplaceDistance;
            }
            else return;
            Relocate(target, Paths.ClampDash(from, to), DisplaceDuration, true);
        }

        /// <summary>
        /// Advance until a player unit needs a command, a dodge-dash needs a direction,
        /// the battle ends, or maxSimSeconds elapse.
        /// </summary>
        public SimSegmentResult RunUntilDecision(float maxSimSeconds = 300f)
        {
            long deadline = tick + (long)(maxSimSeconds / TickLength);
            while (true)
            {
                if (Outcome != BattleOutcome.InProgress) return Segment(SegmentEnd.BattleOver, null);
                playerTurnOpen = false;
                var decisions = ProcessReadyUnits();
                if (decisions.Count > 0)
                {
                    playerTurnOpen = decisions.Any(d => d.Kind == DecisionKind.Action);
                    return Segment(SegmentEnd.DecisionNeeded, decisions);
                }
                if (tick >= deadline) return Segment(SegmentEnd.TimedOut, null);
                Step();
            }
        }

        // ------------------------------------------------------------------ decisions

        private SimSegmentResult Segment(SegmentEnd end, List<DecisionRequest> decisions)
        {
            var r = new SimSegmentResult { End = end, Time = Now, Outcome = Outcome };
            if (decisions != null) r.Decisions = decisions;
            return r;
        }

        private List<DecisionRequest> ProcessReadyUnits()
        {
            var decisions = new List<DecisionRequest>(pendingDashes);
            foreach (var unit in State.Units)
            {
                if (!unit.IsAlive || unit.Phase != UnitPhase.Ready || IsGliding(unit)) continue;   // nothing new mid-dash

                UnitCommand cmd = null;
                bool isPlayer = unit.Team == Team.Player;
                if (isPlayer)
                {
                    Queue<UnitCommand> q;
                    if (queues.TryGetValue(unit.Id, out q) && q.Count > 0) cmd = q.Dequeue();
                }
                else
                {
                    cmd = EnemyAI.Decide(this, unit) ?? UnitCommand.Wait(unit.Id, 0.5f);
                }

                if (cmd == null)
                {
                    decisions.Add(new DecisionRequest { Kind = DecisionKind.Action, UnitId = unit.Id });
                    continue;
                }

                string reason;
                if (!TryStartCommand(unit, cmd, out reason))
                {
                    if (isPlayer)
                    {
                        queues.Remove(unit.Id);   // a stale plan: drop the rest and ask again
                        decisions.Add(new DecisionRequest { Kind = DecisionKind.Action, UnitId = unit.Id, Reason = reason });
                    }
                    else StartWait(unit, 0.5f);   // never let an AI stall the sim on a bad command
                }
            }
            return decisions;
        }

        private bool TryStartCommand(Unit unit, UnitCommand cmd, out string reason)
        {
            reason = null;
            switch (cmd.Type)
            {
                case CommandType.Wait:
                    unit.LastActionId = null;   // a wait or a move breaks a follow-up chain
                    StartWait(unit, cmd.WaitSeconds);
                    return true;

                case CommandType.Move:
                {
                    if (unit.IsRooted) { reason = "immobilized"; return false; }
                    var path = new List<Vector3>();
                    if (!PlanPath(unit, unit.Position, FreeSpotNear(unit, cmd.TargetPos, unit.Position, true), path)) { reason = "no path"; return false; }
                    unit.LastActionId = null;
                    StartMove(unit, path);
                    return true;
                }

                case CommandType.Action:
                {
                    if (cmd.Action == null) { reason = "no action"; return false; }
                    var target = GetUnit(cmd.TargetUnitId);
                    if (!CanUse(unit, cmd.Action, target, out reason))
                    {
                        if (!cmd.WaitForRange || reason != "out of range") return false;
                        if (!cmd.Action.IsRanged) return StartChase(unit, cmd, target, out reason);
                        QueueRanged(unit, cmd, target);   // the unit stays ready: it is asked for its next order straight away
                        return true;
                    }
                    // Point-targeted actions must land in range; a line only takes its direction from the point.
                    if (cmd.Action.Targeting == TargetType.Position && cmd.Action.Shape != AreaShape.Line &&
                        Vector3.Distance(unit.Position, cmd.TargetPos) > cmd.Action.Range) { reason = "out of range"; return false; }
                    var behavior = ActionBehaviors.Get(cmd.Action.BehaviorId);
                    if (!behavior.CanStart(this, unit, cmd, out reason)) return false;
                    StartAction(unit, cmd, behavior);
                    return true;
                }
            }
            reason = "unknown command";
            return false;
        }

        // ------------------------------------------------------------------ starting things

        private void StartWait(Unit unit, float seconds)
        {
            unit.Waiting = true;
            unit.Phase = UnitPhase.Recovery;
            unit.PhaseEndsAt = Now + seconds;
            Emit(EventType.ActionStarted, unit.Id, null, new ActionStartedInfo { ActionId = "wait", Windup = 0f, Recovery = seconds });
        }

        /// <summary>Cut a recovery short: the unit is ready (and asked for orders) right now. Block uses it when the guard is hit.</summary>
        public void EndRecoveryNow(Unit u)
        {
            if (u.Phase != UnitPhase.Recovery) return;
            u.Waiting = false;
            u.Phase = UnitPhase.Ready;
            u.PhaseEndsAt = Now;
            Emit(EventType.UnitReady, u.Id, null);
            Triggers.Fire(new TriggerContext { Type = TriggerType.UnitReady, Sim = this, Subject = u });
        }

        // ------------------------------------------------------------------ out-of-range orders

        /// <summary>Melee, target out of reach: walk the shortest path at it (re-pathed as it moves) and strike once in range.</summary>
        private bool StartChase(Unit unit, UnitCommand cmd, Unit target, out string reason)
        {
            reason = null;
            if (unit.IsRooted) { reason = "immobilized"; return false; }
            ClearQueued(unit, null);   // committing to an ability overrides a queued ranged one
            unit.LastActionId = null;
            if (!RepathChase(unit, target)) { reason = "no path"; return false; }
            unit.Chase = cmd;
            unit.ChaseUntil = Now + MaxRangeWait;
            Emit(EventType.QueuedActionChanged, unit.Id, target.Id, new QueuedActionInfo { ActionId = cmd.Action.Id, Chase = true });
            return true;
        }

        private bool RepathChase(Unit unit, Unit target)
        {
            var path = new List<Vector3>();
            if (!PlanPath(unit, unit.Position, target.Position, path)) return false;
            StartMove(unit, path);
            unit.ChaseRepathAt = Now + ChaseRepathSeconds;
            return true;
        }

        /// <summary>Ranged, target out of range: a standing order. The unit stays free to move or wait (moving doesn't drop it,
        /// using another ability replaces it) and fires the moment the target is in range.</summary>
        private void QueueRanged(Unit unit, UnitCommand cmd, Unit target)
        {
            unit.Queued = cmd;
            unit.QueuedUntil = Now + MaxRangeWait;
            Emit(EventType.QueuedActionChanged, unit.Id, target.Id, new QueuedActionInfo { ActionId = cmd.Action.Id });
        }

        private void ClearQueued(Unit u, string reason)
        {
            if (u.Queued == null) return;
            u.Queued = null;
            Emit(EventType.QueuedActionChanged, u.Id, null, new QueuedActionInfo());
            if (reason != null) Emit(EventType.ActionInterrupted, u.Id, null, new InterruptInfo { Reason = reason });
        }

        private void ClearChase(Unit u)
        {
            if (u.Chase == null) return;
            u.Chase = null;
            Emit(EventType.QueuedActionChanged, u.Id, null, new QueuedActionInfo { Chase = true });
        }

        /// <summary>Give up a chase: stop where the unit is and hand it back for orders.</summary>
        private void StopChase(Unit u, string reason)
        {
            ClearChase(u);
            Emit(EventType.ActionInterrupted, u.Id, null, new InterruptInfo { Reason = reason });
            if (u.Phase == UnitPhase.Moving) EndMove(u);
        }

        /// <summary>Stop a walk on the spot without handing the unit back (it is about to start an action).</summary>
        private void HaltWalk(Unit u)
        {
            if (u.Phase != UnitPhase.Moving) return;
            u.Move = null;
            u.Phase = UnitPhase.Ready;
            Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
            SettleOutOfBodies(u);
        }

        private void TickOutOfRangeOrders()
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || IsGliding(u)) continue;
                string reason;
                if (u.Chase != null)
                {
                    var cmd = u.Chase;
                    var target = GetUnit(cmd.TargetUnitId);
                    if (u.Phase != UnitPhase.Moving) { ClearChase(u); continue; }   // something else stopped the walk
                    if (target == null || !target.IsAlive || !IsTargetableBy(target, u)) { StopChase(u, "target lost"); continue; }
                    if (Now >= u.ChaseUntil) { StopChase(u, "never came in range"); continue; }
                    if (CanUse(u, cmd.Action, target, out reason))
                    {
                        var behavior = ActionBehaviors.Get(cmd.Action.BehaviorId);
                        if (behavior.CanStart(this, u, cmd, out reason))
                        {
                            ClearChase(u);
                            HaltWalk(u);
                            StartAction(u, cmd, behavior);
                            continue;
                        }
                    }
                    if (reason != "out of range") { StopChase(u, reason); continue; }
                    if (Now >= u.ChaseRepathAt && !RepathChase(u, target)) StopChase(u, "no path");
                    continue;
                }
                if (u.Queued != null)
                {
                    var cmd = u.Queued;
                    var target = GetUnit(cmd.TargetUnitId);
                    if (target == null || !target.IsAlive || !IsTargetableBy(target, u)) { ClearQueued(u, "target lost"); continue; }
                    if (Now >= u.QueuedUntil) { ClearQueued(u, "never came in range"); continue; }
                    bool free = u.Phase == UnitPhase.Ready || u.Phase == UnitPhase.Moving || (u.Phase == UnitPhase.Recovery && u.Waiting);
                    if (!free || !CanUse(u, cmd.Action, target, out reason)) continue;   // busy, or still out of reach: keep it
                    var behavior = ActionBehaviors.Get(cmd.Action.BehaviorId);
                    if (!behavior.CanStart(this, u, cmd, out reason)) continue;
                    u.Queued = null;
                    Emit(EventType.QueuedActionChanged, u.Id, null, new QueuedActionInfo());
                    HaltWalk(u);
                    u.Waiting = false;
                    StartAction(u, cmd, behavior);
                }
            }
        }

        // ------------------------------------------------------------------ dashes, shoves and pulls

        public bool IsGliding(Unit u) { return Now < u.GlideEnd; }

        private void AdvanceGlides()
        {
            foreach (var u in State.Units)
            {
                if (u.GlideEnd < 0f) continue;
                float k = u.GlideEnd > u.GlideStart ? Mathf.Clamp01((Now - u.GlideStart) / (u.GlideEnd - u.GlideStart)) : 1f;
                u.Position = Vector3.Lerp(u.GlideFrom, u.GlideTo, k);
                if (k >= 1f) u.GlideEnd = -1f;
            }
        }

        private void StartMove(Unit unit, List<Vector3> path)
        {
            unit.Phase = UnitPhase.Moving;
            float moveSpeed = unit.EffectiveMoveSpeed();
            unit.Move = new ActiveMove { Path = path, NextIndex = 1, Speed = moveSpeed };
            Emit(EventType.MoveStarted, unit.Id, null, new MoveInfo { Path = new List<Vector3>(path), Speed = moveSpeed });
        }

        /// <summary>windupLeft &gt;= 0 resumes an action a dodge cut short: that much windup, and no costs (already paid).</summary>
        private void StartAction(Unit unit, UnitCommand cmd, ActionBehavior behavior, float windupLeft = -1f)
        {
            var def = cmd.Action;
            float speed = unit.EffectiveActionSpeed();
            bool resuming = windupLeft >= 0f;
            float windup = resuming ? windupLeft : def.Windup / speed;
            unit.Waiting = false;
            if (!resuming && unit.Queued != cmd) ClearQueued(unit, null);   // using another ability overrides a queued ranged one
            var inst = new ActionInstance
            {
                Actor = unit, Definition = def, Behavior = behavior,
                TargetUnitId = cmd.TargetUnitId, TargetPos = cmd.TargetPos,
                StartedAt = Now, ResolveAt = Now + windup,
            };
            unit.Phase = UnitPhase.Windup;
            unit.PhaseEndsAt = inst.ResolveAt;
            unit.ActiveAction = inst;
            unit.LastActionId = def.Id;
            unit.LastActionTargetId = cmd.TargetUnitId;
            unit.LastActionPos = cmd.TargetPos;
            if (!resuming)
            {
                float energyCost = behavior.EnergyCost(this, unit, def);   // before anything below changes the discount
                float strainCost = behavior.StrainCost(this, unit, def, cmd.TargetUnitId);
                if (def.Cooldown > 0f) unit.CooldownReadyAt[def.Id] = Now + def.Cooldown;
                AddStrain(unit, strainCost);
                if (energyCost > 0f) AddEnergy(unit, -energyCost);
                if (def.ConsumesRequiredStatus && !string.IsNullOrEmpty(def.RequiredStatus)) RemoveStatus(unit, def.RequiredStatus);
                if (def.SelfStatusesOnWindup) ApplySelfStatuses(unit, def);
            }

            behavior.OnWindupStart(this, inst);
            Emit(EventType.ActionStarted, unit.Id, cmd.TargetUnitId,
                new ActionStartedInfo { ActionId = def.Id, Windup = windup, Recovery = def.Recovery / speed, TargetPos = cmd.TargetPos });
            Triggers.Fire(new TriggerContext { Type = TriggerType.ActionStarted, Sim = this, Subject = unit, Action = def });
        }

        private void ApplySelfStatuses(Unit unit, ActionDefinition def)
        {
            foreach (var spec in def.SelfStatuses) ApplyStatus(unit, spec.Create(Now, unit.Id, def.LinkedAction), unit);
        }

        // ------------------------------------------------------------------ the tick

        private void Step()
        {
            tick++;
            AdvanceGlides();
            AdvanceMovement();
            TickOutOfRangeOrders();
            CheckTraps();
            TickWindups();
            ResolveDueActions();
            ProcessDeaths();
            ExpireTimers();
            RecoverStrain();
        }

        private void AdvanceMovement()
        {
            foreach (var u in State.Units)
            {
                if (u.Phase != UnitPhase.Moving || u.Move == null) continue;
                float remaining = u.Move.Speed * TickLength;
                while (remaining > 0f && u.Move.NextIndex < u.Move.Path.Count)
                {
                    Vector3 next = u.Move.Path[u.Move.NextIndex];
                    float dist = Vector3.Distance(u.Position, next);
                    if (dist <= remaining) { u.Position = next; remaining -= dist; u.Move.NextIndex++; }
                    else { u.Position = u.Position + (next - u.Position) * (remaining / dist); remaining = 0f; }
                }
                if (u.Move.NextIndex >= u.Move.Path.Count && u.Chase == null) EndMove(u);   // a chaser waits there for its next re-path
            }
        }

        /// <summary>Armed traps spring on the first unit of another team inside them; unsprung ones expire.</summary>
        private void CheckTraps()
        {
            for (int i = State.Traps.Count - 1; i >= 0; i--)
            {
                var t = State.Traps[i];
                if (t.ExpiresAt <= Now)
                {
                    State.Traps.RemoveAt(i);
                    Emit(EventType.TrapRemoved, t.OwnerId, null, t.Id);
                    continue;
                }
                if (Now + 1e-4f < t.ArmsAt) continue;

                Unit victim = null;
                foreach (var u in State.Units)
                {
                    if (!u.IsAlive || u.Team == t.OwnerTeam) continue;
                    Vector3 d = u.Position - t.Position; d.y = 0f;
                    if (d.magnitude <= t.Radius + u.BodyRadius) { victim = u; break; }
                }
                if (victim == null) continue;

                State.Traps.RemoveAt(i);
                Emit(EventType.TrapTriggered, t.OwnerId, victim.Id, t.Id);
                var owner = GetUnit(t.OwnerId);
                foreach (var spec in t.Statuses) ApplyStatus(victim, spec.Create(Now, t.OwnerId), owner);
            }
        }

        private void EndMove(Unit u, bool settle = true)
        {
            u.Move = null;
            ClearChase(u);   // cancelled, rooted or dashed out of a chase: it is over
            u.Phase = UnitPhase.Ready;
            Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
            if (settle) SettleOutOfBodies(u);
            Emit(EventType.UnitReady, u.Id, null);
            Triggers.Fire(new TriggerContext { Type = TriggerType.UnitReady, Sim = this, Subject = u });
        }

        private void TickWindups()
        {
            foreach (var u in State.Units)
            {
                if (u.Phase != UnitPhase.Windup || u.ActiveAction == null || !u.IsAlive) continue;
                if (!u.ActiveAction.Behavior.OnWindupTick(this, u.ActiveAction)) Interrupt(u, "condition lost");
            }
            foreach (var f in freeActions.ToArray())
            {
                if (!f.Actor.IsAlive) { freeActions.Remove(f); continue; }
                if (!f.Behavior.OnWindupTick(this, f)) { freeActions.Remove(f); Emit(EventType.ActionInterrupted, f.Actor.Id, null, new InterruptInfo { Reason = "condition lost" }); }
            }
        }

        private void ResolveDueActions()
        {
            var due = new List<ActionInstance>();
            foreach (var u in State.Units)
                if (u.Phase == UnitPhase.Windup && u.ActiveAction != null && Now + 1e-4f >= u.ActiveAction.ResolveAt)
                    due.Add(u.ActiveAction);
            foreach (var f in freeActions.ToArray())
                if (Now + 1e-4f >= f.ResolveAt) { due.Add(f); freeActions.Remove(f); }
            if (due.Count == 0) return;

            // Snapshot first: simultaneous attacks all see the same dodge/block.
            var snapshot = new Dictionary<string, CombatSnapshot>();
            foreach (var u in State.Units)
                snapshot[u.Id] = new CombatSnapshot { Dodge = u.EffectiveDodge(), Block = u.EffectiveBlock() };

            var effects = new List<SimEffect>();
            foreach (var a in due)
            {
                a.Behavior.OnResolve(this, a, effects);
                if (!a.Definition.SelfStatusesOnWindup)
                    foreach (var spec in a.Definition.SelfStatuses)
                        effects.Add(new ApplyStatusEffect { Source = a.Actor, TargetId = a.Actor.Id, Status = spec.Create(Now, a.Actor.Id, a.Definition.LinkedAction) });
                if (a.Definition.EnergyGain > 0f) AddEnergy(a.Actor, a.Definition.EnergyGain);
                float recovery = a.IsFree ? 0f : a.Definition.Recovery / a.Actor.EffectiveActionSpeed();
                Emit(EventType.ActionResolved, a.Actor.Id, a.TargetUnitId, new ActionResolvedInfo { ActionId = a.Definition.Id, Recovery = recovery });
                if (!a.IsFree)
                {
                    a.Actor.ActiveAction = null;
                    a.Actor.Phase = UnitPhase.Recovery;
                    a.Actor.PhaseEndsAt = Now + recovery;
                }
                Triggers.Fire(new TriggerContext { Type = TriggerType.ActionResolved, Sim = this, Subject = a.Actor, Action = a.Definition });
            }

            foreach (var e in effects) ApplyEffect(e, snapshot);
        }

        private void ProcessDeaths()
        {
            foreach (var u in State.Units)
            {
                if (u.Health > 0f || u.Phase == UnitPhase.Dead) continue;
                u.Phase = UnitPhase.Dead;
                u.ActiveAction = null;
                u.Move = null;
                u.Chase = u.Queued = null;
                u.GlideEnd = -1f;
                pendingDashes.RemoveAll(d => d.UnitId == u.Id);
                queues.Remove(u.Id);
                Emit(EventType.Death, u.LastAttackerId, u.Id);
                Triggers.Fire(new TriggerContext { Type = TriggerType.Killed, Sim = this, Subject = u, Other = GetUnit(u.LastAttackerId), Action = u.LastHitAction, Tag = u.LastHitTag });
            }

            bool playersAlive = State.Units.Any(u => u.Team == Team.Player && u.IsAlive);
            bool enemiesAlive = State.Units.Any(u => u.Team == Team.Enemy && u.IsAlive);
            if (playersAlive && enemiesAlive) return;
            Outcome = !playersAlive && !enemiesAlive ? BattleOutcome.Draw
                    : playersAlive ? BattleOutcome.PlayerVictory : BattleOutcome.PlayerDefeat;
            Emit(EventType.BattleEnd, null, null);
        }

        private void ExpireTimers()
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive) continue;
                for (int i = u.Statuses.Count - 1; i >= 0; i--)
                {
                    if (u.Statuses[i].ExpiresAt > Now) continue;
                    float oldAction = u.EffectiveActionSpeed(), oldMove = u.EffectiveMoveSpeed();
                    Emit(EventType.StatusExpired, u.Id, null, u.Statuses[i].Id);
                    u.Statuses.RemoveAt(i);
                    HandleSpeedChange(u, oldAction, oldMove);
                }
                bool locked = u.Phase == UnitPhase.Recovery || u.Phase == UnitPhase.Stunned;
                if (locked && Now + 1e-4f >= u.PhaseEndsAt)
                {
                    u.Phase = UnitPhase.Ready;
                    u.Waiting = false;
                    if (u.ResumeAction != null && TryResume(u)) continue;
                    Emit(EventType.UnitReady, u.Id, null);
                    Triggers.Fire(new TriggerContext { Type = TriggerType.UnitReady, Sim = this, Subject = u });
                }
            }

            for (int i = State.Occluders.Count - 1; i >= 0; i--)
            {
                if (State.Occluders[i].ExpiresAt > Now) continue;
                Emit(EventType.DeployableRemoved, null, null, State.Occluders[i].Id);
                State.Occluders.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ effects

        private void ApplyEffect(SimEffect effect, Dictionary<string, CombatSnapshot> snapshot)
        {
            var attack = effect as AttackEffect;
            if (attack != null) { ApplyAttack(attack, snapshot); return; }

            var heal = effect as HealEffect;
            if (heal != null)
            {
                var t = GetUnit(heal.TargetId);
                if (t == null || !t.IsAlive) return;
                t.Health = Mathf.Min(t.MaxHealth, t.Health + heal.Amount);
                Emit(EventType.Heal, heal.Source != null ? heal.Source.Id : null, t.Id, heal.Amount);
                return;
            }

            var deploy = effect as DeployEffect;
            if (deploy != null)
            {
                var o = deploy.Occluder;
                if (string.IsNullOrEmpty(o.Id)) o.Id = "deployable_" + (++deployCounter);
                State.Occluders.Add(o);
                Emit(EventType.DeployableSpawned, deploy.Source != null ? deploy.Source.Id : null, null,
                    new DeployableInfo { Id = o.Id, Position = o.Position, Radius = o.Radius, Height = o.Height, ExpiresAt = o.ExpiresAt });
                return;
            }

            var status = effect as ApplyStatusEffect;
            if (status != null)
            {
                ApplyStatus(GetUnit(status.TargetId), status.Status, status.Source);
                return;
            }

            var displace = effect as DisplaceEffect;
            if (displace != null)
            {
                DisplaceUnit(GetUnit(displace.TargetId), displace.Source, displace.Definition);
                return;
            }

            var trap = effect as TrapEffect;
            if (trap != null)
            {
                var t = trap.Trap;
                if (string.IsNullOrEmpty(t.Id)) t.Id = "trap_" + (++trapCounter);
                State.Traps.Add(t);
                Emit(EventType.TrapPlaced, t.OwnerId, null,
                    new TrapInfo { Id = t.Id, Position = t.Position, Radius = t.Radius, ArmsAt = t.ArmsAt, OwnerTeam = t.OwnerTeam });
            }
        }

        private void ApplyAttack(AttackEffect atk, Dictionary<string, CombatSnapshot> snapshot)
        {
            var src = atk.Source;
            var def = atk.Definition;
            var target = GetUnit(atk.TargetId);
            if (target == null || target.Phase == UnitPhase.Dead) return;

            // 0. A target that went hidden during the windup can't be found
            if (!IsTargetableBy(target, src))
            {
                Emit(EventType.Miss, src.Id, target.Id, new MissInfo { Reason = "hidden" });
                return;
            }

            // 1. Range (melee whiffs if the target moved away during windup; area hits only need to be in the area)
            if (!atk.IgnoreRange && Vector3.Distance(src.Position, target.Position) > def.Range * RangeTolerance)
            {
                Emit(EventType.Miss, src.Id, target.Id, new MissInfo { Reason = "out_of_range" });
                return;
            }

            // 2. Accuracy: base * body exposure (ranged vs cover) + modifiers
            float exposure = (!atk.IgnoreCover && def.IsRanged && !def.PiercesTerrain) ? Visibility.GetExposure(src, target, false) : 1f;
            float accuracyBonus = atk.AccuracyBonus;
            foreach (var s in target.Statuses) accuracyBonus += s.IncomingAccuracyBonus;
            float hitChance = Mathf.Clamp01(def.BaseAccuracy * exposure + accuracyBonus);
            if (rng.NextDouble() >= hitChance)
            {
                Emit(EventType.Miss, src.Id, target.Id, new MissInfo { Reason = exposure < 0.999f ? "cover" : "miss" });
                return;
            }

            // 3. Dodge (uses the pre-tick snapshot; stunned units snapshot at 0)
            CombatSnapshot snap;
            if (!snapshot.TryGetValue(target.Id, out snap)) snap = new CombatSnapshot { Dodge = target.EffectiveDodge(), Block = target.EffectiveBlock() };
            if (rng.NextDouble() < snap.Dodge)
            {
                OnDodged(src, target, def);
                return;
            }

            // 4. Damage through block
            float raw = def.Damage * atk.DamageMultiplier;
            float effectiveBlock = Mathf.Clamp01(snap.Block * (1f - Mathf.Clamp01(def.BlockPierce + atk.BlockPierceBonus)));
            float dealt = raw * (1f - effectiveBlock);
            target.Health -= dealt;
            target.LastAttackerId = src.Id;
            target.LastHitAction = def;
            target.LastHitTag = atk.Tag;
            if (snap.Block > 0f) target.ReduceBlock(def.Impact == ImpactType.Heavy ? HeavyBlockBreak : LightBlockBreak);

            // 5. Strain / Unravel (checked against strain BEFORE this hit adds to it)
            bool atMaxStrain = target.Strain >= target.MaxStrain - 0.001f;
            bool unravel = atMaxStrain && def.Impact == ImpactType.Heavy;   // even through Block: Unravel pierces stun immunity
            AddStrain(target, def.StrainOnTarget);

            Emit(EventType.Damage, src.Id, target.Id, new DamageInfo
            {
                ActionId = def.Id, Amount = dealt, BlockedAmount = raw - dealt, Impact = def.Impact,
                RemainingHealth = target.Health, CausedUnravel = unravel,
            });
            Triggers.Fire(new TriggerContext { Type = TriggerType.DamageTaken, Sim = this, Subject = target, Other = src, Amount = dealt, Impact = def.Impact, Action = def });

            // Taking damage breaks fragile statuses (Marking Shot's stealth).
            for (int i = target.Statuses.Count - 1; i >= 0; i--)
                if (target.Statuses[i].BreaksOnDamage) RemoveStatus(target, target.Statuses[i].Id);

            // 6. Interrupts and stun (a dead unit needs neither).
            // Rule: a Heavy impact that lands always cancels a windup; Light impacts only do if that action is InterruptedByDamage.
            if (target.Health <= 0f) return;
            foreach (var spec in def.TargetStatusesOnHit) ApplyStatus(target, spec.Create(Now, src.Id), src);
            if (def.Displace == DisplaceMode.TowardSource) DisplaceUnit(target, src, def);
            bool willStun = unravel || def.StunDuration > 0f;
            if (target.Phase == UnitPhase.Windup && target.ActiveAction != null &&
                (def.Impact == ImpactType.Heavy || target.ActiveAction.Definition.InterruptedByDamage))
                Interrupt(target, def.Impact == ImpactType.Heavy ? "heavy impact" : "damage", !willStun);

            if (unravel)
            {
                AddStrain(target, -target.Strain);   // assumption: Unravel resets strain
                Emit(EventType.Unravel, src.Id, target.Id);
                ApplyStun(target, UnravelStunSeconds, src, true);
            }
            else if (def.StunDuration > 0f)
            {
                ApplyStun(target, def.StunDuration, src);
            }
        }

        private void OnDodged(Unit attacker, Unit target, ActionDefinition def)
        {
            Emit(EventType.Dodge, attacker.Id, target.Id, new MissInfo { Reason = "dodge" });
            Triggers.Fire(new TriggerContext { Type = TriggerType.Dodged, Sim = this, Subject = target, Other = attacker, Action = def });
            if (!target.IsAlive || target.IsRooted) return;   // rooted units still dodge, but can't dash

            if (target.Team == Team.Player)
                pendingDashes.Add(new DecisionRequest { Kind = DecisionKind.DodgeDash, UnitId = target.Id, AttackerId = attacker.Id });
            else
                ExecuteDash(target, BestDashDirection(target, attacker));
        }

        private void ExecuteDash(Unit unit, int direction8)
        {
            int idx = ((direction8 % 8) + 8) % 8;
            Vector3 from = unit.Position;
            DodgeTo(unit, Paths.ClampDash(from, from + Directions8[idx] * unit.DashDistanceNow()));
        }

        /// <summary>
        /// Dodge dash. Dashing out of a windup cancels it, but the unit remembers the ability and target and retries
        /// them when the dash ends, keeping the windup progress it had and paying nothing twice (see TryResume).
        /// Dashing nowhere (picking your own spot) stands firm and leaves the windup running.
        /// </summary>
        private void DodgeTo(Unit unit, Vector3 to)
        {
            if ((to - unit.Position).sqrMagnitude <= 1e-4f) return;
            var a = unit.ActiveAction;
            if (unit.Phase == UnitPhase.Windup && a != null)
            {
                float left = Mathf.Max(0f, a.ResolveAt - Now);
                Interrupt(unit, "dodge", false);
                unit.ResumeAction = a;
                unit.ResumeWindupLeft = left;
                unit.Phase = UnitPhase.Recovery;
                unit.PhaseEndsAt = Now + DashDuration;
            }
            else if (unit.ResumeAction != null) unit.PhaseEndsAt = Now + DashDuration;   // dodging again mid-dodge: retry when this dash lands
            Relocate(unit, to, DashDuration, false);
        }

        /// <summary>The dash after a mid-windup dodge has ended: start the remembered ability again if it is still possible.</summary>
        private bool TryResume(Unit u)
        {
            var r = u.ResumeAction;
            u.ResumeAction = null;
            if (r == null) return false;
            var def = r.Definition;
            var cmd = def.Targeting == TargetType.Position ? UnitCommand.UseActionAt(u.Id, def, r.TargetPos) : UnitCommand.UseAction(u.Id, def, r.TargetUnitId);
            string reason;
            bool ok = CanUse(u, def, GetUnit(r.TargetUnitId), out reason, false);   // already paid for: skip resource checks
            if (ok && def.Targeting == TargetType.Position && def.Shape != AreaShape.Line && Vector3.Distance(u.Position, r.TargetPos) > def.Range) { ok = false; reason = "out of range"; }
            if (ok && !r.Behavior.CanStart(this, u, cmd, out reason)) ok = false;
            if (!ok)
            {
                Emit(EventType.ActionInterrupted, u.Id, null, new InterruptInfo { Reason = "resume failed: " + reason });
                return false;
            }
            StartAction(u, cmd, r.Behavior, u.ResumeWindupLeft);
            return true;
        }

        /// <summary>
        /// A walking path from 'from' to 'to' that steps around units standing in the way (the navmesh only knows about
        /// cover). Units already walking are ignored: they will have moved by the time we get there. False if unreachable.
        /// The orders panel previews moves with this too, so the drawn route is the one the unit takes.
        /// </summary>
        public bool PlanPath(Unit unit, Vector3 from, Vector3 to, List<Vector3> path)
        {
            if (!Paths.TryGetPath(from, to, path) || path.Count < 2) return false;
            path[0] = from;
            RouteAroundBodies(unit, path);
            return true;
        }

        private void RouteAroundBodies(Unit unit, List<Vector3> path)
        {
            var skip = new HashSet<string>();   // blockers we couldn't find a way around
            for (int pass = 0; pass < 16; pass++)
            {
                bool changed = false;
                for (int i = 0; i + 1 < path.Count && !changed; i++)
                {
                    Vector3 a = path[i], b = path[i + 1];
                    foreach (var u in State.Units)
                    {
                        if (!u.IsAlive || u == unit || u.Phase == UnitPhase.Moving || skip.Contains(u.Id)) continue;
                        float clearance = unit.BodyRadius + u.BodyRadius + 0.1f;
                        Vector3 closest;
                        if (!SegmentPassesWithin(a, b, u.Position, clearance, out closest)) continue;
                        if (TryDetour(unit, path, i, u, clearance, closest)) changed = true;
                        else skip.Add(u.Id);
                        break;
                    }
                }
                if (!changed) return;
            }
        }

        /// <summary>True if the segment a-b passes closer than 'radius' to c (XZ), with both ends outside that circle.</summary>
        private static bool SegmentPassesWithin(Vector3 a, Vector3 b, Vector3 c, float radius, out Vector3 closest)
        {
            Vector3 ab = b - a; ab.y = 0f;
            Vector3 ac = c - a; ac.y = 0f;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ac, ab) / len2) : 0f;
            closest = a + ab * t;
            Vector3 d = c - closest; d.y = 0f;
            Vector3 da = c - a, db = c - b; da.y = 0f; db.y = 0f;
            return d.sqrMagnitude < radius * radius && da.sqrMagnitude >= radius * radius && db.sqrMagnitude >= radius * radius;
        }

        /// <summary>Inserts two waypoints that skirt 'blocker' after path[i], on whichever side is walkable and clear.</summary>
        private bool TryDetour(Unit unit, List<Vector3> path, int i, Unit blocker, float clearance, Vector3 closest)
        {
            Vector3 a = path[i], b = path[i + 1];
            Vector3 dir = b - a; dir.y = 0f; dir.Normalize();
            Vector3 n = closest - blocker.Position; n.y = 0f;
            n = n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.Cross(Vector3.up, dir);
            float r = clearance * 1.15f;
            for (int side = 0; side < 2; side++, n = -n)
            {
                Vector3 p1 = blocker.Position + n * r - dir * r * 0.75f; p1.y = a.y;
                Vector3 p2 = blocker.Position + n * r + dir * r * 0.75f; p2.y = b.y;
                if (!Walkable(a, p1) || !Walkable(p1, p2) || !Walkable(p2, b)) continue;
                if (InsideStandingBody(unit, p1) || InsideStandingBody(unit, p2)) continue;
                path.Insert(i + 1, p2);
                path.Insert(i + 1, p1);
                return true;
            }
            return false;
        }

        private bool Walkable(Vector3 a, Vector3 b) { return (Paths.ClampDash(a, b) - b).sqrMagnitude < 0.01f; }

        private bool InsideStandingBody(Unit unit, Vector3 p)
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u == unit || u.Phase == UnitPhase.Moving) continue;
                Vector3 d = u.Position - p; d.y = 0f;
                float min = unit.BodyRadius + u.BodyRadius;
                if (d.sqrMagnitude < min * min) return true;
            }
            return false;
        }

        /// <summary>
        /// The nearest point to 'desired' where 'unit' can stand without overlapping another unit's body, counting
        /// where walking units are headed as taken. Searches outward in rings, trying the side facing 'from' first, so
        /// melee units crowding one target fan out around it instead of piling onto the same spot. 'walk' checks
        /// candidates with a path (move orders); otherwise with ClampDash (dashes and shoves).
        /// </summary>
        public Vector3 FreeSpotNear(Unit unit, Vector3 desired, Vector3 from, bool walk)
        {
            if (IsSpotFree(unit, desired)) return desired;
            Vector3 back = from - desired; back.y = 0f;
            float baseAngle = back.sqrMagnitude > 1e-6f ? Mathf.Atan2(back.z, back.x) : 0f;
            float step = unit.BodyRadius + StandingGap;
            var scratch = walk ? new List<Vector3>() : null;
            for (int ring = 1; ring <= 6; ring++)
            {
                float radius = ring * step;
                int count = 6 * ring;
                for (int k = 0; k < count; k++)
                {
                    // 0, +1, -1, +2, -2 ... steps away from the approach side.
                    int offset = (k + 1) / 2 * (k % 2 == 1 ? 1 : -1);
                    float a = baseAngle + offset * Mathf.PI * 2f / count;
                    var c = new Vector3(desired.x + Mathf.Cos(a) * radius, desired.y, desired.z + Mathf.Sin(a) * radius);
                    if (!IsSpotFree(unit, c)) continue;
                    if (walk)
                    {
                        if (!Paths.TryGetPath(from, c, scratch) || scratch.Count < 2) continue;
                        c = scratch[scratch.Count - 1];
                        if (!IsSpotFree(unit, c)) continue;
                    }
                    else
                    {
                        var clamped = Paths.ClampDash(from, c);
                        if ((clamped - c).sqrMagnitude > 0.01f) continue;
                    }
                    return c;
                }
            }
            return desired;   // nowhere free nearby: overlap rather than refuse the order
        }

        /// <summary>
        /// Where 'unit' should walk to fight 'target' from 'distance' away: the free, reachable point on that circle
        /// closest to the unit's own side. Attackers ganging up on one target surround it at striking distance instead
        /// of queueing behind each other (which FreeSpotNear alone would do, pushing them out of reach).
        /// </summary>
        public Vector3 ApproachSpot(Unit unit, Unit target, float distance)
        {
            Vector3 dir = unit.Position - target.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            var scratch = new List<Vector3>();
            for (int k = 0; k < 24; k++)
            {
                int offset = (k + 1) / 2 * (k % 2 == 1 ? 1 : -1);   // 0, +15, -15, +30 ... degrees from straight on
                float a = offset * 15f * Mathf.Deg2Rad, cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                Vector3 c = target.Position + new Vector3(dir.x * cos - dir.z * sin, 0f, dir.x * sin + dir.z * cos) * distance;
                if (!IsSpotFree(unit, c)) continue;
                if (!Paths.TryGetPath(unit.Position, c, scratch) || scratch.Count < 2) continue;
                c = scratch[scratch.Count - 1];
                if (IsSpotFree(unit, c)) return c;
            }
            return target.Position + dir * distance;   // fully surrounded: the move order falls back to FreeSpotNear
        }

        private bool IsSpotFree(Unit unit, Vector3 p)
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u == unit) continue;
                Vector3 spot = IsGliding(u) ? u.GlideTo
                    : u.Phase == UnitPhase.Moving && u.Move != null && u.Move.Path.Count > 0 ? u.Move.Path[u.Move.Path.Count - 1] : u.Position;
                float dx = spot.x - p.x, dz = spot.z - p.z, min = unit.BodyRadius + u.BodyRadius + StandingGap;
                if (dx * dx + dz * dz < min * min) return false;
            }
            return true;
        }

        /// <summary>
        /// A walk that stops partway (cancelled, rooted, stunned) or arrives where someone else got stopped can leave the
        /// unit inside another body: slide it to the nearest free spot. Shown as a short shuffle, not a dodge or shove.
        /// </summary>
        private void SettleOutOfBodies(Unit u)
        {
            if (!u.IsAlive || IsSpotFree(u, u.Position)) return;
            Vector3 from = u.Position, to = FreeSpotNear(u, from, from, false);
            if ((to - from).sqrMagnitude < 1e-6f) return;
            u.Position = to;
            Emit(EventType.Dash, u.Id, null, new DashInfo { From = from, To = to, Duration = SettleDuration, Forced = true, Settle = true });
        }

        /// <summary>
        /// Slide a unit (dash, shove, pull) to 'to' over 'duration' (AdvanceGlides moves it; the view plays the same slide).
        /// It can't start anything until it lands. A new relocation mid-slide starts from where it is now, replacing the
        /// old destination (dodging again mid-dodge). Ends a walk in progress.
        /// </summary>
        private void Relocate(Unit unit, Vector3 to, float duration, bool forced)
        {
            Vector3 from = unit.Position;
            to = FreeSpotNear(unit, to, from, false);
            if (unit.Phase == UnitPhase.Moving) EndMove(unit, false);   // assumption: being relocated ends a walk in progress
            unit.GlideFrom = from;
            unit.GlideTo = to;
            unit.GlideStart = Now;
            unit.GlideEnd = Now + Mathf.Max(TickLength, duration);
            Emit(EventType.Dash, unit.Id, null, new DashInfo { From = from, To = to, Duration = duration, Forced = forced });
        }

        private void Interrupt(Unit u, string reason, bool emitReady = true)
        {
            var a = u.ActiveAction;
            if (a == null) return;
            a.Behavior.OnInterrupted(this, a);
            u.ActiveAction = null;
            if (u.Phase == UnitPhase.Windup) u.Phase = UnitPhase.Ready;
            Emit(EventType.ActionInterrupted, u.Id, null, new InterruptInfo { Reason = reason });
            if (emitReady && u.Phase == UnitPhase.Ready)
            {
                Emit(EventType.UnitReady, u.Id, null);
                Triggers.Fire(new TriggerContext { Type = TriggerType.UnitReady, Sim = this, Subject = u });
            }
        }

        /// <summary>
        /// Stun on a winding-up unit does nothing unless that action is InterruptedByStun (Heavy impacts already
        /// cancelled the windup before we get here). Recovery is replaced by the stun, never shortened.
        /// Stun drops Dodge to 0 through UnitPhase.Stunned.
        /// </summary>
        /// <param name="piercesImmunity">Unravel: lands even on a stun-immune (blocking) unit.</param>
        private void ApplyStun(Unit target, float duration, Unit source, bool piercesImmunity = false)
        {
            if (target.Phase == UnitPhase.Dead) return;
            if (target.IsStunImmune && !piercesImmunity) return;   // Block
            target.ResumeAction = null;   // a stun mid-dodge drops the remembered action
            ClearChase(target);           // and a stun ends a chase (a queued ranged order survives it)
            if (target.Phase == UnitPhase.Windup)
            {
                if (target.ActiveAction == null || !target.ActiveAction.Definition.InterruptedByStun) return;
                Interrupt(target, "stun", false);
            }
            if (target.Phase == UnitPhase.Moving)
            {
                target.Move = null;
                Emit(EventType.MoveEnded, target.Id, null, new MoveEndedInfo { Position = target.Position });
                SettleOutOfBodies(target);
            }

            float end = Now + duration;
            if (target.Phase == UnitPhase.Stunned || target.Phase == UnitPhase.Recovery) end = Mathf.Max(end, target.PhaseEndsAt);
            target.Phase = UnitPhase.Stunned;
            target.PhaseEndsAt = end;
            Emit(EventType.Stun, source != null ? source.Id : null, target.Id, new StunInfo { Duration = end - Now });
            Triggers.Fire(new TriggerContext { Type = TriggerType.Stunned, Sim = this, Subject = target, Other = source, Amount = duration });
        }

        /// <summary>Strain drains on its own once a unit has gone StrainRecoveryDelay without gaining any. Applied in
        /// StrainRecoveryTicks steps so the timeline gets a few events per second rather than one per tick.</summary>
        private void RecoverStrain()
        {
            if (tick % StrainRecoveryTicks != 0) return;
            float step = StrainRecoveryTicks * TickLength;
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u.Strain <= 0f || u.StrainRecoveryPerSecond <= 0f) continue;
                if (Now - u.LastStrainGainAt < u.StrainRecoveryDelay) continue;
                AddStrain(u, -u.StrainRecoveryPerSecond * step);
            }
        }

        /// <summary>Add (or, negative, relieve) strain, clamped to 0..MaxStrain.</summary>
        public void AddStrain(Unit u, float delta)
        {
            if (delta > 0f) u.LastStrainGainAt = Now;
            float before = u.Strain;
            u.Strain = Mathf.Clamp(u.Strain + delta, 0f, u.MaxStrain);
            if (Mathf.Abs(u.Strain - before) < 1e-4f) return;
            Emit(EventType.StrainChanged, u.Id, null, new StrainInfo { Amount = u.Strain - before, Total = u.Strain });
        }

        /// <summary>
        /// Haste/slow changed a unit's speeds mid-action: rescale the time that is LEFT in its windup/recovery,
        /// and restart a walk in progress at the new speed. Emits events so the view can follow.
        /// </summary>
        private void HandleSpeedChange(Unit u, float oldAction, float oldMove)
        {
            float newAction = u.EffectiveActionSpeed(), newMove = u.EffectiveMoveSpeed();
            bool actionChanged = Mathf.Abs(newAction - oldAction) > 1e-4f;
            bool moveChanged = Mathf.Abs(newMove - oldMove) > 1e-4f;
            if (!actionChanged && !moveChanged) return;

            if (actionChanged && (u.Phase == UnitPhase.Windup || u.Phase == UnitPhase.Recovery) && u.PhaseEndsAt > Now)
            {
                float newEnd = Now + (u.PhaseEndsAt - Now) * oldAction / newAction;
                u.PhaseEndsAt = newEnd;
                if (u.Phase == UnitPhase.Windup && u.ActiveAction != null) u.ActiveAction.ResolveAt = newEnd;
            }

            if (moveChanged && u.Phase == UnitPhase.Moving && u.Move != null)
            {
                u.Move.Speed = newMove;
                var rest = new List<Vector3> { u.Position };
                for (int i = u.Move.NextIndex; i < u.Move.Path.Count; i++) rest.Add(u.Move.Path[i]);
                Emit(EventType.MoveStarted, u.Id, null, new MoveInfo { Path = rest, Speed = newMove });
            }

            Emit(EventType.SpeedChanged, u.Id, null, new SpeedInfo { ActionSpeed = newAction, MoveSpeed = newMove });
        }

        private void Emit(EventType type, string actorId, string targetId, object payload = null)
        {
            Timeline.AddEvent(new TimelineEvent(Now, actorId, targetId, type, payload));
        }
    }
}
