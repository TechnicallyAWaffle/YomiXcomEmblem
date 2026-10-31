using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Block, every unit's universal option: an instant guard (SelfStatusesOnWindup "blocking": +block, stun immune) held
    /// through the action's short recovery, paid for in strain. Taking a hit while guarding hands the turn straight
    /// back (the recovery ends), so the unit can block again (Block is a follow-up: Space) or punish.
    /// Blocking is always allowed: past max strain it just leaves you at max (and so open to Unravel, which pierces Block).
    /// A Heavy hit that lands on the guard puts Block on cooldown for BattleSimulation.HeavyBlockCooldown.
    /// Transcended: the guard stops all damage (and strain, and Unravel) from every hit, pierce or not, and heavy hits
    /// don't put it on cooldown.
    /// </summary>
    public class BlockBehavior : SelfBuffBehavior
    {
        public override bool AllowsStrainOverflow { get { return true; } }

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx =>
            {
                var u = ctx.Subject;
                var guard = u.GetStatus(StatusIds.Blocking);
                if (guard == null) return;
                if (ctx.Impact == ImpactType.Heavy && !guard.NegatesDamage)
                    foreach (var a in u.Actions)
                        if (a.BehaviorId == "block") u.CooldownReadyAt[a.Id] = Mathf.Max(sim.Now + sim.HeavyBlockCooldown, u.CooldownReadyAt.TryGetValue(a.Id, out var at) ? at : 0f);
                if (u.IsAlive && u.Phase == UnitPhase.Recovery) sim.EndRecoveryNow(u);
            });
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            if (!a.Transcended) return;
            var guard = a.Actor.GetStatus(StatusIds.Blocking);   // put up as the windup started (SelfStatusesOnWindup)
            if (guard != null) guard.NegatesDamage = true;
        }
    }

    /// <summary>
    /// Dash, every unit's other universal option (key G): slide toward the target point at dodge speed, as far as the
    /// unit's dash distance (BattleSimulation.DashDestination), paid for in strain. Like a dodge, the unit can't start
    /// anything until it lands. Transcended: TranscendDashDistanceMult times as far, in TranscendDashSeconds.
    /// </summary>
    public class DashBehavior : ActionBehavior
    {
        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            sim.DashUnitTo(a.Actor, a.TargetPos, a.Transcended);
        }
    }
}
