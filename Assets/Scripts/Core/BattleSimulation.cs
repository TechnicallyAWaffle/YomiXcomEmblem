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
    public partial class BattleSimulation
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
        /// <summary>Plays the AI-controlled units on the player's side (Unit.AIControlled: Recollections of Self's Wraiths), by their
        /// AIProfile, apart from EnemyAI (which a scripted battle may replace to hold the enemies still).</summary>
        public IUnitAI AllyAI = new AIRouter();
        /// <summary>Plays every Maddened unit (Madness), whichever side it is on: its basic attack on the nearest enemy, or the one MADNESS lends it.</summary>
        public IUnitAI MaddenedAI = new MaddenedAI();
        /// <summary>A player unit was given an order (SubmitCommand), or a walk was stopped (CancelMove): for watchers such as the tutorial.</summary>
        public event System.Action<UnitCommand> CommandSubmitted;
        public event System.Action<Unit> MoveCancelled;
        /// <summary>
        /// A scripted battle (the tutorial): defeating every enemy does not end it, so more can arrive (SpawnUnit); it ends
        /// when the squad falls or FinishBattle is called.
        /// </summary>
        public bool HoldVictory;

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
        public float FallDamagePerMeter = 3f;      // was 6: an 8m drop (Skyward Demise) did 51
        /// <summary>Ranged attacks from at least HighGroundMinHeight above their target get this much extra hit chance.</summary>
        public float HighGroundAccuracyBonus = 0.15f;
        public float HighGroundMinHeight = 0.75f;
        /// <summary>Wake waiting (or walking) enemy-team units when an attack starts winding up on them (AlertTarget).</summary>
        public bool AlertAiTargets = true;

        // Focus (the Wait order): FocusDelay seconds of unbroken focusing, and strain drains at FocusStrainPerSecond on top
        // of normal recovery (no recovery delay) until the unit does anything else. Taking damage restarts the count.
        public float FocusDelay = 1.5f;
        /// <summary>The shortest a unit can take to reach Clear Mind, however much its Etchings cut FocusDelay (two Mantras of
        /// Focus would otherwise make it instant).</summary>
        public float MinFocusDelay = 0.1f;
        public float FocusStrainPerSecond = 35f;
        /// <summary>Focus drains strain in steps of this many ticks (0.1s), at FocusStrainPerSecond all the same.</summary>
        public const int FocusDrainTicks = 4;


        // Transcend (UnitCommand.Transcend). The strain price is per ability (ActionDefinition.TranscendStrain).
        /// <summary>A Heavy hit that lands on a (plain) Block puts Block on cooldown this long.</summary>
        public float HeavyBlockCooldown = 1f;
        /// <summary>Windup of a stance's reaction shot (Take Aim's snipe at the first enemy to act).</summary>
        public float ReactionWindup = 0.1f;
        /// <summary>Strain shed on every dodge by a unit with the strain_relief_on_dodge passive (the Fighter).</summary>
        public float DodgeStrainRelief = 15f;
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
            EnemyAI = enemyAI ?? new AIRouter();   // each unit's AIProfile (UnitAIs), else BasicEnemyAI
            Outcome = BattleOutcome.InProgress;

            // Reactions, stances and passives subscribe to Triggers once per battle.
            foreach (var u in State.Units) AttachBehaviors(u);
            Passives.Attach(this);
            EtchingTraits.Attach(this);
            DeliveranceParts.Attach(this);
            // Hit while focusing: the count toward FocusDelay starts over.
            Triggers.Subscribe(TriggerType.DamageTaken, ctx => { if (ctx.Subject.FocusSince >= 0f) BreakFocus(ctx.Subject, true); });
            AddBuiltInHitRules();
            foreach (var u in State.Units) Arrive(u);
        }

        private readonly HashSet<ActionBehavior> attached = new HashSet<ActionBehavior>();

        /// <summary>The behaviors a unit's abilities use subscribe their reactions once per battle (a summon's too, on arrival).</summary>
        private void AttachBehaviors(Unit u)
        {
            if (u.Passives.Contains(DeliveranceParts.LastAttachments) && !u.SpentOnce.Contains(DeliveranceParts.LastAttachments)) CheapenGuard(u);   // Wex: from the moment it is on the field
            foreach (var def in u.Actions)
            {
                AttachBehavior(def);
                if (def != null) AttachBehavior(def.LinkedAction);   // Take Aim's Snipe, Retaliate's shot
            }
        }

        private void AttachBehavior(ActionDefinition def)
        {
            if (def == null) return;
            var b = ActionBehaviors.Get(def.BehaviorId);
            if (attached.Add(b)) b.Attach(this);
        }

        /// <summary>A unit takes the field (the start, or a summon arriving): abilities that start on cooldown go on it, and its
        /// always-on domes go up.</summary>
        private void Arrive(Unit u)
        {
            foreach (var def in u.Actions)
                if (def != null && def.StartsOnCooldown && def.Cooldown > 0f) u.CooldownReadyAt[def.Id] = Now + u.ActionStat(def, ActionStat.Cooldown);
            StartInnateAuras(u);
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
            var u = GetUnit(cmd.UnitId);
            if (CommandSubmitted != null && u != null && u.Team == Team.Player && !u.AIControlled) CommandSubmitted(cmd);
        }

        public void ClearQueue(string unitId) { queues.Remove(unitId); }

        /// <summary>End the battle now with 'outcome' (a scripted battle's end, HoldVictory). Nothing happens once it is over.</summary>
        public void FinishBattle(BattleOutcome outcome)
        {
            if (Outcome != BattleOutcome.InProgress || outcome == BattleOutcome.InProgress) return;
            Outcome = outcome;
            Emit(EventType.BattleEnd, null, null);
        }

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
            if (u == null || !u.IsAlive || u.Team != Team.Player || u.AIControlled) { reason = "invalid unit"; return false; }   // AI-played summons aren't the player's to stop
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
            if (MoveCancelled != null) MoveCancelled(u);
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

        /// <summary>How far the unit's Dash (and its dodge dashes) reach: its dash distance, with the Dash ability's Range
        /// modifiers on it (Unit.RangeOf), further Transcended.</summary>
        public float DashReach(Unit unit, bool transcended = false)
        {
            var dash = unit.DashAction();
            float reach = dash != null ? unit.RangeOf(dash) : unit.DashDistanceNow();
            return reach * (transcended ? TranscendDashDistanceMult : 1f);
        }

        /// <summary>Where a dash toward 'point' lands: capped at the unit's dash distance and clamped to walkable space.</summary>
        public Vector3 DashDestination(Unit unit, Vector3 point, bool transcended = false)
        {
            Vector3 d = point - unit.Position; d.y = 0f;
            float max = DashReach(unit, transcended);
            if (d.magnitude > max) d = d.normalized * max;
            return Paths.ClampDash(unit.Position, unit.Position + d);
        }

        /// <summary>
        /// The Enemy side would rather fight the squad than a third side (Deliverance's): a target that isn't the squad's
        /// counts as SquadTargetPreference farther off, or worth that much less, wherever an AI picks one by distance or value
        /// (TargetWeight), so they are about that much likelier to go for the squad.
        /// </summary>
        public float SquadTargetPreference = 0.2f;

        /// <summary>How much less 'chooser' wants 'target' than its distance or worth says: 1, or 1 + SquadTargetPreference
        /// for an enemy weighing a unit that isn't the squad's. Divide a target's value by it, multiply its distance.</summary>
        public float TargetWeight(Unit chooser, Unit target)
        {
            return chooser != null && target != null && chooser.Team == Team.Enemy && target.Team != Team.Player ? 1f + SquadTargetPreference : 1f;
        }

        /// <summary>The nearest unit 'unit' can target (TargetWeight: an enemy counts a non-squad unit as farther).</summary>
        public Unit NearestEnemy(Unit unit)
        {
            Unit best = null;
            float bestDist = float.MaxValue;
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u.Team == unit.Team || !IsTargetableBy(u, unit)) continue;
                float d = Vector3.Distance(unit.Position, u.Position) * TargetWeight(unit, u);
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
                if (Vector3.Distance(actor.Position, target.Position) > actor.ActionStat(def, ActionStat.Range)) { reason = "out of range"; return false; }
                if (def.IsRanged && !def.PiercesTerrain && !Visibility.HasLineOfSight(actor, target)) { reason = "no line of sight"; return false; }
            }
            else if (def.Targeting == TargetType.Ally || def.Targeting == TargetType.AnyUnit)
            {
                if (target == null || !target.IsAlive) { reason = "invalid target"; return false; }
                if (def.Targeting == TargetType.Ally && target.Team != actor.Team) { reason = "invalid target"; return false; }
                if (def.CannotTargetSelf && target.Id == actor.Id) { reason = "can't target yourself"; return false; }
                if (!IsTargetableBy(target, actor)) { reason = "target hidden"; return false; }
                if (Vector3.Distance(actor.Position, target.Position) > actor.ActionStat(def, ActionStat.Range)) { reason = "out of range"; return false; }
                if (def.Targeting == TargetType.AnyUnit && def.IsRanged && !HasSightOf(actor, def, target)) { reason = "no line of sight"; return false; }
            }
            return true;
        }

        /// <summary>Cooldown, energy and required-status checks only (no target). Drives greyed-out ability buttons.</summary>
        public bool CanAfford(Unit actor, ActionDefinition def, out string reason, string targetId = null, bool transcend = false)
        {
            reason = null;
            float readyAt;
            var behavior = ActionBehaviors.Get(def.BehaviorId);
            if (actor.CooldownReadyAt.TryGetValue(def.Id, out readyAt) && readyAt > Now && !behavior.IgnoresCooldown(this, actor, def, targetId)) { reason = "cooldown"; return false; }
            if (def.HealthCost > 0f && actor.Health <= actor.ActionStat(def, ActionStat.HealthCost) + 1e-4f) { reason = "not enough health"; return false; }
            var required = def.RequiredStatuses();
            if (required.Length > 0 && !System.Array.Exists(required, actor.HasStatus)) { reason = "needs " + string.Join(" or ", required); return false; }
            // Tied to a target (RequiredStatusOnTarget): only at the unit the opener hit. With no target given, the button shows it ready.
            if (required.Length > 0 && def.RequiredStatusOnTarget && targetId != null && !actor.Statuses.Exists(s => System.Array.IndexOf(required, s.Id) >= 0 && s.TargetId == targetId)) { reason = "only at the unit it followed up on"; return false; }
            var forbidding = actor.StatusForbidding(def.Id);
            if (forbidding != null) { reason = "not during " + forbidding.Id.Replace('_', ' '); return false; }
            if (def.InnatePassive) { reason = "always on"; return false; }
            if (EnergyCostOf(actor, def) > actor.Energy + 1e-4f) { reason = "not enough energy"; return false; }
            float strain = StrainCostOf(actor, def, targetId, transcend);
            if (strain > 0f && actor.Strain + strain > actor.MaxStrain + 1e-4f && !behavior.AllowsStrainOverflow && !def.UnravelsOverMaxStrain && !EtchingTraits.SuffersPastMax(actor)) { reason = "too much strain"; return false; }
            return true;
        }

        public float EnergyCostOf(Unit actor, ActionDefinition def) { return ActionBehaviors.Get(def.BehaviorId).EnergyCost(this, actor, def); }

        /// <summary>Strain for using 'def', plus its TranscendStrain when Transcended (and it can be). A cost (not relief) is
        /// scaled by the actor's statuses (Wrath makes it free).</summary>
        public float StrainCostOf(Unit actor, ActionDefinition def, string targetId = null, bool transcend = false)
        {
            float cost = ActionBehaviors.Get(def.BehaviorId).StrainCost(this, actor, def, targetId) + (transcend && actor.CanTranscend(def) ? actor.TranscendStrainOf(def) : 0f);
            return cost > 0f ? cost * actor.StrainCostMult() : cost;
        }

        /// <summary>How long 'u' focuses before it enters Clear Mind (FocusDelay, with its FocusDelay modifiers: Mantra of Focus).</summary>
        public float FocusDelayOf(Unit u) { return Mathf.Max(Mathf.Min(MinFocusDelay, FocusDelay), u.Stat(StatId.FocusDelay, FocusDelay)); }

        /// <summary>Strain 'u' sheds per second while in Clear Mind (FocusStrainPerSecond, with its modifiers).</summary>
        public float FocusStrainOf(Unit u) { return Mathf.Max(0f, u.Stat(StatId.FocusStrainRelief, FocusStrainPerSecond)); }

        /// <summary>What an order's windup is scaled by for how close its target is (Senses of Closure; 1 = unchanged). The
        /// target is its unit, else its point; aimed at the actor itself, it isn't targeted.</summary>
        public float ClosingWindupScale(Unit actor, ActionDefinition def, UnitCommand cmd)
        {
            if (cmd.TargetUnitId == actor.Id) return 1f;
            var t = GetUnit(cmd.TargetUnitId);
            return EtchingTraits.WindupScale(actor, def, t != null ? t.Position : cmd.TargetPos);
        }

        /// <summary>Seconds 'def' would wind up for if 'actor' started it now: its behavior's windup (Flowing Death shortens with
        /// stacks) at the actor's action speed.</summary>
        public float WindupOf(Unit actor, ActionDefinition def)
        {
            return ActionBehaviors.Get(def.BehaviorId).WindupSeconds(this, actor, def) / actor.EffectiveActionSpeed();
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
        /// Height of a ranged Line attack 'along' metres out: a straight line from the actor's feet through the aimed point,
        /// carried on at the same slope past it. So a line aimed onto a ledge tilts up toward it (and keeps climbing), one
        /// aimed down from a ledge tilts down, and one aimed along the ground stays level.
        /// </summary>
        public static float LineHeightAt(Vector3 origin, Vector3 targetPos, float along)
        {
            float aimDist = FlatDistance(origin, targetPos);
            if (aimDist < 1e-3f) return targetPos.y;
            return origin.y + (targetPos.y - origin.y) * (along / aimDist);
        }

        /// <summary>Enemies of the actor caught in the definition's area (see AreaAttackBehavior).</summary>
        /// circleAtTarget: a circle centred on targetPos whatever the targeting (Split Apart The Sky smites around a unit it
        /// aims at); otherwise only ground-targeted circles are, and the rest are around the actor.
        public List<Unit> UnitsInArea(Unit actor, ActionDefinition def, Vector3 targetPos, bool circleAtTarget = false)
        {
            var hits = new List<Unit>();
            Vector3 origin = actor.Position;
            Vector3 dir = targetPos - origin; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 center = circleAtTarget || def.Targeting == TargetType.Position ? targetPos : origin;
            foreach (var u in EnemiesOf(actor))
            {
                Vector3 p = u.Position;
                if (def.Shape == AreaShape.Line)
                {
                    Vector3 v = p - origin; v.y = 0f;
                    float along = Vector3.Dot(v, dir);
                    float side = (v - dir * along).magnitude;
                    if (along < -u.BodyRadius || along > actor.ActionStat(def, ActionStat.Range) + u.BodyRadius || side > def.LineWidth / 2f + u.BodyRadius) continue;
                    // Ranged lines (spells, projectiles) fly at the aimed elevation; a melee sweep catches anyone in reach.
                    float h = def.IsRanged ? LineHeightAt(origin, targetPos, along) : p.y;
                    if (h >= p.y - LineReachDown && h <= p.y + u.BodyHeight * LineReachUp) hits.Add(u);
                }
                else
                {
                    Vector3 v = p - center; v.y = 0f;
                    if (v.magnitude <= actor.ActionStat(def, ActionStat.AreaRadius) + u.BodyRadius) hits.Add(u);
                }
            }
            return hits;
        }

        /// <summary>Grant an immediate reaction action (Retaliate's free shot, a Snipe from Take Aim...).
        /// It runs alongside the actor's normal state: it does not require or change the actor's phase.</summary>
        /// <param name="duringChannel">It is the channel's own reaction (Scry The Meek's Ripples): allowed while channeling.</param>
        public ActionInstance QueueFreeAction(Unit actor, ActionDefinition def, string targetId, float windupOverride = -1f, float damageMultiplier = 1f, bool duringChannel = false)
        {
            if (!actor.IsAlive || (actor.Phase == UnitPhase.Channeling && !duringChannel)) return null;   // a channel allows nothing else, reactions included
            var behavior = ActionBehaviors.Get(def.BehaviorId);
            float windup = windupOverride >= 0f ? windupOverride : actor.ActionStat(def, ActionStat.Windup) / actor.EffectiveActionSpeed();
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
    }
}
