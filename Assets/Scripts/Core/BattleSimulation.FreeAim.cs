using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// BattleSimulation: free aim. A single-target ability (CanFreeAim) can be ordered toward a point instead of at a unit
    /// (UnitCommand.UseActionToward, UnitCommand.FreeAim): it winds up at once, with no target, facing that way. As it lands,
    /// the closest unit it could be used on that stands within its range and inside a FreeAimCone (90 degree) cone toward the
    /// aimed point becomes its target (FreeAimTarget), and it resolves on that unit as usual; with nobody there it does
    /// nothing. With no target during the windup, nothing that reads one then applies: the windup's statuses and step-in on
    /// the target, and the Etchings that look at the target while it winds up (Senses of Closure, Perception for Violence).
    /// </summary>
    public partial class BattleSimulation
    {
        /// <summary>Degrees: the cone a freely aimed ability looks for its target in, centred on where it was aimed.</summary>
        public const float FreeAimCone = 90f;

        /// <summary>Can 'def' be aimed freely: aimed at one unit (an enemy, an ally, or either), not charged, and with no
        /// second pick after its target (a destination, a second unit).</summary>
        public static bool CanFreeAim(ActionDefinition def)
        {
            if (def == null || def.IsCharge || def.InnatePassive || def.DestinationRange > 0f) return false;
            if (def.Targeting != TargetType.Enemy && def.Targeting != TargetType.Ally && def.Targeting != TargetType.AnyUnit) return false;
            return !ActionBehaviors.Get(def.BehaviorId).PicksSecondUnit(def);
        }

        /// <summary>
        /// The unit a freely aimed 'def' by 'actor' toward 'aim' would land on now: the closest one it can be used on, within
        /// its range, inside the FreeAimCone toward 'aim' (and in sight, for a ranged one that needs it). Null: nobody.
        /// </summary>
        public Unit FreeAimTarget(Unit actor, ActionDefinition def, Vector3 aim)
        {
            Vector3 dir = aim - actor.Position; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return null;
            dir.Normalize();
            float range = actor.RangeOf(def), cos = Mathf.Cos(FreeAimCone * 0.5f * Mathf.Deg2Rad);
            Unit best = null; float bestDist = float.MaxValue;
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || IsVoided(u)) continue;
                bool self = u == actor, ally = u.Team == actor.Team;
                if (def.Targeting == TargetType.Enemy && ally) continue;
                if (def.Targeting == TargetType.Ally && !ally) continue;
                if (self) continue;   // a cone in front of the user never holds the user
                if (!IsTargetableBy(u, actor)) continue;
                Vector3 to = u.Position - actor.Position;
                float dist = to.magnitude;
                if (dist > range) continue;
                Vector3 flat = to; flat.y = 0f;
                if (flat.sqrMagnitude > 1e-6f && Vector3.Dot(flat.normalized, dir) < cos) continue;
                if (def.IsRanged && !HasSightOf(actor, def, u)) continue;
                if (dist < bestDist) { bestDist = dist; best = u; }
            }
            return best;
        }

        /// <summary>A freely aimed action lands: its target is whoever is in its cone now (FreeAimTarget). False: nobody is.</summary>
        private bool ResolveFreeAim(ActionInstance a)
        {
            var t = FreeAimTarget(a.Actor, a.Definition, a.TargetPos);
            if (t == null) return false;
            a.TargetUnitId = t.Id;
            a.TargetPos = t.Position;
            return true;
        }
    }
}
