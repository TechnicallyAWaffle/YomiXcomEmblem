using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Placeholder AI: use the first usable enemy-targeting action on the nearest enemy,
    /// otherwise close the distance. Replace with real behavior (flanking, cover, ability choice).
    /// </summary>
    public class BasicEnemyAI : IUnitAI
    {
        public UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            Unit target = sim.NearestEnemy(unit);
            if (target == null) return UnitCommand.Wait(unit.Id, 1f);

            ActionDefinition firstOffense = null;
            string why;
            foreach (var def in unit.Actions)
            {
                if (def.Targeting != TargetType.Enemy) continue;
                if (firstOffense == null) firstOffense = def;
                if (sim.CanUse(unit, def, target, out why)) return UnitCommand.UseAction(unit.Id, def, target.Id);
            }
            if (firstOffense == null) return UnitCommand.Wait(unit.Id, 1f);

            Vector3 toTarget = target.Position - unit.Position;
            float stopDistance = Mathf.Max(1f, firstOffense.Range * 0.8f);
            if (toTarget.magnitude <= stopDistance)
            {
                // In range but blocked or cooling down. Close in when it's line of sight, so two shooters behind cover can't stall forever.
                sim.CanUse(unit, firstOffense, target, out why);
                if (why != "no line of sight") return UnitCommand.Wait(unit.Id, 0.3f);
                return UnitCommand.MoveTo(unit.Id, target.Position - toTarget.normalized * Mathf.Max(1f, toTarget.magnitude * 0.5f));
            }
            return UnitCommand.MoveTo(unit.Id, sim.ApproachSpot(unit, target, stopDistance));
        }
    }
}
