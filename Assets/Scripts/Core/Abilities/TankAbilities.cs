using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Bash: hit everyone in a short strip in front of the Tank (a Line area toward the target point) and shove them
    /// DisplaceDistance straight back. Anyone the push drives into a wall (the navmesh edge stops them short) or the face
    /// of higher ground takes WallMultiplier damage and stun. Pushed off a ledge, they fall (see BattleSimulation.Relocate).
    /// </summary>
    public class BashBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return true; }

        public const float WallMultiplier = 2f;

        /// <summary>Damage and stun multiplier for a target driven into a wall (1 = no bonus: Repel).</summary>
        protected virtual float WallBonus { get { return WallMultiplier; } }
        public const float WallTolerance = 0.3f;   // a push stopped more than this short of its full distance hit a wall

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            Vector3 dir = a.TargetPos - a.Actor.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            foreach (var target in sim.UnitsInArea(a.Actor, a.Definition, a.TargetPos))
            {
                Vector3 full = target.Position + dir * a.Stat(ActionStat.DisplaceDistance);
                Vector3 landed = sim.Paths.ClampDash(target.Position, full);   // higher ground counts as a wall; a ledge drops them off it
                Vector3 shortBy = full - landed; shortBy.y = 0f;
                bool walled = WallBonus > 1f && shortBy.magnitude > WallTolerance;
                effects.Add(new AttackEffect
                {
                    Source = a.Actor, TargetId = target.Id, Definition = a.Definition, Tag = walled ? "bash_wall" : a.Tag,
                    DamageMultiplier = a.DamageMultiplier * (walled ? WallBonus : 1f), StunMultiplier = walled ? WallBonus : 1f,
                    IgnoreRange = true, IgnoreCover = true, HasPush = true, PushTo = landed,
                });
            }
        }
    }

    /// <summary>
    /// Subjugate. The grab: stun immune from the start of the windup ("grappling"), a Heavy hit for no damage that stuns
    /// for StunDuration. If it lands and the target is stunned, the Tank holds it (BattleSimulation.StartGrapple): it
    /// carries the target, can only move, headbutts it (the LinkedAction) every GrappleHeadbuttSeconds and is asked after
    /// each one whether to let go. When the stun runs out or it lets go, the throw winds up (this same action, tagged
    /// BattleSimulation.ThrowTag) and hurls the target GrappleThrowDistance the way the Tank faces, for this action's Damage.
    /// </summary>
    public class SubjugateBehavior : ActionBehavior
    {
        // The grab's own numbers read as the hold; Damage is the throw's; the headbutts are the linked action's.
        public override void Describe(AbilityTip tip)
        {
            var headbutt = tip.Def.LinkedAction;
            if (headbutt != null)
                tip.Add("Headbutts", string.Format("{0:0} / {1:0.#}s", tip.Who != null ? tip.Who.ActionStat(headbutt, ActionStat.Damage) : headbutt.Damage, tip.Sim != null ? tip.Sim.GrappleHeadbuttSeconds : 0.5f));
            DescribeHit(tip, "Throw", "Hold", false);
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            if (a.Tag == BattleSimulation.ThrowTag) return;
            sim.ApplyStatus(a.Actor, new StatusEffect { Id = StatusIds.Grappling, SourceId = a.Actor.Id, ExpiresAt = float.MaxValue, Flags = StatusFlags.StunImmune  }, a.Actor);
        }

        // An interrupted grab loses its stun immunity once the windup is over (BattleSimulation.TickGrapples); an
        // interrupted throw drops the target where it is.
        public override void OnInterrupted(BattleSimulation sim, ActionInstance a)
        {
            if (a.Tag == BattleSimulation.ThrowTag) sim.EndGrapple(a.Actor);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            var target = sim.GetUnit(a.TargetUnitId);
            var def = a.Definition;
            if (a.Tag == BattleSimulation.ThrowTag)
            {
                Vector3 dir = sim.GrappleFacing(actor);
                sim.EndGrapple(actor);   // let go first: the throw's push moves it from here
                if (target == null || !target.IsAlive) return;
                Vector3 landed = sim.Paths.ClampDash(target.Position, target.Position + dir * sim.GrappleThrowDistance);
                effects.Add(new AttackEffect
                {
                    Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = BattleSimulation.ThrowTag,
                    StunMultiplier = def.StunDuration > 0f ? sim.GrappleThrowStun / actor.ActionStat(def, ActionStat.StunDuration) : 0f,   // the throw stuns for GrappleThrowStun whatever the grab's stun
                    AccuracyBonus = 1f, IgnoreRange = true, IgnoreCover = true, HasPush = true, PushTo = landed,   // it is in your hands: it can't miss
                });
                return;
            }
            if (target == null || !target.IsAlive) return;
            effects.Add(new AttackEffect
            {
                Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = 0f, Tag = a.Tag,   // the grab itself does no damage
                OnLanded = (s, t) => s.StartGrapple(actor, t, def),
            });
        }
    }

    /// <summary>
    /// Heretic's Ascent (aerial_slam): a grab, a single hit (usually Heavy, so it breaks a windup) that stuns; if it lands and
    /// the target is stunned, the user carries it up into the air and slams it down behind itself (BattleSimulation.StartAerialSlam:
    /// height DisplaceDistance, timing AirRiseSeconds / AirHoldSeconds / AirSlamSeconds, StunDuration again after the landing).
    /// </summary>
    public class AerialSlamBehavior : ActionBehavior
    {
        protected override void DescribeMovement(AbilityTip tip)
        {
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Slam", string.Format("from {0:0.#}m up", tip.V(ActionStat.DisplaceDistance)));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            var actor = a.Actor;
            var def = a.Definition;
            effects.Add(new AttackEffect
            {
                Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                OnLanded = (s, t) => s.StartAerialSlam(actor, t, def),
            });
        }
    }

    /// <summary>
    /// Behemoth's Arrival (arrival): blink to the target point (as far as Range that way, like Polystep), then slam down: a
    /// hit on every enemy in AreaRadius of where it lands (no cover, no range check) that lifts each one it stuns
    /// DisplaceDistance metres into the air for the stun, to drop and take fall damage (BattleSimulation.LiftUnit, as Amblivate).
    /// </summary>
    public class ArrivalBehavior : BlinkBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return true; }

        protected override void DescribeMovement(AbilityTip tip)
        {
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Lift", AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            if (actor.IsRooted) return;
            base.OnResolve(sim, a, effects);   // the blink
            float height = a.Stat(ActionStat.DisplaceDistance);
            foreach (var target in sim.UnitsInArea(actor, a.Definition, actor.Position))
                effects.Add(new AttackEffect
                {
                    Source = actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                    IgnoreRange = true, IgnoreCover = true,
                    OnLanded = height > 0f ? (s, t) => s.LiftUnit(t, height, actor) : (System.Action<BattleSimulation, Unit>)null,
                });
        }
    }
}
