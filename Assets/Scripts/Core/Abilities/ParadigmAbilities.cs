using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // Paradigm User abilities that need rules beyond data. Reconstruct is heal_displace; Rift Line and Cataclysm are area_attack;
    // Second Mind is status_spell.

    /// <summary>Stutter: a near-instant jolt that breaks the target's windup or charge. No damage, no hit roll; costs the user strain.</summary>
    public class StutterBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }   // no hit roll, no damage: it just interrupts

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !sim.InReach(a.Actor, a.Definition, target)) return;
            sim.InterruptUnit(target, "stutter");
            if (a.Definition.GuardBreakSeconds > 0f) sim.BreakGuard(target, a.Definition.GuardBreakSeconds);
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            if (tip.Def.GuardBreakSeconds > 0f) tip.Add("Breaks guard", "no Block for " + AbilityTip.Secs(tip.Def.GuardBreakSeconds));
        }
    }

    /// <summary>
    /// Second Mind (and any status-only spell): puts TargetStatusesOnHit on the target unit as it lands, no hit roll. Each
    /// status's AppliesTo picks whom it is for, so one spell can haste an ally and slow an enemy. Fizzles if the target
    /// left its range or sight during the windup. Its StrainOnTarget lands too: negative relieves the target's strain
    /// (Minutes Turn To Centuries), positive adds to it.
    /// </summary>
    public class StatusSpellBehavior : ActionBehavior
    {
        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            float strain = tip.V(ActionStat.StrainOnTarget);
            if (strain < 0f) tip.Add("Strain relief", (-strain).ToString("0"));
            else if (strain > 0f) tip.Add("Target strain", "+" + strain.ToString("0"));
            foreach (var s in tip.Def.TargetStatusesOnHit)   // a delayed heal (Unmake The Harm)
                if (s != null && (s.HealOnExpire > 0f || s.HealMissingOnExpire > 0f))
                    tip.Add("Heals", string.Format("{0:0} + {1:0}% of missing health, after {2}", s.HealOnExpire, s.HealMissingOnExpire * 100f, AbilityTip.Secs(s.Duration)));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !sim.InReach(a.Actor, a.Definition, target)) return;
            // StrainOnTarget on the target as it lands: negative relieves (Minutes Turn To Centuries), positive adds.
            float strain = a.Stat(ActionStat.StrainOnTarget);
            if (strain > 0f && target.Team != a.Actor.Team) strain *= a.Actor.StrainDealtMult();   // as a hit's strain (Despair and Panic)
            if (Mathf.Abs(strain) > 1e-4f) sim.AddStrain(target, strain);
            foreach (var spec in a.Definition.TargetStatusesOnHit)
                if (spec.AppliesToUnit(a.Actor, target))
                    effects.Add(new ApplyStatusEffect { Source = a.Actor, TargetId = target.Id, Status = spec.Create(sim.Now, a.Actor.Id, a.Definition.LinkedAction, a.Actor.DurationScale(a.Definition)) });
        }
    }

    /// <summary>
    /// Wrinkles of Reality: teleport the target unit (ally, enemy or the caster) to the chosen point (the command's TargetPos),
    /// at most DestinationRange from where the target stands when it lands, onto the nearest walkable ground. No hit roll.
    /// </summary>
    public class TeleportBehavior : ActionBehavior
    {
        public override void Describe(AbilityTip tip) { base.Describe(tip); tip.Add("Teleport", "to a point"); }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !sim.InReach(a.Actor, a.Definition, target)) return;
            Vector3 d = a.TargetPos - target.Position; d.y = 0f;
            float reach = a.Definition.DestinationRange;
            if (reach > 0f && d.magnitude > reach) d = d.normalized * reach;
            sim.TeleportUnit(target, sim.WalkablePointNear(target.Position + d, target.Position), a.Actor);
        }
    }

    /// <summary>
    /// Amblivate: a stun (a hit for this action's Damage, 0 by default) that lifts the target DisplaceDistance metres into
    /// the air for as long as the stun lasts, then drops it to take fall damage (BattleSimulation.LiftUnit).
    /// </summary>
    public class LevitateBehavior : ActionBehavior
    {
        protected override void DescribeMovement(AbilityTip tip)
        {
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Lift", AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            var actor = a.Actor;
            float height = a.Stat(ActionStat.DisplaceDistance);
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
        public override bool RollsToHit(ActionDefinition def) { return true; }

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

    /// <summary>
    /// A status held up by a channel (True Inversion): as it lands, TargetStatusesOnHit go on the target unit (each per its
    /// AppliesTo) for the channel's length (ChannelSeconds); the user then channels, and they come off when the channel
    /// ends, however it ends (damage on the user breaks it). No hit roll; fizzles if the target left range during the windup.
    /// </summary>
    public class ChanneledStatusBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Channel", "up to " + AbilityTip.Secs(tip.Def.ChannelSeconds) + "; damage on you breaks it");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !sim.InReach(a.Actor, a.Definition, target)) return;
            foreach (var spec in a.Definition.TargetStatusesOnHit)
            {
                if (spec == null || !spec.AppliesToUnit(a.Actor, target)) continue;
                var status = spec.Create(sim.Now, a.Actor.Id, a.Definition.LinkedAction);
                status.ExpiresAt = sim.Now + Mathf.Max(a.Definition.ChannelSeconds, BattleSimulation.TickLength) + BattleSimulation.TickLength;
                sim.ApplyStatus(target, status, a.Actor);
            }
        }

        public override void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null) return;
            foreach (var spec in a.Definition.TargetStatusesOnHit)
            {
                if (spec == null) continue;
                var s = target.GetStatus(spec.Id);
                if (s != null && s.SourceId == a.Actor.Id) sim.RemoveStatus(target, spec.Id);
            }
        }
    }
}
