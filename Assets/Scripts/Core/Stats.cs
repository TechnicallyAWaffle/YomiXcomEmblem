using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Unit stats that modifiers (statuses, upgrades) can change. Each is read as (base + every Add) x every Multiply; see
    /// Unit.Stat. New values go at the end: assets store the number.
    /// </summary>
    public enum StatId
    {
        MaxHealth,          // fixed for the battle: upgrades only (baked in when the unit is made)
        MaxStrain,          // fixed: upgrades only
        StrainRecovery,     // fixed: upgrades only (strain shed per second)
        StrainRecoveryDelay,// fixed: upgrades only
        Dodge,              // 0..1
        Block,              // 0..1; a status's Add is worn down by hits unless it KeepsBlock
        ActionSpeed,        // scales windups and recoveries; a status's also scales walking
        MoveSpeed,
        DashDistance,
        IncomingAccuracy,   // added to the hit chance of attacks on the unit (Marked)
        DamageDealt,        // multiplier on the unit's attacks (Wrath), base 1
        StrainCost,         // multiplier on what its abilities cost in strain (Wrath: x0), base 1
        GuardBlock,         // multiplier on the Block its guard gives (Living Carapace), base 1
        FocusDelay,         // seconds of focusing before it enters Clear Mind; base BattleSimulation.FocusDelay (Mantra of Focus: Add -0.75)
        FocusStrainRelief,  // strain shed per second while in Clear Mind; base BattleSimulation.FocusStrainPerSecond
        StrainDealt,        // multiplier on the strain its abilities put on enemies (Despair and Panic), base 1
        StunTaken,          // multiplier on how long stuns that land on it last (Block: x0.5), base 1; Unravel and falls ignore it
    }

    /// <summary>
    /// An ability's numbers that modifiers can change, per unit (see Unit.ActionStat). New values go at the end.
    /// Duration scales how long everything the ability puts on units lasts (its statuses, a dome) and is read as a scale
    /// (Unit.DurationScale), so give it Multiply modifiers; its Base is the longest of those durations.
    /// </summary>
    public enum ActionStat
    {
        Damage, Windup, Recovery, Cooldown, Range, StunDuration, Accuracy, StrainOnSelf, StrainOnTarget,
        Heal, AreaRadius, BlockPierce, DisplaceDistance, HealthCost, EnergyCost, Duration,
        TranscendStrain,   // the extra strain Transcending it costs (its own, or a granted one's): Stepweaver's Dash
    }

    public enum ModOp { Add, Multiply }

    /// <summary>On/off rules a status gives the unit holding it. New values go at the end (bit by bit).</summary>
    [System.Flags]
    public enum StatusFlags
    {
        None = 0,
        Rooted = 1 << 0,          // can't move or dash (Trap)
        Untargetable = 1 << 1,    // enemies can't target or hit it (Marking Shot's stealth)
        BreaksOnDamage = 1 << 2,  // removed the moment the unit takes damage
        StunImmune = 1 << 3,      // stuns don't land (Block); Unravel always does
        KeepsBlock = 1 << 4,      // its Block isn't worn down by hits (Brace)
        NegatesDamage = 1 << 5,   // hits that land deal no damage and no strain (Transcended Block)
        RedirectsHits = 1 << 6,   // projectiles and single-target melee hits on it go to whoever applied the status (Sanctity)
        Unstoppable = 1 << 7,     // no stun lands and nothing interrupts it, Unravel aside; nothing else moves it (BattleSimulation.CanDisplace) (Subjugate's windup)
        InvertsStrainGain = 1 << 8,   // strain it would gain, from any source, it loses instead (True Inversion)
        BasicAttackOnly = 1 << 9,     // every ability but the unit's basic attack is locked while it lasts (Flurry)
        UnravelsOnExpire = 1 << 10,   // when it runs out, its holder Unravels, whatever its strain (Mark of the Unmaker's Fated)
        Unkillable = 1 << 11,         // its holder's health can't fall below 1 (Split The Minds' windup)
        Maddened = 1 << 12,           // out of its side's control: it keeps using its basic attack on the nearest enemy, or the one the status lends it without one (MADNESS; MaddenedAI)
        ArmsHits = 1 << 13,           // its holder's hits put the TargetStatusesOnHit of the ability that gave it on what they hit (Crippling Pursuit's rounds)
        NoDash = 1 << 14,             // it can't use dashes (abilities whose behavior Dashes: the Dash, Phantom Strike...); dodges still dash (CRIPPLED)
    }

    /// <summary>A change to one unit stat: added to it, or multiplying it.</summary>
    [System.Serializable]
    public struct Modifier
    {
        public StatId Stat;
        public ModOp Op;
        public float Value;

        public Modifier(StatId stat, ModOp op, float value) { Stat = stat; Op = op; Value = value; }
        public static Modifier Add(StatId stat, float value) { return new Modifier(stat, ModOp.Add, value); }
        public static Modifier Mult(StatId stat, float value) { return new Modifier(stat, ModOp.Multiply, value); }
    }

    /// <summary>A change to one number of one ability (or of every ability, with ActionId empty).</summary>
    [System.Serializable]
    public struct ActionModifier
    {
        [Tooltip("The ActionDefinition.Id it changes; empty = every ability the unit has.")]
        public string ActionId;
        public ActionStat Stat;
        public ModOp Op;
        public float Value;

        public ActionModifier(string actionId, ActionStat stat, ModOp op, float value) { ActionId = actionId; Stat = stat; Op = op; Value = value; }
        public bool AppliesTo(ActionDefinition def) { return string.IsNullOrEmpty(ActionId) || (def != null && def.Id == ActionId); }
    }

    /// <summary>Folding modifiers into a value: (base + adds) x mults.</summary>
    public struct StatFold
    {
        public float AddSum, MultProduct;
        public static StatFold Start { get { return new StatFold { AddSum = 0f, MultProduct = 1f }; } }

        public void Take(ModOp op, float value)
        {
            if (op == ModOp.Add) AddSum += value;
            else MultProduct *= value;
        }

        public void Take(List<Modifier> mods, StatId stat)
        {
            if (mods == null) return;
            for (int i = 0; i < mods.Count; i++) if (mods[i].Stat == stat) Take(mods[i].Op, mods[i].Value);
        }

        public void Take(List<ActionModifier> mods, ActionDefinition def, ActionStat stat)
        {
            if (mods == null) return;
            for (int i = 0; i < mods.Count; i++) if (mods[i].Stat == stat && mods[i].AppliesTo(def)) Take(mods[i].Op, mods[i].Value);
        }

        public float Of(float baseValue) { return (baseValue + AddSum) * MultProduct; }
    }

    public static class Stats
    {
        /// <summary>Unit stats fixed for a battle: upgrades to them are baked into the unit when it is made, statuses can't change them.</summary>
        public static bool IsFixed(StatId stat) { return stat <= StatId.StrainRecoveryDelay; }

        /// <summary>An ability's own value for 'stat', before modifiers.</summary>
        public static float Base(ActionDefinition def, ActionStat stat)
        {
            switch (stat)
            {
                case ActionStat.Damage: return def.Damage;
                case ActionStat.Windup: return def.Windup;
                case ActionStat.Recovery: return def.Recovery;
                case ActionStat.Cooldown: return def.Cooldown;
                case ActionStat.Range: return def.Range;
                case ActionStat.StunDuration: return def.StunDuration;
                case ActionStat.Accuracy: return def.BaseAccuracy;
                case ActionStat.StrainOnSelf: return def.StrainOnSelf;
                case ActionStat.StrainOnTarget: return def.StrainOnTarget;
                case ActionStat.Heal: return def.Heal;
                case ActionStat.AreaRadius: return def.AreaRadius;
                case ActionStat.BlockPierce: return def.BlockPierce;
                case ActionStat.DisplaceDistance: return def.DisplaceDistance;
                case ActionStat.HealthCost: return def.HealthCost;
                case ActionStat.EnergyCost: return def.EnergyCost;
                case ActionStat.Duration: return LongestDuration(def);
                case ActionStat.TranscendStrain: return def.CanTranscend ? def.TranscendStrain : 0f;
            }
            return 0f;
        }

        /// <summary>The longest-lasting thing the ability puts on units: a status it grants or inflicts, or its dome.</summary>
        public static float LongestDuration(ActionDefinition def)
        {
            float d = def.ChannelSeconds > 0f ? 0f : def.AuraSeconds;
            if (def.SelfStatuses != null) foreach (var s in def.SelfStatuses) if (s != null) d = Mathf.Max(d, s.Duration);
            if (def.TargetStatusesOnHit != null) foreach (var s in def.TargetStatusesOnHit) if (s != null) d = Mathf.Max(d, s.Duration);
            return d;
        }

        /// <summary>
        /// Does the ability really have this number (so changing it does something)? Its value is above zero, and it is used:
        /// a range needs something to aim at, a stun something that hits, a radius an area. Every ability carries the
        /// defaults (a 15m range, a 0.5s stun) whether it uses them or not.
        /// </summary>
        public static bool Has(ActionDefinition def, ActionStat stat)
        {
            if (def == null || Base(def, stat) <= 0f) return false;
            switch (stat)
            {
                case ActionStat.Range: return def.Targeting != TargetType.Self && def.Targeting != TargetType.None;
                case ActionStat.StunDuration: return def.Damage > 0f || def.Targeting == TargetType.Enemy;
                case ActionStat.Accuracy: case ActionStat.BlockPierce: return def.Damage > 0f;
                case ActionStat.AreaRadius: return def.Shape != AreaShape.None || def.BehaviorId == "trap" || def.BehaviorId == "aura";
            }
            return true;
        }

        /// <summary>Can this ability number go below zero? (Only a strain cost: negative relieves strain.)</summary>
        public static bool AllowsNegative(ActionStat stat) { return stat == ActionStat.StrainOnSelf || stat == ActionStat.StrainOnTarget; }   // negative: relief (Minutes Turn To Centuries)
    }
}
