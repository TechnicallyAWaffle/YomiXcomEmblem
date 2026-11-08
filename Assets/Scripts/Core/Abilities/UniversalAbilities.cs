using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Block, every unit's universal option: an instant guard (SelfStatusesOnWindup "blocking": +block, stuns that land last
    /// half as long: StatId.StunTaken) held
    /// through the action's short recovery, paid for in strain. Taking a hit while guarding hands the turn straight
    /// back (the recovery ends), so the unit can block again (Block is a follow-up: Space) or punish.
    /// Blocking is always allowed: past max strain it just leaves you at max (and so open to Unravel, which pierces Block).
    /// A Heavy hit that lands on the guard puts Block on cooldown for BattleSimulation.HeavyBlockCooldown.
    /// Transcended: the guard raises Block to 100% (block pierce still gets that share through), makes the unit stun immune
    /// (Unravel aside), isn't worn down by hits, and heavy hits don't put Block on cooldown.
    /// </summary>
    public class BlockBehavior : SelfBuffBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Guard; }

        public override bool AllowsStrainOverflow { get { return true; } }

        /// <summary>The guard's Stacks once it is Transcended (1 marks a guard already scaled by GuardBlock).</summary>
        public const int TranscendedGuard = 2;

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx =>
            {
                var u = ctx.Subject;
                var guard = u.GetStatus(StatusIds.Blocking);
                if (guard == null) return;
                if (ctx.Impact == ImpactType.Heavy && guard.Stacks < TranscendedGuard) sim.BlockDownFor(u, sim.HeavyBlockCooldown);
                if (u.IsAlive && u.Phase == UnitPhase.Recovery) sim.EndRecoveryNow(u);
            });
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            var guard = a.Actor.GetStatus(StatusIds.Blocking);   // put up as the windup started (SelfStatusesOnWindup)
            if (guard == null) return;
            // Its Block is scaled by the unit's GuardBlock (Living Carapace), once: Stacks marks a guard already scaled.
            float more = a.Actor.Stat(StatId.GuardBlock, 1f);
            if (guard.Stacks == 0 && guard.Modifiers != null && Mathf.Abs(more - 1f) > 1e-4f)
                for (int i = 0; i < guard.Modifiers.Count; i++)
                    if (guard.Modifiers[i].Stat == StatId.Block && guard.Modifiers[i].Op == ModOp.Add)
                        guard.Modifiers[i] = Modifier.Add(StatId.Block, guard.Modifiers[i].Value * more);
            guard.Stacks = 1;
            if (a.Transcended)
            {
                // Block raised to 100% (clamped there, so block pierce still gets through), stun immune, not worn down.
                if (guard.Modifiers == null) guard.Modifiers = new List<Modifier>();
                guard.Modifiers.Add(Modifier.Add(StatId.Block, 1f));
                guard.Flags |= StatusFlags.StunImmune | StatusFlags.KeepsBlock;
                guard.Stacks = TranscendedGuard;
                EtchingTraits.PrimeRevenge(sim, a.Actor);
            }
        }
    }

    /// <summary>
    /// Dash, every unit's other universal option (key G): slide toward the target point at dodge speed, as far as the
    /// unit's dash distance (BattleSimulation.DashDestination), paid for in strain. Like a dodge, the unit can't start
    /// anything until it lands. Transcended: TranscendDashDistanceMult times as far, in TranscendDashSeconds.
    /// </summary>
    public class DashBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Dash; }

        public override bool MovesUser { get { return true; } }   // not while airborne
        public override bool Dashes { get { return true; } }      // a CRIPPLED unit can't

        public override bool KeepsQueuedOrder { get { return true; } }   // dashing into range for a queued shot keeps the shot

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
