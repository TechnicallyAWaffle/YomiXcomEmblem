using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>What an Etching goes on: the whole unit, one ability the player picks for it, or two (Related Concepts links
    /// them). New values go at the end: assets store the number.</summary>
    public enum EtchingScope { Unit, OneAbility, TwoAbilities }

    /// <summary>
    /// A Transcend an Etching gives one ability that has none of its own (EtchingDefinition.Grant): hold its key, pay the
    /// extra strain, and the ability gets this. UnstoppableWindup: Unstoppable while it winds up (Body On Strings).
    /// NoRecovery: no recovery after it (Uncloud The Mind). NoCooldown: it doesn't go on cooldown (Echo).
    /// DodgeOnCast: 'dodge' (0.75) more Dodge for 'seconds' (2) from when it starts (Flow State).
    /// QuickWindup: its windup x 'windup_mult' (0.25) and its recovery x 'recovery_mult' (1.25) (Quickness And Lethargy).
    /// CritChance: its chance to strike critically is at least 'crit_chance' (0.5) (Eye For Weakness; BattleSimulation.CritChanceFor).
    /// BreakWeakpoints: each hit breaks up to 'breaks' (2) of the target's weakpoints from any side (Brute Force).
    /// ExposeWeakpoints: each hit opens 'count' (4) weakpoints on an enemy for 'seconds' (3) (Unwind Their Self).
    /// ShieldOnCast: a shield of 'shield' (40) health for 'seconds' (1.5) from when it starts (Guarded Approach; StatusIds.Shielded).
    /// Their numbers are the Etching's Params. New values go at the end: assets store the number.
    /// </summary>
    public enum TranscendGrant { None, UnstoppableWindup, NoRecovery, NoCooldown, DodgeOnCast, QuickWindup, CritChance, BreakWeakpoints, ExposeWeakpoints, ShieldOnCast }

    /// <summary>A granted Transcend on one ability of a unit in battle: what it does, its extra strain, and its numbers
    /// (the Etching's Params).</summary>
    [System.Serializable]
    public struct GrantedTranscend
    {
        public TranscendGrant Kind; public float Strain; public List<BehaviorParam> Params;

        public float Param(string key, float fallback)
        {
            if (Params != null) foreach (var p in Params) if (p.Key == key) return p.Value;
            return fallback;
        }
    }

    /// <summary>
    /// A follow-up an Etching gives one ability that has none of its own (EtchingDefinition.GrantsFollowup).
    /// Recast (Incessant): once the ability is over (the unit is ready again), for 'window' (1.5s) it can be cast again,
    /// on the same target or point, for its strain plus the grant's Strain (25), ignoring its cooldown; the recast opens no
    /// window of its own.
    /// Linked (Related Concepts, on two abilities): once one of them lands, the other (PartnerId) is a linked ability: used
    /// straight after it (no time limit; walking and dashing aside), it winds up and recovers 'speed' (0.5) as long, and
    /// opens nothing back.
    /// See BattleSimulation.Recasts.cs. New values go at the end: assets store the number.
    /// </summary>
    public enum FollowupGrant { None, Recast, Linked }

    /// <summary>A granted follow-up on one ability of a unit in battle: what it is, its extra strain, its numbers.</summary>
    [System.Serializable]
    public struct GrantedFollowup
    {
        public FollowupGrant Kind; public float Strain; public List<BehaviorParam> Params;
        public string PartnerId;   // Linked: the other ability of the pair

        public float Param(string key, float fallback)
        {
            if (Params != null) foreach (var p in Params) if (p.Key == key) return p.Value;
            return fallback;
        }
    }

    /// <summary>
    /// A permanent passive a squad member can take between missions (ProgressionConfig.Etchings; see Progression). It can
    /// change the unit's stats (UnitModifiers), its abilities' numbers (ActionModifiers), give a rule of its own (Trait,
    /// with Params: see EtchingTraits) and pay ability points. Taking one twice stacks it: modifiers fold in again (two
    /// Multiply x0.7 are x0.49) and a Trait counts its copies.
    /// OneAbility: the ActionModifiers go on the ability picked for it, and only abilities that have one of the numbers
    /// they change can be picked (Stats.Has: no range Etching on an ability without a range).
    /// </summary>
    [System.Serializable]
    public class EtchingDefinition
    {
        public string Id = "etching";
        public string Name = "Etching";
        [TextArea(2, 4)] public string Description = "";
        [Tooltip("How likely it is to be among the ones offered, against the others' weights.")]
        [Min(0f)] public float Weight = 1f;
        public EtchingScope Scope = EtchingScope.Unit;
        public List<Modifier> UnitModifiers = new List<Modifier>();
        [Tooltip("Unit scope: an empty Action Id changes every ability. One Ability: the Action Id is ignored; it goes on the picked ability.")]
        public List<ActionModifier> ActionModifiers = new List<ActionModifier>();
        [Tooltip("A rule of its own (EtchingTraits): closing_windup, compiled. Empty: none.")]
        public string Trait = "";
        [Tooltip("The Trait's numbers, by key (any left out keep EtchingTraits' default).")]
        public List<BehaviorParam> Params = new List<BehaviorParam>();
        [Tooltip("One Ability: gives that ability a Transcend (it must have none of its own), for GrantStrain extra strain.")]
        public TranscendGrant Grant = TranscendGrant.None;
        public float GrantStrain = 15f;
        [Tooltip("One Ability: gives that ability a follow-up (it must have none of its own), for FollowupStrain extra strain. Its numbers are Params.")]
        public FollowupGrant GrantsFollowup = FollowupGrant.None;
        public float FollowupStrain = 25f;
        [Tooltip("One Ability: changes used instead of Action Modifiers when the ability has none of the numbers they change (Imprinted Memory: no strain cost, so it sheds strain instead).")]
        public List<ActionModifier> FallbackActionModifiers = new List<ActionModifier>();
        [Tooltip("Abilities the unit loses, by behavior id (Witless: block; Dance Between Their Rage: dash).")]
        public List<string> RemovesBehaviors = new List<string>();

        /// <summary>One Ability: can it go on 'a'? (The ability has at least one of the numbers it changes.)</summary>
        public bool CanEtch(ActionDefinition a)
        {
            if (Scope == EtchingScope.Unit || a == null) return false;
            if (GrantsFollowup == FollowupGrant.Linked) return CanLink(a);
            if (GrantsFollowup != FollowupGrant.None)
            {
                if (!CanGetFollowup(a)) return false;
                if (Grant == TranscendGrant.None) return true;
            }
            if (Grant != TranscendGrant.None)   // a Transcend only for one that has none, and that has what it changes
            {
                if (a.CanTranscend) return false;
                switch (Grant)
                {
                    case TranscendGrant.DodgeOnCast: return true;   // anything can be cast
                    case TranscendGrant.ShieldOnCast: return true;   // anything can be cast
                    case TranscendGrant.CritChance: return a.Damage > 0f;   // only what deals damage can strike critically
                    case TranscendGrant.BreakWeakpoints: case TranscendGrant.ExposeWeakpoints: return a.Damage > 0f;   // through its hits
                    case TranscendGrant.UnstoppableWindup: case TranscendGrant.QuickWindup: return Stats.Has(a, ActionStat.Windup);
                    case TranscendGrant.NoRecovery: return Stats.Has(a, ActionStat.Recovery);
                    default: return Stats.Has(a, ActionStat.Cooldown);
                }
            }
            if (FallbackActionModifiers != null && FallbackActionModifiers.Count > 0) return true;   // something for any ability
            foreach (var m in ActionModifiers) if (Stats.Has(a, m.Stat)) return true;
            return false;
        }

        /// <summary>
        /// Can 'a' be given a follow-up: it has none of its own (it opens no chain, isn't re-cast by Space, doesn't chain, isn't
        /// itself a stage that needs another to open it, and isn't always on), and it has a cooldown for the recast to skip.
        /// </summary>
        public static bool CanGetFollowup(ActionDefinition a)
        {
            if (a == null || a.InnatePassive || a.IsFollowup || a.ChainFollowups > 0 || a.IsCharge) return false;   // a charge is paid as it starts charging
            if (a.FollowUps != null && a.FollowUps.Exists(f => f != null)) return false;
            if (a.RequiresLink || (a.LinkedAbilities != null && a.LinkedAbilities.Exists(f => f != null))) return false;   // links of its own
            if (a.RequiredStatuses().Length > 0) return false;
            return Stats.Has(a, ActionStat.Cooldown);
        }

        /// <summary>Can 'a' be one of a linked pair (Related Concepts): anything used now, with a windup to shorten (not always on,
        /// not charged, not a follow-up stage another ability opens).</summary>
        public static bool CanLink(ActionDefinition a)
        {
            return a != null && !a.InnatePassive && !a.IsCharge && !a.RequiresLink && a.RequiredStatuses().Length == 0;
        }

        /// <summary>One Ability: the changes it makes to 'a': its Action Modifiers if 'a' has a number they change, else the fallback.</summary>
        public List<ActionModifier> ModifiersFor(ActionDefinition a)
        {
            if (a == null || FallbackActionModifiers == null || FallbackActionModifiers.Count == 0) return ActionModifiers;
            foreach (var m in ActionModifiers) if (Stats.Has(a, m.Stat)) return ActionModifiers;
            return FallbackActionModifiers;
        }

        // ---- authoring helpers
        public EtchingDefinition Add(StatId stat, float value) { UnitModifiers.Add(Modifier.Add(stat, value)); return this; }
        public EtchingDefinition Mult(StatId stat, float value) { UnitModifiers.Add(Modifier.Mult(stat, value)); return this; }
        public EtchingDefinition MultAbility(ActionStat stat, float value) { ActionModifiers.Add(new ActionModifier("", stat, ModOp.Multiply, value)); return this; }
        public EtchingDefinition MultAbility(string actionId, ActionStat stat, float value) { ActionModifiers.Add(new ActionModifier(actionId, stat, ModOp.Multiply, value)); return this; }
        public EtchingDefinition AddAbility(ActionStat stat, float value) { ActionModifiers.Add(new ActionModifier("", stat, ModOp.Add, value)); return this; }
        public EtchingDefinition AddAbility(string actionId, ActionStat stat, float value) { ActionModifiers.Add(new ActionModifier(actionId, stat, ModOp.Add, value)); return this; }
        public EtchingDefinition GrantsTranscend(TranscendGrant kind, float strain, params BehaviorParam[] ps) { Grant = kind; GrantStrain = strain; Params.AddRange(ps); return this; }
        public EtchingDefinition WithTrait(string trait, params BehaviorParam[] ps) { Trait = trait; Params.AddRange(ps); return this; }

        public EtchingDefinition Otherwise(ActionStat stat, ModOp op, float value) { FallbackActionModifiers.Add(new ActionModifier("", stat, op, value)); return this; }
        public EtchingDefinition Removes(string behaviorId) { RemovesBehaviors.Add(behaviorId); return this; }

        /// <summary>The Transcend it gives the ability it is on (Grant, GrantStrain, and its Params for the numbers).</summary>
        public GrantedTranscend GrantFor() { return new GrantedTranscend { Kind = Grant, Strain = GrantStrain, Params = Params }; }

        public EtchingDefinition GrantsFollowupOf(FollowupGrant kind, float strain, params BehaviorParam[] ps) { GrantsFollowup = kind; FollowupStrain = strain; Params.AddRange(ps); return this; }

        /// <summary>The follow-up it gives the ability it is on (GrantsFollowup, FollowupStrain, and its Params).</summary>
        /// <param name="partnerId">Linked: the other ability of the pair.</param>
        public GrantedFollowup FollowupFor(string partnerId = null) { return new GrantedFollowup { Kind = GrantsFollowup, Strain = FollowupStrain, Params = Params, PartnerId = partnerId }; }
    }

    /// <summary>An Etching a member took (and, for a One Ability one, the ability it is on).</summary>
    [System.Serializable]
    public class EtchingPick
    {
        public string Id;
        public string ActionId;   // One Ability: the ability's id; else empty
        public string SecondActionId;   // Two Abilities: the other ability's id; else empty
        public bool GrantsTranscend;   // it gave that ability a Transcend (no other Etching may give it one)
        public bool GrantsFollowup;    // it gave that ability a follow-up (no other Etching may give it one)
        public int Grasp;              // the Grasp it uses (Progression.EtchingPrice when taken)
    }

    /// <summary>One copy of an Etching's Trait on a unit in battle, with that Etching's numbers.</summary>
    public class UnitTrait
    {
        public string Id;
        public List<BehaviorParam> Params;

        public float Param(string key, float fallback)
        {
            if (Params != null) foreach (var p in Params) if (p.Key == key) return p.Value;
            return fallback;
        }
    }

    /// <summary>
    /// Copies of the same Etching, listed once with how many there are (the screens show "x2", "x3"): the same Etching on
    /// the same ability (or abilities) counts as one entry. First-seen order is kept.
    /// </summary>
    public static class EtchingStacks
    {
        public static List<KeyValuePair<EtchingNote, int>> Of(IEnumerable<EtchingNote> notes)
        {
            var list = new List<KeyValuePair<EtchingNote, int>>();
            if (notes == null) return list;
            foreach (var n in notes)
            {
                int i = list.FindIndex(p => p.Key.Name == n.Name && (p.Key.ActionId ?? "") == (n.ActionId ?? ""));
                if (i >= 0) list[i] = new KeyValuePair<EtchingNote, int>(list[i].Key, list[i].Value + 1);
                else list.Add(new KeyValuePair<EtchingNote, int>(n, 1));
            }
            return list;
        }

        public static List<KeyValuePair<EtchingPick, int>> Of(IEnumerable<EtchingPick> picks)
        {
            var list = new List<KeyValuePair<EtchingPick, int>>();
            if (picks == null) return list;
            foreach (var p in picks)
            {
                int i = list.FindIndex(q => q.Key.Id == p.Id && (q.Key.ActionId ?? "") == (p.ActionId ?? "") && (q.Key.SecondActionId ?? "") == (p.SecondActionId ?? ""));
                if (i >= 0) list[i] = new KeyValuePair<EtchingPick, int>(list[i].Key, list[i].Value + 1);
                else list.Add(new KeyValuePair<EtchingPick, int>(p, 1));
            }
            return list;
        }

        /// <summary>The count's tag after a name: "  x2" for two or more copies, nothing for one.</summary>
        public static string Times(int count) { return count > 1 ? "  x" + count : ""; }
    }

    /// <summary>An Etching on a unit, for the screen (the ability popup lists the ones on that ability).</summary>
    public struct EtchingNote
    {
        public string Name, Description;
        public string ActionId;          // on one ability; empty = on the unit
        public bool ChangesAbilities;    // a unit one that changes every ability's numbers or windups (The Mind's Reach)
    }

    /// <summary>What a unit has grown into (a member's Etchings, see Progression.Growth): handed to Unit.FromDefinition.</summary>
    public class UnitGrowth
    {
        public List<Modifier> UnitModifiers = new List<Modifier>();
        public List<ActionModifier> ActionModifiers = new List<ActionModifier>();
        public List<UnitTrait> Traits = new List<UnitTrait>();
        public List<EtchingNote> Notes = new List<EtchingNote>();
        public Dictionary<string, GrantedTranscend> Transcends = new Dictionary<string, GrantedTranscend>();   // by ability id
        public Dictionary<string, GrantedFollowup> Followups = new Dictionary<string, GrantedFollowup>();     // by ability id
        public List<string> RemovedBehaviors = new List<string>();
        public List<StatId> LockedStats = new List<StatId>();   // Perpetuated: no modifier from growth or upgrades changes these
    }

    /// <summary>
    /// The Etchings' rules of their own (EtchingDefinition.Trait), each read off the copies a unit has (Unit.Traits):
    ///   closing_windup (Senses of Closure): a targeted ability winds up faster the closer the target is when the windup
    ///                  starts: by max_bonus (0.5) at point-blank, nothing at the edge of its range. Copies multiply.
    ///   compiled (Persistent Algorithms): paying strain for an ability gives a stack of Compiled for 'seconds' (1; each
    ///                  new one restarts the timer), up to max_stacks (5). Each stack cuts strain costs by per_stack (0.1,
    ///                  added up over copies), at most max_reduction (0.5).
    ///   revenge_maxim (Revenge Maxim): once a Transcended Block ends (the unit is ready again), Revenge for 'seconds' (1):
    ///                  the next ability started in that time spends it and winds up windup_mult (0.25) as long.
    ///   suffer_beyond (Suffer Beyond Your Limits): strain may be spent at or past max strain; a cost that goes past max
    ///                  costs health_share (0.6) of it in health as well, per_copy (0.2) less for each copy past the first.
    ///   perception_for_violence (Perception for Violence): windups aimed at a stunned enemy are 'bonus' (0.3) faster.
    ///                  Copies multiply.
    ///   cannibalize_cognition (Cannibalize Cognition): a killing blow sheds 'relief' (40) strain. Copies add up.
    ///   imperceptible_steps (Imperceptible Steps): a dodge has 'chance' (0.5; copies combine) to break every ranged attack
    ///                  winding up at the unit.
    ///   repetition_is_strength (Repetition Is Strength): walking without a break adds speed_per_second (0.25, copies add
    ///                  up) of move speed per second walked, counted up to max_seconds (8); after unstoppable_after (2,
    ///                  30% less per extra copy: 'faster') it is Unstoppable until it stops. A pause under 'grace' (0.15s)
    ///                  between walks doesn't count as stopping.
    ///   untethered_to_sanity (Untethered To Sanity): an Unravel stuns for 'stun_mult' (0.25) as long. Copies multiply.
    ///   desecrate_your_being (Desecrate Your Being): an Unravel doesn't stun; it costs 'health_share' (0.35) of max health
    ///                  instead (it can kill; copies add up), and for as long as the stun would have lasted the unit deals
    ///                  'damage_bonus' (0.5: +50%) more damage (DesecratedStatus; copies multiply, as Etchings' modifiers do).
    ///   avidous_march (Avidous March): each new walk (a move order, or a chase starting) begins with 'start_bonus' (1:
    ///                  +100%) more move speed, falling to 'end_bonus' (0.25) over 'seconds' (0.75), then gone; it ends
    ///                  with the walk. Copies add up.
    ///   instinct_overtakes_methods (Instinct Overtakes Methods): a dodge always dashes straight at the unit's own target
    ///                  (what it is winding up at or chasing, else whoever it dodged), to arm's length; no choosing.
    ///   dissipation (Dissipation): damage the unit's Block stops sheds that much strain ('share' 1 of it; copies add up).
    ///   acceleration_bypass (Acceleration Bypass): its Dash is 'speed' (0.5: +50%) faster (copies add up); Transcended, an
    ///                  instant blink there instead.
    ///   steady_center (Steady Center): it sheds 'per_second' (5) strain a second, always (copies add up).
    ///   clarity_from_aggravation (Clarity From Aggravation): damage taken sheds 'strain' (2) every 'every' (0.1s) for
    ///                  'seconds' (1) (ClarityStatus, restarted by each hit; copies add up).
    ///   lucid_dreams (Lucid Dreams): in Clear Mind, every 'every' (0.25s) its cooldowns tick 'cut' (0.1s, copies add up)
    ///                  further.
    /// </summary>
    public static class EtchingTraits
    {
        public const string UntetheredToSanity = "untethered_to_sanity";
        public const string DesecrateYourBeing = "desecrate_your_being";
        public const string AvidousMarch = "avidous_march", MarchStatus = "avidous_march";
        public const string InstinctOvertakesMethods = "instinct_overtakes_methods";
        public const string LucidDreams = "lucid_dreams";
        public const string Dissipation = "dissipation";
        public const string AccelerationBypass = "acceleration_bypass";
        public const string SteadyCenter = "steady_center";
        public const string ClarityFromAggravation = "clarity_from_aggravation";
        public const string SightBeyondSight = "sight_beyond_sight";
        public const string FlowStateStatus = "flow_state";
        public const string DesecratedStatus = "desecrated";
        public const string ClosingWindup = "closing_windup";
        public const string Compiled = "compiled";
        public const string CompiledStatus = "compiled";
        public const string RevengeMaxim = "revenge_maxim";
        public const string SufferBeyond = "suffer_beyond";
        public const string PerceptionForViolence = "perception_for_violence";
        public const string CannibalizeCognition = "cannibalize_cognition";
        public const string ImperceptibleSteps = "imperceptible_steps";
        public const string RepetitionIsStrength = "repetition_is_strength";
        public const string MomentumStatus = "momentum", StrideStatus = "unstoppable_stride";
        public const string FracturesOfStress = "fractures_of_stress", FracturedStatus = "fractured";
        public const string Overburdened = "overburdened";   // more Grasp used than held (Progression.Growth): 'per_second' strain, always

        public static void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.StrainSpent, ctx => AddCompiled(sim, ctx.Subject));
            sim.Triggers.Subscribe(TriggerType.UnitReady, ctx => GrantRevenge(sim, ctx.Subject));
            sim.Triggers.Subscribe(TriggerType.UnitReady, ctx => sim.OpenRecast(ctx.Subject));   // Incessant (BattleSimulation.Recasts.cs)
            sim.Triggers.Subscribe(TriggerType.Killed, ctx => Cannibalize(sim, ctx.Other));
            sim.Triggers.Subscribe(TriggerType.Dodged, ctx => SlipAway(sim, ctx.Subject));
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx => Fracture(sim, ctx.Subject, ctx.Amount));
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx => Aggravate(sim, ctx.Subject, ctx.Amount));
        }

        /// <summary>Every tick: the traits that run over time (Repetition Is Strength, Avidous March, Lucid Dreams).</summary>
        public static void Tick(BattleSimulation sim)
        {
            TickMomentum(sim);
            TickMarch(sim);
            TickLucidDreams(sim);
            TickSight(sim);
            TickSteadyCenter(sim);
            TickClarity(sim);
            TickOverburdened(sim);
        }

        /// <summary>Overburdened: strain builds on its own, 'per_second' (every copy's), against its recovery.</summary>
        private static void TickOverburdened(BattleSimulation sim)
        {
            if (!sim.IsStep(BattleSimulation.RateTicks)) return;
            float step = BattleSimulation.RateTicks * BattleSimulation.TickLength;
            foreach (var u in sim.State.Units)
            {
                if (!u.IsAlive || u.Traits.Count == 0) continue;
                float gain = Sum(u, Overburdened, "per_second", 0f) * step;
                if (gain > 0f) sim.AddStrain(u, gain, false);   // a drain on top: recovery still runs
            }
        }

        // ---- Steady Center, Clarity From Aggravation, Dissipation, Acceleration Bypass

        /// <summary>What every copy of trait 'id' on 'u' adds up to for 'key' (0 without it).</summary>
        private static float Sum(Unit u, string id, string key, float fallback)
        {
            float total = 0f;
            foreach (var t in u.Traits) if (t.Id == id) total += t.Param(key, fallback);
            return total;
        }

        private const string ClarityNext = "clarity_next", ClarityLeft = "clarity_left";

        /// <summary>Clarity From Aggravation's clock: each 'every' that comes round, 'strain' (every copy's) comes off.</summary>
        private static void TickClarity(BattleSimulation sim)
        {
            foreach (var u in sim.State.Units)
            {
                float left, next;
                if (u.Traits.Count == 0 || !u.Timers.TryGetValue(ClarityLeft, out left) || left <= 0f) continue;
                if (!u.IsAlive) { u.Timers.Remove(ClarityLeft); continue; }
                if (!u.Timers.TryGetValue(ClarityNext, out next) || sim.Now + 1e-4f < next) continue;
                var first = First(u, ClarityFromAggravation);
                float every = Mathf.Max(BattleSimulation.TickLength, first != null ? first.Param("every", 0.1f) : 0.1f);
                float shed = Sum(u, ClarityFromAggravation, "strain", 2f);
                if (shed > 0f && u.Strain > 0f) sim.AddStrain(u, -Mathf.Min(shed, u.Strain));
                u.Timers[ClarityLeft] = left - 1f;
                u.Timers[ClarityNext] = next + every;
            }
        }

        private static void TickSteadyCenter(BattleSimulation sim)
        {
            if (!sim.IsStep(BattleSimulation.RateTicks)) return;
            float step = BattleSimulation.RateTicks * BattleSimulation.TickLength;
            foreach (var u in sim.State.Units)
            {
                if (!u.IsAlive || u.Traits.Count == 0 || u.Strain <= 0f) continue;
                float shed = Sum(u, SteadyCenter, "per_second", 5f) * step;
                if (shed > 0f) sim.AddStrain(u, -Mathf.Min(shed, u.Strain));
            }
        }

        /// <summary>Clarity From Aggravation: a hit that hurt starts (or restarts) the strain shedding.</summary>
        private static void Aggravate(BattleSimulation sim, Unit u, float dealt)
        {
            if (u == null || !u.IsAlive || u.Traits.Count == 0 || dealt <= 0f) return;
            var first = First(u, ClarityFromAggravation);
            if (first == null) return;
            // Its own clock: 'strain' every 'every' for 'seconds', counted out (the status only shows it), restarted by each hit.
            float every = Mathf.Max(BattleSimulation.TickLength, first.Param("every", 0.1f)), seconds = Mathf.Max(every, first.Param("seconds", 1f));
            u.Timers[ClarityNext] = sim.Now + every;
            u.Timers[ClarityLeft] = Mathf.Round(seconds / every);
            sim.ApplyStatus(u, new StatusEffect { Id = StatusIds.Clarity, SourceId = u.Id, ExpiresAt = sim.Now + seconds + BattleSimulation.TickLength }, u);
        }

        /// <summary>Dissipation: 'blocked' damage its Block stopped sheds that much strain.</summary>
        public static void Dissipate(BattleSimulation sim, Unit u, float blocked)
        {
            if (u == null || !u.IsAlive || u.Traits.Count == 0 || blocked <= 0f) return;
            float share = Sum(u, Dissipation, "share", 1f);
            if (share > 0f) sim.AddStrain(u, -Mathf.Min(blocked * share, u.Strain));
        }

        /// <summary>Acceleration Bypass: what a Dash's time is divided by (1 without it).</summary>
        public static float DashSpeedup(Unit u) { return u.Traits.Count == 0 ? 1f : 1f + Mathf.Max(0f, Sum(u, AccelerationBypass, "speed", 0.5f)); }

        /// <summary>Acceleration Bypass: is a Transcended Dash an instant blink?</summary>
        public static bool DashBlinks(Unit u) { return u.Traits.Count > 0 && First(u, AccelerationBypass) != null; }

        // ---- Sight Beyond Sight

        /// <summary>Sight Beyond Sight: every 'every' (1.5s) the nearest enemy within 'range' (10m) gets a weakpoint (one per
        /// copy), opened on its far side from the unit.</summary>
        private static void TickSight(BattleSimulation sim)
        {
            foreach (var u in sim.State.Units.ToArray())
            {
                if (u.Traits.Count == 0 || !u.IsAlive) continue;
                var first = First(u, SightBeyondSight);
                if (first == null) continue;
                int every = Mathf.Max(1, Mathf.RoundToInt(first.Param("every", 1.5f) / BattleSimulation.TickLength));
                if (!sim.IsStep(every)) continue;
                int count = 0;
                foreach (var t in u.Traits) if (t.Id == SightBeyondSight) count++;
                float range = first.Param("range", 10f);
                Unit near = null; float best = range;
                foreach (var e in sim.EnemiesOf(u))
                {
                    Vector3 d = e.Position - u.Position; d.y = 0f;
                    if (d.magnitude <= best) { best = d.magnitude; near = e; }
                }
                if (near != null) sim.AddWeakpoints(near, count, near.Position - u.Position, 0f, u);
            }
        }

        // ---- Untethered To Sanity, Desecrate Your Being

        /// <summary>What an Unravel's stun on 'u' is scaled by (Untethered To Sanity; 1 without it).</summary>
        public static float UnravelStunScale(Unit u)
        {
            float scale = 1f;
            foreach (var t in u.Traits) if (t.Id == UntetheredToSanity) scale *= Mathf.Clamp01(t.Param("stun_mult", 0.25f));
            if (u.Passives.Contains(DeliverancePassives.UnravelResistance)) scale *= Mathf.Clamp01(DeliverancePassives.UnravelResistanceScale);
            return scale;
        }

        /// <summary>
        /// Desecrate Your Being, as 'u' Unravels (its stun would last 'stunSeconds'): the health instead of the stun, and the
        /// damage surge for that long. False without it (the Unravel stuns as usual).
        /// </summary>
        public static bool Desecrate(BattleSimulation sim, Unit u, float stunSeconds)
        {
            float share = DesecrateShare(u);
            if (share <= 0f) return false;
            float mult = 1f;
            foreach (var t in u.Traits) if (t.Id == DesecrateYourBeing) mult *= 1f + Mathf.Max(0f, t.Param("damage_bonus", 0.5f));
            if (stunSeconds > 0f && mult > 1f + 1e-4f)
                sim.ApplyStatus(u, new StatusEffect { Id = DesecratedStatus, SourceId = u.Id, ExpiresAt = sim.Now + stunSeconds, Modifiers = new List<Modifier> { Modifier.Mult(StatId.DamageDealt, mult) } }, u);
            sim.LoseHealth(u, share * u.MaxHealth, DesecrateYourBeing);
            return true;
        }

        /// <summary>Desecrate Your Being: the share of max health an Unravel costs 'u' in place of its stun (0: it stuns as usual).</summary>
        public static float DesecrateShare(Unit u)
        {
            float share = 0f;
            foreach (var t in u.Traits) if (t.Id == DesecrateYourBeing) share += Mathf.Max(0f, t.Param("health_share", 0.35f));
            return share;
        }

        // ---- Avidous March

        /// <summary>A new walk starts (a move order, a chase): with Avidous March, the burst of speed (MarchStatus) begins.</summary>
        public static void BeginMarch(BattleSimulation sim, Unit u)
        {
            if (u == null || u.Traits.Count == 0) return;
            var first = First(u, AvidousMarch);
            if (first == null) return;
            float start, end;
            MarchBonus(u, out start, out end);
            float seconds = Mathf.Max(BattleSimulation.TickLength, first.Param("seconds", 0.75f));
            sim.ApplyStatus(u, new StatusEffect { Id = MarchStatus, SourceId = u.Id, ExpiresAt = sim.Now + seconds, Modifiers = new List<Modifier> { Modifier.Mult(StatId.MoveSpeed, 1f + start) } }, u);
        }

        /// <summary>The burst's bonus at its start and at its end, over every copy.</summary>
        private static void MarchBonus(Unit u, out float start, out float end)
        {
            start = end = 0f;
            foreach (var t in u.Traits)
            {
                if (t.Id != AvidousMarch) continue;
                start += t.Param("start_bonus", 1f);
                end += t.Param("end_bonus", 0.25f);
            }
        }

        /// <summary>Steps between Avidous March's speed updates (each restarts the walk at the new speed for the view).</summary>
        public const int MarchStepTicks = 3;

        private static void TickMarch(BattleSimulation sim)
        {
            bool step = sim.IsStep(MarchStepTicks);
            foreach (var u in sim.State.Units)
            {
                if (u.Traits.Count == 0) continue;
                var s = u.GetStatus(MarchStatus);
                if (s == null) continue;
                if (!u.IsAlive || u.Phase != UnitPhase.Moving) { if (u.IsAlive) sim.RemoveStatus(u, MarchStatus); continue; }   // the burst ends with the walk
                if (!step) continue;
                var first = First(u, AvidousMarch);
                float seconds = Mathf.Max(BattleSimulation.TickLength, first != null ? first.Param("seconds", 0.75f) : 0.75f);
                float start, end;
                MarchBonus(u, out start, out end);
                float t = Mathf.Clamp01(1f - (s.ExpiresAt - sim.Now) / seconds);
                sim.RetuneStatus(u, s, new List<Modifier> { Modifier.Mult(StatId.MoveSpeed, 1f + Mathf.Lerp(start, end, t)) });
            }
        }

        // ---- Instinct Overtakes Methods

        /// <summary>Does 'u' dodge on instinct (straight at its target, never choosing)?</summary>
        public static bool DodgesOnInstinct(Unit u) { return u.Traits.Count > 0 && First(u, InstinctOvertakesMethods) != null; }

        // ---- Lucid Dreams

        private static void TickLucidDreams(BattleSimulation sim)
        {
            foreach (var u in sim.State.Units)
            {
                if (u.Traits.Count == 0 || !u.IsAlive || !u.HasStatus(StatusIds.ClearMind)) continue;
                var first = First(u, LucidDreams);
                if (first == null) continue;
                int every = Mathf.Max(1, Mathf.RoundToInt(first.Param("every", 0.25f) / BattleSimulation.TickLength));
                if (!sim.IsStep(every)) continue;
                float cut = 0f;
                foreach (var t in u.Traits) if (t.Id == LucidDreams) cut += Mathf.Max(0f, t.Param("cut", 0.1f));
                if (cut > 0f) sim.CutCooldowns(u, cut);
            }
        }

        // ---- Cannibalize Cognition

        private static void Cannibalize(BattleSimulation sim, Unit killer)
        {
            if (killer == null || !killer.IsAlive || killer.Traits.Count == 0) return;
            float relief = 0f;
            foreach (var t in killer.Traits) if (t.Id == CannibalizeCognition) relief += t.Param("relief", 40f);
            if (relief > 0f) sim.AddStrain(killer, -relief);
        }

        // ---- Fractures Of Stress

        /// <summary>
        /// A hit took 'u' from at least 'below' (30%) of its health to under it, and it lived: it becomes Unstoppable and heals
        /// 'heal_share' of its max health over 'seconds' (FracturedStatus). Copies add their heal. Once per battle
        /// ('once_per_battle' 1; 0: every time it falls back under).
        /// </summary>
        private static void Fracture(BattleSimulation sim, Unit u, float dealt)
        {
            if (u == null || !u.IsAlive || u.Traits.Count == 0 || dealt <= 0f) return;
            var first = First(u, FracturesOfStress);
            if (first == null) return;
            float line = Mathf.Clamp01(first.Param("below", 0.3f)) * u.MaxHealth;
            if (u.Health >= line || u.Health + dealt < line) return;   // not crossing it on this hit
            bool once = first.Param("once_per_battle", 1f) > 0f;
            if (once && u.SpentOnce.Contains(FracturesOfStress)) return;   // already used this battle
            if (once) u.SpentOnce.Add(FracturesOfStress);
            float share = 0f;
            foreach (var t in u.Traits) if (t.Id == FracturesOfStress) share += t.Param("heal_share", 0.3f);
            float seconds = Mathf.Max(0.1f, first.Param("seconds", 3f));
            sim.ApplyStatus(u, new StatusEffect
            {
                Id = FracturedStatus, SourceId = u.Id, ExpiresAt = sim.Now + seconds, Flags = StatusFlags.Unstoppable,
                HealPerSecond = share * u.MaxHealth / seconds,
            }, u);
        }

        // ---- Imperceptible Steps

        private static void SlipAway(BattleSimulation sim, Unit u)
        {
            if (u == null || !u.IsAlive || u.Traits.Count == 0) return;
            float miss = 1f;   // the chance every copy fails
            foreach (var t in u.Traits) if (t.Id == ImperceptibleSteps) miss *= 1f - Mathf.Clamp01(t.Param("chance", 0.5f));
            if (miss >= 1f || sim.RandomValue() < miss) return;
            foreach (var e in sim.State.Units.ToArray())
            {
                var a = e.ActiveAction;
                if (!e.IsAlive || e.Team == u.Team || a == null || a.TargetUnitId != u.Id || !a.Definition.IsRanged) continue;
                if (e.Phase == UnitPhase.Windup || e.Phase == UnitPhase.Charging) sim.InterruptUnit(e, "lost its mark");
            }
        }

        // ---- Repetition Is Strength

        /// <summary>Each tick: walking units with Repetition Is Strength build speed, and in time become Unstoppable.</summary>
        public static void TickMomentum(BattleSimulation sim)
        {
            foreach (var u in sim.State.Units)
            {
                if (u.Traits.Count == 0) continue;
                int copies = 0;
                UnitTrait first = null;
                foreach (var t in u.Traits) if (t.Id == RepetitionIsStrength) { copies++; if (first == null) first = t; }
                if (copies == 0) continue;
                bool walking = u.IsAlive && u.Phase == UnitPhase.Moving;
                if (!walking)
                {
                    if (u.MovingSince < 0f) continue;
                    if (u.StillSince < 0f) u.StillSince = sim.Now;
                    if (sim.Now - u.StillSince < first.Param("grace", 0.15f) && u.IsAlive) continue;
                    u.MovingSince = u.StillSince = -1f;
                    if (u.HasStatus(MomentumStatus)) sim.RemoveStatus(u, MomentumStatus);
                    if (u.HasStatus(StrideStatus)) sim.RemoveStatus(u, StrideStatus);
                    continue;
                }
                u.StillSince = -1f;
                if (u.MovingSince < 0f) u.MovingSince = sim.Now;
                float walked = Mathf.Min(sim.Now - u.MovingSince, first.Param("max_seconds", 8f));
                if (sim.IsStep(BattleSimulation.RateTicks))
                {
                    float bonus = first.Param("speed_per_second", 0.25f) * copies * walked;
                    if (bonus > 0.01f)
                        sim.ApplyStatus(u, new StatusEffect { Id = MomentumStatus, SourceId = u.Id, ExpiresAt = float.MaxValue, Modifiers = new List<Modifier> { Modifier.Add(StatId.MoveSpeed, bonus) } }, u);
                }
                float after = first.Param("unstoppable_after", 2f) * Mathf.Pow(1f - Mathf.Clamp01(first.Param("faster", 0.3f)), copies - 1);
                if (sim.Now - u.MovingSince >= after - 1e-4f && !u.HasStatus(StrideStatus))
                    sim.ApplyStatus(u, new StatusEffect { Id = StrideStatus, SourceId = u.Id, ExpiresAt = float.MaxValue, Flags = StatusFlags.Unstoppable }, u);
            }
        }

        /// <summary>The first copy of trait 'id' on 'u' (its numbers), or null.</summary>
        private static UnitTrait First(Unit u, string id) { return u.Traits.Find(t => t.Id == id); }

        // ---- Perception for Violence

        /// <summary>What a windup aimed at 'target' is scaled by (Perception for Violence): faster against a stunned one.</summary>
        public static float PerceptionScale(Unit actor, Unit target)
        {
            if (actor.Traits.Count == 0 || target == null || target.Phase != UnitPhase.Stunned) return 1f;
            float scale = 1f;
            foreach (var t in actor.Traits) if (t.Id == PerceptionForViolence) scale *= 1f - Mathf.Clamp01(t.Param("bonus", 0.3f));
            return scale;
        }

        // ---- Revenge Maxim

        /// <summary>A Transcended Block started: with Revenge Maxim, Revenge comes when it ends (GrantRevenge).</summary>
        public static void PrimeRevenge(BattleSimulation sim, Unit u)
        {
            if (First(u, RevengeMaxim) == null) return;
            sim.ApplyStatus(u, new StatusEffect { Id = StatusIds.RevengePrimed, SourceId = u.Id, ExpiresAt = sim.Now + 30f }, u);
        }

        private static void GrantRevenge(BattleSimulation sim, Unit u)
        {
            var t = First(u, RevengeMaxim);
            if (t == null || !u.HasStatus(StatusIds.RevengePrimed)) return;
            sim.RemoveStatus(u, StatusIds.RevengePrimed);
            sim.ApplyStatus(u, new StatusEffect { Id = StatusIds.Revenge, SourceId = u.Id, ExpiresAt = sim.Now + t.Param("seconds", 1f) }, u);
        }

        /// <summary>What the next windup is scaled by while Revenge is up (1 without it).</summary>
        public static float RevengeScale(Unit u)
        {
            if (!u.HasStatus(StatusIds.Revenge)) return 1f;
            var t = First(u, RevengeMaxim);
            return Mathf.Clamp01(t != null ? t.Param("windup_mult", 0.25f) : 0.25f);
        }

        /// <summary>An ability started: it spends a Revenge waiting for it.</summary>
        public static void SpendRevenge(BattleSimulation sim, Unit u)
        {
            if (u.HasStatus(StatusIds.Revenge)) sim.RemoveStatus(u, StatusIds.Revenge);
        }

        // ---- Suffer Beyond Your Limits

        /// <summary>Can 'u' spend strain past its max (Suffer Beyond Your Limits)?</summary>
        public static bool SuffersPastMax(Unit u) { return u.Traits.Count > 0 && First(u, SufferBeyond) != null; }

        /// <summary>
        /// Paying 'cost' strain for 'def', before it lands: if it goes past max and the unit Suffers Beyond Its Limits, it
        /// loses health_share of the cost in health (it can die of it). Abilities that already allow it (Block, Praa's Truth)
        /// keep their own rule.
        /// </summary>
        public static void SufferPastMax(BattleSimulation sim, Unit u, ActionDefinition def, float cost)
        {
            if (cost <= 0f || u.Strain + cost <= u.MaxStrain + 1e-4f || !SuffersPastMax(u)) return;
            if (def.UnravelsOverMaxStrain || ActionBehaviors.Get(def.BehaviorId).AllowsStrainOverflow) return;
            float share = 0f, less = 0f;
            int copies = 0;
            foreach (var t in u.Traits)
            {
                if (t.Id != SufferBeyond) continue;
                copies++;
                share = Mathf.Max(share, t.Param("health_share", 0.6f));
                less = Mathf.Max(less, t.Param("per_copy", 0.2f));
            }
            share = Mathf.Max(0f, share - less * (copies - 1));
            sim.LoseHealth(u, cost * share, def.Id);
        }

        /// <summary>What the windup of 'def' aimed at 'targetPoint' is scaled by (Senses of Closure): 1 = unchanged.</summary>
        public static float WindupScale(Unit actor, ActionDefinition def, Vector3 targetPoint)
        {
            if (actor.Traits.Count == 0 || def == null) return 1f;
            if (def.Targeting == TargetType.Self || def.Targeting == TargetType.None) return 1f;
            float range = actor.ActionStat(def, ActionStat.Range);
            if (range <= 0f) return 1f;
            Vector3 d = targetPoint - actor.Position; d.y = 0f;
            float close = 1f - Mathf.Clamp01(d.magnitude / range);
            float scale = 1f;
            foreach (var t in actor.Traits)
                if (t.Id == ClosingWindup) scale *= 1f - Mathf.Clamp01(t.Param("max_bonus", 0.5f)) * close;
            return scale;
        }

        private static void AddCompiled(BattleSimulation sim, Unit u)
        {
            float perStack = 0f, cap = 0f, seconds = 0f;
            int max = 0;
            foreach (var t in u.Traits)
            {
                if (t.Id != Compiled) continue;
                perStack += t.Param("per_stack", 0.1f);
                cap = Mathf.Max(cap, t.Param("max_reduction", 0.5f));
                seconds = Mathf.Max(seconds, t.Param("seconds", 1f));
                max = Mathf.Max(max, Mathf.RoundToInt(t.Param("max_stacks", 5f)));
            }
            if (max <= 0 || seconds <= 0f) return;
            var old = u.GetStatus(CompiledStatus);
            int stacks = Mathf.Min(max, (old != null ? old.Stacks : 0) + 1);
            var s = new StatusEffect
            {
                Id = CompiledStatus, SourceId = u.Id, ExpiresAt = sim.Now + seconds,
                Modifiers = new List<Modifier> { Modifier.Mult(StatId.StrainCost, 1f - Mathf.Min(cap, perStack * stacks)) },
            };
            sim.ApplyStatus(u, s, u);
            sim.SetStacks(u, s, stacks);
        }
    }
}
