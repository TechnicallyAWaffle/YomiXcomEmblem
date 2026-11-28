using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>Author-time stat template. Copied into a runtime Unit at battle start.</summary>
    [CreateAssetMenu(menuName = "TacticsGame/Unit Definition")]
    public class UnitDefinition : ScriptableObject
    {
        [Tooltip("Stable id for saves (rosters name classes by it). Empty: the asset's name.")]
        public string Id = "";
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
        [Tooltip("Enemies: seconds it takes to react to standing in an area attack's windup or a projectile's path, before it tries to get out of the way (rolled each time: x0.5-1.5 for areas, x1-2 for projectiles). See BattleSimulation.Evasion.")]
        [Min(0f)] public float ReactionSpeed = 0.25f;
        [Tooltip("Always-available fallback attack (no cooldown or cost). Added after Actions.")]
        public ActionDefinition BasicAttack;
        [Tooltip("A Makina: a construct the player orders, but which can't move, Block or Dash, only Waits (no Clear Mind), gains Strain Per Second, and breaks at max strain (BattleSimulation, Makina.cs).")]
        public bool Makina;
        [Tooltip("It has no Block (no guard in its kit, whatever Universal holds), so it never guards (the Howler).")]
        public bool CannotBlock;
        [Tooltip("It can't Focus: a Wait is only a pause, never Clear Mind (the Howler).")]
        public bool CannotFocus;
        [Tooltip("It can't walk (a Makina can't either). It can still be moved by others.")]
        public bool CannotMove;
        [Tooltip("It has no Dash.")]
        public bool CannotDash;
        [Tooltip("It never dodges: no dodge rolls at all, whatever raises its dodge.")]
        public bool CannotDodge;
        [Tooltip("A map objective (the Omen): placed on a map with the Map Builder rather than listed in an encounter, and it doesn't count toward victory (the battle is won with only objectives left).")]
        public bool MapObjective;
        [Tooltip("Strain it gains every second, always (a Makina's decay). 0: none.")]
        [Min(0f)] public float StrainPerSecond = 0f;
        [Tooltip("What its Strain is called on screen (the Forsaken: Entropy). Empty: Strain. It works as Strain whatever its name.")]
        public string StrainName = "";
        [Tooltip("Etching ids it is never offered (the Forsaken: those that Unravel or spend past maximum strain).")]
        public List<string> ExcludedEtchings = new List<string>();
        [Tooltip("The Forsaken: the ability cast for it when its strain (Entropy) reaches maximum, which turns it Diverged (NEUROSIS). Never ordered.")]
        public ActionDefinition DivergeAction;
        [Tooltip("The Forsaken: the ability cast for it when its Entropy runs out while Diverged, which turns it Composed again (REGRESSION). Never ordered.")]
        public ActionDefinition RegressAction;
        [Tooltip("The Forsaken: the ability cast for it once its HYSTERIA reaches the limit, as soon as it can act (SHATTERED MIND), which makes it INSANE. Never ordered. Empty: INSANE at once.")]
        public ActionDefinition InsanityAction;
        [Tooltip("What this class is good at, shown when the player inspects it on the class selection screen.")]
        [TextArea(3, 8)] public string Description = "";
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
        [Tooltip("How the AI plays it, as data: a style and its tuned numbers. Overrides AIProfile when set.")]
        public AIProfileDefinition AI;
        [Tooltip("A boss: while any boss is in the battle, defeating every boss wins it, whatever else is left on the field (endless reinforcements).")]
        public bool Boss;
        public GameObject ViewPrefab;
        [Tooltip("Its 3D model (an imported FBX), shown in place of the placeholder capsule and props. Empty: the placeholder. Filled in on import for models under Assets/Models/<DisplayName>/ (UnitModelImport).")]
        public GameObject Model;
        [Tooltip("The model's Animator Controller: its clips played on battle events (UnitView's triggers). Made and kept up to date on import (UnitModelImport).")]
        public RuntimeAnimatorController ModelAnimations;
        [Tooltip("The pattern on the model's \"Etched\" material slot (dark lines on white), laid over the team's colour. Set on import from Assets/Models/<DisplayName>/<DisplayName>Etchings.png (UnitModelImport).")]
        public Texture2D ModelEtchings;
        [Tooltip("The Forsaken: the model of its Diverged form, shown in place of Model while Diverged. Filled in on import for models under Assets/Models/<DisplayName>Diverged/ (UnitModelImport).")]
        public GameObject DivergedModel;
        [Tooltip("The Diverged model's Animator Controller (UnitModelImport).")]
        public RuntimeAnimatorController DivergedAnimations;
        [Tooltip("Portrait in the battle's unit panel (lower left). Empty: the unit's initials on a coloured tile.")]
        public Texture2D Icon;
        [HideInInspector] public List<string> GrantedActions = new List<string>();   // new abilities SquadAssets already handed this unit once (SquadAssets.Grant)
        [HideInInspector] public int DataVersion;   // which SquadAssets data revision this asset was last written with
    }
}
