using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// A mission's battle as an asset: the map to fight on and the encounter itself (enemies, objective, rewards; see
    /// EncounterSpec). BattleLauncher fights it with a roster; the pre-battle screen lists these to pick from.
    /// </summary>
    [CreateAssetMenu(menuName = "TacticsGame/Encounter")]
    public class EncounterDefinition : ScriptableObject
    {
        public string DisplayName = "New Encounter";
        [TextArea(2, 5)] public string Description = "";
        [Tooltip("The map to fight on (Tools > Combat Test > Map Builder makes them). Empty: whatever map is loaded.")]
        public MapTemplate Map;
        public EncounterSpec Spec = new EncounterSpec();

        public string Title { get { return string.IsNullOrEmpty(DisplayName) ? name : DisplayName; } }
        /// <summary>Its id in saves (completed encounters): the spec's, or the asset name.</summary>
        public string EncounterId { get { return Spec != null && !string.IsNullOrEmpty(Spec.Id) ? Spec.Id : name; } }
    }
}
