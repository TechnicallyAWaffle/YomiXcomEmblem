using System.Collections.Generic;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>Stand-in that lets the full loop run with no UI: players play like the placeholder enemy AI.</summary>
    public class AutoPlayerInput : PlayerInputProvider
    {
        private readonly IUnitAI ai = new AIRouter();

        public override void RequestDecisions(BattleSimulation sim, IReadOnlyList<DecisionRequest> requests, System.Action onComplete)
        {
            foreach (var r in requests)
            {
                var unit = sim.GetUnit(r.UnitId);
                if (unit == null) continue;
                if (r.Kind == DecisionKind.DodgeDash)
                {
                    var attacker = sim.GetUnit(r.AttackerId);
                    sim.SubmitDodgeDash(unit.Id, attacker != null ? sim.BestDashDirection(unit, attacker) : 0);
                }
                else sim.SubmitCommand(ai.Decide(sim, unit));
            }
            onComplete();
        }
    }
}