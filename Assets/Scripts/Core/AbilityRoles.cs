namespace TacticsGame.Core
{
    /// <summary>
    /// What an ability is for, as the AI sees it. The AI looks abilities up by role, never by name or behavior, so any kit
    /// works with any AI style: give a new enemy a dash-in strike and the AI that hunts with an Opener uses it. An ability's
    /// roles come from its behavior and data (ActionBehavior.DefaultRoles) unless ActionDefinition.Roles sets them.
    /// New values go at the end (bit by bit): assets store the number.
    /// </summary>
    [System.Flags]
    public enum AbilityRole
    {
        None = 0,
        Strike = 1 << 0,      // a plain attack on one enemy
        Snipe = 1 << 1,       // a long, heavy shot saved for marked targets
        Mark = 1 << 2,        // marks its target (attacks hit it more easily)
        Gapcloser = 1 << 3,   // takes the user to a point: blinks
        Escape = 1 << 4,      // takes the user out of danger: blinks
        Opener = 1 << 5,      // a strong first strike that closes in itself (Hidden Strike)
        Guard = 1 << 6,       // a guard against an incoming hit (Block)
        Dash = 1 << 7,        // a quick dash anywhere (the universal Dash)
        Heal = 1 << 8,
        Peel = 1 << 9,        // pushes enemies off the user (Repel)
        AreaBlast = 1 << 10,  // damage in a circle at a point
        DodgeBuff = 1 << 11,  // a self buff that raises dodge (Mental Currents)
        Combo = 1 << 12,      // only after another ability, best on a stunned target (Ravage Follow-up)
        Rush = 1 << 13,       // a charge that drags enemies along (Charge)
        Chain = 1 << 14,      // gets better used again and again (Flowing Death)
    }

    public static class AbilityRoles
    {
        /// <summary>An ability's roles: its own (ActionDefinition.Roles) if set, else its behavior's reading of it.</summary>
        public static AbilityRole Of(ActionDefinition def)
        {
            if (def == null) return AbilityRole.None;
            return def.Roles != AbilityRole.None ? def.Roles : ActionBehaviors.Get(def.BehaviorId).DefaultRoles(def);
        }

        public static bool Has(ActionDefinition def, AbilityRole role) { return (Of(def) & role) != 0; }

        /// <summary>The unit's first ability with 'role' (and matching 'also', if given), in the order it lists them; or null.</summary>
        public static ActionDefinition Find(Unit unit, AbilityRole role, System.Predicate<ActionDefinition> also = null)
        {
            foreach (var a in unit.Actions) if (a != null && Has(a, role) && (also == null || also(a))) return a;
            return null;
        }

        /// <summary>Does the unit have any ability with 'role'?</summary>
        public static bool Any(Unit unit, AbilityRole role, System.Predicate<ActionDefinition> also = null) { return Find(unit, role, also) != null; }
    }
}
