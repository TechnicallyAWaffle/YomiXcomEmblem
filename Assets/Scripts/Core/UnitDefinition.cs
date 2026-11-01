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
        public float StrainRecoveryDelay = 0.5f;
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
        [Tooltip("The universal Block (shared asset). Added after BasicAttack.")]
        [UnityEngine.Serialization.FormerlySerializedAs("Block")]
        public ActionDefinition Universal;
        [Tooltip("The universal Dash (shared asset; key G). Every unit has it alongside Block.")]
        public ActionDefinition Dash;
        public List<ActionDefinition> Actions = new List<ActionDefinition>();
        [Tooltip("Passive ids, see Core.Passives (e.g. strain_relief_on_dodge).")]
        public List<string> Passives = new List<string>();
        [Tooltip("How the AI plays this unit: a UnitAIs profile (sniper, assassin, skirmisher, healer). Empty = the general BasicEnemyAI.")]
        public string AIProfile = "";
        public GameObject ViewPrefab;
        [HideInInspector] public int DataVersion;   // which SquadAssets data revision this asset was last written with
    }
}
