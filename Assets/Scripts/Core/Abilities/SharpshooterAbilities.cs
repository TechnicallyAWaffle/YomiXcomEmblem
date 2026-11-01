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
                    Statuses = new List<StatusSpec>(def.TargetStatusesOnHit),
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
            Vector3 to = sim.WalkablePointNear(actor.Position + d) + Vector3.up * a.Stat(ActionStat.DisplaceDistance);
            float lasts = 0f;
            var ids = new List<string>();
            foreach (var s in def.SelfStatuses) { lasts = Mathf.Max(lasts, s.Duration); ids.Add(s.Id); }
            sim.HoverUnit(actor, to, sim.Now + lasts, ids);
        }
    }
}
