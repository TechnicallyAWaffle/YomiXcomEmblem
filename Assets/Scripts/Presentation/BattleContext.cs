using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// The one place scene and simulation meet. Drop on an empty "BattleSetup" object, then list
    /// each placed UnitView with its UnitDefinition and team. It builds the Core.BattleState for the
    /// simulation (using each view's current position) and the Id -> UnitView registry for playback.
    /// </summary>
    public class BattleContext : MonoBehaviour
    {
        [System.Serializable]
        public struct Placement
        {
            public UnitView View;
            public UnitDefinition Definition;
            public Team Team;
        }

        public List<Placement> Placements = new List<Placement>();
        public Dictionary<string, UnitView> Registry { get; private set; }

        public BattleState BuildBattleState()
        {
            var state = new BattleState();
            Registry = new Dictionary<string, UnitView>();

            for (int i = 0; i < Placements.Count; i++)
            {
                var p = Placements[i];
                string id = p.Definition.name + "_" + i;
                state.Units.Add(Unit.FromDefinition(p.Definition, id, p.Team, p.View.transform.position));
                p.View.UnitId = id;
                Registry[id] = p.View;
            }
            return state;
        }
    }
}
