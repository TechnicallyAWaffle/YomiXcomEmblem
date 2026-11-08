using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Helpers for AI styles that pick abilities by what they do (their behavior, or their shape) rather than by role, so a
    /// style plays any kit built the same way: a new enemy with a "beam" ability flies and fires it like the Monarch does.
    /// </summary>
    public abstract class KitAI : ProfileAI
    {
        /// <summary>The unit's first ability using behavior 'behaviorId' (always-on ones aside), or null.</summary>
        protected static ActionDefinition ByBehavior(Unit unit, string behaviorId, System.Predicate<ActionDefinition> also = null)
        {
            foreach (var a in unit.Actions)
                if (a != null && !a.InnatePassive && a.BehaviorId == behaviorId && (also == null || also(a))) return a;
            return null;
        }

        /// <summary>A circle around the user (Targeting Self, Shape Circle) that hurts: the biggest, or the one matching 'also'.</summary>
        protected static ActionDefinition SelfBlast(Unit unit, System.Predicate<ActionDefinition> also = null)
        {
            ActionDefinition best = null;
            foreach (var a in unit.Actions)
            {
                if (a == null || a.InnatePassive || a.Targeting != TargetType.Self || a.Shape != AreaShape.Circle || a.Damage <= 0f) continue;
                if (also != null && !also(a)) continue;
                if (best == null || unit.ActionStat(a, ActionStat.AreaRadius) > unit.ActionStat(best, ActionStat.AreaRadius)) best = a;
            }
            return best;
        }

        /// <summary>How many of the unit's enemies stand within 'radius' of 'p' (bodies count).</summary>
        protected static int EnemiesWithin(BattleSimulation sim, Unit unit, Vector3 p, float radius)
        {
            int n = 0;
            foreach (var e in sim.EnemiesOf(unit)) if (Flat(e.Position, p) - e.BodyRadius <= radius) n++;
            return n;
        }

        /// <summary>Walk 'distance' straight away from 'from' (to the nearest walkable point that way).</summary>
        protected static UnitCommand StepAway(BattleSimulation sim, Unit unit, Vector3 from, float distance)
        {
            Vector3 d = unit.Position - from; d.y = 0f;
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.back;
            return UnitCommand.MoveTo(unit.Id, sim.WalkablePointNear(unit.Position + d * distance));
        }

        /// <summary>Walk until 'target' is about 'distance' away.</summary>
        protected static UnitCommand Approach(BattleSimulation sim, Unit unit, Unit target, float distance)
        {
            return UnitCommand.MoveTo(unit.Id, sim.ApproachSpot(unit, target, distance));
        }
    }

    /// <summary>Wraith: mindless. It goes for the nearest enemy and strikes it, again and again: no guarding, no resting.</summary>
    public class WraithAI : KitAI
    {
        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var target = sim.NearestEnemy(unit);
            var strike = MeleeStrike(unit) ?? Find(unit, AbilityRole.Strike);
            if (strike == null || !Ready(sim, unit, strike)) return Approach(sim, unit, target, 1f);
            return UnitCommand.UseActionWhenInRange(unit.Id, strike, target.Id);
        }
    }

    /// <summary>
    /// Monarch: keeps its distance throwing its ranged strike (BasicEnemyAI's ranged play), but closes in to fire its beam
    /// (Incineration) whenever that is ready, and calls its summons (Horde of Memories) when enemies are about. Once it is up
    /// in the air for good (Ascension) it can't move: it beams, summons or throws at whatever it can reach.
    /// </summary>
    public class MonarchAI : KitAI
    {
        public MonarchAI() { KeepDistance = 10f; }

        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            var target = sim.NearestEnemy(unit);
            if (target == null || unit.Phase == UnitPhase.Charging) return base.Decide(sim, unit);
            var horde = ByBehavior(unit, "summon");
            var beam = ByBehavior(unit, "beam");
            bool aloft = unit.Hovering;

            if (!aloft)
            {
                if (Basic(sim, unit)) return base.Decide(sim, unit);
                var r = React(sim, unit) ?? Rest(sim, unit);
                if (r != null) return r;
            }
            if (Ready(sim, unit, horde) && Flat(unit.Position, target.Position) <= 25f) return UnitCommand.UseAction(unit.Id, horde, unit.Id);
            if (Ready(sim, unit, beam))
            {
                if (Reach(sim, unit, beam, target)) return UnitCommand.UseAction(unit.Id, beam, target.Id);
                if (!aloft) return Approach(sim, unit, target, unit.ActionStat(beam, ActionStat.Range) * 0.8f);
            }
            if (aloft)
            {
                ActionDefinition shot; Unit at;
                if (BestShot(sim, unit, unit.Position, out shot, out at) > 0f) return UnitCommand.UseAction(unit.Id, shot, at.Id);
                return UnitCommand.Idle(unit.Id, 0.3f);
            }
            return DecideRanged(sim, unit);
        }
    }

    /// <summary>
    /// Zola: walks at the nearest enemy to get it inside its stomp, and unleashes its biggest blast (Mental Overload) as soon
    /// as anyone is inside that and it is ready. Its strain aura (Wasteful Thought) needs no orders.
    /// </summary>
    public class ZolaAI : KitAI
    {
        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var r = React(sim, unit) ?? Rest(sim, unit);
            if (r != null) return r;
            var target = sim.NearestEnemy(unit);
            var overload = SelfBlast(unit);
            var stomp = SelfBlast(unit, a => a != overload);
            if (Ready(sim, unit, overload) && EnemiesWithin(sim, unit, unit.Position, unit.ActionStat(overload, ActionStat.AreaRadius)) > 0) return UnitCommand.UseAction(unit.Id, overload, unit.Id);
            float stompRadius = stomp != null ? unit.ActionStat(stomp, ActionStat.AreaRadius) : 2f;
            if (Ready(sim, unit, stomp) && EnemiesWithin(sim, unit, unit.Position, stompRadius) > 0) return UnitCommand.UseAction(unit.Id, stomp, unit.Id);
            if (Flat(unit.Position, target.Position) > stompRadius * 0.6f) return Approach(sim, unit, target, stompRadius * 0.5f);
            return UnitCommand.Idle(unit.Id, 0.25f);   // in reach, stomp still on cooldown
        }
    }

    /// <summary>
    /// Primavaeran: keeps its distance casting its zone (Abstract Decay) on whoever isn't already in one. When it is far
    /// enough from every enemy and its Tear Apart Reality is ready, it casts that over the spot that takes in the most of
    /// them. Pressed close, it teleports away (Distortion, leaving its rift on them) or, with that down, repels them.
    /// </summary>
    public class PrimavaeranAI : KitAI
    {
        /// <summary>Closer than this is too close: it gets away or pushes them off.</summary>
        public float TooClose = 5f;
        /// <summary>It only tears reality open when every enemy is at least this far away.</summary>
        public float SafeToTear = 10f;

        public PrimavaeranAI() { KeepDistance = 9f; }

        public override UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            if (Basic(sim, unit)) return base.Decide(sim, unit);
            var r = React(sim, unit) ?? Rest(sim, unit);
            if (r != null) return r;
            var nearest = sim.NearestEnemy(unit);
            float d = Flat(unit.Position, nearest.Position);

            if (d < TooClose)
            {
                var escape = ByBehavior(unit, "distortion");
                Vector3 to;
                if (!unit.IsRooted && Ready(sim, unit, escape) && SafeJump(sim, unit, unit.ActionStat(escape, ActionStat.Range), out to)) return UnitCommand.UseActionAt(unit.Id, escape, to);
                var repel = SelfBlast(unit, a => a.Displace == DisplaceMode.AwayFromSource);
                if (Ready(sim, unit, repel)) return UnitCommand.UseAction(unit.Id, repel, unit.Id);
            }

            var tear = ByBehavior(unit, "reality_tear");
            Vector3 center;
            if (d >= SafeToTear && Ready(sim, unit, tear) && BestCircle(sim, unit, tear, out center)) return UnitCommand.UseActionAt(unit.Id, tear, center);

            var zone = ByBehavior(unit, "aura", a => a.AuraAtTarget);
            if (Ready(sim, unit, zone) && BestCircle(sim, unit, zone, out center, true)) return UnitCommand.UseActionAt(unit.Id, zone, center);

            if (d < KeepDistance && !unit.IsRooted) return StepAway(sim, unit, nearest.Position, KeepDistance - d + 1f);
            if (zone != null && d > unit.ActionStat(zone, ActionStat.Range)) return Approach(sim, unit, nearest, unit.ActionStat(zone, ActionStat.Range) * 0.85f);
            return UnitCommand.Idle(unit.Id, 0.3f);
        }

        /// <summary>
        /// The point in 'def's Range whose AreaRadius takes in the most enemies: each enemy's spot, and the middle of them all,
        /// are the candidates. freshOnly: enemies already in one of this unit's zones (Decayed) don't count.
        /// </summary>
        private static bool BestCircle(BattleSimulation sim, Unit unit, ActionDefinition def, out Vector3 center, bool freshOnly = false)
        {
            center = unit.Position;
            float range = unit.ActionStat(def, ActionStat.Range), radius = unit.ActionStat(def, ActionStat.AreaRadius);
            var candidates = new List<Vector3>();
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var e in sim.EnemiesOf(unit)) { candidates.Add(e.Position); sum += e.Position; n++; }
            if (n == 0) return false;
            candidates.Add(sum / n);
            int best = 0;
            foreach (var c in candidates)
            {
                if (Flat(unit.Position, c) > range) continue;
                int count = 0;
                foreach (var e in sim.EnemiesOf(unit))
                    if (Flat(e.Position, c) - e.BodyRadius <= radius && (!freshOnly || !e.HasStatus(StatusIds.Decayed))) count++;
                if (count > best) { best = count; center = c; }
            }
            return best > 0;
        }
    }
}
