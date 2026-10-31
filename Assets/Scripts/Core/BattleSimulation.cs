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

        // Elevation (see TerrainMap). A fall: shoved off a ledge, or hit out of a jump. The unit is stunned until it lands
        // plus FallLandingSeconds, and takes FallDamageBase + FallDamagePerMeter * the height it fell.
        public float FallGravity = 12f;            // m/s^2: how long a fall takes (gentler than real gravity, so it reads)
        public float FallLandingSeconds = 0.25f;
        public float FallDamageBase = 3f;
        public float FallDamagePerMeter = 6f;
        /// <summary>Ranged attacks from at least HighGroundMinHeight above their target get this much extra hit chance.</summary>
        public float HighGroundAccuracyBonus = 0.15f;
        public float HighGroundMinHeight = 0.75f;
        /// <summary>Wake waiting (or walking) enemy-team units when an attack starts winding up on them (AlertTarget).</summary>
        public bool AlertAiTargets = true;

        // Focus (the Wait order): FocusDelay seconds of unbroken focusing, and strain drains at FocusStrainPerSecond on top
        // of normal recovery (no recovery delay) until the unit does anything else. Taking damage restarts the count.
        public float FocusDelay = 1.5f;
        public float FocusStrainPerSecond = 35f;
        /// <summary>Extra strain a Transcended Focus costs (it focuses until Focused in one order).</summary>
        public float TranscendFocusStrain = 0f;

        // Transcend (UnitCommand.Transcend). The strain price is per ability (ActionDefinition.TranscendStrain).
        /// <summary>A Heavy hit that lands on a (plain) Block puts Block on cooldown this long.</summary>
        public float HeavyBlockCooldown = 1f;
        /// <summary>Transcended Dash: this many times the dash distance, covered in TranscendDashSeconds.</summary>
        public float TranscendDashDistanceMult = 1.8f;
        public float TranscendDashSeconds = 0.1f;

        public TerrainMap Terrain { get { return State.Terrain; } }
        private TerrainPaths terrainPaths;

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
            if (State.Terrain == null) State.Terrain = Timeline.InitialState.Terrain = new TerrainMap();
            foreach (var u in State.Units) u.Position = Terrain.OnGround(u.Position);   // everyone starts standing on the ground
            foreach (var u in Timeline.InitialState.Units) u.Position = Terrain.OnGround(u.Position);
            rng = new System.Random(seed);
            Visibility = new SimVisibility(staticOcclusion ?? new NoStaticOcclusion(), State);
            Paths = terrainPaths = new TerrainPaths(paths ?? new StraightLinePathService(), State.Terrain);
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
            // Hit while focusing: the count toward FocusDelay starts over.
            Triggers.Subscribe(TriggerType.DamageTaken, ctx => { if (ctx.Subject.FocusSince >= 0f) BreakFocus(ctx.Subject, true); });
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
            if (u.IsJumping) { reason = "mid-jump"; return false; }
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
            var rest = new List<Vector3> { from };
            for (int i = u.Move.NextIndex; i < u.Move.Path.Count; i++)
            {
                length += FlatDistance(from, u.Move.Path[i]);
                from = u.Move.Path[i];
                rest.Add(from);
            }
            return length / Mathf.Max(0.01f, u.Move.Speed) + Terrain.JumpSecondsAlong(rest);
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

        /// <summary>The Dash ability: slide toward 'point' like a dodge (same speed and reach), or Transcended: further and
        /// much faster. Doesn't touch a windup.</summary>
        public void DashUnitTo(Unit unit, Vector3 point, bool transcended = false)
        {
            if (!unit.IsAlive || unit.IsRooted) return;
            Vector3 to = DashDestination(unit, point, transcended);
            if ((to - unit.Position).sqrMagnitude > 1e-4f) Relocate(unit, to, transcended ? TranscendDashSeconds : DashDuration, false);
        }

        /// <summary>How far the unit's Dash reaches (Transcended or not).</summary>
        public float DashReach(Unit unit, bool transcended = false)
        {
            return unit.DashDistanceNow() * (transcended ? TranscendDashDistanceMult : 1f);
        }

        /// <summary>Where a dash toward 'point' lands: capped at the unit's dash distance and clamped to walkable space.</summary>
        public Vector3 DashDestination(Unit unit, Vector3 point, bool transcended = false)
        {
            Vector3 d = point - unit.Position; d.y = 0f;
            float max = DashReach(unit, transcended);
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
        public bool CanUse(Unit actor, ActionDefinition def, Unit target, out string reason, bool checkResources = true, bool transcend = false)
        {
            reason = null;
            if (checkResources && !CanAfford(actor, def, out reason, target != null ? target.Id : null, transcend)) return false;
            if (def.Targeting == TargetType.Enemy)
            {
                if (target == null || !target.IsAlive || target.Team == actor.Team) { reason = "invalid target"; return false; }
                if (!IsTargetableBy(target, actor)) { reason = "target hidden"; return false; }
                if (Vector3.Distance(actor.Position, target.Position) > def.Range) { reason = "out of range"; return false; }
                if (def.IsRanged && !def.PiercesTerrain && !Visibility.HasLineOfSight(actor, target)) { reason = "no line of sight"; return false; }
            }
            else if (def.Targeting == TargetType.Ally || def.Targeting == TargetType.AnyUnit)
            {
                if (target == null || !target.IsAlive) { reason = "invalid target"; return false; }
                if (def.Targeting == TargetType.Ally && target.Team != actor.Team) { reason = "invalid target"; return false; }
                if (def.CannotTargetSelf && target.Id == actor.Id) { reason = "can't target yourself"; return false; }
                if (!IsTargetableBy(target, actor)) { reason = "target hidden"; return false; }
                if (Vector3.Distance(actor.Position, target.Position) > def.Range) { reason = "out of range"; return false; }
                if (def.Targeting == TargetType.AnyUnit && def.IsRanged && !HasSightOf(actor, def, target)) { reason = "no line of sight"; return false; }
            }
            return true;
        }

        /// <summary>Cooldown, energy and required-status checks only (no target). Drives greyed-out ability buttons.</summary>
        public bool CanAfford(Unit actor, ActionDefinition def, out string reason, string targetId = null, bool transcend = false)
        {
            reason = null;
            float readyAt;
            if (actor.CooldownReadyAt.TryGetValue(def.Id, out readyAt) && readyAt > Now) { reason = "cooldown"; return false; }
            var required = def.RequiredStatuses();
            if (required.Length > 0 && !System.Array.Exists(required, actor.HasStatus)) { reason = "needs " + string.Join(" or ", required); return false; }
            var forbidding = actor.StatusForbidding(def.Id);
            if (forbidding != null) { reason = "not during " + forbidding.Id.Replace('_', ' '); return false; }
            if (EnergyCostOf(actor, def) > actor.Energy + 1e-4f) { reason = "not enough energy"; return false; }
            float strain = StrainCostOf(actor, def, targetId, transcend);
            if (strain > 0f && actor.Strain + strain > actor.MaxStrain + 1e-4f && !ActionBehaviors.Get(def.BehaviorId).AllowsStrainOverflow) { reason = "too much strain"; return false; }
            return true;
        }

        public float EnergyCostOf(Unit actor, ActionDefinition def) { return ActionBehaviors.Get(def.BehaviorId).EnergyCost(this, actor, def); }

        /// <summary>Strain for using 'def', plus its TranscendStrain when Transcended (and it can be). A cost (not relief) is
        /// scaled by the actor's statuses (Wrath makes it free).</summary>
        public float StrainCostOf(Unit actor, ActionDefinition def, string targetId = null, bool transcend = false)
        {
            float cost = ActionBehaviors.Get(def.BehaviorId).StrainCost(this, actor, def, targetId) + (transcend && def.CanTranscend ? def.TranscendStrain : 0f);
            return cost > 0f ? cost * actor.StrainCostMult() : cost;
        }

        /// <summary>Seconds a walk along 'path' takes the unit: ground distance at its speed now, plus any jumps.</summary>
        public float MoveSeconds(Unit unit, List<Vector3> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++) length += FlatDistance(path[i - 1], path[i]);
            return length / Mathf.Max(0.01f, unit.EffectiveMoveSpeed()) + Terrain.JumpSecondsAlong(path);
        }

        /// <summary>Hidden (untargetable) units can't be targeted or hit by the other team.</summary>
        public bool IsTargetableBy(Unit target, Unit actor) { return target.Team == actor.Team || !target.IsHidden; }

        /// <summary>Can the actor see this unit to target it with 'def'? Abilities that pierce terrain don't need to.</summary>
        public bool HasSightOf(Unit actor, ActionDefinition def, Unit target)
        {
            return (def != null && def.PiercesTerrain) || target.Id == actor.Id || Visibility.HasLineOfSight(actor, target);
        }

        /// <summary>Standing at least HighGroundMinHeight above the target.</summary>
        public bool HasHighGround(Unit attacker, Unit target) { return attacker.Position.y - target.Position.y >= HighGroundMinHeight - 1e-3f; }

        /// <summary>Extra hit chance this attack gets from height: HighGroundAccuracyBonus for a ranged one from high ground.</summary>
        public float HeightAccuracyBonus(Unit attacker, Unit target, ActionDefinition def)
        {
            return def != null && def.IsRanged && HasHighGround(attacker, target) ? HighGroundAccuracyBonus : 0f;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

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

        /// <summary>A line can pass a unit this far below its feet and still catch it (and up to LineReachUp of its height above).</summary>
        public float LineReachDown = 0.5f;
        public float LineReachUp = 0.75f;   // fraction of BodyHeight

        /// <summary>
        /// Height of a ranged Line attack 'along' metres out: it ramps from the actor's feet to the aimed point's elevation, then
        /// carries on level. So a line aimed onto a ledge climbs to it, and one aimed along the ground passes under it.
        /// </summary>
        public static float LineHeightAt(Vector3 origin, Vector3 targetPos, float along)
        {
            float aimDist = FlatDistance(origin, targetPos);
            if (aimDist < 1e-3f) return targetPos.y;
            return Mathf.Lerp(origin.y, targetPos.y, Mathf.Clamp01(along / aimDist));
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
                    if (along < -u.BodyRadius || along > def.Range + u.BodyRadius || side > def.LineWidth / 2f + u.BodyRadius) continue;
                    // Ranged lines (spells, projectiles) fly at the aimed elevation; a melee sweep catches anyone in reach.
                    float h = def.IsRanged ? LineHeightAt(origin, targetPos, along) : p.y;
                    if (h >= p.y - LineReachDown && h <= p.y + u.BodyHeight * LineReachUp) hits.Add(u);
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
            EmitStatusTimer(target, status, source);
            HandleSpeedChange(target, oldAction, oldMove);
            if (status.Rooted && target.Phase == UnitPhase.Moving) EndMove(target);
        }

        /// <summary>Statuses lasting at least this long count as "until removed" and get no timer.</summary>
        public const float UntimedStatus = 1e6f;

        /// <summary>Every (re)application of a timed status announces its end time, so the HUD can count it down (StatusTimer).
        /// To lengthen one, re-apply a copy with a later ExpiresAt (as Final Cut does with the trance).</summary>
        private void EmitStatusTimer(Unit target, StatusEffect status, Unit source)
        {
            if (status.ExpiresAt >= UntimedStatus) return;
            Emit(EventType.StatusTimer, source != null ? source.Id : null, target.Id,
                new StatusTimerInfo { Id = status.Id, ExpiresAt = status.ExpiresAt, Duration = Mathf.Max(0f, status.ExpiresAt - Now) });
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
            Relocate(target, Paths.ClampDash(from, to), DisplaceDuration, true, source);
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
                if (!unit.IsAlive || IsGliding(unit)) continue;   // nothing new mid-dash
                // A charging unit comes up for a decision every ChargeDecisionAt: keep charging or release.
                bool charging = unit.Phase == UnitPhase.Charging;
                if (charging ? Now + 1e-4f < unit.ChargeDecisionAt : unit.Phase != UnitPhase.Ready) continue;
                var kind = charging ? DecisionKind.Charge : DecisionKind.Action;

                // A Mesmerize counter whose target is gone just ends.
                if (unit.MesmerStrikesLeft > 0)
                {
                    var mt = GetUnit(unit.MesmerTargetId);
                    if (mt == null || !mt.IsAlive) unit.MesmerStrikesLeft = 0;
                }

                UnitCommand cmd = null;
                bool isPlayer = unit.Team == Team.Player;
                if (isPlayer)
                {
                    Queue<UnitCommand> q;
                    if (queues.TryGetValue(unit.Id, out q) && q.Count > 0) cmd = q.Dequeue();
                }
                else if (unit.MesmerStrikesLeft > 0 && !charging)
                {
                    cmd = UnitCommand.UseAction(unit.Id, unit.MesmerStrike, unit.MesmerTargetId);   // the AI always keeps striking
                }
                else
                {
                    cmd = EnemyAI.Decide(this, unit) ?? (charging ? UnitCommand.ContinueCharge(unit.Id, 0.25f) : UnitCommand.Wait(unit.Id, 0.5f));
                }

                if (cmd == null)
                {
                    decisions.Add(new DecisionRequest { Kind = kind, UnitId = unit.Id });
                    continue;
                }

                string reason;
                if (!TryStartCommand(unit, cmd, out reason))
                {
                    if (isPlayer)
                    {
                        queues.Remove(unit.Id);   // a stale plan: drop the rest and ask again
                        decisions.Add(new DecisionRequest { Kind = kind, UnitId = unit.Id, Reason = reason });
                    }
                    else if (charging) TryStartCommand(unit, UnitCommand.ContinueCharge(unit.Id, 0.25f), out reason);
                    else StartWait(unit, 0.5f);   // never let an AI stall the sim on a bad command
                }
            }
            return decisions;
        }

        private bool TryStartCommand(Unit unit, UnitCommand cmd, out string reason)
        {
            reason = null;
            // Charging can't be cancelled: the only orders are keep charging and release.
            bool chargeOrder = cmd.Type == CommandType.ContinueCharge || cmd.Type == CommandType.ReleaseCharge;
            if (unit.Phase == UnitPhase.Charging && !chargeOrder) { reason = "charging: keep charging or release it"; return false; }
            if (unit.Phase != UnitPhase.Charging && chargeOrder) { reason = "not charging"; return false; }
            // Any order other than the next rapid strike ends a Mesmerize counter.
            if (unit.MesmerStrikesLeft > 0 && (cmd.Action == null || cmd.Action != unit.MesmerStrike)) unit.MesmerStrikesLeft = 0;
            // Holding someone (Subjugate): only moving, headbutting on (a Wait counts as that) or the throw.
            if (unit.Grapple != null)
            {
                if (cmd.Type == CommandType.HoldGrapple || cmd.Type == CommandType.Wait) { HoldGrapple(unit); return true; }
                if (cmd.Type == CommandType.ReleaseGrapple) { StartThrow(unit); return true; }
                if (cmd.Type != CommandType.Move) { reason = "grappling: move, keep headbutting or throw"; return false; }
            }
            else if (cmd.Type == CommandType.HoldGrapple || cmd.Type == CommandType.ReleaseGrapple) { reason = "not grappling"; return false; }

            switch (cmd.Type)
            {
                case CommandType.ContinueCharge:
                    unit.ChargeDecisionAt = Now + Mathf.Max(TickLength, cmd.WaitSeconds);
                    return true;

                case CommandType.ReleaseCharge:
                    return ReleaseCharge(unit, cmd, out reason);

                case CommandType.Wait:
                    unit.LastActionId = null;   // a wait or a move breaks a follow-up chain
                    StartWait(unit, cmd.WaitSeconds, cmd.Transcend);
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
                    if (cmd.Action.IsCharge)
                    {
                        // Targeting happens at release, so only resources and the behavior's own rules are checked now.
                        if (!CanAfford(unit, cmd.Action, out reason, null, cmd.Transcend)) return false;
                        var chargeBehavior = ActionBehaviors.Get(cmd.Action.BehaviorId);
                        if (!chargeBehavior.CanStart(this, unit, cmd, out reason)) return false;
                        StartCharge(unit, cmd, chargeBehavior);
                        return true;
                    }
                    var target = GetUnit(cmd.TargetUnitId);
                    if (!CanUse(unit, cmd.Action, target, out reason, true, cmd.Transcend))
                    {
                        if (!cmd.WaitForRange || reason != "out of range") return false;
                        if (!cmd.Action.IsRanged) return StartChase(unit, cmd, target, out reason);
                        QueueRanged(unit, cmd, target);   // the unit stays ready: it is asked for its next order straight away
                        return true;
                    }
                    // Point-targeted actions must land in range; a line only takes its direction from the point.
                    if (cmd.Action.Targeting == TargetType.Position && cmd.Action.Shape != AreaShape.Line &&
                        Vector3.Distance(unit.Position, cmd.TargetPos) > cmd.Action.Range) { reason = "out of range"; return false; }
                    // A unit and a point: the point must be within DestinationRange of the unit.
                    if (cmd.Action.DestinationRange > 0f && FlatDistance(target.Position, cmd.TargetPos) > cmd.Action.DestinationRange + 1e-3f) { reason = "destination out of range"; return false; }
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

        /// <summary>A Wait order, shown to the player as Focus: back-to-back ones add up toward FocusDelay (see TickFocus).</summary>
        /// Transcended, it lasts exactly until the unit is Focused (and stretches if a hit restarts the count), so the unit is
        /// asked for orders the moment strain starts draining; already Focused, it is a plain wait.
        private void StartWait(Unit unit, float seconds, bool transcend = false)
        {
            if (unit.Grapple != null) { HoldGrapple(unit); return; }   // no focusing with someone in your grip
            if (unit.FocusSince < 0f)
            {
                unit.FocusSince = Now;
                ApplyStatus(unit, new StatusEffect { Id = StatusIds.Focusing, SourceId = unit.Id, ExpiresAt = Now + FocusDelay }, unit);
            }
            unit.FocusTranscended = transcend && !unit.HasStatus(StatusIds.Focused);
            if (unit.FocusTranscended)
            {
                seconds = unit.FocusSince + FocusDelay - Now + TickLength;   // a tick past, so Focused is on before it ends
                if (TranscendFocusStrain > 0f) AddStrain(unit, TranscendFocusStrain);
            }
            unit.Waiting = true;
            unit.Phase = UnitPhase.Recovery;
            unit.PhaseEndsAt = Now + seconds;
            Emit(EventType.ActionStarted, unit.Id, null, new ActionStartedInfo { ActionId = "wait", Windup = 0f, Recovery = seconds, Transcended = unit.FocusTranscended });
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

        // ------------------------------------------------------------------ charge abilities

        /// <summary>
        /// A charge ability (ActionDefinition.IsCharge): pay its costs and start charging. From here the unit can only keep
        /// charging (ContinueCharge) or release it (ReleaseCharge, which picks the target); it is asked at once and then every
        /// time its ContinueCharge runs out. Charging drains the definition's per-second resources and breaks on any damage.
        /// </summary>
        private void StartCharge(Unit unit, UnitCommand cmd, ActionBehavior behavior)
        {
            var def = cmd.Action;
            unit.Waiting = false;
            ClearQueued(unit, null);
            PayCosts(unit, def, behavior, null);
            unit.ActiveAction = new ActionInstance { Actor = unit, Definition = def, Behavior = behavior, StartedAt = Now, ResolveAt = float.MaxValue };
            unit.Phase = UnitPhase.Charging;
            unit.PhaseEndsAt = float.MaxValue;
            unit.ChargeDecisionAt = Now;
            unit.LastActionId = def.Id;
            unit.LastActionTargetId = null;
            Emit(EventType.ChargeStarted, unit.Id, null, new ChargeInfo { ActionId = def.Id, MaxSeconds = def.MaxChargeSeconds });
        }

        /// <summary>0..1: how far a charging unit is toward full power.</summary>
        public float ChargeLevel(Unit unit)
        {
            if (unit.Phase != UnitPhase.Charging || unit.ActiveAction == null) return 0f;
            var def = unit.ActiveAction.Definition;
            return def.MaxChargeSeconds > 0f ? Mathf.Clamp01((Now - unit.ActiveAction.StartedAt) / def.MaxChargeSeconds) : 1f;
        }

        private bool ReleaseCharge(Unit unit, UnitCommand cmd, out string reason)
        {
            reason = null;
            var charged = unit.ActiveAction;
            if (charged == null) { reason = "not charging"; return false; }
            var def = charged.Definition;
            var release = def.Targeting == TargetType.Position ? UnitCommand.UseActionAt(unit.Id, def, cmd.TargetPos)
                                                               : UnitCommand.UseAction(unit.Id, def, cmd.TargetUnitId ?? unit.Id);
            if (def.Targeting == TargetType.Enemy || def.Targeting == TargetType.Ally)
            {
                if (!CanUse(unit, def, GetUnit(release.TargetUnitId), out reason, false)) return false;   // already paid for
            }
            else if (def.Targeting == TargetType.Position && def.Shape != AreaShape.Line && Vector3.Distance(unit.Position, cmd.TargetPos) > def.Range)
            {
                reason = "out of range"; return false;
            }
            float level = ChargeLevel(unit);
            unit.ActiveAction = null;
            unit.Phase = UnitPhase.Ready;
            StartAction(unit, release, charged.Behavior, def.Windup / unit.EffectiveActionSpeed(), level);
            return true;
        }

        /// <summary>Charging units drain their charge resources (in StrainRecoveryTicks steps, like strain recovery).</summary>
        private void TickCharges()
        {
            if (tick % StrainRecoveryTicks != 0) return;
            float step = StrainRecoveryTicks * TickLength;
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u.Phase != UnitPhase.Charging || u.ActiveAction == null) continue;
                var def = u.ActiveAction.Definition;
                if (def.ChargeStrainPerSecond > 0f) AddStrain(u, def.ChargeStrainPerSecond * step);
                if (def.ChargeHealthPerSecond > 0f)
                {
                    float drain = def.ChargeHealthPerSecond * step;
                    u.Health -= drain;
                    Emit(EventType.Damage, u.Id, u.Id, new DamageInfo { ActionId = def.Id, Amount = drain, Impact = ImpactType.Light, RemainingHealth = u.Health });
                }
            }
        }

        // ------------------------------------------------------------------ helpers for movement abilities

        /// <summary>Slide a unit to 'to' over 'duration' (Phantom Strike, Mesmerize's blink). Can't start anything until it lands.
        /// phaseFrom: for a move that passes through walls (Phantom Strike), the point a crowded landing spot is searched
        /// from instead of the unit, so the search stays on the far side.</summary>
        public void SlideUnitTo(Unit unit, Vector3 to, float duration, Vector3? phaseFrom = null)
        {
            if (!unit.IsAlive || (to - unit.Position).sqrMagnitude <= 1e-4f) return;
            Relocate(unit, to, duration, false, null, phaseFrom);
        }

        /// <summary>Break a unit's windup or charge, if it has one (Stutter). True if something was interrupted.</summary>
        public bool InterruptUnit(Unit unit, string reason)
        {
            if (unit == null || !unit.IsAlive || unit.ActiveAction == null) return false;
            if (unit.Phase != UnitPhase.Windup && unit.Phase != UnitPhase.Charging) return false;
            Interrupt(unit, reason);
            return true;
        }

        public const float MesmerBlinkSeconds = 0.06f;

        /// <summary>
        /// Mesmerize's counter: drop whatever the unit was doing, blink next to the attacker and line up 'strikes' rapid
        /// strikes at it. Each strike is its own order (the AI always continues; a player can stop after any), and any
        /// other order ends the counter.
        /// </summary>
        public void StartMesmerCounter(Unit unit, Unit attacker, ActionDefinition strike, int strikes)
        {
            if (!unit.IsAlive || attacker == null || !attacker.IsAlive || strike == null) return;
            if (unit.Phase == UnitPhase.Windup || unit.Phase == UnitPhase.Charging) Interrupt(unit, "mesmerize", false);
            HaltWalk(unit);
            ClearChase(unit);
            unit.Waiting = false;
            unit.ResumeAction = null;
            unit.Phase = UnitPhase.Ready;
            unit.PhaseEndsAt = Now;
            unit.MesmerTargetId = attacker.Id;
            unit.MesmerStrike = strike;
            unit.MesmerStrikesLeft = strikes;
            Vector3 away = unit.Position - attacker.Position; away.y = 0f;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward;
            SlideUnitTo(unit, attacker.Position + away * (unit.BodyRadius + attacker.BodyRadius + 0.3f), MesmerBlinkSeconds);
            Emit(EventType.UnitReady, unit.Id, null);
        }

        /// <summary>The player chose to stop a Mesmerize counter early.</summary>
        public void CancelMesmer(string unitId)
        {
            var u = GetUnit(unitId);
            if (u != null) u.MesmerStrikesLeft = 0;
        }

        // ------------------------------------------------------------------ grapples (Subjugate)

        // Holding someone: a headbutt (the Subjugate's LinkedAction) every GrappleHeadbuttSeconds while the grab's stun lasts,
        // then (or when the holder lets go early) a GrappleThrowWindup windup and a throw GrappleThrowDistance onward that
        // deals the Subjugate's own damage and stuns for GrappleThrowStun.
        public float GrappleHeadbuttSeconds = 0.5f;
        public float GrappleThrowWindup = 0.35f;
        public float GrappleThrowDistance = 4f;
        public float GrappleThrowStun = 0.5f;
        /// <summary>ActionInstance.Tag of Subjugate's throw (the same action, second stage).</summary>
        public const string ThrowTag = "subjugate_throw";

        /// <summary>How far in front of the grappler the held unit is carried.</summary>
        public static float CarryOffset(Unit holder, Unit held) { return holder.BodyRadius + held.BodyRadius + 0.1f; }

        /// <summary>
        /// Subjugate's grab landed and stunned 'target': 'unit' holds it (stun immune, "grappling") and carries it in front
        /// of itself until the stun runs out or it lets go (ReleaseGrapple), then throws it. Meanwhile the unit can only move
        /// or keep headbutting: it is asked for orders after every headbutt (see TickGrapples, HoldGrapple).
        /// </summary>
        public void StartGrapple(Unit unit, Unit target, ActionDefinition def)
        {
            if (!unit.IsAlive || !target.IsAlive || unit.Grapple != null || target.Phase != UnitPhase.Stunned || target.IsLifted || target.FallLandAt >= 0f) return;
            if (target.HeldById != null) ReleaseHeld(target);   // taken out of someone else's grip
            if (target.Grapple != null) EndGrapple(target);     // and it lets go of whoever it was holding
            target.GlideEnd = -1f;                              // a shove in progress stops here
            Vector3 facing = target.Position - unit.Position; facing.y = 0f;
            unit.Grapple = new GrappleState
            {
                TargetId = target.Id, Definition = def, NextHeadbuttAt = Now + GrappleHeadbuttSeconds, EndsAt = target.PhaseEndsAt,
                Facing = facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.forward,
            };
            target.HeldById = unit.Id;
            ApplyStatus(unit, new StatusEffect { Id = StatusIds.Grappling, SourceId = unit.Id, ExpiresAt = float.MaxValue, StunImmune = true }, unit);
            ApplyStatus(target, new StatusEffect { Id = StatusIds.Grappled, SourceId = unit.Id, ExpiresAt = float.MaxValue }, unit);
            PlaceHeld(unit, target);
            Emit(EventType.Carry, unit.Id, target.Id, new CarryInfo { Carrying = true, Offset = CarryOffset(unit, target), Position = target.Position });
            HoldGrapple(unit);
        }

        /// <summary>Keep holding: stand and headbutt, asked for orders again right after the next headbutt.</summary>
        private void HoldGrapple(Unit unit)
        {
            var g = unit.Grapple;
            if (g == null || g.Throwing) return;
            unit.Waiting = false;
            unit.Phase = UnitPhase.Recovery;
            unit.PhaseEndsAt = Mathf.Max(Now + TickLength, Mathf.Min(g.NextHeadbuttAt, g.EndsAt));
        }

        /// <summary>Let go into the throw: its windup starts now (the held unit stays stunned through it).</summary>
        private void StartThrow(Unit unit)
        {
            var g = unit.Grapple;
            var held = g != null ? GetUnit(g.TargetId) : null;
            if (g == null || g.Throwing || held == null) return;
            HaltWalk(unit);
            g.Throwing = true;
            unit.Waiting = false;
            var def = g.Definition;
            float speed = unit.EffectiveActionSpeed(), windup = GrappleThrowWindup / speed;
            var inst = new ActionInstance
            {
                Actor = unit, Definition = def, Behavior = ActionBehaviors.Get(def.BehaviorId), TargetUnitId = held.Id, TargetPos = held.Position,
                StartedAt = Now, ResolveAt = Now + windup, Tag = ThrowTag,
            };
            unit.ActiveAction = inst;
            unit.Phase = UnitPhase.Windup;
            unit.PhaseEndsAt = inst.ResolveAt;
            ApplyStun(held, windup + DisplaceDuration, unit, true);
            Emit(EventType.ActionStarted, unit.Id, held.Id, new ActionStartedInfo { ActionId = def.Id, Windup = windup, Recovery = def.Recovery / speed, TargetPos = held.Position });
            Triggers.Fire(new TriggerContext { Type = TriggerType.ActionStarted, Sim = this, Subject = unit, Action = def });
        }

        /// <summary>Stop holding (the throw went off, or the hold broke): both statuses come off and the held unit is put down.
        /// A unit that was only holding (not throwing) is handed back for orders.</summary>
        public void EndGrapple(Unit unit)
        {
            var g = unit.Grapple;
            if (g == null) return;
            unit.Grapple = null;
            RemoveStatus(unit, StatusIds.Grappling);
            var held = GetUnit(g.TargetId);
            if (held != null)
            {
                held.HeldById = null;
                RemoveStatus(held, StatusIds.Grappled);
                Emit(EventType.Carry, unit.Id, held.Id, new CarryInfo { Carrying = false, Position = held.Position });
            }
            if (!g.Throwing && unit.IsAlive && unit.Phase == UnitPhase.Recovery) EndRecoveryNow(unit);
        }

        /// <summary>Free 'held' from whoever is holding it.</summary>
        private void ReleaseHeld(Unit held)
        {
            var holder = GetUnit(held.HeldById);
            if (holder != null && holder.Grapple != null && holder.Grapple.TargetId == held.Id) EndGrapple(holder);
            held.HeldById = null;
        }

        /// <summary>The direction a unit holding someone is facing right now (where the throw goes).</summary>
        public Vector3 GrappleFacing(Unit unit) { return unit.Grapple != null ? unit.Grapple.Facing : Vector3.forward; }

        private void PlaceHeld(Unit unit, Unit held)
        {
            held.Position = Paths.ClampDash(unit.Position, unit.Position + unit.Grapple.Facing * CarryOffset(unit, held));
        }

        private void TickGrapples()
        {
            foreach (var u in State.Units)
            {
                if (u.Grapple == null)
                {
                    // Stun immunity from a grab that never took hold (missed, dodged, blocked) goes once the windup is over.
                    if (u.HasStatus(StatusIds.Grappling) && !(u.Phase == UnitPhase.Windup && u.ActiveAction != null && u.ActiveAction.Behavior is SubjugateBehavior))
                        RemoveStatus(u, StatusIds.Grappling);
                    continue;
                }
                var g = u.Grapple;
                var held = GetUnit(g.TargetId);
                // The hold breaks if either goes down, the holder is knocked out of it (Unravel, a fall), or the held unit is lifted away.
                if (!u.IsAlive || held == null || !held.IsAlive || u.Phase == UnitPhase.Stunned || held.IsLifted || held.FallLandAt >= 0f) { EndGrapple(u); continue; }
                if (!g.Throwing && u.Phase == UnitPhase.Moving && u.Move != null && u.Move.NextIndex < u.Move.Path.Count)
                {
                    Vector3 d = u.Move.Path[u.Move.NextIndex] - u.Position; d.y = 0f;
                    if (d.sqrMagnitude > 1e-4f) g.Facing = d.normalized;   // carried in front, the way it walks
                }
                PlaceHeld(u, held);
                if (g.Throwing) continue;
                if (Now + 1e-4f >= g.EndsAt) { StartThrow(u); continue; }
                if (Now + 1e-4f >= g.NextHeadbuttAt)
                {
                    g.NextHeadbuttAt += GrappleHeadbuttSeconds;
                    if (g.Definition.LinkedAction != null) QueueFreeAction(u, g.Definition.LinkedAction, held.Id, 0f);
                }
            }
        }

        // ------------------------------------------------------------------ lifts (Amblivate) and teleports (Wrinkles of Reality)

        /// <summary>How long a lifted unit takes to rise to its height.</summary>
        public float LiftRiseSeconds = 0.4f;

        /// <summary>
        /// Raise a stunned unit 'height' metres straight up and hold it there until its stun runs out, then drop it: a fall
        /// (StartFall) from that height, with the fall damage that goes with it, credited to 'source'. Nothing moves it while
        /// it hangs there, and melee can't reach it.
        /// </summary>
        public void LiftUnit(Unit target, float height, Unit source)
        {
            if (!target.IsAlive || target.Phase != UnitPhase.Stunned || target.IsLifted || target.FallLandAt >= 0f || height <= 0f) return;
            if (target.HeldById != null) ReleaseHeld(target);
            if (target.Grapple != null) EndGrapple(target);
            target.GlideEnd = -1f;
            target.LiftFrom = target.Position;
            target.LiftTo = target.Position + Vector3.up * height;
            target.LiftStart = Now;
            target.LiftRiseEnd = Now + LiftRiseSeconds;
            target.LiftDropAt = Mathf.Max(target.LiftRiseEnd, target.PhaseEndsAt);
            target.PhaseEndsAt = target.LiftDropAt;   // never free to act in mid-air
            target.LiftSourceId = source != null ? source.Id : null;
            Emit(EventType.Lift, source != null ? source.Id : null, target.Id,
                new LiftInfo { From = target.LiftFrom, To = target.LiftTo, RiseSeconds = LiftRiseSeconds, DropAt = target.LiftDropAt });
        }

        private void TickLifts()
        {
            foreach (var u in State.Units)
            {
                if (!u.IsLifted || !u.IsAlive) continue;
                float k = Mathf.Clamp01((Now - u.LiftStart) / Mathf.Max(1e-4f, u.LiftRiseEnd - u.LiftStart));
                u.Position = Vector3.Lerp(u.LiftFrom, u.LiftTo, k * k * (3f - 2f * k));
                if (Now + 1e-4f < u.LiftDropAt) continue;
                u.LiftDropAt = -1f;
                Vector3 landing = Terrain.OnGround(u.Position);
                StartFall(u, landing, u.Position.y - landing.y, 0f, GetUnit(u.LiftSourceId), false);
            }
        }

        /// <summary>The walkable point nearest 'p' (the navmesh's, where there is one): where a teleport can land.</summary>
        public Vector3 WalkablePointNear(Vector3 p)
        {
            var path = new List<Vector3>();
            return Paths.TryGetPath(p, p, path) && path.Count > 0 ? path[path.Count - 1] : Terrain.OnGround(p);
        }

        /// <summary>
        /// Move a unit to 'to' at once (Wrinkles of Reality): no path, no walls, no fall. Whatever it was doing in motion stops
        /// (a walk, a dash, a jump, a lift; a grip on it breaks); an action it was winding up carries on from the new spot.
        /// </summary>
        public void TeleportUnit(Unit unit, Vector3 to, Unit source)
        {
            if (unit == null || !unit.IsAlive || unit.FallLandAt >= 0f) return;
            if (unit.HeldById != null) ReleaseHeld(unit);
            unit.LiftDropAt = -1f;
            unit.GlideEnd = -1f;
            if (unit.IsJumping) { unit.JumpEnd = -1f; unit.Position = Terrain.OnGround(unit.Position); }
            Vector3 from = unit.Position;
            to = Terrain.OnGround(to);
            to = Terrain.OnGround(FreeSpotNear(unit, to, to, false));   // crowded: the nearest free spot around the destination
            if (unit.Phase == UnitPhase.Moving) EndMove(unit, false);
            unit.Position = to;
            Emit(EventType.Dash, unit.Id, null, new DashInfo { From = from, To = to, Duration = 0f, Forced = true, Teleport = true });
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

        /// <summary>A chase only strikes once inside this fraction of the ability's range, so a target stepping back doesn't
        /// leave it swinging at the edge (never closer than the two bodies allow). Abilities that close the distance themselves
        /// (ActionBehavior.ClosesDistance: Phantom Strike) go as soon as the target is in range.</summary>
        public float ChaseStrikeFraction = 0.5f;

        private bool ChaseCloseEnough(Unit u, ActionDefinition def, Unit target)
        {
            if (ActionBehaviors.Get(def.BehaviorId).ClosesDistance) return true;   // CanUse already found it in range
            float close = Mathf.Min(def.Range, Mathf.Max(def.Range * ChaseStrikeFraction, u.BodyRadius + target.BodyRadius + 0.15f));
            if (Vector3.Distance(u.Position, target.Position) <= close) return true;
            return u.Move == null || u.Move.NextIndex >= u.Move.Path.Count;   // can't get any closer on this path: strike from here
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
            if (u.IsJumping) { u.Position = u.JumpTo; u.JumpEnd = -1f; }   // finish the jump rather than stop in mid-air
            u.Move = null;
            u.Phase = UnitPhase.Ready;
            Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
            SettleOutOfBodies(u);
        }

        private void TickOutOfRangeOrders()
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || IsGliding(u) || u.IsJumping) continue;   // mid-air: no stopping to strike or re-planning
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
                        if (!ChaseCloseEnough(u, cmd.Action, target)) reason = "out of range";   // in reach, but keep closing in
                        else if (behavior.CanStart(this, u, cmd, out reason))
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
                Vector3 p = Vector3.Lerp(u.GlideFrom, u.GlideTo, k);
                float ground = Terrain.HeightAt(p);
                // A fall keeps its height a while, then drops faster and faster (never below the ground it is over).
                p.y = u.FallLandAt >= 0f ? Mathf.Max(ground, u.GlideFrom.y + (u.GlideTo.y - u.GlideFrom.y) * k * k) : ground;
                u.Position = p;
                if (k < 1f) continue;
                u.GlideEnd = -1f;
                if (u.FallLandAt >= 0f) Land(u);
            }
        }

        /// <summary>
        /// Knock 'unit' into a fall toward 'landing' (on lower ground), 'drop' meters down. 'leadSeconds' of sideways
        /// travel come first (a shove off a ledge); the drop itself takes sqrt(2h/g). The unit is stunned through the fall
        /// (Block doesn't help) and takes fall damage when it lands (see Land).
        /// </summary>
        private void StartFall(Unit unit, Vector3 landing, float drop, float leadSeconds, Unit source, bool fromJump)
        {
            drop = Mathf.Max(0f, drop);
            float seconds = Mathf.Max(TickLength, leadSeconds + Mathf.Sqrt(2f * drop / Mathf.Max(0.1f, FallGravity)));
            landing = Terrain.OnGround(landing);
            unit.GlideFrom = unit.Position;
            unit.GlideTo = landing;
            unit.GlideStart = Now;
            unit.GlideEnd = Now + seconds;
            unit.FallLandAt = unit.GlideEnd;
            unit.FallDamage = FallDamageBase + FallDamagePerMeter * drop;
            unit.FallSourceId = source != null ? source.Id : null;
            Emit(EventType.Dash, unit.Id, null, new DashInfo { From = unit.Position, To = landing, Duration = seconds, Forced = true, Fall = true });
            Emit(EventType.Fall, source != null ? source.Id : null, unit.Id, new FallInfo { Height = drop, Duration = seconds, FromJump = fromJump });
            ApplyStun(unit, seconds + FallLandingSeconds, source, true);
        }

        /// <summary>A falling unit hits the ground: fall damage, credited to whoever knocked it down.</summary>
        private void Land(Unit u)
        {
            u.FallLandAt = -1f;
            if (!u.IsAlive) return;
            var source = GetUnit(u.FallSourceId);
            float dealt = u.FallDamage;
            u.Health -= dealt;
            if (source != null) u.LastAttackerId = source.Id;
            u.LastHitAction = null;
            u.LastHitTag = "fall";
            Emit(EventType.Damage, source != null ? source.Id : null, u.Id, new DamageInfo
            {
                ActionId = "fall", Amount = dealt, Impact = ImpactType.Light, RemainingHealth = u.Health,
            });
        }

        /// <summary>Hit, stunned or shoved mid-jump: the jump fails and the unit falls to the lower of its two sides.</summary>
        private void BreakJump(Unit u, Unit source)
        {
            if (!u.IsJumping) return;
            Vector3 low = u.JumpFrom.y <= u.JumpTo.y ? u.JumpFrom : u.JumpTo;
            float drop = u.Position.y - low.y;
            u.JumpEnd = -1f;
            u.Move = null;
            ClearChase(u);
            if (u.Phase == UnitPhase.Moving) u.Phase = UnitPhase.Ready;
            Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
            StartFall(u, low, drop, 0f, source, true);
        }

        private void StartJump(Unit u, Vector3 from, Vector3 to)
        {
            u.Position = from;
            u.JumpFrom = from;
            u.JumpTo = to;
            u.JumpStart = Now;
            float seconds = TerrainMap.JumpSeconds(to.y - from.y);
            u.JumpEnd = Now + seconds;
            Emit(EventType.Jump, u.Id, null, new JumpInfo { From = from, To = to, Duration = seconds });
        }

        private void StartMove(Unit unit, List<Vector3> path)
        {
            unit.Phase = UnitPhase.Moving;
            float moveSpeed = unit.EffectiveMoveSpeed();
            unit.Move = new ActiveMove { Path = path, NextIndex = 1, Speed = moveSpeed };
            Emit(EventType.MoveStarted, unit.Id, null, new MoveInfo { Path = new List<Vector3>(path), Speed = moveSpeed });
        }

        /// <summary>windupLeft &gt;= 0 resumes an action a dodge cut short: that much windup, and no costs (already paid).</summary>
        /// <param name="charge">0..1 for a released charge ability (see ReleaseCharge).</param>
        private void StartAction(Unit unit, UnitCommand cmd, ActionBehavior behavior, float windupLeft = -1f, float charge = 0f)
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
                StartedAt = Now, ResolveAt = Now + windup, Charge = charge,
                Transcended = cmd.Transcend && def.CanTranscend,
            };
            unit.Phase = UnitPhase.Windup;
            unit.PhaseEndsAt = inst.ResolveAt;
            unit.ActiveAction = inst;
            unit.LastActionId = def.Id;
            unit.LastActionTargetId = cmd.TargetUnitId;
            unit.LastActionPos = cmd.TargetPos;
            if (!resuming) PayCosts(unit, def, behavior, cmd.TargetUnitId, inst.Transcended);
            if (unit.MesmerStrike != null && def == unit.MesmerStrike && unit.MesmerStrikesLeft > 0) unit.MesmerStrikesLeft--;

            behavior.OnWindupStart(this, inst);
            Emit(EventType.ActionStarted, unit.Id, cmd.TargetUnitId,
                new ActionStartedInfo { ActionId = def.Id, Windup = windup, Recovery = def.Recovery / speed, TargetPos = cmd.TargetPos, Transcended = inst.Transcended });
            ApplyWindupStatuses(inst);
            Triggers.Fire(new TriggerContext { Type = TriggerType.ActionStarted, Sim = this, Subject = unit, Action = def });
            if (!resuming) AlertTarget(unit, cmd.TargetUnitId);
        }

        /// <summary>ActionDefinition.TargetStatusesDuringWindup: on the target while the windup runs (until ClearWindupStatuses).</summary>
        private void ApplyWindupStatuses(ActionInstance a)
        {
            var specs = a.Definition.TargetStatusesDuringWindup;
            var target = GetUnit(a.TargetUnitId);
            if (specs == null || target == null || target == a.Actor) return;
            foreach (var spec in specs)
            {
                if (!spec.AppliesToUnit(a.Actor, target)) continue;
                var s = spec.Create(Now, a.Actor.Id);
                s.ExpiresAt = float.MaxValue;   // taken off when the windup ends, however long it runs
                ApplyStatus(target, s, a.Actor);
            }
        }

        /// <summary>The windup ended (landed, interrupted, or the actor died): take its TargetStatusesDuringWindup back off.</summary>
        private void ClearWindupStatuses(ActionInstance a)
        {
            var specs = a != null ? a.Definition.TargetStatusesDuringWindup : null;
            var target = a != null ? GetUnit(a.TargetUnitId) : null;
            if (specs == null || specs.Count == 0 || target == null) return;
            foreach (var spec in specs)
            {
                var s = target.GetStatus(spec.Id);
                if (s != null && s.SourceId == a.Actor.Id) RemoveStatus(target, spec.Id);
            }
        }

        /// <summary>
        /// An attack started winding up on an AI-controlled (enemy team) unit: if it is only waiting, or walking with a
        /// Block or Dash it can afford, hand it back for orders now so the AI can guard or get away (see BasicEnemyAI).
        /// </summary>
        private void AlertTarget(Unit attacker, string targetId)
        {
            if (!AlertAiTargets) return;
            var t = GetUnit(targetId);
            if (t == null || !t.IsAlive || t.Team == attacker.Team || t.Team == Team.Player) return;
            if (t.Phase == UnitPhase.Recovery && t.Waiting) { EndRecoveryNow(t); return; }
            if (t.Phase != UnitPhase.Moving || t.IsJumping || t.Chase != null || IsGliding(t)) return;
            string reason;
            foreach (var def in t.Actions)
                if ((def.BehaviorId == "block" || def.BehaviorId == "dash") && CanAfford(t, def, out reason)) { EndMove(t); return; }
        }

        /// <summary>Cooldown, strain, energy, a consumed required status and on-windup self statuses: what starting it costs.</summary>
        private void PayCosts(Unit unit, ActionDefinition def, ActionBehavior behavior, string targetId, bool transcended = false)
        {
            float energyCost = behavior.EnergyCost(this, unit, def);   // before anything below changes the discount
            float strainCost = StrainCostOf(unit, def, targetId, transcended);
            if (def.Cooldown > 0f) unit.CooldownReadyAt[def.Id] = Now + def.Cooldown;
            AddStrain(unit, strainCost);
            if (energyCost > 0f) AddEnergy(unit, -energyCost);
            if (def.ConsumesRequiredStatus && !def.ConsumeOnResolve)
                foreach (var id in def.RequiredStatuses()) RemoveStatus(unit, id);
            if (def.SelfStatusesOnWindup) ApplySelfStatuses(unit, def);
        }

        private void ApplySelfStatuses(Unit unit, ActionDefinition def)
        {
            foreach (var spec in def.SelfStatuses) ApplyStatus(unit, spec.Create(Now, unit.Id, def.LinkedAction), unit);
        }

        // ------------------------------------------------------------------ the tick

        private void Step()
        {
            tick++;
            TickFocus();
            TickLifts();
            AdvanceGlides();
            AdvanceMovement();
            TickGrapples();   // after movement, so the held unit is carried to where the grappler walked
            TickOutOfRangeOrders();
            TickCharges();
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
                if (u.IsJumping)
                {
                    float k = Mathf.Clamp01((Now - u.JumpStart) / Mathf.Max(1e-4f, u.JumpEnd - u.JumpStart));
                    Vector3 p = Vector3.Lerp(u.JumpFrom, u.JumpTo, k);
                    p.y = TerrainMap.JumpY(u.JumpFrom, u.JumpTo, k);
                    u.Position = p;
                    if (k < 1f) continue;
                    u.Position = u.JumpTo;
                    u.JumpEnd = -1f;
                    if (u.Move.NextIndex < u.Move.Path.Count)
                    {
                        // Landed: the walk carries on from here.
                        var rest = new List<Vector3> { u.Position };
                        for (int i = u.Move.NextIndex; i < u.Move.Path.Count; i++) rest.Add(u.Move.Path[i]);
                        Emit(EventType.MoveStarted, u.Id, null, new MoveInfo { Path = rest, Speed = u.Move.Speed });
                    }
                }
                else WalkStep(u);
                if (!u.IsJumping && u.Move.NextIndex >= u.Move.Path.Count && u.Chase == null) EndMove(u);   // a chaser waits there for its next re-path
            }
        }

        /// <summary>
        /// One tick of walking along the path, on the ground. Where the ground ahead changes height by more than
        /// TerrainMap.StepHeight the unit stops a body's width short of the edge and jumps it (StartJump), landing a
        /// body's width past it; the walk resumes once it lands.
        /// </summary>
        private void WalkStep(Unit u)
        {
            float remaining = u.Move.Speed * TickLength;
            while (remaining > 1e-6f && u.Move.NextIndex < u.Move.Path.Count)
            {
                Vector3 pos = u.Position;
                Vector3 next = u.Move.Path[u.Move.NextIndex];
                Vector3 d = next - pos; d.y = 0f;
                float dist = d.magnitude;
                if (dist < 1e-5f) { u.Position = Terrain.OnGround(next); u.Move.NextIndex++; continue; }
                d /= dist;
                float step = Mathf.Min(dist, remaining);
                float ground = Terrain.HeightAt(pos);
                // Look a body radius ahead (within this leg), so the unit takes off before its body meets the ledge.
                float reach = step + Mathf.Min(u.BodyRadius, dist - step);
                float edge = Terrain.FirstEdge(pos, pos + d * reach, ground);
                if (edge >= 0f)
                {
                    float takeoff = Mathf.Clamp(edge - u.BodyRadius, 0f, step);
                    float land = Mathf.Min(edge + u.BodyRadius, dist);
                    if (land >= dist - 1e-4f) u.Move.NextIndex++;
                    Vector3 from = pos + d * takeoff; from.y = ground;
                    StartJump(u, from, Terrain.OnGround(pos + d * land));
                    return;
                }
                u.Position = Terrain.OnGround(pos + d * step);
                remaining -= step;
                if (step >= dist) u.Move.NextIndex++;
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
                int first = effects.Count;
                a.Behavior.OnResolve(this, a, effects);
                // The actor's damage statuses (Wrath) count as of now: baked in before anything below can take them away.
                float dealt = a.Actor.DamageDealtMult();
                for (int i = first; i < effects.Count; i++)
                {
                    var atk = effects[i] as AttackEffect;
                    if (atk != null && atk.Source == a.Actor) atk.DamageMultiplier *= dealt;
                }
                if (!a.IsFree) ClearWindupStatuses(a);
                if (a.Definition.ConsumesRequiredStatus && a.Definition.ConsumeOnResolve)
                    foreach (var id in a.Definition.RequiredStatuses()) RemoveStatus(a.Actor, id);
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
                // Killed in mid-air (jumping, shoved, falling, lifted): drop the body where it was headed.
                bool airborne = u.IsJumping || u.GlideEnd >= 0f || u.IsLifted;
                if (u.IsJumping) u.Position = u.JumpFrom.y <= u.JumpTo.y ? u.JumpFrom : u.JumpTo;
                else if (u.GlideEnd >= 0f) u.Position = u.GlideTo;
                else if (u.IsLifted) u.Position = Terrain.OnGround(u.Position);
                if (airborne) Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
                if (u.Phase == UnitPhase.Windup) ClearWindupStatuses(u.ActiveAction);
                if (u.Grapple != null) EndGrapple(u);
                if (u.HeldById != null) ReleaseHeld(u);
                u.LiftDropAt = -1f;
                u.Phase = UnitPhase.Dead;
                u.ActiveAction = null;
                u.Move = null;
                u.Chase = u.Queued = null;
                u.GlideEnd = -1f;
                u.JumpEnd = -1f;
                u.FallLandAt = -1f;
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

            // 2. Accuracy: base * body exposure (ranged vs cover) + modifiers (high ground among them)
            float exposure = (!atk.IgnoreCover && def.IsRanged && !def.PiercesTerrain) ? Visibility.GetExposure(src, target, false) : 1f;
            float accuracyBonus = atk.AccuracyBonus + HeightAccuracyBonus(src, target, def);
            foreach (var s in target.Statuses) accuracyBonus += s.IncomingAccuracyBonus;
            float hitChance = Mathf.Clamp01(def.BaseAccuracy * exposure + accuracyBonus);
            double roll = rng.NextDouble();
            if (roll >= hitChance)
            {
                // A roll that would have hit in the open was stopped by cover: the shot strikes that cover. Where on it comes
                // from the same roll (no extra draw), so the outcome stays seeded.
                var miss = new MissInfo { Reason = "miss", ActionId = def.Id };
                float openChance = Mathf.Clamp01(def.BaseAccuracy + accuracyBonus);
                if (exposure < 0.999f && roll < openChance)
                {
                    miss.Reason = "cover";
                    var sight = Visibility as SimVisibility;
                    float pick = (float)((roll - hitChance) / Mathf.Max(1e-6f, openChance - hitChance));
                    Vector3 at;
                    if (sight != null && sight.TryCoverPoint(src, target, pick, out at)) { miss.HitCover = true; miss.CoverPoint = at; }
                }
                Emit(EventType.Miss, src.Id, target.Id, miss);
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
            // A Transcended Block (NegatesDamage) stops all of it, pierce or not: no damage, no strain, no wear on block.
            bool negated = target.Statuses.Exists(s => s.NegatesDamage);
            float raw = def.Damage * atk.DamageMultiplier;
            float effectiveBlock = negated ? 1f : Mathf.Clamp01(snap.Block * (1f - Mathf.Clamp01(def.BlockPierce + atk.BlockPierceBonus)));
            float dealt = raw * (1f - effectiveBlock);
            target.Health -= dealt;
            target.LastAttackerId = src.Id;
            target.LastHitAction = def;
            target.LastHitTag = atk.Tag;
            if (snap.Block > 0f && !negated) target.ReduceBlock(def.Impact == ImpactType.Heavy ? HeavyBlockBreak : LightBlockBreak);

            // 5. Strain / Unravel (checked against strain BEFORE this hit adds to it)
            bool atMaxStrain = target.Strain >= target.MaxStrain - 0.001f;
            bool unravel = atMaxStrain && def.Impact == ImpactType.Heavy && !negated;   // even through Block: Unravel pierces stun immunity
            if (!negated) AddStrain(target, def.StrainOnTarget);

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
            BreakJump(target, src);   // any hit that lands knocks a jumping unit out of the air
            foreach (var spec in def.TargetStatusesOnHit) if (spec.AppliesToUnit(src, target)) ApplyStatus(target, spec.Create(Now, src.Id), src);
            if (def.Displace == DisplaceMode.TowardSource) DisplaceUnit(target, src, def);
            float stunFor = def.StunDuration * atk.StunMultiplier;
            bool willStun = unravel || stunFor > 0f;
            if (atk.HasPush) Relocate(target, atk.PushTo, DisplaceDuration, true, src);
            if (target.Phase == UnitPhase.Charging) Interrupt(target, "charge broken", !willStun);   // any damage breaks a charge
            if (target.Phase == UnitPhase.Windup && target.ActiveAction != null &&
                (def.Impact == ImpactType.Heavy || target.ActiveAction.Definition.InterruptedByDamage))
                Interrupt(target, def.Impact == ImpactType.Heavy ? "heavy impact" : "damage", !willStun);

            if (unravel)
            {
                AddStrain(target, -target.Strain);   // assumption: Unravel resets strain
                Emit(EventType.Unravel, src.Id, target.Id);
                ApplyStun(target, UnravelStunSeconds, src, true);
            }
            else if (stunFor > 0f)
            {
                ApplyStun(target, stunFor, src);
            }
            if (atk.OnLanded != null && target.IsAlive) atk.OnLanded(this, target);
        }

        private void OnDodged(Unit attacker, Unit target, ActionDefinition def)
        {
            Emit(EventType.Dodge, attacker.Id, target.Id, new MissInfo { Reason = "dodge" });
            Triggers.Fire(new TriggerContext { Type = TriggerType.Dodged, Sim = this, Subject = target, Other = attacker, Action = def });
            if (!target.IsAlive || target.IsRooted) return;   // rooted units still dodge, but can't dash
            if (target.MesmerStrikesLeft > 0) return;   // Mesmerize blinked it to the attacker instead

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
            var cmd = new UnitCommand { UnitId = u.Id, Type = CommandType.Action, Action = def, TargetUnitId = r.TargetUnitId, TargetPos = r.TargetPos }
                .Transcended(r.Transcended);
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
                        if (!u.IsAlive || u == unit || u.Phase == UnitPhase.Moving || skip.Contains(u.Id) || Holds(unit, u)) continue;
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
                Vector3 p1 = Terrain.OnGround(blocker.Position + n * r - dir * r * 0.75f);
                Vector3 p2 = Terrain.OnGround(blocker.Position + n * r + dir * r * 0.75f);
                if (!Walkable(a, p1) || !Walkable(p1, p2) || !Walkable(p2, b)) continue;
                if (InsideStandingBody(unit, p1) || InsideStandingBody(unit, p2)) continue;
                path.Insert(i + 1, p2);
                path.Insert(i + 1, p1);
                return true;
            }
            return false;
        }

        private bool Walkable(Vector3 a, Vector3 b)
        {
            Vector3 gap = terrainPaths.ClampWalk(a, b) - b; gap.y = 0f;   // walks jump height changes: only walls stop them
            return gap.sqrMagnitude < 0.01f;
        }

        private bool InsideStandingBody(Unit unit, Vector3 p)
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u == unit || u.Phase == UnitPhase.Moving || Holds(unit, u)) continue;
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
                        float gx = clamped.x - c.x, gz = clamped.z - c.z;
                        if (gx * gx + gz * gz > 0.01f) continue;
                        c = clamped;
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

        /// <summary>Is 'unit' holding 'other' (Subjugate)? A captive is carried along, so it is never in its captor's way.</summary>
        private static bool Holds(Unit unit, Unit other) { return unit.Grapple != null && unit.Grapple.TargetId == other.Id; }

        private bool IsSpotFree(Unit unit, Vector3 p)
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u == unit || Holds(unit, u) || Holds(u, unit)) continue;
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
        /// Elevation: a forced move (shove, pull) that carries the unit off a ledge becomes a fall (StartFall); one that
        /// catches it mid-jump knocks it out of the jump instead; a unit already falling just keeps falling.
        /// </summary>
        private void Relocate(Unit unit, Vector3 to, float duration, bool forced, Unit source = null, Vector3? phaseFrom = null)
        {
            if (unit.FallLandAt >= 0f || unit.IsLifted) return;   // falling or held up in the air: nothing moves it
            if (unit.HeldById != null) ReleaseHeld(unit);   // shoved out of a grip
            if (unit.IsJumping)
            {
                if (forced) { BreakJump(unit, source); return; }
                HaltWalk(unit);
            }
            Vector3 from = unit.Position;
            to = Terrain.OnGround(FreeSpotNear(unit, to, phaseFrom ?? from, false));
            if (unit.Phase == UnitPhase.Moving) EndMove(unit, false);   // assumption: being relocated ends a walk in progress
            float drop = from.y - to.y;
            if (forced && drop > TerrainMap.StepHeight)
            {
                StartFall(unit, to, drop, duration, source, false);
                return;
            }
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
            ClearWindupStatuses(a);
            u.ActiveAction = null;
            if (u.Phase == UnitPhase.Windup || u.Phase == UnitPhase.Charging) u.Phase = UnitPhase.Ready;
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
        /// <param name="piercesImmunity">Unravel and falls: lands even on a stun-immune (blocking) unit, and always breaks a windup.</param>
        private void ApplyStun(Unit target, float duration, Unit source, bool piercesImmunity = false)
        {
            if (target.Phase == UnitPhase.Dead) return;
            BreakJump(target, source);    // stunned mid-jump: it falls (and that fall's own stun lands first)
            if (target.IsStunImmune && !piercesImmunity) return;   // Block
            target.ResumeAction = null;   // a stun mid-dodge drops the remembered action
            ClearChase(target);           // and a stun ends a chase (a queued ranged order survives it)
            if (target.Phase == UnitPhase.Windup)
            {
                if (target.ActiveAction == null || (!target.ActiveAction.Definition.InterruptedByStun && !piercesImmunity)) return;
                Interrupt(target, "stun", false);
            }
            if (target.Phase == UnitPhase.Charging) Interrupt(target, "stun", false);   // a stun always breaks a charge
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
                if (!u.IsAlive || u.Strain <= 0f) continue;
                float rate = u.HasStatus(StatusIds.Focused) ? FocusStrainPerSecond : 0f;   // focus drains even right after a strain gain
                if (u.StrainRecoveryPerSecond > 0f && Now - u.LastStrainGainAt >= u.StrainRecoveryDelay) rate += u.StrainRecoveryPerSecond;
                if (rate > 0f) AddStrain(u, -rate * step);
            }
        }

        /// <summary>
        /// Focus bookkeeping, first thing every tick. A unit that isn't in a Wait any more has stopped focusing (back-to-back
        /// Waits never show a tick in between: the next one starts before the sim steps again). Once a unit has focused for
        /// FocusDelay without a break it gets the Focused status, which RecoverStrain drains strain for.
        /// </summary>
        private void TickFocus()
        {
            foreach (var u in State.Units)
            {
                bool waiting = u.IsAlive && u.Phase == UnitPhase.Recovery && u.Waiting;
                if (!waiting) { if (u.FocusSince >= 0f) BreakFocus(u, false); continue; }
                if (u.FocusSince >= 0f && Now - u.FocusSince >= FocusDelay - 1e-4f && !u.HasStatus(StatusIds.Focused))
                {
                    RemoveStatus(u, StatusIds.Focusing);
                    ApplyStatus(u, new StatusEffect { Id = StatusIds.Focused, SourceId = u.Id, ExpiresAt = float.MaxValue }, u);
                }
            }
        }

        /// <summary>Stop (or, restart: hit while focusing) a unit's run of focus. A Transcended Focus keeps going until Focused.</summary>
        private void BreakFocus(Unit u, bool restart)
        {
            u.FocusSince = restart ? Now : -1f;
            if (u.HasStatus(StatusIds.Focused)) RemoveStatus(u, StatusIds.Focused);
            if (!restart) { RemoveStatus(u, StatusIds.Focusing); u.FocusTranscended = false; return; }
            ApplyStatus(u, new StatusEffect { Id = StatusIds.Focusing, SourceId = u.Id, ExpiresAt = Now + FocusDelay }, u);
            if (u.FocusTranscended && u.Waiting && u.Phase == UnitPhase.Recovery)
                u.PhaseEndsAt = Mathf.Max(u.PhaseEndsAt, Now + FocusDelay + TickLength);
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
                if (!u.IsJumping)   // mid-jump, the landing re-sends the rest of the walk at the new speed
                {
                    var rest = new List<Vector3> { u.Position };
                    for (int i = u.Move.NextIndex; i < u.Move.Path.Count; i++) rest.Add(u.Move.Path[i]);
                    Emit(EventType.MoveStarted, u.Id, null, new MoveInfo { Path = rest, Speed = newMove });
                }
            }

            Emit(EventType.SpeedChanged, u.Id, null, new SpeedInfo { ActionSpeed = newAction, MoveSpeed = newMove });
        }

        private void Emit(EventType type, string actorId, string targetId, object payload = null)
        {
            Timeline.AddEvent(new TimelineEvent(Now, actorId, targetId, type, payload));
        }
    }
}
