using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>Author-time stat template. Copied into a runtime Unit at battle start.</summary>
    [CreateAssetMenu(menuName = "TacticsGame/Unit Definition")]
    public class UnitDefinition : ScriptableObject
    {
        public string DisplayName;
        public float MaxHealth = 100f;
        public float BaseDodge = 0.1f;
        public float BaseBlock = 0f;
        public float MaxStrain = 100f;
        [Tooltip("Strain shed per second once the unit has gone StrainRecoveryDelay seconds without gaining any.")]
        public float StrainRecoveryPerSecond = 6f;
        public float StrainRecoveryDelay = 1.5f;
        public float MaxEnergy = 0f;       // 0 = this unit doesn't use energy
        public float StartingEnergy = 0f;
        public float ActionSpeed = 1f;     // scales windup/recovery (2 = twice as fast)
        public float MoveSpeed = 4f;
        public float DashDistance = 3f;
        public float EyeHeight = 1.6f;
        public float BodyHeight = 1.8f;
        public float BodyRadius = 0.4f;    // units block line of sight; bigger units block more
        [Tooltip("Always-available fallback attack (no cooldown or cost). Added after Actions.")]
        public ActionDefinition BasicAttack;
        [Tooltip("The unit's universal option: Block or Dash (shared assets), never both. Added after BasicAttack.")]
        [UnityEngine.Serialization.FormerlySerializedAs("Block")]
        public ActionDefinition Universal;
        /// <summary>Default pick for the universal option: ranged units dash, melee units block.</summary>
        public bool PrefersDash
        {
            get
            {
                var main = BasicAttack != null ? BasicAttack : (Actions.Count > 0 ? Actions[0] : null);
                return main != null && main.IsRanged;
            }
        }
        public List<ActionDefinition> Actions = new List<ActionDefinition>();
        [Tooltip("Passive ids, see Core.Passives (e.g. strain_relief_on_dodge).")]
        public List<string> Passives = new List<string>();
        public GameObject ViewPrefab;
        [HideInInspector] public int DataVersion;   // which SquadAssets data revision this asset was last written with
    }
}
