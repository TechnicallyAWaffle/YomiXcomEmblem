using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // Paradigm User abilities that need rules beyond data. Reconstruct is heal_displace; Rift Line and Cataclysm are area_attack;
    // Second Mind is status_spell.

    /// <summary>Stutter: a near-instant jolt that breaks the target's windup or charge. No damage, no hit roll; costs the user strain.</summary>
    public class StutterBehavior : ActionBehavior
    {
        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !sim.InReach(a.Actor, a.Definition, target)) return;
            sim.InterruptUnit(target, "stutter");
        }
    }

    /// <summary>
    /// Second Mind (and any status-only spell): puts TargetStatusesOnHit on the target unit as it lands, no hit roll. Each
    /// status's AppliesTo picks whom it is for, so one spell can haste an ally and slow an enemy. Fizzles if the target
    /// left its range or sight during the windup.
    /// </summary>
    public class StatusSpellBehavior : ActionBehavior
    {
        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !sim.InReach(a.Actor, a.Definition, target)) return;
            foreach (var spec in a.Definition.TargetStatusesOnHit)
                if (spec.AppliesToUnit(a.Actor, target))
                    effects.Add(new ApplyStatusEffect { Source = a.Actor, TargetId = target.Id, Status = spec.Create(sim.Now, a.Actor.Id, a.Definition.LinkedAction) });
        }
    }

    /// <summary>
    /// Wrinkles of Reality: teleport the target unit (ally, enemy or the caster) to the chosen point (the command's TargetPos),
    /// at most DestinationRange from where the target stands when it lands, onto the nearest walkable ground. No hit roll.
    /// </summary>
    public class TeleportBehavior : ActionBehavior
    {
        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !sim.InReach(a.Actor, a.Definition, target)) return;
            Vector3 d = a.TargetPos - target.Position; d.y = 0f;
            float reach = a.Definition.DestinationRange;
            if (reach > 0f && d.magnitude > reach) d = d.normalized * reach;
            sim.TeleportUnit(target, sim.WalkablePointNear(target.Position + d), a.Actor);
        }
    }

    /// <summary>
    /// Amblivate: a stun (a hit for this action's Damage, 0 by default) that lifts the target DisplaceDistance metres into
    /// the air for as long as the stun lasts, then drops it to take fall damage (BattleSimulation.LiftUnit).
    /// </summary>
    public class LevitateBehavior : ActionBehavior
    {
        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            var actor = a.Actor;
            float height = a.Definition.DisplaceDistance;
            effects.Add(new AttackEffect
            {
                Source = actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                OnLanded = (s, t) => s.LiftUnit(t, height, actor),
            });
        }
    }

    /// <summary>
    /// Lance of Destiny: a charge ability. Released toward a point, the lance hits the first enemy along its line (Range
    /// long, LineWidth wide); damage and stun grow from x1 to ChargeDamageMultiplier / ChargeStunMultiplier with charge.
    /// </summary>
    public class LanceBehavior : ActionBehavior
    {
        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var def = a.Definition;
            Unit first = null;
            float best = float.MaxValue;
            foreach (var u in sim.UnitsInArea(a.Actor, def, a.TargetPos))
            {
                float d = Vector3.Distance(a.Actor.Position, u.Position);
                if (d < best) { best = d; first = u; }
            }
            if (first == null) return;
            effects.Add(new AttackEffect
            {
                Source = a.Actor, TargetId = first.Id, Definition = def, Tag = a.Tag,
                DamageMultiplier = a.DamageMultiplier * Mathf.Lerp(1f, def.ChargeDamageMultiplier, a.Charge),
                StunMultiplier = Mathf.Lerp(1f, def.ChargeStunMultiplier, a.Charge),
                IgnoreRange = true, IgnoreCover = true,
            });
        }
    }
}
