using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // The Exemplar's abilities that need rules beyond data. The rest are data on existing behaviors: Intrepid Advance and
    // Light the Pyre (phantom_strike: dash to the target and strike), Spark the Flame (basic_attack, whose landing opens Light
    // the Pyre through ActionDefinition.SelfStatusesOnHit and RequiredStatus).

    /// <summary>
    /// Piercing Retreat (retreat_strike): as the windup starts the user dashes 'retreat' metres straight away from its target
    /// (walls stop it short), then the strike lands as a line from where it ended up toward the target: Range long and
    /// LineWidth wide, hitting every enemy in it (it pierces). Any target in range can be picked; the line is aimed when it lands.
    /// </summary>
    public class RetreatStrikeBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Escape; }

        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("retreat", 5f);           // metres it dashes back
                yield return new BehaviorParam("retreat_seconds", 0.25f); // how long the dash takes
            }
        }

        public override bool MovesUser { get { return true; } }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Retreat", AbilityTip.Metres(Param(tip.Def, "retreat")) + " back as it winds up");
            tip.Add("Line", AbilityTip.Metres(tip.Def.LineWidth) + " wide, pierces");
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            var actor = a.Actor;
            if (target == null || actor.IsRooted) return;
            Vector3 away = actor.Position - target.Position; away.y = 0f;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.back;
            Vector3 to = sim.Paths.ClampDash(actor.Position, actor.Position + away * Param(a.Definition, "retreat"));
            sim.SlideUnitTo(actor, sim.Terrain.OnGround(to), Mathf.Max(BattleSimulation.TickLength, Param(a.Definition, "retreat_seconds")));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target != null && target.IsAlive) a.TargetPos = target.Position;   // aimed now, from where the retreat ended
            foreach (var u in sim.UnitsInArea(a.Actor, a.Definition, a.TargetPos))
                effects.Add(new AttackEffect { Source = a.Actor, TargetId = u.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, IgnoreRange = true, Tag = a.Tag });
        }
    }

    /// <summary>
    /// Split Apart The Sky (sky_smite): as the windup starts the user blinks to a random spot up to 'blink_range' metres away
    /// (and at least 'min_distance' from its target) and rises 'height' metres into the air; the smite's circle (AreaRadius)
    /// is set on the target where it stands then. When it lands every enemy in the circle is hit, and the user comes back
    /// down over the recovery. Struck while up there it falls (a hover), and the smite with it.
    /// </summary>
    public class SkySmiteBehavior : FlyingBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.AreaBlast; }

        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("blink_range", 8f);
                yield return new BehaviorParam("height", 2f);
                yield return new BehaviorParam("min_distance", 4f);
            }
        }

        public override bool MovesUser { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return true; }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Blink", string.Format("{0} away, {1} up, {2}+ from the target", AbilityTip.Metres(Param(tip.Def, "blink_range")),
                AbilityTip.Metres(Param(tip.Def, "height")), AbilityTip.Metres(Param(tip.Def, "min_distance"))));
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            var actor = a.Actor;
            if (target != null) a.TargetPos = target.Position;   // the smite is set where the target stands now
            if (actor.IsRooted || actor.IsLifted) return;
            var spot = BlinkSpot(sim, actor, a.TargetPos, Param(a.Definition, "blink_range"), Param(a.Definition, "min_distance"));
            sim.TeleportUnit(actor, spot, actor);
            TakeOff(sim, a, actor.Position + Vector3.up * Param(a.Definition, "height"));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            foreach (var u in sim.UnitsInArea(a.Actor, a.Definition, a.TargetPos, true))   // around the target, not the user
                effects.Add(new AttackEffect { Source = a.Actor, TargetId = u.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, IgnoreRange = true, IgnoreCover = true, Tag = a.Tag });
            Land(sim, a);
        }

        /// <summary>A random walkable spot within 'range' of the user and at least 'minDistance' from 'target' (seeded rolls).</summary>
        public static Vector3 BlinkSpot(BattleSimulation sim, Unit actor, Vector3 target, float range, float minDistance)
        {
            Vector3 best = actor.Position; float bestGap = -1f;
            for (int i = 0; i < 20; i++)
            {
                float ang = sim.RandomValue() * Mathf.PI * 2f, dist = Mathf.Lerp(range * 0.35f, range, sim.RandomValue());
                Vector3 p = sim.WalkablePointNear(actor.Position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist, actor.Position);
                Vector3 d = p - target; d.y = 0f;
                float gap = d.magnitude;
                if (gap >= minDistance) return p;
                if (gap > bestGap) { bestGap = gap; best = p; }
            }
            return best;
        }
    }
}
