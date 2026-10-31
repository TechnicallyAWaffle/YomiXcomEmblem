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
        public const float WallMultiplier = 2f;
        public const float WallTolerance = 0.3f;   // a push stopped more than this short of its full distance hit a wall

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            Vector3 dir = a.TargetPos - a.Actor.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            foreach (var target in sim.UnitsInArea(a.Actor, a.Definition, a.TargetPos))
            {
                Vector3 full = target.Position + dir * a.Definition.DisplaceDistance;
                Vector3 landed = sim.Paths.ClampDash(target.Position, full);   // higher ground counts as a wall; a ledge drops them off it
                Vector3 shortBy = full - landed; shortBy.y = 0f;
                bool walled = shortBy.magnitude > WallTolerance;
                effects.Add(new AttackEffect
                {
                    Source = a.Actor, TargetId = target.Id, Definition = a.Definition, Tag = walled ? "bash_wall" : a.Tag,
                    DamageMultiplier = a.DamageMultiplier * (walled ? WallMultiplier : 1f), StunMultiplier = walled ? WallMultiplier : 1f,
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
        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            if (a.Tag == BattleSimulation.ThrowTag) return;
            sim.ApplyStatus(a.Actor, new StatusEffect { Id = StatusIds.Grappling, SourceId = a.Actor.Id, ExpiresAt = float.MaxValue, StunImmune = true }, a.Actor);
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
                    StunMultiplier = def.StunDuration > 0f ? sim.GrappleThrowStun / def.StunDuration : 0f,
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
}
