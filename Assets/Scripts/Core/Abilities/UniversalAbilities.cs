using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Block, every unit's universal option: an instant guard (SelfStatusesOnWindup "blocking": +block, stun immune) held
    /// through the action's short recovery, paid for in strain. Taking a hit while guarding hands the turn straight
    /// back (the recovery ends), so the unit can block again (Block is a follow-up: Space) or punish.
    /// Blocking is always allowed: past max strain it just leaves you at max (and so open to Unravel, which pierces Block).
    /// </summary>
    public class BlockBehavior : SelfBuffBehavior
    {
        public override bool AllowsStrainOverflow { get { return true; } }

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx =>
            {
                var u = ctx.Subject;
                if (u.IsAlive && u.Phase == UnitPhase.Recovery && u.HasStatus(StatusIds.Blocking)) sim.EndRecoveryNow(u);
            });
        }
    }

    /// <summary>
    /// Dash, the other universal option: slide toward the target point at dodge speed, as far as the unit's dash distance
    /// (BattleSimulation.DashDestination), paid for in strain. Like a dodge, the unit can't start anything until it lands.
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
            sim.DashUnitTo(a.Actor, a.TargetPos);
        }
    }
}
