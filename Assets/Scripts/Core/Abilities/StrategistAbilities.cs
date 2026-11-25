using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    // The Strategist: moves units about and folds them out of the world. Madness and Wrinkles Of Reality (status_spell,
    // teleport) are the Paradigm User's old ones. VOIDED, swaps and the way back across a threshold are BattleSimulation's (Void.cs).

    /// <summary>
    /// Scepter Of Tyranny (scepter), a follow-up in three stages: each use within ChainWindow of the last one's recovery
    /// (on anyone) is the next stage, then it starts over. First: blink the target to a picked point within
    /// DestinationRange of it (as Wrinkles Of Reality). Second: 'haste' (x1.5) action speed for 'haste_seconds' (1.5s) and
    /// 'haste_weakpoints' (2) weakpoints. Third: the ability's TargetStatusesOnHit (MADNESS, with its LinkedAction to
    /// swing with) on the target, and the ability goes on cooldown for 'final_cooldown' (2s). On an ally or an enemy. The
    /// stage waiting is the ScepterStage status's Stacks; Focusing clears it (EndsOnFocus), back to the first. Its button
    /// goes by the ability's StageNames at the later stages.
    /// </summary>
    public class ScepterBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("haste", 1.5f);
                yield return new BehaviorParam("haste_seconds", 1.5f);
                yield return new BehaviorParam("haste_weakpoints", 2f);
                yield return new BehaviorParam("final_cooldown", 2f);
            }
        }

        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        /// <summary>0, 1 or 2: the stage 'actor''s next use is (0 with no follow-up waiting).</summary>
        public static int Stage(Unit actor)
        {
            var s = actor != null ? actor.GetStatus(StatusIds.ScepterStage) : null;
            return s != null ? Mathf.Clamp(s.Stacks, 0, 2) : 0;
        }

        /// <summary>Only its first stage sends the target somewhere.</summary>
        public override bool PicksDestination(BattleSimulation sim, Unit actor, ActionDefinition def) { return def.DestinationRange > 0f && Stage(actor) == 0; }

        public override void Describe(AbilityTip tip)
        {
            var d = tip.Def;
            tip.Add("First", "blinks the target to a point within " + AbilityTip.Metres(d.DestinationRange) + " of it");
            tip.Add("Follow-up", string.Format("{0} action speed for {1}, and {2:0} weakpoints", AbilityTip.Mult(Param(d, "haste")), AbilityTip.Secs(Param(d, "haste_seconds")), Param(d, "haste_weakpoints")));
            float mad = 0f;
            foreach (var s in d.TargetStatusesOnHit) if (s != null) mad = Mathf.Max(mad, s.Duration);
            tip.Add("Second follow-up", string.Format("MADNESS for {0}; then {1} cooldown", AbilityTip.Secs(mad), AbilityTip.Secs(Param(d, "final_cooldown"))));
            tip.Add("Focus", "starts it over");
            if (tip.Who != null && Stage(tip.Who) > 0) tip.Add("Next use", Stage(tip.Who) == 1 ? "the follow-up" : "the second follow-up");
        }

        public override bool FollowupRetargets(ActionDefinition def) { return true; }   // each stage picks its own target

        public override string NameFor(Unit actor, ActionDefinition def)
        {
            int stage = Stage(actor);
            return stage > 0 && def.StageNames != null && stage - 1 < def.StageNames.Count && !string.IsNullOrEmpty(def.StageNames[stage - 1]) ? def.StageNames[stage - 1] : def.DisplayName;
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a) { a.Steps = Stage(a.Actor); }   // the stage it started as, whatever the window does meanwhile

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            int stage = Mathf.Clamp(a.Steps, 0, 2);
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || !sim.InReach(actor, def, target)) return;
            switch (stage)
            {
                case 0:
                {
                    // To the picked point, no further than DestinationRange from where it stands. With no point picked (an AI's
                    // use), a random one.
                    float reach = def.DestinationRange;
                    Vector3 d = a.TargetPos - target.Position; d.y = 0f;
                    if (a.TargetPos == Vector3.zero)
                    {
                        float ang = sim.RandomValue() * Mathf.PI * 2f;
                        d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Mathf.Lerp(Mathf.Min(1.5f, reach), reach, sim.RandomValue());
                    }
                    if (d.magnitude > reach) d = d.normalized * reach;
                    sim.TeleportUnit(target, sim.WalkablePointNear(target.Position + d, target.Position), actor);
                    break;
                }
                case 1:
                    sim.ApplyStatus(target, new StatusEffect
                    {
                        Id = StatusIds.Hastened, SourceId = actor.Id, ExpiresAt = sim.Now + Param(def, "haste_seconds") * actor.DurationScale(def),
                        Modifiers = new List<Modifier> { Modifier.Mult(StatId.ActionSpeed, Param(def, "haste")) },
                    }, actor);
                    sim.AddWeakpoints(target, Mathf.RoundToInt(Param(def, "haste_weakpoints")), target.Position - actor.Position, def.WeakpointSeconds, actor);
                    break;
                default:
                {
                    foreach (var spec in def.TargetStatusesOnHit)
                        if (spec != null && spec.AppliesToUnit(actor, target)) sim.ApplyStatus(target, spec.Create(sim.Now, actor.Id, def.LinkedAction, actor.DurationScale(def)), actor);
                    float cooldown = Param(def, "final_cooldown");
                    if (cooldown > 0f) actor.CooldownReadyAt[def.Id] = sim.Now + cooldown;
                    break;
                }
            }
            sim.EmitFx(actor, target, "scepter", def, target.Position, stage);
            // The next stage waits for a follow-up; after the last, it starts over.
            if (stage >= 2) { if (actor.HasStatus(StatusIds.ScepterStage)) sim.RemoveStatus(actor, StatusIds.ScepterStage); return; }
            float window = actor.ActionStat(def, ActionStat.Recovery) / actor.EffectiveActionSpeed() + def.ChainWindow;
            sim.ApplyStatus(actor, new StatusEffect { Id = StatusIds.ScepterStage, SourceId = actor.Id, ExpiresAt = sim.Now + window, Stacks = stage + 1, Flags = StatusFlags.FollowupWindow | StatusFlags.EndsOnFocus }, actor);
        }
    }

    /// <summary>
    /// Spatial Chrysalis (chrysalis): VOIDs its target and channels (ChannelSeconds at most). When the channel ends, or is
    /// broken, the target comes back and is stunned for StunDuration.
    /// </summary>
    public class ChrysalisBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("VOIDED", "while you channel, up to " + AbilityTip.Secs(tip.Def.ChannelSeconds));
            if (tip.V(ActionStat.StunDuration) > 0f) tip.Add("Then stunned", AbilityTip.Secs(tip.V(ActionStat.StunDuration)));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            a.Steps = target != null && sim.InReach(a.Actor, a.Definition, target) && sim.BeginVoid(target, float.MaxValue, a.Actor) ? 1 : 0;
        }

        public override bool OnChannelTick(BattleSimulation sim, ActionInstance a)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            return a.Steps > 0 && target != null && target.IsAlive && sim.IsVoided(target);   // nothing held: the channel is over
        }

        public override void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (a.Steps > 0 && target != null && sim.IsVoided(target)) sim.EndVoid(target, a.Stat(ActionStat.StunDuration), a.Actor);
        }
    }

    /// <summary>
    /// Cross Beyond Thresholds (threshold): blink to a point and remember where you stood. While its SelfStatuses'
    /// BeyondThresholds lasts, taking damage or using a dash sends you back there and ends it.
    /// </summary>
    public class ThresholdBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser | AbilityRole.Escape; }
        public override bool MovesUser { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Blink", "to a point");
            tip.Add("Way back", "damage taken or a dash returns you to where you stood");
        }

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx =>
            {
                if (ctx.Subject != null && ctx.Amount > 0f && ctx.Other != ctx.Subject && ctx.Subject.GetState<ThresholdState>() != null) sim.ReturnAcrossThreshold(ctx.Subject);
            });
            sim.Triggers.Subscribe(TriggerType.ActionStarted, ctx =>
            {
                if (ctx.Subject == null || ctx.Action == null || ctx.Subject.GetState<ThresholdState>() == null) return;
                var b = ActionBehaviors.Get(ctx.Action.BehaviorId);
                if (b != null && b.Dashes) sim.ReturnAcrossThreshold(ctx.Subject);
            });
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor;
            if (u.IsRooted) return;
            Vector3 origin = u.Position;
            sim.TeleportUnit(u, a.TargetPos, u);
            var old = u.GetState<ThresholdState>();
            if (old != null) u.RemoveState(old);
            u.AddState(new ThresholdState { Origin = origin });
            sim.EmitFx(u, null, "threshold", a.Definition, origin);
        }
    }

    /// <summary>
    /// Stride In Unison (stride): blink to a point, taking every ally within AreaRadius along, each as far from you as it
    /// was. Transcended: enemies within it come too.
    /// </summary>
    public class StrideBehavior : ActionBehavior
    {
        public override bool MovesUser { get { return true; } }
        public override float GatherRadius(Unit actor, ActionDefinition def) { return actor.ActionStat(def, ActionStat.AreaRadius); }
        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Blink", "you and allies within " + AbilityTip.Metres(tip.V(ActionStat.AreaRadius)) + ", in formation");
            if (tip.Def.CanTranscend) tip.Add("Transcended", "enemies within it come too");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor;
            if (u.IsRooted) return;
            float radius = a.Stat(ActionStat.AreaRadius);
            Vector3 from = u.Position;
            Vector3 delta = a.TargetPos - from; delta.y = 0f;
            var along = sim.State.Units.Where(o => o != u && o.IsAlive && (o.Team == u.Team || a.Transcended) && sim.IsTargetableBy(o, u))
                                       .Where(o => { Vector3 d = o.Position - from; d.y = 0f; return d.magnitude <= radius + o.BodyRadius; }).ToList();
            sim.TeleportUnit(u, from + delta, u);
            Vector3 moved = u.Position - from; moved.y = 0f;   // where it really landed (the map's edge may have cut it short)
            foreach (var o in along) sim.TeleportUnit(o, o.Position + moved, u);
            sim.EmitFx(u, null, "stride", a.Definition, u.Position, radius, from);
        }
    }

    /// <summary>
    /// Supersede (supersede): swap where two units stand: its target and a second unit picked after it
    /// (UnitCommand.SecondTargetId), both within its range. Each swapped enemy gets WeakpointsOnTarget weakpoints; each
    /// ally its TargetStatusesOnHit (a burst of action speed).
    /// </summary>
    public class SupersedeBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }
        public override bool PicksSecondUnit(ActionDefinition def) { return true; }

        /// <summary>Can 'second' be the other unit of the swap? (Alive, not the first, one 'actor' can target, in range.)</summary>
        private static string SecondProblem(BattleSimulation sim, Unit actor, ActionDefinition def, Unit first, Unit second)
        {
            if (second == null || !second.IsAlive) return "pick a second unit to swap it with";
            if (second == first) return "pick two different units";
            if (second.Team != actor.Team && !sim.IsTargetableBy(second, actor)) return "can't target the second unit";
            if (!sim.InReach(actor, def, second)) return "the second unit is out of range";
            return null;
        }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            var first = sim.GetUnit(cmd.TargetUnitId);
            reason = first == null ? "needs a target" : SecondProblem(sim, actor, cmd.Action, first, sim.GetUnit(cmd.SecondTargetId));
            return reason == null;
        }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Swap", "two units you pick, both in range");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            var first = sim.GetUnit(a.TargetUnitId);
            if (first == null || !first.IsAlive || !sim.InReach(actor, def, first)) return;
            var second = sim.GetUnit(a.SecondTargetId);
            if (SecondProblem(sim, actor, def, first, second) != null || !sim.SwapUnits(first, second, actor)) return;
            foreach (var u in new[] { first, second })
            {
                if (u.Team != actor.Team) { if (u != actor) sim.AddTargetWeakpoints(actor, u, def); }
                foreach (var spec in def.TargetStatusesOnHit)
                    if (spec != null && spec.AppliesToUnit(actor, u)) sim.ApplyStatus(u, spec.Create(sim.Now, actor.Id, null, actor.DurationScale(def)), actor);
            }
            sim.EmitFx(actor, first, "supersede", def, second.Position, 0f, first.Position);
        }
    }

    /// <summary>
    /// Mindmeld (mindmeld): every debuff on an ally (a status an enemy put on it: BattleSimulation.CleanseDebuffs) is gone,
    /// and if it is in its recovery, that is over at once. A stun is not a status: it stays.
    /// </summary>
    public class MindmeldBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Cleanse", "every debuff on the ally (not a stun)");
            tip.Add("Recovery", "theirs ends at once");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || target.Team != actor.Team || !sim.InReach(actor, def, target)) return;
            sim.CleanseDebuffs(target, actor);
            if (target.Phase == UnitPhase.Recovery) sim.EndRecoveryNow(target);
            sim.EmitFx(actor, target, "mindmeld", def, target.Position);
        }
    }

    /// <summary>
    /// Theory Of Displacement (displacement): two units, picked one after the other (UnitCommand.SecondTargetId), both
    /// within its range, are shoved toward each other, each up to DisplaceDistance (they stop when they meet). Transcended
    /// (TranscendStrain more strain): away from each other instead, each DisplaceDistance. Shoves, as a push is: off a
    /// ledge they fall, and Unstoppable or a Transcended Block holds them.
    /// </summary>
    public class DisplacementBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }
        public override bool PicksSecondUnit(ActionDefinition def) { return true; }
        public override bool MovesTargets(ActionDefinition def) { return true; }

        private static string SecondProblem(BattleSimulation sim, Unit actor, ActionDefinition def, Unit first, Unit second)
        {
            if (second == null || !second.IsAlive) return "pick a second unit";
            if (second == first) return "pick two different units";
            if (second.Team != actor.Team && !sim.IsTargetableBy(second, actor)) return "can't target the second unit";
            if (!sim.InReach(actor, def, second)) return "the second unit is out of range";
            return null;
        }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            var first = sim.GetUnit(cmd.TargetUnitId);
            reason = first == null ? "needs a target" : SecondProblem(sim, actor, cmd.Action, first, sim.GetUnit(cmd.SecondTargetId));
            return reason == null;
        }

        protected override void DescribeMovement(AbilityTip tip) { }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Displace", "two units you pick toward each other, each up to " + AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)));
            if (tip.Def.CanTranscend) tip.Add("Transcended", "away from each other instead");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            var first = sim.GetUnit(a.TargetUnitId);
            if (first == null || !first.IsAlive || !sim.InReach(actor, def, first)) return;
            var second = sim.GetUnit(a.SecondTargetId);
            if (SecondProblem(sim, actor, def, first, second) != null) return;
            float reach = a.Stat(ActionStat.DisplaceDistance);
            Vector3 line = second.Position - first.Position; line.y = 0f;
            float apart = line.magnitude;
            Vector3 dir = apart > 1e-4f ? line / apart : Vector3.forward;
            // Toward: each closes up to its reach, meeting in the middle at the latest. Away: each goes its full reach.
            float each = a.Transcended ? reach : Mathf.Min(reach, Mathf.Max(0f, apart - first.BodyRadius - second.BodyRadius - BattleSimulation.StandingGap - 0.1f) / 2f);
            if (each <= 0.01f) return;
            Vector3 a1 = first.Position, a2 = second.Position;
            Vector3 to1 = a1 + (a.Transcended ? -dir : dir) * each, to2 = a2 + (a.Transcended ? dir : -dir) * each;
            sim.PushUnit(first, sim.Paths.ClampDash(a1, to1), BattleSimulation.DisplaceDuration, actor);
            sim.PushUnit(second, sim.Paths.ClampDash(a2, to2), BattleSimulation.DisplaceDuration, actor);
            foreach (var u in new[] { first, second })
                if (u.Team != actor.Team) sim.AddTargetWeakpoints(actor, u, def);
            sim.EmitFx(actor, first, "displacement", def, second.Position, a.Transcended ? 1f : 0f, a1);
        }
    }

    /// <summary>
    /// Pressure Points (pressure_points): break up to BreaksWeakpoints of an enemy's weakpoints, from any side (the last one
    /// breaking interrupts it). One with none is shoved DisplaceDistance away instead.
    /// </summary>
    public class PressurePointsBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }
        public override bool MovesTargets(ActionDefinition def) { return true; }

        protected override void DescribeMovement(AbilityTip tip) { }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("No weakpoints", "pushes them " + AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)) + " away instead");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || !sim.InReach(actor, def, target)) return;
            if (target.Weakpoints.Count > 0) { sim.BreakWeakpoints(target, Mathf.Max(1, def.BreaksWeakpoints), actor); return; }
            Vector3 away = target.Position - actor.Position; away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;
            sim.PushUnit(target, sim.Paths.ClampDash(target.Position, target.Position + away.normalized * a.Stat(ActionStat.DisplaceDistance)), BattleSimulation.DisplaceDuration, actor);
        }
    }
}
