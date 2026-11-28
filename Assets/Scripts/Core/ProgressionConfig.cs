using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>How a new member's class abilities are chosen: the tuned starting set, or a random handful.</summary>
    public enum LoadoutStart { Default, Random }

    /// <summary>A class's starting abilities (Default) and how many a Random start gets.</summary>
    [System.Serializable]
    public class ClassStart
    {
        public UnitDefinition Class;
        public List<ActionDefinition> DefaultAbilities = new List<ActionDefinition>();
        [Min(0)] public int RandomCount = 3;
    }

    /// <summary>What a Form ritual does to a stat (ProgressionConfig.Rituals).</summary>
    public enum RitualKind { Raise, Lower, Perpetuate }

    /// <summary>
    /// A Form ritual: Raise or Lower a stat by a share of its base (rolled between MinShare and MaxShare), or Perpetuate it
    /// (back to its base and locked there: no ritual, Etching or upgrade changes it again). It uses GraspCost and raises the
    /// member's max Grasp by GraspBonus.
    /// </summary>
    [System.Serializable]
    public class RitualDefinition
    {
        public string Id = "";
        public string Name = "";
        [TextArea] public string Description = "";
        public RitualKind Kind = RitualKind.Raise;
        [Min(0f)] public float MinShare = 0.1f, MaxShare = 0.3f;
        [Min(0)] public int GraspCost = 20;
        [Min(0)] public int GraspBonus = 0;
    }

    /// <summary>A stat Form rituals work on, by name ("Compute" is action speed). BaseFloor: the base a share is taken of is at
    /// least this (for a stat some units have none of).</summary>
    [System.Serializable]
    public class FormStat
    {
        public StatId Stat;
        public string Name = "";
        [Min(0f)] public float BaseFloor = 0f;
    }

    /// <summary>
    /// The progression's numbers (edit the asset in the Inspector: Assets/Campaign/Progression): what each class starts
    /// with, and the Grasp economy between missions: Form (stat rituals), Cognition (Etchings), Knowledge (abilities).
    /// </summary>
    [CreateAssetMenu(menuName = "TacticsGame/Progression Config")]
    public class ProgressionConfig : ScriptableObject
    {
        [Tooltip("How a new campaign's squad starts unless the player picks otherwise.")]
        public LoadoutStart DefaultStart = LoadoutStart.Default;
        public List<ClassStart> Classes = new List<ClassStart>();

        [Header("Grasp")]
        [Tooltip("Max Grasp a member starts the campaign with.")]
        [Min(0)] public int StartingGrasp = 0;
        [Tooltip("Overburdened (using more Grasp than its max): strain per second in battle, as a share of the Grasp over.")]
        [Min(0f)] public float OverburdenStrainShare = 0.1f;
        [Tooltip("Holding the mouse on an ability or Etching this long destroys it...")]
        [Min(0.1f)] public float DestroyHoldSeconds = 2f;
        [Tooltip("... and lowers max Grasp by this much (the Grasp it used is freed).")]
        [Min(0)] public int DestroyGraspLoss = 10;

        [Header("Knowledge (abilities)")]
        [Tooltip("Abilities offered to learn per upgrade phase, drawn at random from the class's locked ones (0 = all of them).")]
        [Min(0)] public int AbilitiesOffered = 6;
        [Tooltip("Grasp to learn the first ability.")]
        [Min(0)] public int AbilityGrasp = 10;
        [Tooltip("Grasp added to an ability's price for each one the member has learned.")]
        [Min(0)] public int AbilityGraspStep = 10;
        [Tooltip("The abilities a member started with count as learned for the price (off: only ones learned in Knowledge).")]
        public bool StartersRaisePrice = false;

        [Header("Cognition (Etchings)")]
        [Tooltip("The pool the offers are drawn from.")]
        public List<EtchingDefinition> Etchings = new List<EtchingDefinition>();
        [Tooltip("Etchings offered at a time (all different); taking one draws a fresh set.")]
        [Min(1)] public int EtchingsOffered = 3;
        [Tooltip("Grasp the first Etching uses.")]
        [Min(0)] public int EtchingGrasp = 20;
        [Tooltip("Grasp added to an Etching's price for each Etching the member already has.")]
        [Min(0)] public int EtchingGraspStep = 10;

        [Header("Form (stats)")]
        public List<FormStat> FormStats = DefaultFormStats();
        public List<RitualDefinition> Rituals = DefaultRituals();

        public ClassStart StartFor(UnitDefinition c) { return Classes.Find(s => s != null && s.Class == c); }

        public RitualDefinition Ritual(string id) { return Rituals.Find(r => r != null && r.Id == id); }

        public FormStat Form(StatId stat) { return FormStats.Find(f => f != null && f.Stat == stat); }

        public static List<FormStat> DefaultFormStats()
        {
            return new List<FormStat>
            {
                new FormStat { Stat = StatId.MaxHealth, Name = "Health" },
                new FormStat { Stat = StatId.MaxStrain, Name = "Max Strain" },
                new FormStat { Stat = StatId.StrainRecovery, Name = "Strain Recovery" },
                new FormStat { Stat = StatId.MoveSpeed, Name = "Movement Speed" },
                new FormStat { Stat = StatId.DashDistance, Name = "Dash Distance" },
                new FormStat { Stat = StatId.ActionSpeed, Name = "Compute" },
            };
        }

        public static List<RitualDefinition> DefaultRituals()
        {
            return new List<RitualDefinition>
            {
                new RitualDefinition { Id = "graft", Name = "Graft", Kind = RitualKind.Raise, MinShare = 0.1f, MaxShare = 0.3f, GraspCost = 20,
                                       Description = "Raises the stat by a random 10-30% of its base value." },
                new RitualDefinition { Id = "weave", Name = "Weave", Kind = RitualKind.Raise, MinShare = 0.5f, MaxShare = 0.5f, GraspCost = 50,
                                       Description = "Raises the stat by 50% of its base value." },
                new RitualDefinition { Id = "amputate", Name = "Amputate", Kind = RitualKind.Lower, MinShare = 0.3f, MaxShare = 0.3f, GraspCost = 0, GraspBonus = 20,
                                       Description = "Lowers the stat by 30% of its base value. Raises maximum Grasp by 20." },
                new RitualDefinition { Id = "perpetuate", Name = "Perpetuate", Kind = RitualKind.Perpetuate, GraspCost = 100,
                                       Description = "Resets the stat to its base value and locks it there permanently. No other effect can override this." },
            };
        }

        public EtchingDefinition Etching(string id) { return Etchings.Find(e => e != null && e.Id == id); }

        /// <summary>The starting Etchings (Build Scene fills a new asset with these; tune them there).</summary>
        public void FillDefaultEtchings()
        {
            Etchings = DefaultEtchings();
        }

        public static List<EtchingDefinition> DefaultEtchings()
        {
            return new List<EtchingDefinition>
            {
                new EtchingDefinition { Id = "minds_reach", Name = "The Mind's Reach Grows", Description = "Increase maximum strain by 30 and the range of all of your abilities by 30%." }
                    .Add(StatId.MaxStrain, 30f).MultAbility(ActionStat.Range, 1.3f),
                new EtchingDefinition { Id = "senses_of_closure", Name = "Senses of Closure", Description = "Your targeted abilities wind up faster the closer you are to your target when the windup starts, up to 50% faster." }
                    .WithTrait(EtchingTraits.ClosingWindup, new BehaviorParam("max_bonus", 0.5f)),
                new EtchingDefinition { Id = "senses_of_space", Name = "Senses of Space", Description = "Gain 2 m/s movement speed and 50% increased dash distance." }
                    .Add(StatId.MoveSpeed, 2f).Mult(StatId.DashDistance, 1.5f),
                new EtchingDefinition { Id = "living_carapace", Name = "Living Carapace", Description = "Gain 30 maximum health. Guarding grants 25% more Block." }
                    .Add(StatId.MaxHealth, 30f).Mult(StatId.GuardBlock, 1.25f),
                new EtchingDefinition { Id = "mantra_of_focus", Name = "Mantra of Focus", Description = "Reach Clear Mind 50% faster (stacks multiplicatively) and shed 5 more strain per second while in Clear Mind." }
                    .Mult(StatId.FocusDelay, 0.5f).Add(StatId.FocusStrainRelief, 5f),
                new EtchingDefinition { Id = "persistent_algorithms", Name = "Persistent Algorithms", Description = "Using strain grants a stack of Compiled for 1 second, up to 5. Each stack reduces your strain costs by 10%, up to 50%." }
                    .WithTrait(EtchingTraits.Compiled, new BehaviorParam("seconds", 1f), new BehaviorParam("max_stacks", 5f), new BehaviorParam("per_stack", 0.1f), new BehaviorParam("max_reduction", 0.5f)),
                new EtchingDefinition { Id = "etching_of_efficiency", Name = "Etching of Efficiency", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability: 30% reduced cooldown (stacks multiplicatively)." }
                    .MultAbility(ActionStat.Cooldown, 0.7f),
                new EtchingDefinition { Id = "etching_of_summation", Name = "Etching of Summation", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability: 30% increased damage (stacks multiplicatively)." }
                    .MultAbility(ActionStat.Damage, 1.3f),
                new EtchingDefinition { Id = "etching_of_complexity", Name = "Etching of Complexity", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability: 50% increased range, radius and knockback (stacks multiplicatively)." }
                    .MultAbility(ActionStat.Range, 1.5f).MultAbility(ActionStat.AreaRadius, 1.5f).MultAbility(ActionStat.DisplaceDistance, 1.5f),
                new EtchingDefinition { Id = "etching_of_recurrence", Name = "Etching of Recurrence", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability: 40% increased duration of its stuns, buffs and debuffs (stacks multiplicatively)." }
                    .MultAbility(ActionStat.StunDuration, 1.4f).MultAbility(ActionStat.Duration, 1.4f),
                new EtchingDefinition { Id = "burst_of_cognition", Name = "Burst of Cognition", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability: 30% shorter windup (stacks multiplicatively)." }
                    .MultAbility(ActionStat.Windup, 0.7f),
                new EtchingDefinition { Id = "processing_proxies", Name = "Processing Proxies", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability: 30% shorter recovery (stacks multiplicatively)." }
                    .MultAbility(ActionStat.Recovery, 0.7f),
                new EtchingDefinition { Id = "revenge_maxim", Name = "Revenge Maxim", Description = "After a Transcended Block, gain REVENGE for 1 second: your next ability in that time spends it and winds up 75% faster." }
                    .WithTrait(EtchingTraits.RevengeMaxim, new BehaviorParam("seconds", 1f), new BehaviorParam("windup_mult", 0.25f)),
                new EtchingDefinition { Id = "suffer_beyond_your_limits", Name = "Suffer Beyond Your Limits", Description = "You can spend strain at or past maximum strain. A strain cost that goes past your maximum also costs health equal to 60% of it (20% less for each extra copy)." }
                    .WithTrait(EtchingTraits.SufferBeyond, new BehaviorParam("health_share", 0.6f), new BehaviorParam("per_copy", 0.2f)),
                new EtchingDefinition { Id = "perception_for_violence", Name = "Perception for Violence", Description = "Your abilities wind up 30% faster against stunned enemies." }
                    .WithTrait(EtchingTraits.PerceptionForViolence, new BehaviorParam("bonus", 0.3f)),
                new EtchingDefinition { Id = "cannibalize_cognition", Name = "Cannibalize Cognition", Description = "Dealing the killing blow to an enemy reduces your strain by 40." }
                    .WithTrait(EtchingTraits.CannibalizeCognition, new BehaviorParam("relief", 40f)),
                new EtchingDefinition { Id = "despair_and_panic", Name = "Despair and Panic", Description = "Your abilities put 25% more strain on the enemies they hit." }
                    .Mult(StatId.StrainDealt, 1.25f),
                new EtchingDefinition { Id = "imperceptible_steps", Name = "Imperceptible Steps", Description = "Gain 20% dodge. Dodging has a 50% chance to break every ranged attack winding up at you." }
                    .Add(StatId.Dodge, 0.2f).WithTrait(EtchingTraits.ImperceptibleSteps, new BehaviorParam("chance", 0.5f)),
                new EtchingDefinition { Id = "body_on_strings", Name = "Body On Strings", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability with no Transcend: it can be Transcended for +10 strain, making you Unstoppable while it winds up." }
                    .GrantsTranscend(TranscendGrant.UnstoppableWindup, 10f),
                new EtchingDefinition { Id = "uncloud_the_mind", Name = "Uncloud The Mind", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability with no Transcend: it can be Transcended for +15 strain, with no recovery." }
                    .GrantsTranscend(TranscendGrant.NoRecovery, 15f),
                new EtchingDefinition { Id = "repetition_is_strength", Name = "Repetition Is Strength", Description = "Walking without stopping grants 0.25 movement speed per second walked. After 2 seconds of walking you are Unstoppable until you stop. Copies add more speed and cut the 2 seconds by 30% each." }
                    .WithTrait(EtchingTraits.RepetitionIsStrength, new BehaviorParam("speed_per_second", 0.25f), new BehaviorParam("unstoppable_after", 2f), new BehaviorParam("faster", 0.3f), new BehaviorParam("max_seconds", 8f)),
                new EtchingDefinition { Id = "imprinted_memory", Name = "Imprinted Memory", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability: 40% lower strain cost. An ability with no strain cost instead sheds 7 strain when used (copies add up)." }
                    .MultAbility(ActionStat.StrainOnSelf, 0.6f).Otherwise(ActionStat.StrainOnSelf, ModOp.Add, -7f),
                new EtchingDefinition { Id = "witless", Name = "Witless", Description = "Gain 40% maximum health. You can no longer Block." }
                    .Mult(StatId.MaxHealth, 1.4f).Removes("block"),
                new EtchingDefinition { Id = "dance_between_their_rage", Name = "Dance Between Their Rage", Description = "Gain 35% dodge. You can no longer Dash." }
                    .Add(StatId.Dodge, 0.35f).Removes("dash"),
                new EtchingDefinition { Id = "fractures_of_stress", Name = "Fractures Of Stress", Description = "Once per battle, when a hit takes you under 30% health, become UNSTOPPABLE and heal 30% of your maximum health over 3 seconds." }
                    .WithTrait(EtchingTraits.FracturesOfStress, new BehaviorParam("below", 0.3f), new BehaviorParam("heal_share", 0.3f), new BehaviorParam("seconds", 3f), new BehaviorParam("once_per_battle", 1f)),
                new EtchingDefinition { Id = "echo", Name = "Echo", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability with no Transcend: it can be Transcended for +30 strain, putting it on no cooldown." }
                    .GrantsTranscend(TranscendGrant.NoCooldown, 30f),
                new EtchingDefinition { Id = "flow_state", Name = "Flow State", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability to grant you the option to Transcend it for +15 Strain. When Transcended, you gain 75% Dodge for 2s when cast." }
                    .GrantsTranscend(TranscendGrant.DodgeOnCast, 15f, new BehaviorParam("dodge", 0.75f), new BehaviorParam("seconds", 2f)),
                new EtchingDefinition { Id = "untethered_to_sanity", Name = "Untethered To Sanity", Description = "Reduce your maximum Strain by 30%. Unravel's stun is 75% shorter for you." }
                    .Mult(StatId.MaxStrain, 0.7f).WithTrait(EtchingTraits.UntetheredToSanity, new BehaviorParam("stun_mult", 0.25f)),
                new EtchingDefinition { Id = "quickness_and_lethargy", Name = "Quickness And Lethargy", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability to grant the option to Transcend it for +25 Strain. When Transcended, the ability's windup is reduced by 75%, but its recovery is increased by 25%." }
                    .GrantsTranscend(TranscendGrant.QuickWindup, 25f, new BehaviorParam("windup_mult", 0.25f), new BehaviorParam("recovery_mult", 1.25f)),
                new EtchingDefinition { Id = "stepweaver", Name = "Stepweaver", Description = "Reduce your maximum health and strain by 30%. Dash and Transcended Dash cost 75% less Strain." }
                    .Mult(StatId.MaxHealth, 0.7f).Mult(StatId.MaxStrain, 0.7f).MultAbility("dash", ActionStat.StrainOnSelf, 0.25f).MultAbility("dash", ActionStat.TranscendStrain, 0.25f),
                new EtchingDefinition { Id = "avidous_march", Name = "Avidous March", Description = "Gain a burst of 100% increased movespeed at the start of each unique move action that decays to 25% over 0.75 seconds." }
                    .WithTrait(EtchingTraits.AvidousMarch, new BehaviorParam("start_bonus", 1f), new BehaviorParam("end_bonus", 0.25f), new BehaviorParam("seconds", 0.75f)),
                new EtchingDefinition { Id = "desecrate_your_being", Name = "Desecrate Your Being", Description = "Becoming Unraveled deals 35% maximum health damage to you but no longer stuns you. Instead, you deal 50% more damage for as long as the stun would have lasted." }
                    .WithTrait(EtchingTraits.DesecrateYourBeing, new BehaviorParam("health_share", 0.35f), new BehaviorParam("damage_bonus", 0.5f)),
                new EtchingDefinition { Id = "instinct_overtakes_methods", Name = "Instinct Overtakes Methods", Description = "Gain 50% Dodge. You now always automatically Dodge directly towards your target." }
                    .Add(StatId.Dodge, 0.5f).WithTrait(EtchingTraits.InstinctOvertakesMethods),
                new EtchingDefinition { Id = "lucid_dreams", Name = "Lucid Dreams", Description = "While you have Clear Mind, your ability cooldowns are also reduced by 0.1s every 0.25 seconds." }
                    .WithTrait(EtchingTraits.LucidDreams, new BehaviorParam("every", 0.25f), new BehaviorParam("cut", 0.1f)),
                new EtchingDefinition { Id = "eye_for_weakness", Name = "Eye For Weakness", Scope = EtchingScope.OneAbility, Description = "Etch onto a damaging ability with no Transcend: it has a 25% chance to critically strike for 75% more damage. It can be Transcended for +10 strain, raising that chance to 50%." }
                    .AddAbility(ActionStat.CritChance, 0.25f).AddAbility(ActionStat.CritDamage, 0.75f)
                    .GrantsTranscend(TranscendGrant.CritChance, 10f, new BehaviorParam("crit_chance", 0.5f)),
                new EtchingDefinition { Id = "gaps_in_their_mind", Name = "Gaps In Their Mind", Scope = EtchingScope.OneAbility, Description = "Etch onto a damaging ability: it exposes 1 weakpoint on enemies it hits (copies add up)." }
                    .AddAbility(ActionStat.Weakpoints, 1f),
                new EtchingDefinition { Id = "brute_force", Name = "Brute Force", Scope = EtchingScope.OneAbility, Description = "Etch onto a damaging ability with no Transcend: it can be Transcended for +15 strain, breaking up to 2 weakpoints on each enemy it hits, from any direction." }
                    .GrantsTranscend(TranscendGrant.BreakWeakpoints, 15f, new BehaviorParam("breaks", 2f)),
                new EtchingDefinition { Id = "unwind_their_self", Name = "Unwind Their Self", Scope = EtchingScope.OneAbility, Description = "Etch onto a damaging ability with no Transcend: it can be Transcended for +30 strain, exposing 4 weakpoints on enemies it hits for 3 seconds." }
                    .GrantsTranscend(TranscendGrant.ExposeWeakpoints, 30f, new BehaviorParam("count", 4f), new BehaviorParam("seconds", 3f)),
                new EtchingDefinition { Id = "sight_beyond_sight", Name = "Sight Beyond Sight", Description = "Every 1.5 seconds, the nearest enemy within 10m has a weakpoint exposed." }
                    .WithTrait(EtchingTraits.SightBeyondSight, new BehaviorParam("every", 1.5f), new BehaviorParam("range", 10f)),
                new EtchingDefinition { Id = "incessant", Name = "Incessant", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability without a follow-up: for 1.5 seconds after it ends, you can re-cast it on the same target or location for +25 strain, ignoring its cooldown." }
                    .GrantsFollowupOf(FollowupGrant.Recast, 25f, new BehaviorParam("window", 1.5f)),
                new EtchingDefinition { Id = "related_concepts", Name = "Related Concepts", Scope = EtchingScope.TwoAbilities, Description = "Etch into two abilities to link them. When one lands, the other becomes a linked ability: used straight after it (walking and dashing aside), it has 50% shorter windup and recovery." }
                    .GrantsFollowupOf(FollowupGrant.Linked, 0f, new BehaviorParam("speed", 0.5f)),
                new EtchingDefinition { Id = "dissipation", Name = "Dissipation", Description = "Blocking costs +10 strain. Damage your Block stops reduces your strain by as much." }
                    .AddAbility("block", ActionStat.StrainOnSelf, 10f).WithTrait(EtchingTraits.Dissipation, new BehaviorParam("share", 1f)),
                new EtchingDefinition { Id = "acceleration_bypass", Name = "Acceleration Bypass", Description = "Your Dash is 50% faster. Transcended Dash becomes an instant blink." }
                    .WithTrait(EtchingTraits.AccelerationBypass, new BehaviorParam("speed", 0.5f)),
                new EtchingDefinition { Id = "steady_center", Name = "Steady Center", Description = "Passively recover 5 strain per second." }
                    .WithTrait(EtchingTraits.SteadyCenter, new BehaviorParam("per_second", 5f)),
                new EtchingDefinition { Id = "clarity_from_aggravation", Name = "Clarity From Aggravation", Description = "Taking damage makes you recover 2 strain every 0.1 seconds for 1 second." }
                    .WithTrait(EtchingTraits.ClarityFromAggravation, new BehaviorParam("strain", 2f), new BehaviorParam("every", 0.1f), new BehaviorParam("seconds", 1f)),
                new EtchingDefinition { Id = "hone_the_body", Name = "Hone The Body", Description = "Increase your maximum health by 50." }
                    .Add(StatId.MaxHealth, 50f),
                new EtchingDefinition { Id = "hone_the_mind", Name = "Hone The Mind", Description = "Increase your maximum strain by 50." }
                    .Add(StatId.MaxStrain, 50f),
                new EtchingDefinition { Id = "guarded_approach", Name = "Guarded Approach", Scope = EtchingScope.OneAbility, Description = "Etch onto an ability with no Transcend: it can be Transcended for +10 strain, giving you a 40 health shield for 1.5 seconds." }
                    .GrantsTranscend(TranscendGrant.ShieldOnCast, 10f, new BehaviorParam("shield", 40f), new BehaviorParam("seconds", 1.5f)),
            };
        }
    }
}
