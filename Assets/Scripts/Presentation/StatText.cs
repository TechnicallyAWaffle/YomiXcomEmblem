using System.Collections.Generic;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Player-facing wording for stats, modifiers and status rules: one place, so the ability popup, status tooltips and
    /// upgrade screens all describe them the same way. A new StatId, ActionStat or StatusFlags value only needs a line here.
    /// </summary>
    public static class StatText
    {
        public static string Name(StatId stat)
        {
            switch (stat)
            {
                case StatId.MaxHealth: return "Max health";
                case StatId.MaxStrain: return "Max strain";
                case StatId.StrainRecovery: return "Strain recovery";
                case StatId.StrainRecoveryDelay: return "Strain recovery delay";
                case StatId.Dodge: return "Dodge";
                case StatId.Block: return "Block";
                case StatId.ActionSpeed: return "Action speed";
                case StatId.MoveSpeed: return "Move speed";
                case StatId.DashDistance: return "Dash distance";
                case StatId.IncomingAccuracy: return "Hit chance vs";
                case StatId.DamageDealt: return "Damage dealt";
                case StatId.StrainCost: return "Strain costs";
            }
            return stat.ToString();
        }

        public static string Name(ActionStat stat)
        {
            switch (stat)
            {
                case ActionStat.StunDuration: return "Stun";
                case ActionStat.StrainOnSelf: return "Strain cost";
                case ActionStat.StrainOnTarget: return "Target strain";
                case ActionStat.AreaRadius: return "Radius";
                case ActionStat.BlockPierce: return "Block pierce";
                case ActionStat.DisplaceDistance: return "Push / lift";
                case ActionStat.HealthCost: return "Health cost";
                case ActionStat.EnergyCost: return "Energy cost";
            }
            return stat.ToString();
        }

        /// <summary>How a unit stat's numbers read: fractions (dodge, block, hit chance) as percentages, distances in metres.</summary>
        private static bool IsFraction(StatId stat) { return stat == StatId.Dodge || stat == StatId.Block || stat == StatId.IncomingAccuracy; }
        private static bool IsFraction(ActionStat stat) { return stat == ActionStat.Accuracy || stat == ActionStat.BlockPierce; }

        private static string UnitOf(ActionStat stat)
        {
            switch (stat)
            {
                case ActionStat.Windup: case ActionStat.Recovery: case ActionStat.Cooldown: case ActionStat.StunDuration: return "s";
                case ActionStat.Range: case ActionStat.AreaRadius: case ActionStat.DisplaceDistance: return "m";
            }
            return "";
        }

        /// <summary>A modifier's change: "+30%", "+2m", "x1.5", "none" (a multiplier of 0 on a cost).</summary>
        public static string Change(ModOp op, float value, bool fraction, string unit, bool isCost)
        {
            if (op == ModOp.Multiply) return value <= 0f && isCost ? "none" : "x" + value.ToString("0.##");
            string sign = value >= 0f ? "+" : "-";
            float v = System.Math.Abs(value);
            return fraction ? string.Format("{0}{1:0}%", sign, v * 100f) : string.Format("{0}{1:0.##}{2}", sign, v, unit);
        }

        public static string Change(Modifier m)
        {
            string unit = m.Stat == StatId.DashDistance ? "m" : "";
            return Change(m.Op, m.Value, IsFraction(m.Stat), unit, m.Stat == StatId.StrainCost);
        }

        public static string Change(ActionModifier m)
        {
            bool cost = m.Stat == ActionStat.StrainOnSelf || m.Stat == ActionStat.HealthCost || m.Stat == ActionStat.EnergyCost;
            return Change(m.Op, m.Value, IsFraction(m.Stat), UnitOf(m.Stat), cost);
        }

        /// <summary>"Slash damage" / "Ability damage" for an ability modifier ('nameOf' turns an action id into its name).</summary>
        public static string Label(ActionModifier m, System.Func<string, string> nameOf)
        {
            if (string.IsNullOrEmpty(m.ActionId)) return "Abilities' " + Name(m.Stat).ToLowerInvariant();
            return (nameOf != null ? nameOf(m.ActionId) : m.ActionId) + " " + Name(m.Stat).ToLowerInvariant();
        }

        /// <summary>Status rules as short cells (label, value) and longer notes, in a fixed order.</summary>
        public static void Describe(StatusFlags flags, List<KeyValuePair<string, string>> cells, List<string> notes)
        {
            if ((flags & StatusFlags.StunImmune) != 0) cells.Add(new KeyValuePair<string, string>("Stun", "immune"));
            if ((flags & StatusFlags.Rooted) != 0) cells.Add(new KeyValuePair<string, string>("Movement", "rooted"));
            if ((flags & StatusFlags.Untargetable) != 0) cells.Add(new KeyValuePair<string, string>("Enemies", "can't target"));
            if ((flags & StatusFlags.KeepsBlock) != 0) notes.Add("Hits don't wear its block down.");
            if ((flags & StatusFlags.BreaksOnDamage) != 0) notes.Add("Ends on taking damage.");
            if ((flags & StatusFlags.NegatesDamage) != 0) notes.Add("Hits deal no damage or strain (Unravel still lands).");
            if ((flags & StatusFlags.RedirectsHits) != 0) notes.Add("Projectiles and melee hits on them go to you instead.");
            if ((flags & StatusFlags.Unstoppable) != 0) notes.Add("No stun or interrupt stops it, except Unravel.");
        }
    }
}
