using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// A chain thrown onto an enemy (You.): no hit roll and no damage on its own; it holds the target to its user
    /// (BattleSimulation.AttachTether) for 'duration' (3s) with 'max_length' (5m) of slack. Once they are further apart than
    /// that for 'snap_delay' (0.1s), however it happened, the chain snaps: the target is dragged back to 'yank_to' (0.5) of
    /// the length, and takes the ability's damage and stun (a hit that can't miss or be dodged). 'snaps' (1) snaps, then
    /// the chain is spent.
    /// </summary>
    public class ChainTetherBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("max_length", 5f);
                yield return new BehaviorParam("snap_delay", 0.1f);
                yield return new BehaviorParam("duration", 3f);
                yield return new BehaviorParam("snaps", 1f);
                yield return new BehaviorParam("yank_to", 0.5f);
            }
        }


        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Chain", string.Format("{0} long, for {1}", AbilityTip.Metres(Param(tip.Def, "max_length")), AbilityTip.Secs(Param(tip.Def, "duration") * DurationScale(tip))));
            tip.Add("Snaps", string.Format("past its length for {0}: yanked back", AbilityTip.Secs(Param(tip.Def, "snap_delay"))));
            DescribeHit(tip, "Snap damage", "Snap stun", false);
        }

        private static float DurationScale(AbilityTip tip) { return tip.Who != null ? tip.Who.DurationScale(tip.Def) : 1f; }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || !sim.IsTargetableBy(target, actor)) return;
            Vector3 d = target.Position - actor.Position; d.y = 0f;
            if (d.magnitude > a.Stat(ActionStat.Range) * BattleSimulation.RangeTolerance)
            {
                sim.EmitMiss(actor, target, "out_of_range", a.Definition);
                return;
            }
            sim.AttachTether(target, actor, a.Definition, Param(a.Definition, "max_length"), Param(a.Definition, "snap_delay"),
                Param(a.Definition, "duration") * actor.DurationScale(a.Definition), Mathf.RoundToInt(Param(a.Definition, "snaps")), Param(a.Definition, "yank_to"));
        }
    }

    /// <summary>
    /// Savior: dash to an ally (Range), arriving at its side as the windup ends, and put the ability's TargetStatusesOnHit
    /// on that ally as the dash starts (its guard: +50% Block). Then a stomp: a hit on every enemy within AreaRadius of where
    /// the user stands (no cover, no range check), knocking each one DisplaceDistance away (DisplaceMode AwayFromSource).
    /// </summary>
    public class SaviorBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser; }

        public override bool MovesUser { get { return true; } }   // not while airborne
        public override bool Dashes { get { return true; } }      // a CRIPPLED unit can't

        public override bool RollsToHit(ActionDefinition def) { return true; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Dash", "to the ally");
            float block = 0f;
            if (tip.Def.TargetStatusesOnHit != null)
                foreach (var s in tip.Def.TargetStatusesOnHit)
                    if (s != null && s.Modifiers != null) foreach (var m in s.Modifiers) if (m.Stat == StatId.Block && m.Op == ModOp.Add) block += m.Value;
            if (block > 0f) tip.Add("Ally", string.Format("+{0:0}% Block", block * 100f));
            base.Describe(tip);
            if (tip.V(ActionStat.AreaRadius) > 0f) tip.Add("Stomp", AbilityTip.Metres(tip.V(ActionStat.AreaRadius)) + " around you");
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            var actor = a.Actor;
            var ally = sim.GetUnit(a.TargetUnitId);
            if (ally == null || !ally.IsAlive || ally == actor) return;
            if (!actor.IsRooted)
            {
                // Beside the ally, on the side it came from; there as the stomp lands.
                Vector3 d = actor.Position - ally.Position; d.y = 0f;
                d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.back;
                Vector3 spot = ally.Position + d * (actor.BodyRadius + ally.BodyRadius + BattleSimulation.StandingGap);
                float seconds = Mathf.Max(BattleSimulation.TickLength, (a.ResolveAt - sim.Now) * 0.9f);
                sim.SlideUnitTo(actor, spot, seconds);
            }
            // The ally's guard (TargetStatusesOnHit: Savior's Guard, +50% Block), as the Tank sets off.
            var def = a.Definition;
            if (def.TargetStatusesOnHit != null)
                foreach (var spec in def.TargetStatusesOnHit)
                    if (spec != null && spec.AppliesToUnit(actor, ally)) sim.ApplyStatus(ally, spec.Create(sim.Now, actor.Id, def.LinkedAction, actor.DurationScale(def)), actor);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            foreach (var target in sim.UnitsInArea(actor, a.Definition, actor.Position))
                effects.Add(new AttackEffect { Source = actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true, IgnoreCover = true });
        }
    }
}
