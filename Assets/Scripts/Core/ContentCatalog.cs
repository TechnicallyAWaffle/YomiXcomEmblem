using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>Every class and upgrade a save can name, so ids resolve to assets (Resources/ContentCatalog, made by Build Scene).</summary>
    [CreateAssetMenu(menuName = "TacticsGame/Content Catalog")]
    public class ContentCatalog : ScriptableObject
    {
        public const string ResourceName = "ContentCatalog";

        public List<UnitDefinition> Units = new List<UnitDefinition>();
        public List<UpgradeDefinition> Upgrades = new List<UpgradeDefinition>();

        /// <summary>A unit definition's id: its Id, or its asset name.</summary>
        public static string IdOf(UnitDefinition u) { return u == null ? null : !string.IsNullOrEmpty(u.Id) ? u.Id : u.name; }

        public UnitDefinition Unit(string id) { return Units.Find(u => u != null && IdOf(u) == id); }
        public UpgradeDefinition Upgrade(string id) { return Upgrades.Find(u => u != null && u.Id == id); }
    }
}
