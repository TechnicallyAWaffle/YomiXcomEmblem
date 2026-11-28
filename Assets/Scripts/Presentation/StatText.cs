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
                case StatId.MaxHealth: return GameText.Get("stat.unit.max_health", "Max health");
                case StatId.MaxStrain: return GameText.Get("stat.unit.max_strain", "Max strain");
                case StatId.StrainRecovery: return GameText.Get("stat.unit.strain_recovery", "Strain recovery");
                case StatId.StrainRecoveryDelay: return GameText.Get("stat.unit.strain_recovery_delay", "Strain recovery delay");
                case StatId.Dodge: return GameText.Get("stat.unit.dodge", "Dodge");
                case StatId.Block: return GameText.Get("stat.unit.block", "Block");
                case StatId.ActionSpeed: return GameText.Get("stat.unit.action_speed", "Action speed");
                case StatId.MoveSpeed: return GameText.Get("stat.unit.move_speed", "Move speed");
                case StatId.DashDistance: return GameText.Get("stat.unit.dash_distance", "Dash distance");
                case StatId.IncomingAccuracy: return GameText.Get("stat.unit.incoming_accuracy", "Hit chance vs");
                case StatId.DamageDealt: return GameText.Get("stat.unit.damage_dealt", "Damage dealt");
                case StatId.StrainCost: return GameText.Get("stat.unit.strain_cost", "Strain costs");
                case StatId.GuardBlock: return GameText.Get("stat.unit.guard_block", "Guard block");
                case StatId.FocusDelay: return GameText.Get("stat.unit.focus_delay", "Time to focus");
                case StatId.FocusStrainRelief: return GameText.Get("stat.unit.focus_strain_relief", "Clear Mind strain relief");
                case StatId.StrainDealt: return GameText.Get("stat.unit.strain_dealt", "Strain dealt");
                case StatId.StunTaken: return GameText.Get("stat.unit.stun_taken", "Stuns taken");
            }
            return stat.ToString();
        }

        public static string Name(ActionStat stat)
        {
            switch (stat)
            {
                case ActionStat.Damage: return GameText.Get("stat.damage", "Damage");
                case ActionStat.Windup: return GameText.Get("stat.windup", "Windup");
                case ActionStat.Recovery: return GameText.Get("stat.recovery", "Recovery");
                case ActionStat.Cooldown: return GameText.Get("stat.cooldown", "Cooldown");
                case ActionStat.Range: return GameText.Get("stat.range", "Range");
                case ActionStat.Accuracy: return GameText.Get("stat.accuracy", "Accuracy");
                case ActionStat.Heal: return GameText.Get("stat.heal", "Heal");
                case ActionStat.Weakpoints: return GameText.Get("stat.weakpoints", "Weakpoints");
                case ActionStat.StunDuration: return GameText.Get("stat.stun_duration", "Stun");
                case ActionStat.StrainOnSelf: return GameText.Get("stat.strain_on_self", "Strain cost");
                case ActionStat.StrainOnTarget: return GameText.Get("stat.strain_on_target", "Target strain");
                case ActionStat.AreaRadius: return GameText.Get("stat.area_radius", "Radius");
                case ActionStat.BlockPierce: return GameText.Get("stat.block_pierce", "Block pierce");
                case ActionStat.DisplaceDistance: return GameText.Get("stat.displace_distance", "Push / lift");
                case ActionStat.HealthCost: return GameText.Get("stat.health_cost", "Health cost");
                case ActionStat.EnergyCost: return GameText.Get("stat.energy_cost", "Energy cost");
                case ActionStat.Duration: return GameText.Get("stat.duration", "Duration");
                case ActionStat.TranscendStrain: return GameText.Get("stat.transcend_strain", "Transcend strain");
                case ActionStat.CritChance: return GameText.Get("stat.crit_chance", "Critical chance");
                case ActionStat.CritDamage: return GameText.Get("stat.crit_damage", "Critical damage");
            }
            return stat.ToString();
        }

        /// <summary>How a unit stat's numbers read: fractions (dodge, block, hit chance) as percentages, distances in metres.</summary>
        private static bool IsFraction(StatId stat) { return stat == StatId.Dodge || stat == StatId.Block || stat == StatId.IncomingAccuracy; }
        private static bool IsFraction(ActionStat stat) { return stat == ActionStat.Accuracy || stat == ActionStat.BlockPierce || stat == ActionStat.CritChance || stat == ActionStat.CritDamage; }

        private static string UnitOf(ActionStat stat)
        {
            switch (stat)
            {
                case ActionStat.Windup: case ActionStat.Recovery: case ActionStat.Cooldown: case ActionStat.StunDuration: case ActionStat.Duration: return "s";
                case ActionStat.Range: case ActionStat.AreaRadius: case ActionStat.DisplaceDistance: return "m";
            }
            return "";
        }

        /// <summary>A modifier's change: "+30%", "+2m", "x1.5", "none" (a multiplier of 0 on a cost).</summary>
        public static string Change(ModOp op, float value, bool fraction, string unit, bool isCost)
        {
            if (op == ModOp.Multiply) return value <= 0f && isCost ? GameText.Get("stat.change_none", "none") : "x" + value.ToString("0.##");
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
            if (string.IsNullOrEmpty(m.ActionId)) return GameText.Format("stat.modifier_all", "Abilities' {0}", Name(m.Stat).ToLowerInvariant());
            return GameText.Format("stat.modifier_one", "{0} {1}", nameOf != null ? nameOf(m.ActionId) : m.ActionId, Name(m.Stat).ToLowerInvariant());
        }

        // A rule's cell or note, unless its GameText line was emptied (hidden).
        private static void Cell(List<KeyValuePair<string, string>> cells, string label, string value) { if (GameText.Shown(value)) cells.Add(new KeyValuePair<string, string>(label, value)); }
        private static void Note(List<string> notes, string text) { if (GameText.Shown(text)) notes.Add(text); }

        /// <summary>Status rules as short cells (label, value) and longer notes, in a fixed order.</summary>
        public static void Describe(StatusFlags flags, List<KeyValuePair<string, string>> cells, List<string> notes)
        {
            if ((flags & StatusFlags.StunImmune) != 0) Cell(cells, GameText.Get("status.stun_immune.label", "Stun"), GameText.Get("status.stun_immune", "immune"));
            if ((flags & StatusFlags.Rooted) != 0) Cell(cells, GameText.Get("status.rooted.label", "Movement"), GameText.Get("status.rooted", "rooted"));
            if ((flags & StatusFlags.Untargetable) != 0) Cell(cells, GameText.Get("status.untargetable.label", "Enemies"), GameText.Get("status.untargetable", "can't target"));
            if ((flags & StatusFlags.KeepsBlock) != 0) Note(notes, GameText.Get("status.keeps_block", "Hits don't wear its block down."));
            if ((flags & StatusFlags.BreaksOnDamage) != 0) Note(notes, GameText.Get("status.breaks_on_damage", "Ends on taking damage."));
            if ((flags & StatusFlags.NegatesDamage) != 0) Note(notes, GameText.Get("status.negates_damage", "Hits deal no damage or strain (Unravel still lands)."));
            if ((flags & StatusFlags.RedirectsHits) != 0) Note(notes, GameText.Get("status.redirects_hits", "Projectiles and melee hits on them go to you instead."));
            if ((flags & StatusFlags.Unstoppable) != 0) Note(notes, GameText.Get("status.unstoppable", "No stun or interrupt stops it, except Unravel."));
            if ((flags & StatusFlags.InvertsStrainGain) != 0) Note(notes, GameText.Get("status.inverts_strain_gain", "Strain it would gain, it loses instead."));
            if ((flags & StatusFlags.BasicAttackOnly) != 0) Note(notes, GameText.Get("status.basic_attack_only", "Only the basic attack can be used."));
            if ((flags & StatusFlags.UnravelsOnExpire) != 0) Note(notes, GameText.Get("status.unravels_on_expire", "Unravels when it runs out, whatever the strain."));
        }
    }
}
