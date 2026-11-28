using System.Collections.Generic;

namespace TacticsGame.Core
{
    /// <summary>
    /// CONVERGE REALITIES (converge_realities): the Omen's one ability. A long windup (UNSTOPPABLE throughout, from the
    /// definition's SelfStatusesDuringWindup; an Unravel still breaks it), then Temperance arrives 'hasten_seconds' sooner
    /// (BattleSimulation.HastenTemperance). It can't be started once Temperance is here, or in a battle it never comes to.
    /// </summary>
    public class ConvergeRealitiesBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("hasten_seconds", 5f); } }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = !sim.TemperanceComing ? "Temperance isn't coming" : sim.TemperanceArrived ? "Temperance is already here" : null;
            return reason == null;
        }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Then", "TEMPERANCE arrives " + AbilityTip.Secs(Param(tip.Def, "hasten_seconds")) + " sooner");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            sim.HastenTemperance(a.Actor, Param(a.Definition, "hasten_seconds"));
        }
    }

    /// <summary>
    /// A map objective's AI ("objective": the Omen): it stands where it was placed and uses the first ability it can,
    /// again and again (on itself, or the nearest enemy in reach); with none it can use, it waits.
    /// </summary>
    public class ObjectiveAI : IUnitAI
    {
        public const string Style = "objective";

        public UnitCommand Decide(BattleSimulation sim, Unit unit)
        {
            var target = sim.NearestEnemy(unit);
            foreach (var def in unit.Actions)
            {
                string why;
                if (def == null || !sim.CanAfford(unit, def, out why)) continue;
                var cmd = def.Targeting == TargetType.Self || def.Targeting == TargetType.None ? UnitCommand.UseAction(unit.Id, def, unit.Id)
                        : def.Targeting == TargetType.Position && target != null ? UnitCommand.UseActionAt(unit.Id, def, target.Position)
                        : target != null && sim.CanUse(unit, def, target, out why) ? UnitCommand.UseAction(unit.Id, def, target.Id) : null;
                if (cmd == null || !ActionBehaviors.Get(def.BehaviorId).CanStart(sim, unit, cmd, out why)) continue;
                return cmd;
            }
            return UnitCommand.Idle(unit.Id, 0.5f);
        }
    }
}
