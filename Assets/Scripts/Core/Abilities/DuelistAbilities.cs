using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // The Duelist: opens weakpoints with its blade and darts in and out. Finite Blade and Incoherency are plain
    // basic_attacks with WeakpointsOnTarget.

    /// <summary>
    /// Gouge (gouge): dash Range along the aimed line, hitting every enemy on it (the ability's line: LineWidth). Each hit
    /// breaks BreaksWeakpoints of its weakpoints, from any side.
    /// </summary>
    public class GougeBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser; }
        public override bool MovesUser { get { return true; } }
        public override bool Dashes { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return true; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        protected override void DescribeMovement(AbilityTip tip) { tip.Add("Dash", AbilityTip.Metres(tip.V(ActionStat.Range)) + ", through enemies"); }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            if (actor.IsRooted) return;
            Vector3 dir = a.TargetPos - actor.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 end = sim.PhaseLanding(actor.Position, actor.Position + dir * a.Stat(ActionStat.Range));   // through walls and cover
            foreach (var u in sim.UnitsInArea(actor, def, actor.Position + dir * a.Stat(ActionStat.Range)))
                effects.Add(new AttackEffect { Source = actor, TargetId = u.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true, IgnoreCover = true });
            sim.SlideUnitTo(actor, end, BattleSimulation.DashDuration, end);
        }
    }

    /// <summary>
    /// Sever (recursive_severance): a slash that links to itself (ActionDefinition.LinkedAbilities). Each use in an unbroken
    /// linked run (any target; walking and dashing don't break it, any other ability does) deals 'amp' (0.25: +25%) more
    /// damage and costs that much more strain than the first, added up per use (Unit.LinkDepth).
    /// </summary>
    public class RecursiveSeveranceBehavior : BasicAttackBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("amp", 0.25f); } }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Strike | AbilityRole.Chain; }

        /// <summary>How deep in a linked run the next use would be: 0 for a fresh one.</summary>
        public static int Depth(BattleSimulation sim, Unit actor, ActionDefinition def)
        {
            return sim != null && sim.LinkedOpenFor(actor, def) ? actor.LinkDepth + 1 : 0;
        }

        public override float StrainCost(BattleSimulation sim, Unit actor, ActionDefinition def, string targetId)
        {
            return actor.ActionStat(def, ActionStat.StrainOnSelf) * (1f + Param(def, "amp") * Depth(sim, actor, def));
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Linked to itself", string.Format("each use in a row +{0:0}% damage and strain cost", Param(tip.Def, "amp") * 100f));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            int first = effects.Count;
            base.OnResolve(sim, a, effects);
            float more = 1f + Param(a.Definition, "amp") * (a.IsFree ? 0 : a.LinkDepth);
            for (int i = first; i < effects.Count; i++)
            {
                var atk = effects[i] as AttackEffect;
                if (atk != null) atk.DamageMultiplier *= more;
            }
        }
    }

    /// <summary>
    /// Monostep (quick_dash): dash up to Range toward a point, at dodge speed. Its SelfStatuses (a burst of move speed) come
    /// with it. Abilities that dash and do more derive from it (Graze The Precipice).
    /// </summary>
    public class QuickDashBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Escape; }
        public override bool MovesUser { get { return true; } }
        public override bool Dashes { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        protected override void DescribeMovement(AbilityTip tip) { tip.Add("Dash", AbilityTip.Metres(tip.V(ActionStat.Range))); }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor;
            if (u.IsRooted) return;
            Vector3 d = a.TargetPos - u.Position; d.y = 0f;
            float reach = a.Stat(ActionStat.Range);
            if (d.magnitude > reach) d = d.normalized * reach;
            sim.PhaseDashTo(u, u.Position + d, BattleSimulation.DashDuration);   // through walls and cover
        }
    }

    /// <summary>
    /// Graze The Precipice (graze): a quick dash; every other ability's cooldown is over. Its WeakpointsOnSelf are the price.
    /// </summary>
    public class GrazeBehavior : QuickDashBehavior
    {
        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Cooldowns", "every other ability is ready again");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor;
            if (u.IsRooted) return;
            base.OnResolve(sim, a, effects);
            foreach (var id in new List<string>(u.CooldownReadyAt.Keys))
                if (id != a.Definition.Id && u.CooldownReadyAt[id] > sim.Now) u.CooldownReadyAt[id] = sim.Now;
        }
    }

    /// <summary>
    /// A Duet (duet): dash to an enemy within Range and strike it. If the strike breaks a weakpoint, the ability's cooldown
    /// is over at once.
    /// </summary>
    public class DuetBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser | AbilityRole.Strike; }
        public override bool MovesUser { get { return true; } }
        public override bool Dashes { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return true; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        protected override void DescribeMovement(AbilityTip tip) { tip.Add("Dash", "to the target, up to " + AbilityTip.Metres(tip.V(ActionStat.Range))); }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Weakpoint hit", "its cooldown resets");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || !sim.InReach(actor, def, target)) return;
            DashBeside(sim, actor, target);
            effects.Add(new AttackEffect
            {
                Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true,
                AfterHit = (s, hit) => { if (hit.WeakpointsBroken > 0) actor.CooldownReadyAt.Remove(def.Id); },
            });
        }

        /// <summary>Dash 'actor' up to 'target', stopping at arm's length on the side it comes from, straight through walls and
        /// cover (BattleSimulation.PhaseDashBeside).</summary>
        public static void DashBeside(BattleSimulation sim, Unit actor, Unit target)
        {
            if (actor.IsRooted) return;
            sim.PhaseDashBeside(actor, target, BattleSimulation.DashDuration);
        }
    }

    /// <summary>
    /// Unsigned Rebuttal (parry): as it lands, PARRYING for 'window' (0.25s): no damage, no stun. A hit taken in that time
    /// sheds 'strain_relief' (15) strain, ends the recovery at once, and opens a follow-up for 'followup_seconds' (1.5s):
    /// the next Unsigned Rebuttal in that time has no windup and ignores its cooldown. It is the Duelist's Block (in the
    /// unit's Block slot, UnitDefinition.Universal): no cooldown; Transcended, no windup but TranscendCooldown (4s).
    /// </summary>
    public class ParryBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("window", 0.25f);
                yield return new BehaviorParam("strain_relief", 15f);
                yield return new BehaviorParam("followup_seconds", 1.5f);
            }
        }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Guard; }
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool TakesBlockSlot { get { return true; } }   // the Duelist's Block

        // A Transcended parry's cooldown holds even when it parries: its Rebuttal (Stacks 1) doesn't lift it.
        public override bool IgnoresCooldown(BattleSimulation sim, Unit actor, ActionDefinition def, string targetId)
        {
            var r = actor.GetStatus(StatusIds.Rebuttal);
            return r != null && r.Stacks == 0;
        }
        public override float WindupSeconds(BattleSimulation sim, Unit actor, ActionDefinition def) { return actor.HasStatus(StatusIds.Rebuttal) ? 0f : base.WindupSeconds(sim, actor, def); }

        public override void Describe(AbilityTip tip)
        {
            var d = tip.Def;
            tip.Add("Parry", "no damage or stun for " + AbilityTip.Secs(Param(d, "window")));
            tip.Add("On a parry", string.Format("sheds {0:0} strain, no recovery, and a follow-up with no windup", Param(d, "strain_relief")));
        }

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx =>
            {
                var u = ctx.Subject;
                if (u == null || !u.IsAlive || ctx.Other == null || ctx.Other == u || !u.HasStatus(StatusIds.Parrying) || u.HasStatus(StatusIds.Rebuttal)) return;   // once per parry
                var def = u.Actions.Find(x => x != null && x.BehaviorId == "parry");
                if (def == null) return;
                sim.AddStrain(u, -Param(def, "strain_relief"));
                var parrying = u.GetStatus(StatusIds.Parrying);
                sim.ApplyStatus(u, new StatusEffect { Id = StatusIds.Rebuttal, SourceId = u.Id, ExpiresAt = sim.Now + Param(def, "followup_seconds"), Flags = StatusFlags.FollowupWindow,
                                                      Stacks = parrying != null ? parrying.Stacks : 0 }, u);   // 1: from a Transcended parry (its cooldown stands)
                if (u.Phase == UnitPhase.Recovery) sim.EndRecoveryNow(u);
                sim.EmitFx(u, ctx.Other, "parry", def, u.Position);
            });
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            if (a.Actor.HasStatus(StatusIds.Rebuttal)) sim.RemoveStatus(a.Actor, StatusIds.Rebuttal);   // spent by this one
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor;
            sim.ApplyStatus(u, new StatusEffect
            {
                Id = StatusIds.Parrying, SourceId = u.Id, ExpiresAt = sim.Now + Param(a.Definition, "window") + BattleSimulation.TickLength,
                Flags = StatusFlags.NegatesDamage | StatusFlags.StunImmune | StatusFlags.Counters,   // meets attacks before any dodge
                Stacks = a.Transcended ? 1 : 0,   // a Transcended parry: what it earns doesn't lift its cooldown
            }, u);
        }
    }

    /// <summary>
    /// Vindication (vindication): its SelfStatuses (HASTE, a moment of doubled action speed) as it lands, and, if the
    /// Duelist has a NEMESIS (Karranos Sigil) within Range, an instant blink to its far side: 'beyond' (0.4m) past its body,
    /// on the line from the Duelist through it, through walls and cover.
    /// </summary>
    public class VindicationBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("beyond", 0.4f); } }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser; }
        public override bool MovesUser { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return false; }

        /// <summary>The Duelist's NEMESIS within its blink range, or null.</summary>
        public static Unit NemesisInReach(BattleSimulation sim, Unit duelist, ActionDefinition def)
        {
            float reach = duelist.ActionStat(def, ActionStat.Range);
            foreach (var u in sim.State.Units)
            {
                if (!u.IsAlive || Passives.NemesisOf(u, duelist) == null) continue;
                Vector3 d = u.Position - duelist.Position; d.y = 0f;
                if (d.magnitude <= reach + u.BodyRadius) return u;
            }
            return null;
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("NEMESIS", "within " + AbilityTip.Metres(tip.V(ActionStat.Range)) + ": blink to its far side");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var u = a.Actor;
            var nemesis = NemesisInReach(sim, u, a.Definition);
            if (nemesis == null || u.IsRooted) return;
            Vector3 through = nemesis.Position - u.Position; through.y = 0f;
            through = through.sqrMagnitude > 1e-6f ? through.normalized : Vector3.forward;
            Vector3 from = u.Position;
            sim.TeleportUnit(u, nemesis.Position + through * (nemesis.BodyRadius + u.BodyRadius + Param(a.Definition, "beyond")), u);
            sim.EmitFx(u, nemesis, "vindication", a.Definition, u.Position, 0f, from);
        }
    }

    /// <summary>
    /// Quell The Uproar (quell): every enemy within AreaRadius around the Duelist gets the ability's TargetStatusesOnHit
    /// (QUELLED, a heavy slow) and is pushed DisplaceDistance straight away from it, except its NEMESIS (Karranos Sigil),
    /// which stays where it is. No roll: nothing dodges it.
    /// </summary>
    public class QuellBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Peel; }
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool MovesTargets(ActionDefinition def) { return true; }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Your NEMESIS", "is slowed but not pushed");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            foreach (var u in sim.UnitsInArea(actor, def, actor.Position))
            {
                foreach (var spec in def.TargetStatusesOnHit)
                    if (spec != null && spec.AppliesToUnit(actor, u)) effects.Add(new ApplyStatusEffect { Source = actor, TargetId = u.Id, Status = spec.Create(sim.Now, actor.Id, null, actor.DurationScale(def)) });
                if (Passives.NemesisOf(u, actor) == null) effects.Add(new DisplaceEffect { Source = actor, TargetId = u.Id, Definition = def });
            }
            sim.EmitFx(actor, null, "quell", def, actor.Position, a.Stat(ActionStat.AreaRadius));
        }
    }

    /// <summary>
    /// Summation (summation): dash to an enemy within Range, then put the ability's TargetStatusesOnHit (FATED) and
    /// WeakpointsOnTarget on it.
    /// </summary>
    public class SummationBehavior : ActionBehavior
    {
        public override bool MovesUser { get { return true; } }
        public override bool Dashes { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool WeakpointsOnHit(ActionDefinition def) { return false; }

        protected override void DescribeMovement(AbilityTip tip) { tip.Add("Dash", "to the target, up to " + AbilityTip.Metres(tip.V(ActionStat.Range))); }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || !sim.InReach(actor, def, target)) { a.TargetUnitId = null; return; }   // nothing there: no weakpoints either
            foreach (var spec in def.TargetStatusesOnHit)
                if (spec != null && spec.AppliesToUnit(actor, target)) effects.Add(new ApplyStatusEffect { Source = actor, TargetId = target.Id, Status = spec.Create(sim.Now, actor.Id, def.LinkedAction, actor.DurationScale(def)) });
            DuetBehavior.DashBeside(sim, actor, target);
        }
    }

    /// <summary>
    /// Opening Theorem (opening_theorem): dash to an enemy within Range (through walls and cover) and strike it, then bound
    /// into the air past it: 'bound_range' (3m) on along the way it came, peaking 'bound_height' (2m) up, over
    /// 'bound_seconds'. Its SelfStatuses open Conjecture (its FollowUps) for a moment.
    /// </summary>
    public class OpeningTheoremBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("bound_height", 2f);
                yield return new BehaviorParam("bound_range", 3f);
                yield return new BehaviorParam("bound_seconds", 0.35f);
            }
        }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser | AbilityRole.Opener; }
        public override bool MovesUser { get { return true; } }
        public override bool Dashes { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return true; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        protected override void DescribeMovement(AbilityTip tip)
        {
            tip.Add("Dash", "to the target, up to " + AbilityTip.Metres(tip.V(ActionStat.Range)));
            tip.Add("Then", string.Format("bound {0} past it, {1} up", AbilityTip.Metres(Param(tip.Def, "bound_range")), AbilityTip.Metres(Param(tip.Def, "bound_height"))));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || !sim.InReach(actor, def, target)) return;
            Vector3 dir = target.Position - actor.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 beside = actor.IsRooted ? actor.Position : sim.PhaseDashBeside(actor, target, BattleSimulation.DashDuration);
            effects.Add(new AttackEffect { Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true });
            float range = Param(def, "bound_range"), height = Param(def, "bound_height"), seconds = Mathf.Max(BattleSimulation.TickLength, Param(def, "bound_seconds"));
            if (range <= 0f) return;
            // Once the dash arrives: up and over, landing past the target.
            sim.After(actor, BattleSimulation.DashDuration, () =>
            {
                if (actor.IsRooted || actor.Phase == UnitPhase.Stunned) return;
                sim.PhaseDashTo(actor, beside + dir * range, seconds, height);
            });
        }
    }

    /// <summary>
    /// Conjecture (conjecture): dash Range along the aimed line, through enemies, walls and cover. Every enemy on the line
    /// gets the ability's TargetStatusesOnHit (FATED) and one slash for each weakpoint it has, 'slash_interval' (0.2s) apart,
    /// the first as the dash goes through. Each slash always connects and breaks a weakpoint (BreaksWeakpoints).
    /// </summary>
    public class ConjectureBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("slash_interval", 0.2f); } }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser; }
        public override bool MovesUser { get { return true; } }
        public override bool Dashes { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool HitsOnArrival(ActionDefinition def) { return false; }   // it times its own slashes, as it passes each enemy

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        protected override void DescribeMovement(AbilityTip tip) { tip.Add("Dash", AbilityTip.Metres(tip.V(ActionStat.Range)) + ", through enemies"); }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Slashes", string.Format("one per weakpoint on each enemy, {0} apart; each breaks one", AbilityTip.Secs(Param(tip.Def, "slash_interval"))));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            if (actor.IsRooted) return;
            Vector3 dir = a.TargetPos - actor.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            float range = a.Stat(ActionStat.Range), interval = Mathf.Max(BattleSimulation.TickLength, Param(def, "slash_interval"));
            Vector3 start = actor.Position;
            float mult = a.DamageMultiplier * actor.DamageDealtMult();
            foreach (var u in sim.UnitsInArea(actor, def, start + dir * range))
            {
                // Each enemy is reached as the dash passes it: FATED then, its first slash then, the rest after.
                Vector3 to = u.Position - start; to.y = 0f;
                float reached = BattleSimulation.DashDuration * Mathf.Clamp01(Vector3.Dot(to, dir) / Mathf.Max(0.01f, range));
                var target = u;
                sim.After(actor, reached, () =>
                {
                    if (!target.IsAlive) return;
                    foreach (var spec in def.TargetStatusesOnHit)
                        if (spec != null && spec.AppliesToUnit(actor, target)) sim.ApplyStatus(target, spec.Create(sim.Now, actor.Id, def.LinkedAction, actor.DurationScale(def)), actor);
                });
                int slashes = u.Weakpoints.Count;
                for (int i = 0; i < slashes; i++)
                    sim.ScheduleStrike(new AttackEffect
                    {
                        Source = actor, TargetId = u.Id, Definition = def, DamageMultiplier = mult, Tag = a.Tag,
                        IgnoreRange = true, IgnoreCover = true, Unerring = true, NoStatuses = true,
                    }, reached + interval * i);
            }
            sim.PhaseDashTo(actor, start + dir * range, BattleSimulation.DashDuration);
        }
    }
}
