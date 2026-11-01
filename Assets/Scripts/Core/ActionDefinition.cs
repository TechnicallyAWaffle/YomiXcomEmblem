using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>One of a behavior's own numbers, set for one ability (ActionDefinition.Params; see ActionBehavior.Parameters).</summary>
    [System.Serializable]
    public struct BehaviorParam
    {
        public string Key;
        public float Value;
        public BehaviorParam(string key, float value) { Key = key; Value = value; }
    }

    /// <summary>What an ability's effect looks like when it lands (CombatVfx). Auto: picked from the ability itself.</summary>
    public enum VfxKind { Auto, Tracer, Bolt, Slash, Beam, Shockwave, Chain, Heal, Buff, Lob }

    [System.Serializable]
    public class ActionVisuals
    {
        public VfxKind Kind = VfxKind.Auto;
        [Tooltip("Its colour (also an aura's dome). Used when Kind isn't Auto.")]
        public Color Color = Color.white;
        [Tooltip("Line width of tracers, bolts, slashes and beams.")]
        public float Width = 0.06f;
        [Tooltip("Tracer: carries on past the target (shots that pierce cover).")]
        public bool Pierce;

        public ActionVisuals() { }
        public ActionVisuals(VfxKind kind, Color color, float width = 0.06f, bool pierce = false) { Kind = kind; Color = color; Width = width; Pierce = pierce; }
        public bool IsSet { get { return Kind != VfxKind.Auto; } }
    }

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
        [Tooltip("See ActionBehaviors: basic_attack, self_buff, area_attack, heal_displace, snipe, take_aim, retaliate, trap, rapid_slices, steady_strikes, final_cut, block, dash, bash, phantom_strike, mesmerize, stutter, lance, subjugate, fatebreak, status_spell, teleport, levitate, blink, flowing_death, repel, charged_heal, charge, chain_attack, leap, arrival, aura, aerial_slam.")]
        public string BehaviorId = "basic_attack";
        [Tooltip("What the AI uses it for. Nothing ticked: worked out from its behavior and numbers (see AbilityRole).")]
        public AbilityRole Roles = AbilityRole.None;
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
        [Tooltip("Health the user pays to start it (Etching of Voracity). It can't be used with this much health or less.")]
        public float HealthCost = 0f;
        [Tooltip("May be used past max strain; if its strain cost takes the user over max, the user Unravels as soon as it lands (Praa's Truth).")]
        public bool UnravelsOverMaxStrain = false;
        public bool PiercesTerrain = false;   // ignores cover exposure
        public float BlockPierce = 0f;        // 0.5 = ignores half of the target's block
        [Tooltip("Damage multiplier against a target that is stunned when the hit lands (Lethal Prescience: 3). 1 = none.")]
        public float StunnedDamageMultiplier = 1f;

        [Header("Area (area_attack / trap)")]
        public AreaShape Shape = AreaShape.None;
        public float AreaRadius = 0f;         // circle radius, or a trap's trigger radius
        public float LineWidth = 1f;          // line length is Range
        [Tooltip("Dome height for an aura (with AreaRadius as its radius): how far above the user's feet it reaches.")]
        public float AreaHeight = 0f;

        [Header("Aura (a dome around the user: behavior aura)")]
        [Tooltip("Held by every unit inside the dome (AreaRadius wide, AreaHeight tall, centred on the user and moving with it) for as long as it is inside and the dome lasts. Each one's AppliesTo picks allies or enemies; the user itself is left out. Durations are ignored.")]
        public List<StatusSpec> AuraStatuses = new List<StatusSpec>();
        [Tooltip("How long the dome lasts once the action lands. A channeled action's dome lasts as long as the channel instead.")]
        public float AuraSeconds = 0f;

        [Header("Channel")]
        [Tooltip("Above 0: once it lands, the user channels it for up to this many seconds, then recovers (Recovery). A channel can't be cancelled, the user can't do anything else meanwhile (the AI never cancels one either), and any damage or stun breaks it.")]
        public float ChannelSeconds = 0f;
        [Tooltip("The channel also breaks when its target unit leaves the user's sight or Range.")]
        public bool ChannelNeedsSight = false;

        [Header("Aerial slam (behavior aerial_slam: DisplaceDistance is the height)")]
        [Tooltip("Seconds rising with the target, hanging at the top, and slamming down.")]
        public float AirRiseSeconds = 0.75f;
        public float AirHoldSeconds = 0.75f;
        public float AirSlamSeconds = 0.5f;

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
        [Tooltip("Put on the user while the windup runs, and taken off when it lands or is interrupted (Hidden Strike's stealth). Durations are ignored.")]
        public List<StatusSpec> SelfStatusesDuringWindup = new List<StatusSpec>();

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
        [Tooltip("Heal multiplier at full charge (charged_heal: Deluge of Creation).")]
        public float ChargeHealMultiplier = 1f;

        [Header("Follow-up")]
        [Tooltip("While this is the last ability the unit used, Space re-casts it (same target) and its button is highlighted.")]
        public bool IsFollowup = false;
        [Tooltip("Chain: right after it, it can be used this many more times in a row on the same target (each within ChainWindow seconds of the last one's recovery), ignoring its cooldown, for ChainCostMultiplier of its strain. 0 = no chain. Behavior chain_attack.")]
        public int ChainFollowups = 0;
        public float ChainCostMultiplier = 1f;
        public float ChainWindow = 1f;

        [Header("Transcend")]
        [Tooltip("Hold the ability's button to Transcend it: a stronger version (its behavior decides how) for TranscendStrain extra strain.")]
        public bool CanTranscend = false;
        public float TranscendStrain = 15f;
        [Tooltip("Windup multiplier when Transcended (Praa's Truth: 0 = instant). 1 = unchanged.")]
        public float TranscendWindupMultiplier = 1f;

        [Header("Presentation")]
        [Tooltip("Signature move: the cinematic camera always films it, in battle and in replays, and slows time around its impact.")]
        public bool ExtraCinematic = false;
        [Tooltip("How it looks when it lands (and its dome, for an aura). Kind Auto: worked out from the ability (shape, range, heal...).")]
        public ActionVisuals Visuals = new ActionVisuals();

        [Header("Interrupts")]
        // Rule: a Heavy impact that lands always cancels a windup; Light impacts do not.
        // These flags make THIS action's windup more fragile (e.g. Tracking Shot).
        public bool InterruptedByDamage = false;      // broken by any landed hit, even Light
        public bool InterruptedByStun = false;        // broken by any stun

        [Header("Behavior numbers")]
        [Tooltip("Numbers its behavior has of its own (Rapid Slices' follow-up discount, Bash's wall bonus...), by key; any left out keep the behavior's default. The Tuning & Text window lists them.")]
        public List<BehaviorParam> Params = new List<BehaviorParam>();

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
