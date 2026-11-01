using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// The one place scene and simulation meet. Drop on an empty "BattleSetup" object, then list
    /// each placed UnitView with its UnitDefinition and team. It builds the Core.BattleState for the
    /// simulation (using each view's current position, plus every ElevationBlock in the scene as terrain) and the
    /// Id -> UnitView registry for playback.
    /// </summary>
    public class BattleContext : MonoBehaviour
    {
        [System.Serializable]
        public struct Placement
        {
            public UnitView View;
            public UnitDefinition Definition;
            public Team Team;
            [Tooltip("Permanent upgrades this unit fights with; its definition is left as it is.")]
            public List<UpgradeDefinition> Upgrades;
            /// <summary>The class abilities it brings (set by the pre-battle screen); null = all of its definition's.</summary>
            [System.NonSerialized] public List<ActionDefinition> Loadout;
            /// <summary>Share of max health it starts without (a squad member's wounds from earlier battles); 0 = unhurt.</summary>
            [System.NonSerialized] public float Wounds;
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
                var unit = Unit.FromDefinition(p.Definition, id, p.Team, p.View.transform.position, p.Loadout, p.Upgrades);
                Campaign.ApplyWounds(unit, p.Wounds);
                state.Units.Add(unit);
                p.View.UnitId = id;
                UnitLooks.Apply(p.View, p.Definition);
                Registry[id] = p.View;
            }
            foreach (var block in FindObjectsByType<ElevationBlock>(FindObjectsSortMode.InstanceID))
                if (block.isActiveAndEnabled) state.Terrain.Blocks.Add(block.ToBlock());
            return state;
        }
    }
}
