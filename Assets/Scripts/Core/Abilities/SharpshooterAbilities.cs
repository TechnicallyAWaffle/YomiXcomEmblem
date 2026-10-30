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
        public const float ReactionWindup = 0.35f;

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
                    sim.QueueFreeAction(aimer, stance.LinkedAction, actor.Id, ReactionWindup);
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
        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var def = a.Definition;
            effects.Add(new TrapEffect
            {
                Source = a.Actor,
                Trap = new Trap
                {
                    OwnerId = a.Actor.Id, OwnerTeam = a.Actor.Team, Position = a.TargetPos,
                    Radius = def.AreaRadius > 0f ? def.AreaRadius : 1f,
                    ArmsAt = sim.Now + def.TrapArmDelay, ExpiresAt = sim.Now + def.TrapLifetime,
                    Statuses = new List<StatusSpec>(def.TargetStatusesOnHit),
                },
            });
        }
    }
}
