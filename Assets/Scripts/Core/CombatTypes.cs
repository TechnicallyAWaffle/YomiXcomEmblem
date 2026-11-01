namespace TacticsGame.Core
{
    public enum Team { Player, Enemy }

    /// <summary>What a unit is currently doing. Only Ready units can be given a command.</summary>
    // Charging: a charge ability building up (ActionDefinition.IsCharge). Channeling: a channeled ability running (ActionDefinition.ChannelSeconds).
    public enum UnitPhase { Ready, Moving, Windup, Recovery, Stunned, Dead, Charging, Channeling }

    /// <summary>Light/Heavy drives block break amounts and Unravel (Heavy at max Strain).</summary>
    public enum ImpactType { Light, Heavy }

    /// <summary>AnyUnit: an ally, an enemy or (unless ActionDefinition.CannotTargetSelf) the user. New values go at the end:
    /// assets store the number.</summary>
    public enum TargetType { None, Self, Enemy, Ally, Position, AnyUnit }

    /// <summary>Which units a StatusSpec in a target list lands on (Second Mind hastes allies, slows enemies).</summary>
    public enum StatusTarget { Any, Allies, Enemies }

    /// <summary>Area of an area_attack: a circle around the target point (or the actor, for Self), or a line from the actor.</summary>
    public enum AreaShape { None, Circle, Line }

    /// <summary>How an action relocates its target: shoved away from the nearest enemy (Reconstruct) or pulled toward the actor (Chain Hook).
    /// AwayFromSource: knocked straight back from the actor on hit (Kinetic Blast). New values go at the end: assets store the number.</summary>
    public enum DisplaceMode { None, AwayFromEnemies, TowardSource, AwayFromSource }

    public enum BattleOutcome { InProgress, PlayerVictory, PlayerDefeat, Draw }

    /// <summary>Status ids that rules and behaviors look for. Assets use these strings in their StatusSpecs.</summary>
    public static class StatusIds
    {
        public const string Marked = "marked";          // attacks against this unit hit more easily
        public const string Hidden = "hidden";          // untargetable by enemies; taking damage reveals
        public const string Rooted = "rooted";          // cannot move or dash
        public const string TakeAim = "take_aim";       // Sharpshooter stance: snipes the next enemy that acts in sight
        public const string Retaliate = "retaliate";    // free shot on every dodge
        public const string Trance = "trance";          // Melee: enables Final Cut
        public const string Evasive = "evasive";
        public const string RapidChain = "rapid_chain"; // Rapid Slices follow-up window
        public const string Focusing = "focusing";      // Focus under way, not yet Focused: runs out the moment Focused starts
        public const string Focused = "focused";        // Focus (wait) held long enough: strain drains fast
        public const string Braced = "braced";
        public const string Blocking = "blocking";      // universal Block: +block, stun immune, a hit hands the turn back
        public const string Mesmerized = "mesmerized";  // Mesmerize: big dodge bonus; the first dodge blinks you to the attacker for rapid strikes
        public const string Wrath = "wrath";            // Fighter: abilities cost no strain and hit harder, but no Block (and no Trance)
        public const string Fated = "fated";            // Fatebreak winding up on this unit: its actions are heavily slowed
        public const string Quickened = "quickened";    // Second Mind on an ally: faster actions
        public const string Slowed = "slowed";          // Second Mind on an enemy: slower actions
        public const string Grappling = "grappling";    // Subjugate: holding someone (stun immune; only moving, headbutting or the throw)
        public const string Grappled = "grappled";      // Subjugate: held and carried
        public const string MentalCurrents = "mental_currents";   // Skirmisher: dodge and move speed
        public const string Flow = "flow";              // Skirmisher: Flowing Death's chain (Stacks = uses in a row)
        public const string RavageReady = "ravage_ready";   // Brutebeast: Ravage landed, its follow-up strike is open
        public const string LastStand = "last_stand";   // AI: stood its ground instead of running, and fights to the death (never rests)
        public const string Skyward = "skyward";        // Skyward Demise: hovering in the air, hitting harder (ends with the hover)
        public const string GuardBroken = "guard_broken";   // display only: a heavy hit on the guard put Block on cooldown until this runs out
        public const string Unstoppable = "unstoppable"; // immune to stuns and interrupts, Unravel aside (Subjugate's windup)
        public const string Sanctity = "sanctity";      // Tank: its dome of protection is up (display; the dome is an aura)
        public const string Sheltered = "sheltered";    // inside an ally's Sanctity dome: hits on it go to the dome's owner (RedirectsHits)
        public const string TimeSlowed = "time_slowed"; // inside an enemy's Chronosphere: slower actions
    }
}
