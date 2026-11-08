using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // Sharpshooter abilities of round AF: Crippling Pursuit, Mental Weapon. Dreadful Fixation is status_spell (a root).

    /// <summary>
    /// Crippling Pursuit: a dash to the target point, at most Range away (a dash: no dashing while CRIPPLED, rooted or in the
    /// air), loading the user's weapon as it lands: its SelfStatuses (a damage penalty, ArmsHits) make every hit the user lands
    /// while they last put this ability's TargetStatusesOnHit (CRIPPLED) on the target.
    /// </summary>
    public class CripplingPursuitBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser; }

        public override bool MovesUser { get { return true; } }
        public override bool Dashes { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            var def = tip.Def;
            tip.Add("Dash", "up to " + AbilityTip.Metres(tip.V(ActionStat.Range)));
            foreach (var s in def.SelfStatuses)
            {
                if (s == null) continue;
                float dealt = s.Total(StatId.DamageDealt, true);
                if (dealt < 0.999f) tip.Add("Your damage", string.Format("-{0:0}% for {1}", (1f - dealt) * 100f, AbilityTip.Secs(s.Duration)));
            }
            foreach (var s in def.TargetStatusesOnHit)
                if (s != null) tip.Add("Your hits", (s.Look != null && !string.IsNullOrEmpty(s.Look.Word) ? s.Look.Word : s.Id.ToUpperInvariant()) + " for " + AbilityTip.Secs(s.Duration));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            if (actor.IsRooted || actor.HasFlag(StatusFlags.NoDash)) return;
            Vector3 d = a.TargetPos - actor.Position; d.y = 0f;
            float reach = a.Stat(ActionStat.Range);
            if (d.magnitude > reach) d = d.normalized * reach;
            sim.SlideUnitTo(actor, sim.Paths.ClampDash(actor.Position, actor.Position + d), BattleSimulation.DashDuration);
        }
    }

    /// <summary>
    /// Mental Weapon: a shot into an ally's mind (no hit roll), giving them its TargetStatusesOnHit (MENTAL WEAPON) for their
    /// duration. The first time while it lasts that the ally deals damage with a single-target ability, the stored bullet
    /// fires out of them fire_delay later: this ability's hit (the Sharpshooter's, sure of range and cover) on that target.
    /// </summary>
    public class MentalWeaponBehavior : ActionBehavior
    {
        public const string FxKind = "mental_bullet";

        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("fire_delay", 0.5f); } }

        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx => OnDamage(ctx.Sim, ctx.Other, ctx.Subject, ctx.Action, ctx.Amount));
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            DescribeHit(tip, "Bullet", "Bullet stun", true);
            tip.Add("Fires", "when the ally next deals single-target damage, " + AbilityTip.Secs(Param(tip.Def, "fire_delay")) + " later");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var ally = sim.GetUnit(a.TargetUnitId);
            if (ally == null || !sim.InReach(a.Actor, a.Definition, ally)) return;
            foreach (var spec in a.Definition.TargetStatusesOnHit)
                if (spec != null && spec.AppliesToUnit(a.Actor, ally))
                    effects.Add(new ApplyStatusEffect { Source = a.Actor, TargetId = ally.Id, Status = spec.Create(sim.Now, a.Actor.Id, a.Definition, a.Actor.DurationScale(a.Definition)) });
        }

        /// <summary>Damage by an ability on one unit: its Targeting picks one unit (enemy or any) and it has no area.</summary>
        public static bool SingleTarget(ActionDefinition def)
        {
            return def != null && (def.Targeting == TargetType.Enemy || def.Targeting == TargetType.AnyUnit) && def.Shape == AreaShape.None;
        }

        private void OnDamage(BattleSimulation sim, Unit dealer, Unit victim, ActionDefinition by, float amount)
        {
            if (dealer == null || victim == null || !victim.IsAlive || amount <= 0f || dealer == victim || victim.Team == dealer.Team) return;
            var st = dealer.GetStatus(StatusIds.MentalWeapon);
            if (st == null || st.LinkedAction == null || by == st.LinkedAction || !SingleTarget(by)) return;
            var def = st.LinkedAction;
            var shooter = sim.GetUnit(st.SourceId);
            sim.RemoveStatus(dealer, StatusIds.MentalWeapon);   // one bullet
            if (shooter == null || !shooter.IsAlive) return;
            sim.ScheduleStrike(new AttackEffect
            {
                Source = shooter, TargetId = victim.Id, Definition = def, DamageMultiplier = shooter.DamageDealtMult(),
                IgnoreRange = true, IgnoreCover = true, Tag = FxKind,
            }, Param(def, "fire_delay"), dealer, FxKind);
        }
    }
}
