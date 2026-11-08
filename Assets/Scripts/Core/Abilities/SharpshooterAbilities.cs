using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // Sharpshooter abilities that need rules beyond data. The rest are plain data on basic_attack:
    //   Snapshot      - short windup, low BaseAccuracy.
    //   Marking Shot  - TargetStatusesOnHit "marked", SelfStatuses "hidden" (Untargetable, BreaksOnDamage).
    //   Tracking Shot - long windup, InterruptedByDamage + InterruptedByStun, PiercesTerrain, BlockPierce 0.5.

    /// <summary>Snipe: aim, then fire. Cancels if the target leaves line of sight (or goes hidden) during the windup.</summary>
    public class SnipeBehavior : BasicAttackBehavior
    {
        // A snipe is saved for marked targets; it marks nothing itself.
        public override AbilityRole DefaultRoles(ActionDefinition def) { return (base.DefaultRoles(def) & ~AbilityRole.Mark) | AbilityRole.Snipe; }

        public override bool OnWindupTick(BattleSimulation sim, ActionInstance a)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || !sim.IsTargetableBy(target, a.Actor)) return false;
            return a.Definition.PiercesTerrain || sim.Visibility.HasLineOfSight(a.Actor, target);
        }
    }

    /// <summary>
    /// Take Aim: enter a stance (the "take_aim" status, whose LinkedAction is Snipe). The first enemy that starts
    /// an action within the snipe's range and sight gets sniped as a fast reaction, which ends the stance. Your own other
    /// actions don't cancel it; it simply runs out (the status lasts 3s in the squad data).
    /// </summary>
    public class TakeAimBehavior : SelfBuffBehavior
    {
        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.ActionStarted, ctx =>
            {
                var actor = ctx.Subject;
                foreach (var aimer in sim.State.Units)
                {
                    if (!aimer.IsAlive || aimer.Team == actor.Team || aimer.Phase == UnitPhase.Stunned) continue;
                    var stance = aimer.GetStatus(StatusIds.TakeAim);
                    if (stance == null || stance.LinkedAction == null) continue;
                    if (!sim.InReach(aimer, stance.LinkedAction, actor)) continue;
                    sim.RemoveStatus(aimer, StatusIds.TakeAim);
                    sim.QueueFreeAction(aimer, stance.LinkedAction, actor.Id, sim.ReactionWindup);
                }
            });
        }
    }

    /// <summary>Retaliate: the "retaliate" status adds dodge and dash distance; every dodge fires its LinkedAction back for free.</summary>
    public class RetaliateBehavior : SelfBuffBehavior
    {
        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.Dodged, ctx =>
            {
                var stance = ctx.Subject.GetStatus(StatusIds.Retaliate);
                if (stance == null || stance.LinkedAction == null || ctx.Other == null || !ctx.Other.IsAlive) return;
                if (!sim.InReach(ctx.Subject, stance.LinkedAction, ctx.Other)) return;
                sim.QueueFreeAction(ctx.Subject, stance.LinkedAction, ctx.Other.Id);
            });
        }
    }

    /// <summary>Trap: toss a trap at the target point. It arms after TrapArmDelay and applies TargetStatusesOnHit (rooted + marked).</summary>
    public class TrapBehavior : ActionBehavior
    {
        public override void Describe(AbilityTip tip) { base.Describe(tip); tip.TargetStatusesTitle = "SPRUNG BY"; }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var def = a.Definition;
            effects.Add(new TrapEffect
            {
                Source = a.Actor,
                Trap = new Trap
                {
                    OwnerId = a.Actor.Id, OwnerTeam = a.Actor.Team, Position = a.TargetPos,
                    Radius = def.AreaRadius > 0f ? a.Stat(ActionStat.AreaRadius) : 1f,
                    ArmsAt = sim.Now + def.TrapArmDelay, ExpiresAt = sim.Now + def.TrapLifetime,
                    Statuses = new List<StatusSpec>(def.TargetStatusesOnHit), DurationScale = a.Actor.DurationScale(def),
                },
            });
        }
    }

    /// <summary>
    /// Skyward Demise (leap): leap toward the target point (up to Range across, over the nearest walkable ground there) and
    /// DisplaceDistance metres up, and hover there (BattleSimulation.HoverUnit) for as long as the longest of its
    /// SelfStatuses lasts (its damage boost). The unit can act but not move up there. The hover ends early once an ability
    /// used from the air lands, or when damage knocks it down (a fall); its SelfStatuses come off when it ends.
    /// </summary>
    public class LeapBehavior : ActionBehavior
    {
        protected override void DescribeMovement(AbilityTip tip)
        {
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Leap height", AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)));
        }

        public override bool MovesUser { get { return true; } }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            var def = a.Definition;
            if (actor.IsRooted) return;
            Vector3 d = a.TargetPos - actor.Position; d.y = 0f;
            float reach = a.Stat(ActionStat.Range);
            if (d.magnitude > reach) d = d.normalized * reach;
            Vector3 to = sim.WalkablePointNear(actor.Position + d, actor.Position) + Vector3.up * a.Stat(ActionStat.DisplaceDistance);
            float lasts = 0f;
            var ids = new List<string>();
            foreach (var s in def.SelfStatuses) { lasts = Mathf.Max(lasts, s.Duration * actor.DurationScale(def)); ids.Add(s.Id); }
            sim.HoverUnit(actor, to, sim.Now + lasts, ids);
        }
    }

    /// <summary>
    /// Extricate (recoil_shot): a shot like any other, and its kick blasts the shooter 'recoil' metres (5) straight away
    /// from the target, hit or miss (a shove: off a ledge it falls, a wall stops it short).
    /// </summary>
    public class RecoilShotBehavior : BasicAttackBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Escape; }

        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("recoil", 5f); } }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Recoil", AbilityTip.Metres(Param(tip.Def, "recoil")) + " back");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            base.OnResolve(sim, a, effects);
            var actor = a.Actor;
            Vector3 away = actor.Position - target.Position; away.y = 0f;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.back;
            Vector3 to = sim.Paths.ClampDash(actor.Position, actor.Position + away * Param(a.Definition, "recoil"));
            sim.PushUnit(actor, to, BattleSimulation.DisplaceDuration, actor);
        }
    }

    /// <summary>
    /// Mark of the Unmaker (unmaker): a shot that puts a stack of Unmake on what it hits (lasting 'mark_seconds', 30;
    /// each one refreshes it). A target already at 'stacks' (3) instead has its marks spent: Fated (its actions slowed to
    /// 'fated_speed', 0.35) for 'fated_seconds' (1), then it Unravels, whatever its strain. Transcended: the shooter is
    /// Hidden (untargetable; damage reveals it) while it winds up.
    /// </summary>
    public class UnmakerBehavior : BasicAttackBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("stacks", 3f);
                yield return new BehaviorParam("mark_seconds", 30f);
                yield return new BehaviorParam("fated_seconds", 1f);
                yield return new BehaviorParam("fated_speed", 0.35f);
            }
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Unmake", string.Format("+1 stack; at {0:0}, Fated {1} then Unravel", Param(tip.Def, "stacks"), AbilityTip.Secs(Param(tip.Def, "fated_seconds"))));
            tip.Add("Transcended", "hidden while winding up");
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            if (!a.Transcended) return;
            sim.ApplyStatus(a.Actor, new StatusEffect
            {
                Id = StatusIds.Hidden, SourceId = a.Actor.Id, ExpiresAt = a.ResolveAt + BattleSimulation.TickLength,
                Flags = StatusFlags.Untargetable | StatusFlags.BreaksOnDamage,
            }, a.Actor);
        }

        public override void OnInterrupted(BattleSimulation sim, ActionInstance a) { EndHidden(sim, a); }

        private static void EndHidden(BattleSimulation sim, ActionInstance a)
        {
            var hidden = a.Actor.GetStatus(StatusIds.Hidden);
            if (a.Transcended && hidden != null && hidden.SourceId == a.Actor.Id && hidden.ExpiresAt <= a.ResolveAt + 2f * BattleSimulation.TickLength)
                sim.RemoveStatus(a.Actor, StatusIds.Hidden);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            EndHidden(sim, a);   // seen again as it fires
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            var actor = a.Actor;
            var def = a.Definition;
            var mark = target.GetStatus(StatusIds.Unmake);
            if (mark != null && mark.Stacks >= Mathf.RoundToInt(Param(def, "stacks")))
                sim.EmitFx(actor, target, "unmade", def, target.Position, 1.5f);   // the unmaking shot: hit or miss, it looks the part
            effects.Add(new AttackEffect
            {
                Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                OnLanded = (s, t) => Mark(s, actor, t, def),
            });
        }

        private void Mark(BattleSimulation sim, Unit actor, Unit target, ActionDefinition def)
        {
            var mark = target.GetStatus(StatusIds.Unmake);
            int stacks = mark != null ? mark.Stacks : 0;
            if (stacks >= Mathf.RoundToInt(Param(def, "stacks")))
            {
                sim.RemoveStatus(target, StatusIds.Unmake);
                sim.ApplyStatus(target, new StatusEffect
                {
                    Id = StatusIds.Fated, SourceId = actor.Id, ExpiresAt = sim.Now + Param(def, "fated_seconds") * actor.DurationScale(def),
                    Modifiers = new List<Modifier> { Modifier.Mult(StatId.ActionSpeed, Param(def, "fated_speed")) },
                    Flags = StatusFlags.UnravelsOnExpire,
                }, actor);
                return;
            }
            var next = new StatusEffect { Id = StatusIds.Unmake, SourceId = actor.Id, ExpiresAt = sim.Now + Param(def, "mark_seconds") };
            sim.ApplyStatus(target, next, actor);
            sim.SetStacks(target, next, stacks + 1);
        }
    }
}
