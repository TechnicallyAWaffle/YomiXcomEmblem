using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Author-time data for one action (basic attack, ability, etc.).
    /// Numbers live here; special rules (e.g. Snipe cancels when line of
    /// sight is lost) live in an ActionBehavior selected by BehaviorId.
    /// </summary>
    [CreateAssetMenu(menuName = "TacticsGame/Action Definition")]
    public class ActionDefinition : ScriptableObject
    {
        public string Id = "basic_attack";
        public string DisplayName = "Attack";
        [Tooltip("See ActionBehaviors: basic_attack, self_buff, area_attack, heal_displace, snipe, take_aim, retaliate, trap, rapid_slices, steady_strikes, final_cut, block, dash.")]
        public string BehaviorId = "basic_attack";
        [Tooltip("Player-facing text shown when hovering the ability in the orders panel.")]
        [TextArea(2, 5)]
        public string Description = "";

        [Header("Targeting")]
        public TargetType Targeting = TargetType.Enemy;
        public float Range = 15f;
        public bool IsRanged = true;          // ranged attacks are affected by cover exposure

        [Header("Timing (seconds, before the unit's ActionSpeed is applied)")]
        public float Windup = 0.5f;
        public float Recovery = 0.5f;
        public float Cooldown = 0f;

        [Header("Resources")]
        public float EnergyCost = 0f;         // spent when the windup starts (behaviors may discount it)
        public float EnergyGain = 0f;         // gained when the action resolves
        [Tooltip("Only usable while the actor has this status (e.g. Final Cut needs 'trance').")]
        public string RequiredStatus = "";
        public bool ConsumesRequiredStatus = false;

        [Header("Offense")]
        public ImpactType Impact = ImpactType.Light;
        public float Damage = 10f;
        public float BaseAccuracy = 0.9f;
        public float StunDuration = 0.5f;
        public float StrainOnTarget = 0f;     // strain added to units this action HITS (0 for most actions)
        public float StrainOnSelf = 0f;       // strain the user gains for using it; negative relieves strain (behaviors may discount it)
        public bool PiercesTerrain = false;   // ignores cover exposure
        public float BlockPierce = 0f;        // 0.5 = ignores half of the target's block

        [Header("Area (area_attack / trap)")]
        public AreaShape Shape = AreaShape.None;
        public float AreaRadius = 0f;         // circle radius, or a trap's trigger radius
        public float LineWidth = 1f;          // line length is Range

        [Header("Support")]
        public float Heal = 0f;
        public DisplaceMode Displace = DisplaceMode.None;   // TowardSource applies on hit; AwayFromEnemies via heal_displace
        public float DisplaceDistance = 0f;

        [Header("Statuses")]
        public List<StatusSpec> SelfStatuses = new List<StatusSpec>();
        [Tooltip("Apply SelfStatuses when the windup starts instead of when the action resolves.")]
        public bool SelfStatusesOnWindup = false;
        [Tooltip("Applied to each unit this action hits (or that springs this trap).")]
        public List<StatusSpec> TargetStatusesOnHit = new List<StatusSpec>();

        [Header("Traps")]
        public float TrapArmDelay = 1f;
        public float TrapLifetime = 30f;

        [Header("Linked action")]
        [Tooltip("The action a stance or reaction fires: Take Aim's Snipe, Retaliate's free shot.")]
        public ActionDefinition LinkedAction;

        [Header("Follow-up")]
        [Tooltip("While this is the last ability the unit used, Space re-casts it (same target) and its button is highlighted.")]
        public bool IsFollowup = false;

        [Header("Presentation")]
        [Tooltip("Signature move: the cinematic camera always films it, in battle and in replays, and slows time around its impact.")]
        public bool ExtraCinematic = false;

        [Header("Interrupts")]
        // Rule: a Heavy impact that lands always cancels a windup; Light impacts do not.
        // These flags make THIS action's windup more fragile (e.g. Tracking Shot).
        public bool InterruptedByDamage = false;      // broken by any landed hit, even Light
        public bool InterruptedByStun = false;        // broken by any stun

        [HideInInspector] public int DataVersion;   // which SquadAssets data revision this asset was last written with
    }
}
