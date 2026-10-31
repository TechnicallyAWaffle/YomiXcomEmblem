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
        [Tooltip("See ActionBehaviors: basic_attack, self_buff, area_attack, heal_displace, snipe, take_aim, retaliate, trap, rapid_slices, steady_strikes, final_cut, block, dash, bash, phantom_strike, mesmerize, stutter, lance, subjugate, fatebreak, status_spell, teleport, levitate.")]
        public string BehaviorId = "basic_attack";
        [Tooltip("Player-facing text shown when hovering the ability in the orders panel.")]
        [TextArea(2, 5)]
        public string Description = "";

        [Header("Targeting")]
        public TargetType Targeting = TargetType.Enemy;
        public float Range = 15f;
        public bool IsRanged = true;          // ranged attacks are affected by cover exposure
        [Tooltip("Unit-targeted (Ally / AnyUnit) abilities only: the user can't pick itself.")]
        public bool CannotTargetSelf = false;
        [Tooltip("Above 0: after the unit, the player also picks a point within this many metres of it (Wrinkles of Reality's teleport).")]
        public float DestinationRange = 0f;

        [Header("Timing (seconds, before the unit's ActionSpeed is applied)")]
        public float Windup = 0.5f;
        public float Recovery = 0.5f;
        public float Cooldown = 0f;

        [Header("Resources")]
        public float EnergyCost = 0f;         // spent when the windup starts (behaviors may discount it)
        public float EnergyGain = 0f;         // gained when the action resolves
        [Tooltip("Only usable while the actor has this status (e.g. Final Cut needs 'trance'). Several, separated by '|': any one will do (Fatebreak: 'trance|wrath').")]
        public string RequiredStatus = "";
        public bool ConsumesRequiredStatus = false;
        [Tooltip("Consume the required status(es) when the action lands instead of when its windup starts (so it still counts for the hit).")]
        public bool ConsumeOnResolve = false;

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
        [Tooltip("Applied to each unit this action hits (or that springs this trap). Each one's AppliesTo can limit it to allies or enemies.")]
        public List<StatusSpec> TargetStatusesOnHit = new List<StatusSpec>();
        [Tooltip("Put on the target unit while the windup runs, and taken off when it lands or is interrupted (Fatebreak's slow). Durations are ignored.")]
        public List<StatusSpec> TargetStatusesDuringWindup = new List<StatusSpec>();

        [Header("Traps")]
        public float TrapArmDelay = 1f;
        public float TrapLifetime = 30f;

        [Header("Linked action")]
        [Tooltip("The action a stance or reaction fires: Take Aim's Snipe, Retaliate's free shot.")]
        public ActionDefinition LinkedAction;

        [Header("Charge (IsCharge)")]
        [Tooltip("Using it starts a charge: the unit can only keep charging or release it, targeting at release. Any damage breaks the charge.")]
        public bool IsCharge = false;
        public float MaxChargeSeconds = 2f;          // power stops growing here (the drain goes on)
        public float ChargeStrainPerSecond = 0f;     // resource drained while charging
        public float ChargeHealthPerSecond = 0f;
        [Tooltip("Damage and stun multipliers at full charge (1 = none); they grow linearly with charge time.")]
        public float ChargeDamageMultiplier = 1f;
        public float ChargeStunMultiplier = 1f;

        [Header("Follow-up")]
        [Tooltip("While this is the last ability the unit used, Space re-casts it (same target) and its button is highlighted.")]
        public bool IsFollowup = false;

        [Header("Transcend")]
        [Tooltip("Hold the ability's button to Transcend it: a stronger version (its behavior decides how) for TranscendStrain extra strain.")]
        public bool CanTranscend = false;
        public float TranscendStrain = 15f;

        [Header("Presentation")]
        [Tooltip("Signature move: the cinematic camera always films it, in battle and in replays, and slows time around its impact.")]
        public bool ExtraCinematic = false;

        [Header("Interrupts")]
        // Rule: a Heavy impact that lands always cancels a windup; Light impacts do not.
        // These flags make THIS action's windup more fragile (e.g. Tracking Shot).
        public bool InterruptedByDamage = false;      // broken by any landed hit, even Light
        public bool InterruptedByStun = false;        // broken by any stun

        [HideInInspector] public int DataVersion;   // which SquadAssets data revision this asset was last written with

        /// <summary>RequiredStatus split on '|' (empty if none): the actor needs any one of them.</summary>
        public string[] RequiredStatuses()
        {
            if (string.IsNullOrEmpty(RequiredStatus)) return System.Array.Empty<string>();
            var parts = RequiredStatus.Split('|');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }
    }
}
