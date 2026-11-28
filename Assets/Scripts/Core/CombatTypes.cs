namespace TacticsGame.Core
{
    /// <summary>Sides. Every two sides are hostile (units fight anyone not on their own side): Rogue is a third side against
    /// both the squad and the enemy (Deliverance, while the enemy's reinforcements keep coming). New values go at the end.</summary>
    public enum Team { Player, Enemy, Rogue }

    /// <summary>What a unit is currently doing. Only Ready units can be given a command.</summary>
    // Charging: a charge ability building up (ActionDefinition.IsCharge). Channeling: a channeled ability running (ActionDefinition.ChannelSeconds).
    public enum UnitPhase { Ready, Moving, Windup, Recovery, Stunned, Dead, Charging, Channeling }

    /// <summary>Light/Heavy drives block break amounts and Unravel (Heavy at max Strain).</summary>
    public enum ImpactType { Light, Heavy }

    /// <summary>How an ability's windup shows (ActionDefinition.WindupDisplay): Auto from its impact, or forced for flavour.</summary>
    public enum WindupDisplay { Auto, Light, Heavy }

    /// <summary>AnyUnit: an ally, an enemy or (unless ActionDefinition.CannotTargetSelf) the user. New values go at the end:
    /// assets store the number.</summary>
    public enum TargetType { None, Self, Enemy, Ally, Position, AnyUnit }

    /// <summary>Which units a StatusSpec in a target list lands on (Second Mind hastes allies, slows enemies).</summary>
    public enum StatusTarget { Any, Allies, Enemies }

    /// <summary>Area of an area_attack: a circle around the target point (or the actor, for Self), or a line from the actor.</summary>
    public enum AreaShape { None, Circle, Line, Cone }   // Cone: AreaRadius long, ActionDefinition.ConeAngle wide, from the user toward the aimed point

    /// <summary>Which of the Forsaken's forms an ability can be used in (ActionDefinition.Form). Any: either.</summary>
    public enum FormLock { Any, Composed, Diverged }

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
        public const string Focusing = "focusing";      // Focus under way, not yet in Clear Mind: runs out the moment Clear Mind starts
        public const string ClearMind = "clear_mind";   // Clear Mind: Focus (wait) held long enough, strain drains fast
        public const string Braced = "braced";
        public const string Blocking = "blocking";      // universal Block: +block, stun immune, a hit hands the turn back
        public const string Mesmerized = "mesmerized";  // Mesmerize: big dodge bonus; the first attack on you is countered (before any dodge): a blink to the attacker for rapid strikes
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
        public const string Burdened = "burdened";      // Cognitive Burden: took on an ally's strain (Stacks = how much); an enemy's hit hands it to them
        public const string TrueInversion = "true_inversion";   // True Inversion: strain it would gain, it loses (InvertsStrainGain), for the channel
        public const string RegenerationSeal = "regeneration_seal";   // heals over time; a hit breaks it and stuns the attacker
        public const string Revenge = "revenge";        // Revenge Maxim: the next ability within its time winds up much faster (spent on use)
        public const string RevengePrimed = "revenge_primed";   // bookkeeping: a Transcended Block under way; Revenge comes when it ends
        public const string Flurry = "flurry";          // Sharpshooter's Flurry: much faster actions and more dodge, basic attack only (BasicAttackOnly)
        public const string Ascended = "ascended";      // Monarch's Ascension happened (once a battle): up in the air, Unstoppable, for a while
        public const string Decayed = "decayed";        // inside an Abstract Decay zone: slowed
        public const string Unmake = "unmake";          // Mark of the Unmaker's marks (Stacks); at the limit the next one Fates and Unravels
        public const string SaviorsGuard = "saviors_guard";   // Savior: the Tank's ally guarded (+Block) as it comes to them
        public const string Ghost = "ghost";             // Alio's ghost: one cast, then gone
        public const string Perplexity = "perplexity";   // Deliverance's Disassociate: lets it step behind its holder at once (Slip)
        public const string Immobilized = "immobilized"; // can't move or dash (Rooted), Joyous Dance's
        public const string Festival = "festival_of_colors";   // Festival Of Colors: faster, and Joyous Dance open
        public const string Reduction = "reduction";     // the corrupted line: slowed, strain building up (StrainPerSecond)
        public const string Melded = "melded";           // Stitch: inside its target, untargetable and taking no damage
        public const string Voided = "voided";           // folded out of the world (VoidState): does nothing, nothing reaches it
        public const string Monostep = "monostep";           // Monostep's burst of move speed
        public const string ClearAndSteady = "clear_and_steady";   // slower, but each weakpoint broken sheds strain
        public const string ConjectureReady = "conjecture_ready";  // Opening Theorem landed: Conjecture, its follow-up, is open
        public const string Beckoned = "beckoned";         // Beckon: made to walk toward The Infinite the next time it can act (BattleSimulation.Compel.cs)
        public const string Apotheosis = "apotheosis";     // caught by Imperfect Apotheosis: pulled in, VOIDED, tossed out (hidden)
        public const string Counterpoise = "counterpoise"; // inside Counterpoise's zone: +100% Block
        public const string Linked = "linked";             // a linked ability is open (Unit.LinkedOpen; BattleSimulation.Recasts.cs)
        public const string Shielded = "shielded";         // a shield soaking up damage (StatusEffect.Shield)
        public const string Clarity = "clarity";           // Clarity From Aggravation: shedding strain after being hurt
        public const string Ruptured = "ruptured";         // Delayed Crush: a heavy blow lands when it runs out (StatusFlags.StrikesOnExpire)
        public const string Grasped = "grasped";           // held by Grasp and Impel's grip: it can't act until thrown or let go
        public const string Incessant = "incessant";       // Incessant's recast is open (BattleSimulation.Recasts.cs)
        public const string Dereliction = "dereliction";   // inside the Stellation's Dereliction: slower movement, wearing down
        public const string ScepterStage = "scepter_stage";   // Scepter Of Tyranny's follow-up window: Stacks is the stage its next use is (1 or 2)
        public const string Hastened = "hastened";       // a burst of action speed (Scepter Of Tyranny, Supersede)
        public const string BeyondThresholds = "beyond_thresholds";   // Cross Beyond Thresholds: faster, and damage or a dash sends it back
        public const string Parrying = "parrying";       // Unsigned Rebuttal's window: no damage, no stun
        public const string Rebuttal = "rebuttal";       // a parry landed: the next Unsigned Rebuttal has no windup
        public const string Nemesis = "nemesis";         // Karranos Sigil: the Duelist's NEMESIS (SourceId), one at a time
        public const string Quelled = "quelled";         // Quell The Uproar: a heavy movement slow
        public const string Unkillable = "unkillable";   // health can't go below 1 (Split The Minds)
        public const string Chained = "chained";        // You.: chained to its anchor (TetherState); shown while the chain lasts
        public const string Withering = "withering";    // Withering End: slowed while the decay is channeled into it
        public const string Madness = "madness";        // Madness: faster and stronger, but it only basic-attacks the nearest enemy (Maddened)
        public const string Splinter = "splinter";      // Splinters (Stacks): each one hurts and stuns it more when it Unravels
        public const string CripplingRounds = "crippling_rounds";   // Crippling Pursuit: less damage, but its hits CRIPPLE (ArmsHits)
        public const string Crippled = "crippled";      // slower walking and dodges, no dashes (NoDash)
        public const string MentalWeapon = "mental_weapon";   // Mental Weapon: its next single-target damage fires the stored bullet at that target
        public const string Intention = "intention";       // Third Intention: the next two abilities have no recovery (SpentOnAbility)
        public const string Diverged = "diverged";         // the Forsaken's Diverged form (NEUROSIS's SelfStatuses): faster, more Block, no Dash or Block (Forsaken.cs)
        public const string Frenzied = "frenzied";         // Madness Spills Forth: faster strikes while it goes after its target
        public const string Hysteria = "hysteria";         // the Forsaken in Diverged form: a stack a second (and per Rampage hit); at the limit, MADNESS
        public const string Measured = "measured";         // Measurement's slow
        public const string Insanity = "insanity";         // the Forsaken at the HYSTERIA limit: MADNESS's rules, but on no side (Team.Rogue, Unit.HomeTeam), striking the nearest unit of any side
        public const string Countersign = "countersign";   // Countersign, on an enemy (SourceId: the Duelist): its next hit on the Duelist's ally goes to the Duelist
        public const string WrathfulReflection = "wrathful_reflection";   // a shield that sends half of what its holder takes back at the attacker
        public const string StrangeAxis = "strange_axis";  // inside Strange Axis: damage taken comes as strain instead (DamageAsStrain)
        public const string Composition = "composition";   // Compositions of Destruction's projectile (a unit: CompositionState), hidden
    }
}
