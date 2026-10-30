namespace TacticsGame.Core
{
    public enum Team { Player, Enemy }

    /// <summary>What a unit is currently doing. Only Ready units can be given a command.</summary>
    public enum UnitPhase { Ready, Moving, Windup, Recovery, Stunned, Dead }

    /// <summary>Light/Heavy drives block break amounts and Unravel (Heavy at max Strain).</summary>
    public enum ImpactType { Light, Heavy }

    public enum TargetType { None, Self, Enemy, Ally, Position }

    /// <summary>Area of an area_attack: a circle around the target point (or the actor, for Self), or a line from the actor.</summary>
    public enum AreaShape { None, Circle, Line }

    /// <summary>How an action relocates its target: shoved away from the nearest enemy (Reconstruct) or pulled toward the actor (Chain Hook).</summary>
    public enum DisplaceMode { None, AwayFromEnemies, TowardSource }

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
        public const string SteadyChain = "steady_chain"; // Steady Strikes follow-up window
        public const string Braced = "braced";
        public const string Blocking = "blocking";      // universal Block: +block, stun immune, a hit hands the turn back
    }
}
