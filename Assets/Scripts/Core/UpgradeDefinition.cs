using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// A permanent improvement to a unit (the metagame's upgrades): stat modifiers for the unit, modifiers to its abilities'
    /// numbers, and abilities it gains. Given to a unit when it is made (Unit.FromDefinition); the definitions themselves are
    /// never changed, so upgrades stack, can be taken away, and are saved as a list of these.
    /// Fixed stats (max health and strain, strain recovery) are baked in at the start of the battle; the rest are read
    /// through Unit.Stat / Unit.ActionStat alongside statuses.
    /// </summary>
    [CreateAssetMenu(menuName = "TacticsGame/Upgrade Definition")]
    public class UpgradeDefinition : ScriptableObject
    {
        public string Id = "upgrade";
        public string DisplayName = "Upgrade";
        [TextArea(2, 5)] public string Description = "";
        public List<Modifier> UnitModifiers = new List<Modifier>();
        public List<ActionModifier> ActionModifiers = new List<ActionModifier>();
        [Tooltip("Abilities the unit gains (added after its own).")]
        public List<ActionDefinition> GrantsActions = new List<ActionDefinition>();
    }
}
