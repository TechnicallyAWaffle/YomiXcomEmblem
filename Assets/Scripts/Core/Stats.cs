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
    }

    /// <summary>An ability's numbers that modifiers can change, per unit (see Unit.ActionStat). New values go at the end.</summary>
    public enum ActionStat
    {
        Damage, Windup, Recovery, Cooldown, Range, StunDuration, Accuracy, StrainOnSelf, StrainOnTarget,
        Heal, AreaRadius, BlockPierce, DisplaceDistance, HealthCost, EnergyCost,
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
        Unstoppable = 1 << 7,     // no stun lands and nothing interrupts it, Unravel aside (Subjugate's windup)
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
            }
            return 0f;
        }

        /// <summary>Can this ability number go below zero? (Only a strain cost: negative relieves strain.)</summary>
        public static bool AllowsNegative(ActionStat stat) { return stat == ActionStat.StrainOnSelf; }
    }
}
