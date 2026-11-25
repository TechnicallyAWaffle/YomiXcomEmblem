using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>A running action: windup in progress (or a free reaction action).</summary>
    public class ActionInstance
    {
        public Unit Actor;
        public ActionDefinition Definition;
        public ActionBehavior Behavior;
        public string TargetUnitId;
        public Vector3 TargetPos;
        public float StartedAt;
        public float ResolveAt;
        public bool IsFree;     // reaction actions don't change the actor's phase
        public float DamageMultiplier = 1f;
        public string Tag;      // behavior bookkeeping, copied onto the hits it makes (e.g. Final Cut follow-ups)
        public float Charge;    // 0..1: how fully a charge ability was charged before release
        public bool Transcended;   // ordered Transcended (and the definition allows it): the behavior's stronger version
        public bool UnravelsActor; // its strain cost took the actor past max (ActionDefinition.UnravelsOverMaxStrain): it Unravels once this lands
        // Behavior bookkeeping for actions that keep going after they land (a channel's strikes, a pull): a count and a time.
        public int Steps;
        public float NextStepAt;
        public bool OwnsHover;               // it lifted its user into the air itself (the hover ends with it)
        public bool NoRecovery;              // Transcended through Uncloud The Mind: no recovery once it lands
        public float RecoveryScale = 1f;     // its recovery is scaled by this (Quickness And Lethargy's Transcend: longer)
        public List<Vector3> Points;         // points it has picked so far (Tear Apart Reality's explosions)
        public string SecondTargetId;        // the other unit of an ability used on two (UnitCommand.SecondTargetId)
        public bool FreeAimed;               // ordered toward a point (UnitCommand.FreeAim): its target is picked as it lands (BattleSimulation.FreeAim.cs)
        public Vector3? ApproachAim;         // where its target stood when its user last stepped toward it (BattleSimulation.StepIntoMelee)
        public float ApproachGlideEnd = -1f; // when that step lands (another glide since: a dodge, a shove; leave it be)
        public bool LinkedUse;               // used as a linked ability (BattleSimulation.Recasts.cs)
        public int LinkDepth;                // linked uses in a row up to and including this one (0: used fresh)

        /// <summary>One of this action's numbers for its actor, upgrades and statuses included (Unit.ActionStat).</summary>
        public float Stat(ActionStat stat) { return Actor.ActionStat(Definition, stat); }
    }

    /// <summary>
    /// What the ability popup says about an action's effect, as label / value cells. Its behavior fills it
    /// (ActionBehavior.Describe), so an ability with special rules words its own lines and the popup needs no list of
    /// ability types. Numbers come from V: the ordered unit's (upgrades and statuses in), or the ability's own.
    /// </summary>
    public class AbilityTip
    {
        public readonly ActionDefinition Def;
        public readonly Unit Who;
        public readonly BattleSimulation Sim;   // for battle rule numbers (null outside a battle)
        public readonly List<KeyValuePair<string, string>> Effect = new List<KeyValuePair<string, string>>();
        /// <summary>Heading for the statuses it puts on what it hits (a trap's: "SPRUNG BY").</summary>
        public string TargetStatusesTitle = "TARGET";

        public AbilityTip(ActionDefinition def, Unit who, BattleSimulation sim = null) { Def = def; Who = who; Sim = sim; }

        public float V(ActionStat stat) { return Who != null ? Who.ActionStat(Def, stat) : Stats.Base(Def, stat); }
        public void Add(string label, string value) { Effect.Add(new KeyValuePair<string, string>(label, value)); }

        public static string Secs(float s) { return s <= 0f ? "instant" : string.Format("{0:0.###}s", s); }
        public static string Metres(float m) { return string.Format("{0:0.#}m", m); }
        public static string Mult(float m) { return "x" + m.ToString("0.##"); }
    }

    // ---- Effects: behaviors return these; the simulation applies them together so
    // ---- actions resolving on the same tick are truly simultaneous.
    public abstract class SimEffect { public Unit Source; }

    public class AttackEffect : SimEffect
    {
        public string TargetId;
        public ActionDefinition Definition;
        public float DamageMultiplier = 1f;
        public float AccuracyBonus;
        public float BlockPierceBonus;   // added to the definition's BlockPierce (Steady Strikes' follow-up)
        public float StunMultiplier = 1f;
        public float CritChance = -1f;   // its chance to strike critically, set as its action resolves (Transcended or not); -1: the ability's own as it lands
        public int BreakWeakpoints;      // it breaks this many of the target's weakpoints from any side (a Transcend an Etching gave: Brute Force)
        public int ExtraWeakpoints;      // it opens this many more on an enemy it hits, for ExtraWeakpointSeconds (Unwind Their Self's Transcend)
        public float ExtraWeakpointSeconds;
        public bool HasPush;        // shove the target to PushTo if this hits (Bash)
        public Vector3 PushTo;
        public bool IgnoreRange;    // area hits: the target only needs to be in the area, not in the action's range
        public bool IgnoreCover;    // area hits land regardless of the target's exposure
        public bool Unerring;       // no hit roll and no dodge: it always connects (a chain snapping taut)
        public bool Heavy;          // lands as a Heavy impact whatever the ability's own (Praa's Hatred deep in its chain)
        public bool NoStatuses;     // lands without the ability's TargetStatusesOnHit: they were put on already (Conjecture's slashes)
        public string Tag;
        /// <summary>Runs once the hit has landed (damage, statuses and stun applied) on a target that is still alive:
        /// for rules that follow a hit (Subjugate's grab, Amblivate's lift).</summary>
        public System.Action<BattleSimulation, Unit> OnLanded;
        /// <summary>Runs once the hit's damage is dealt (killing or not), with everything worked out about it: what it broke,
        /// whether it crit (A Duet's cooldown reset on a weakpoint).</summary>
        public System.Action<BattleSimulation, HitContext> AfterHit;
    }

    public class HealEffect : SimEffect { public string TargetId; public float Amount; }

    public class ApplyStatusEffect : SimEffect { public string TargetId; public StatusEffect Status; }

    /// <summary>Relocate a unit per the definition's DisplaceMode/DisplaceDistance (Reconstruct's reposition).</summary>
    public class DisplaceEffect : SimEffect { public string TargetId; public ActionDefinition Definition; }

    /// <summary>Place a sight-blocking object (smoke, shield, deployed cover). Set Occluder.ExpiresAt = sim.Now + duration.</summary>
    public class DeployEffect : SimEffect { public Occluder Occluder; }

    public class TrapEffect : SimEffect { public Trap Trap; }

    /// <summary>
    /// Special rules for an action. Stateless/shared: per-use data lives in ActionInstance.
    /// Subclass for abilities with unique rules and register under the action's BehaviorId.
    /// Generic data (energy, statuses, cooldowns, interrupt flags) is handled by the simulation itself.
    /// </summary>
    public abstract class ActionBehavior
    {
        /// <summary>Called once per battle for each behavior any unit can use: subscribe reactions/stances to sim.Triggers here.</summary>
        public virtual void Attach(BattleSimulation sim) { }

        /// <summary>Energy this use would cost. Override for discounts (Rapid Slices follow-ups).</summary>
        public virtual float EnergyCost(BattleSimulation sim, Unit actor, ActionDefinition def) { return actor.ActionStat(def, ActionStat.EnergyCost); }

        /// <summary>Strain the user takes for starting it (negative relieves strain). Behaviors may discount it (Rapid Slices).</summary>
        /// <param name="targetId">The unit it would be aimed at, when known (follow-up discounts depend on it).</param>
        public virtual float StrainCost(BattleSimulation sim, Unit actor, ActionDefinition def, string targetId) { return actor.ActionStat(def, ActionStat.StrainOnSelf); }

        /// <summary>True if it may be used even when its strain cost would pass max strain (strain just stops at max).</summary>
        public virtual bool AllowsStrainOverflow { get { return false; } }

        /// <summary>True if it may be used now although it is on cooldown (a chain's follow-ups). targetId null = on anyone.</summary>
        public virtual bool IgnoresCooldown(BattleSimulation sim, Unit actor, ActionDefinition def, string targetId) { return false; }

        /// <summary>A grab's hold (BattleSimulation.TickGrapples): what it strikes the held unit with every GrappleHeadbuttSeconds
        /// (Subjugate's headbutt: its LinkedAction), or null for a hold that only holds.</summary>
        public virtual ActionDefinition GrappleHeadbutt(ActionDefinition def) { return def.LinkedAction; }

        /// <summary>A grab's throw: the ability that winds up and resolves it (Subjugate: itself, tagged ThrowTag).</summary>
        public virtual ActionDefinition GrappleThrow(ActionDefinition def) { return def; }

        /// <summary>How far around its user it takes units along (Stride In Unison: allies within AreaRadius), for the aiming
        /// preview's ring around the user. 0: it takes no one.</summary>
        public virtual float GatherRadius(Unit actor, ActionDefinition def) { return 0f; }

        /// <summary>For one that MovesUser as it lands: are its hits held until the user reaches each target
        /// (BattleSimulation.HoldHitsForArrival)? False for those that time their own (Conjecture's slashes).</summary>
        public virtual bool HitsOnArrival(ActionDefinition def) { return true; }

        /// <summary>True for abilities that move the user (dashes, blinks, leaps): they can't be used while airborne.</summary>
        public virtual bool MovesUser { get { return false; } }

        /// <summary>True for dashes (the Dash, Phantom Strike's, Savior's): a CRIPPLED unit can't use them (StatusFlags.NoDash).</summary>
        public virtual bool Dashes { get { return false; } }
        /// <summary>True for guards that can stand in a unit's Block slot (UnitDefinition.Universal): Block, and a class's own guard
        /// in its place (the Duelist's Unsigned Rebuttal). The editor keeps such an ability there instead of the shared Block.</summary>
        public virtual bool TakesBlockSlot { get { return false; } }

        /// <summary>Is its follow-up open for this unit right now, whatever it did last? Then ordering it needs no aiming: it goes
        /// at FollowupPoint and its button is framed as a follow-up (Baneful Domain, while its zone is up).</summary>
        public virtual bool FollowupOpen(BattleSimulation sim, Unit actor, ActionDefinition def) { return false; }

        /// <summary>Where an open follow-up (FollowupOpen) of a point-aimed ability is aimed, cast from 'from'.</summary>
        public virtual Vector3 FollowupPoint(BattleSimulation sim, Unit actor, ActionDefinition def, Vector3 from) { return actor.LastActionPos; }

        /// <summary>Using it leaves a queued ranged order waiting (UseActionWhenInRange) in place, instead of replacing it:
        /// a Dash to get into range for it.</summary>
        public virtual bool KeepsQueuedOrder { get { return false; } }

        /// <summary>True for abilities that close the distance themselves (Phantom Strike's dash): ordered at a target out of
        /// reach, the unit walks only until the target is within the full Range, not BattleSimulation.ChaseStrikeFraction of it.</summary>
        public virtual bool ClosesDistance { get { return false; } }

        /// <summary>Windup in seconds before action speed (Flowing Death gets faster with stacks). See BattleSimulation.WindupOf.</summary>
        public virtual float WindupSeconds(BattleSimulation sim, Unit actor, ActionDefinition def) { return actor.ActionStat(def, ActionStat.Windup); }

        /// <summary>Extra validity checks beyond the built-in cooldown/resource/range/LOS ones.</summary>
        public virtual bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = null;
            return true;
        }

        public virtual void OnWindupStart(BattleSimulation sim, ActionInstance a) { }

        /// <summary>Called every tick during windup. Return false to cancel (e.g. Snipe when line of sight is lost).</summary>
        public virtual bool OnWindupTick(BattleSimulation sim, ActionInstance a) { return true; }

        /// <summary>Windup finished: add the effects this action produces.</summary>
        public abstract void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects);

        public virtual void OnInterrupted(BattleSimulation sim, ActionInstance a) { }

        // ------------------------------------------------------------------ its own numbers (ActionDefinition.Params)

        /// <summary>The numbers this behavior has of its own, with their defaults. Each ability using it can set any of them in its Params.</summary>
        public virtual IEnumerable<BehaviorParam> Parameters { get { yield break; } }

        private Dictionary<string, float> defaults;

        /// <summary>One of this behavior's numbers for 'def': the ability's Params entry for 'key', else the default declared in Parameters.</summary>
        protected float Param(ActionDefinition def, string key)
        {
            if (def != null && def.Params != null)
                for (int i = 0; i < def.Params.Count; i++) if (def.Params[i].Key == key) return def.Params[i].Value;
            if (defaults == null)
            {
                defaults = new Dictionary<string, float>();
                foreach (var p in Parameters) defaults[p.Key] = p.Value;
            }
            float v;
            if (!defaults.TryGetValue(key, out v)) throw new System.ArgumentException(GetType().Name + " has no parameter '" + key + "' (declare it in Parameters)");
            return v;
        }

        // ------------------------------------------------------------------ what the AI uses it for

        /// <summary>
        /// Its roles when the definition sets none (AbilityRoles.Of). The base reads the data alone: marking someone (Mark),
        /// a damaging circle at a point (AreaBlast), a melee strike that needs a status first (Combo). Behaviors add theirs.
        /// </summary>
        public virtual AbilityRole DefaultRoles(ActionDefinition def)
        {
            var r = AbilityRole.None;
            if (def.Targeting == TargetType.Enemy && def.TargetStatusesOnHit != null && def.TargetStatusesOnHit.Exists(s => s != null && s.Id == StatusIds.Marked)) r |= AbilityRole.Mark;
            if (def.Targeting == TargetType.Position && def.Shape == AreaShape.Circle && def.Damage > 0f) r |= AbilityRole.AreaBlast;
            if (def.Targeting == TargetType.Enemy && !string.IsNullOrEmpty(def.RequiredStatus) && def.Damage > 0f && !def.IsRanged) r |= AbilityRole.Combo;
            return r;
        }

        // ------------------------------------------------------------------ how it reads (the ability popup)

        /// <summary>Does it roll to hit its targets (damage, accuracy and stun lines)? Single-target attacks do; area attacks say so.</summary>
        public virtual bool RollsToHit(ActionDefinition def) { return def.Targeting == TargetType.Enemy; }

        /// <summary>Hit chance the targeting preview should add for this use (Transcended Steady Strikes always hits).</summary>
        public virtual float PreviewAccuracyBonus(ActionDefinition def, bool transcended) { return 0f; }

        /// <summary>
        /// The popup's EFFECT lines. The default covers hits (damage, accuracy, stun, strain), heals, pushes and pulls, and
        /// charge bonuses; override to add or reword lines for the behavior's own rules.
        /// </summary>
        public virtual void Describe(AbilityTip tip)
        {
            var def = tip.Def;
            if (RollsToHit(def)) DescribeHit(tip, "Damage", "Stun", true);
            if (tip.V(ActionStat.Heal) > 0f) tip.Add("Heal", tip.V(ActionStat.Heal).ToString("0"));
            DescribeMovement(tip);
            if (def.IsCharge && def.ChargeDamageMultiplier > 1f) tip.Add("Full charge", AbilityTip.Mult(def.ChargeDamageMultiplier) + " dmg");
            if (def.IsCharge && def.ChargeHealMultiplier > 1f) tip.Add("Full charge", AbilityTip.Mult(def.ChargeHealMultiplier) + " heal");
        }

        /// <summary>
        /// The popup's EFFECT cells as the player sees them: numbers only (the hit, heals, charge bonuses, weakpoints, and a
        /// plain push or pull). The lines behaviors word themselves (Describe's overrides) are left out: what an ability does
        /// beyond its numbers is the asset's Details text. TargetStatusesTitle still comes from the behavior.
        /// </summary>
        public static void DescribeNumbers(AbilityTip tip, ActionBehavior behavior)
        {
            var def = tip.Def;
            var scratch = new AbilityTip(def, tip.Who, tip.Sim);
            behavior.Describe(scratch);   // for its target statuses' heading only
            tip.TargetStatusesTitle = scratch.TargetStatusesTitle;
            if (behavior.RollsToHit(def)) DescribeHit(tip, "Damage", "Stun", true);
            if (tip.V(ActionStat.Heal) > 0f) tip.Add("Heal", tip.V(ActionStat.Heal).ToString("0"));
            // A push or pull, only where DisplaceDistance means one (behaviors that reword it use it as a height or the like).
            var move = behavior.GetType().GetMethod("DescribeMovement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (move != null && move.DeclaringType == typeof(ActionBehavior)) behavior.DescribeMovement(tip);
            if (def.IsCharge && def.ChargeDamageMultiplier > 1f) tip.Add("Full charge", AbilityTip.Mult(def.ChargeDamageMultiplier) + " dmg");
            if (def.IsCharge && def.ChargeHealMultiplier > 1f) tip.Add("Full charge", AbilityTip.Mult(def.ChargeHealMultiplier) + " heal");
            DescribeWeakpoints(tip);
        }

        /// <summary>The hit's lines: damage (or just "heavy" without any), accuracy, stun, target strain, the bonus on a stunned target.</summary>
        protected static void DescribeHit(AbilityTip tip, string damageLabel, string stunLabel, bool accuracy)
        {
            var def = tip.Def;
            float damage = tip.V(ActionStat.Damage);
            if (damage > 0f) tip.Add(damageLabel, string.Format("{0:0} {1}", damage, def.Impact == ImpactType.Heavy ? "heavy" : "light"));
            else if (def.Impact == ImpactType.Heavy) tip.Add("Impact", "heavy");
            if (accuracy && tip.V(ActionStat.Accuracy) < 0.995f) tip.Add("Accuracy", string.Format("{0:0}%", tip.V(ActionStat.Accuracy) * 100f));
            if (tip.V(ActionStat.CritChance) > 0f) tip.Add("Critical", string.Format("{0:0}% for +{1:0}% dmg", tip.V(ActionStat.CritChance) * 100f, tip.V(ActionStat.CritDamage) * 100f));
            if (tip.V(ActionStat.StunDuration) > 0f) tip.Add(stunLabel, AbilityTip.Secs(tip.V(ActionStat.StunDuration)));
            if (tip.V(ActionStat.StrainOnTarget) > 0f) tip.Add("Target strain", string.Format("+{0:0}", tip.V(ActionStat.StrainOnTarget)));
            if (def.StunnedDamageMultiplier > 1f) tip.Add("Vs stunned", AbilityTip.Mult(def.StunnedDamageMultiplier) + " dmg");
        }

        /// <summary>Is its follow-up aimed afresh (true: a new target is picked each time, Scepter Of Tyranny), rather than
        /// re-cast at the last one (false: Space repeats it on the same target)?</summary>
        public virtual bool FollowupRetargets(ActionDefinition def) { return false; }

        /// <summary>The name 'def' goes by for 'actor' right now: its DisplayName, or a StageNames entry at a later stage of a
        /// staged follow-up (Scepter Of Tyranny).</summary>
        public virtual string NameFor(Unit actor, ActionDefinition def) { return def.DisplayName; }

        /// <summary>After its target unit, is a point picked for it (where the unit is sent: within DestinationRange of it)?
        /// By default whenever the ability has a DestinationRange; Scepter Of Tyranny only on its first stage.</summary>
        public virtual bool PicksDestination(BattleSimulation sim, Unit actor, ActionDefinition def) { return def.DestinationRange > 0f; }

        /// <summary>After its target unit, is a second unit picked for it (UnitCommand.SecondTargetId: Supersede's swap)?</summary>
        public virtual bool PicksSecondUnit(ActionDefinition def) { return false; }

        /// <summary>Do its WeakpointsOnTarget open on what its hits land on (true), or, for an ability with no hit of its own
        /// (a status spell, a teleport, a pull), on the unit it was used on as it resolves (false)?</summary>
        public virtual bool WeakpointsOnHit(ActionDefinition def) { return true; }

        /// <summary>Its weakpoint lines for the ability popup (after Describe).</summary>
        public static void DescribeWeakpoints(AbilityTip tip)
        {
            var def = tip.Def;
            string lasts = def.WeakpointSeconds > 0f ? " for " + AbilityTip.Secs(def.WeakpointSeconds) : "";
            if (def.WeakpointsOnTarget > 0) tip.Add("Weakpoints", def.WeakpointsOnTarget + " on the target" + lasts);
            if (def.WeakpointsOnSelfDuringWindup > 0) tip.Add("Weakpoints", def.WeakpointsOnSelfDuringWindup + " on you while it winds up");
            if (def.WeakpointsOnSelf > 0) tip.Add("Weakpoints", def.WeakpointsOnSelf + " on you" + lasts);
            if (def.BreaksWeakpoints > 0) tip.Add("Breaks", def.BreaksWeakpoints + (def.BreaksWeakpoints == 1 ? " weakpoint" : " weakpoints") + " on each unit hit");
        }

        /// <summary>Does its user close in on the target while it winds up (BattleSimulation.StepIntoMelee)? Melee abilities
        /// aimed at a unit, unless the behavior moves its user itself.</summary>
        public virtual bool ApproachesTarget(ActionDefinition def)
        {
            return !def.IsRanged && !MovesUser && (def.Targeting == TargetType.Enemy || def.Targeting == TargetType.AnyUnit);
        }

        /// <summary>Does it move what it hits by itself, beyond its Displace (a line push, a charge carrying them ahead)? Then a
        /// raised Block isn't also knocked back by its hits (BattleSimulation.GuardKnockback): the ability's own push is all.</summary>
        public virtual bool MovesTargets(ActionDefinition def) { return false; }

        /// <summary>What DisplaceDistance does: a pull, a reposition or a knockback by default; behaviors that use it as a
        /// height (lifts, leaps, slams) say that instead.</summary>
        protected virtual void DescribeMovement(AbilityTip tip)
        {
            float d = tip.V(ActionStat.DisplaceDistance);
            if (d <= 0f) return;
            var mode = tip.Def.Displace;
            tip.Add(mode == DisplaceMode.TowardSource ? "Pull" : mode == DisplaceMode.AwayFromEnemies ? "Moves them" : "Knockback", AbilityTip.Metres(d));
        }

        /// <summary>An always-on ability (ActionDefinition.InnatePassive): called every tick its owner is alive and not VOIDED
        /// (BattleSimulation.TickInnatePassives). Imperfect Apotheosis catches what touches The Infinite.</summary>
        public virtual void OnInnateTick(BattleSimulation sim, Unit owner, ActionDefinition def) { }

        /// <summary>Channeled actions (ActionDefinition.ChannelSeconds): called every tick of the channel. Return false to break it.</summary>
        public virtual bool OnChannelTick(BattleSimulation sim, ActionInstance a) { return true; }

        /// <summary>The channel is over: ran its course (broken false) or was broken.</summary>
        public virtual void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken) { }
    }

    /// <summary>Default: one attack against the chosen target.</summary>
    public class BasicAttackBehavior : ActionBehavior
    {
        // A plain attack is a Strike; behaviors built on this one (Snipe, Flowing Death...) are something more and say so.
        public override AbilityRole DefaultRoles(ActionDefinition def)
        {
            var r = base.DefaultRoles(def);
            if (GetType() == typeof(BasicAttackBehavior) && def.Targeting == TargetType.Enemy && def.Damage > 0f) r |= AbilityRole.Strike;
            return r;
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;   // target already gone: action fizzles
            effects.Add(new AttackEffect
            {
                Source = a.Actor,
                TargetId = target.Id,
                Definition = a.Definition,
                DamageMultiplier = a.DamageMultiplier,
                Tag = a.Tag,
            });
        }
    }

    /// <summary>No direct effect: everything comes from the definition's SelfStatuses / EnergyGain (Brace, Battle Trance).</summary>
    public class SelfBuffBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def)
        {
            var r = base.DefaultRoles(def);
            if (def.Targeting == TargetType.Self && def.SelfStatuses != null && def.SelfStatuses.Exists(s => s != null && s.Total(StatId.Dodge, false) > 0f)) r |= AbilityRole.DodgeBuff;
            return r;
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects) { }
    }

    /// <summary>
    /// Hits every enemy in the definition's area: a Circle around the target point (or the actor, for Self
    /// targeting), or a Line of length Range from the actor toward the target point. Area hits ignore cover.
    /// </summary>
    public class AreaAttackBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return true; }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            foreach (var target in sim.UnitsInArea(a.Actor, a.Definition, a.TargetPos))
            {
                effects.Add(new AttackEffect
                {
                    Source = a.Actor, TargetId = target.Id, Definition = a.Definition,
                    DamageMultiplier = a.DamageMultiplier, IgnoreRange = true, IgnoreCover = true, Tag = a.Tag,
                });
            }
        }
    }

    /// <summary>
    /// A dome around the user (Sanctity, Chronosphere): AreaRadius wide and AreaHeight tall, moving with it; everyone inside
    /// holds the AuraStatuses (allies or enemies, per each one's AppliesTo). It lasts AuraSeconds, or, for a channeled
    /// action (ChannelSeconds), as long as the channel does (BattleSimulation.StartAura).
    /// </summary>
    public class AuraBehavior : ActionBehavior
    {
        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var def = a.Definition;
            bool channeled = def.ChannelSeconds > 0f;
            sim.StartAura(a.Actor, def, channeled ? def.ChannelSeconds : def.AuraSeconds * a.Actor.DurationScale(def), channeled, def.AuraAtTarget ? a.TargetPos : (Vector3?)null);
        }
    }

    /// <summary>Heal an ally and relocate them (Reconstruct).</summary>
    public class HealDisplaceBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Heal; }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            if (a.Definition.Heal > 0f) effects.Add(new HealEffect { Source = a.Actor, TargetId = target.Id, Amount = a.Stat(ActionStat.Heal) });
            if (a.Definition.Displace != DisplaceMode.None) effects.Add(new DisplaceEffect { Source = a.Actor, TargetId = target.Id, Definition = a.Definition });
        }
    }

    public static class ActionBehaviors
    {
        private static readonly Dictionary<string, ActionBehavior> map = new Dictionary<string, ActionBehavior>
        {
            { "basic_attack", new BasicAttackBehavior() },
            { "self_buff", new SelfBuffBehavior() },
            { "area_attack", new AreaAttackBehavior() },
            { "heal_displace", new HealDisplaceBehavior() },
            // Sharpshooter
            { "snipe", new SnipeBehavior() },
            { "take_aim", new TakeAimBehavior() },
            { "retaliate", new RetaliateBehavior() },
            { "trap", new TrapBehavior() },
            // Melee
            { "rapid_slices", new RapidSlicesBehavior() },
            { "steady_strikes", new SteadyStrikesBehavior() },
            { "final_cut", new FinalCutBehavior() },
            // Universal
            { "block", new BlockBehavior() },
            { "dash", new DashBehavior() },
            // Duelist and Strategist additions
            { "vindication", new VindicationBehavior() },
            { "quell", new QuellBehavior() },
            { "mindmeld", new MindmeldBehavior() },
            { "displacement", new DisplacementBehavior() },
            // Tank, Fighter, Paradigm additions
            { "bash", new BashBehavior() },
            { "phantom_strike", new PhantomStrikeBehavior() },
            { "mesmerize", new MesmerizeBehavior() },
            { "stutter", new StutterBehavior() },
            { "lance", new LanceBehavior() },
            { "subjugate", new SubjugateBehavior() },
            { "fatebreak", new FatebreakBehavior() },
            { "status_spell", new StatusSpellBehavior() },
            { "teleport", new TeleportBehavior() },
            { "levitate", new LevitateBehavior() },
            // Enemy types
            { "blink", new BlinkBehavior() },
            { "flowing_death", new FlowingDeathBehavior() },
            { "repel", new RepelBehavior() },
            { "charged_heal", new ChargedHealBehavior() },
            { "charge", new ChargeBehavior() },
            // Shared (any class)
            { "chain_attack", new ChainAttackBehavior() },
            { "leap", new LeapBehavior() },
            { "arrival", new ArrivalBehavior() },
            { "aura", new AuraBehavior() },
            { "aerial_slam", new AerialSlamBehavior() },
            { "strain_burden", new StrainBurdenBehavior() },
            { "telekinetic_pull", new TelekineticPullBehavior() },
            { "flurry", new FlurryBehavior() },
            { "hunt", new HuntBehavior() },
            { "channeled_status", new ChanneledStatusBehavior() },
            { "recoil_shot", new RecoilShotBehavior() },
            { "unmaker", new UnmakerBehavior() },
            // Wraith, Monarch, Zola, Primavaeran
            { "summon", new SummonBehavior() },
            { "beam", new BeamBehavior() },
            { "area_lift", new AreaLiftBehavior() },
            { "distortion", new DistortionBehavior() },
            { "reality_tear", new RealityTearBehavior() },
            { "retreat_strike", new RetreatStrikeBehavior() },
            { "sky_smite", new SkySmiteBehavior() },
            { "soulseeker", new SoulseekerBehavior() },
            { "sky_shot", new SkyShotBehavior() },
            { "chain_tether", new ChainTetherBehavior() },
            // Matriarch, Howler (and any kit that fires projectiles: ProjectileBehavior)
            { "volley", new VolleyBehavior() },
            { "volley_stream", new VolleyStreamBehavior() },
            { "reel", new ReelBehavior() },
            { "howl", new HowlBehavior() },
            // Deliverance
            { "perplexity_step", new PerplexityStepBehavior() },
            { "praas_hatred", new PraasHatredBehavior() },
            { "joyous_dance", new JoyousDanceBehavior() },
            { "close_the_loop", new CloseTheLoopBehavior() },
            { "stitch", new StitchBehavior() },
            { "split_the_minds", new SplitTheMindsBehavior() },
            { "scry", new ScryBehavior() },
            { "imprint", new ImprintBehavior() },
            { "equipose", new EquiposeBehavior() },
            { "savior", new SaviorBehavior() },
            // Paradigm User and Sharpshooter, round AF (Madness and Dreadful Fixation are status_spell)
            { "withering_end", new WitheringEndBehavior() },
            // Strategist
            { "scepter", new ScepterBehavior() },
            { "chrysalis", new ChrysalisBehavior() },
            { "threshold", new ThresholdBehavior() },
            { "stride", new StrideBehavior() },
            { "supersede", new SupersedeBehavior() },
            { "pressure_points", new PressurePointsBehavior() },
            // Duelist
            { "gouge", new GougeBehavior() },
            { "recursive_severance", new RecursiveSeveranceBehavior() },
            { "graze", new GrazeBehavior() },
            { "parry", new ParryBehavior() },
            { "summation", new SummationBehavior() },
            { "quick_dash", new QuickDashBehavior() },
            { "duet", new DuetBehavior() },
            { "opening_theorem", new OpeningTheoremBehavior() },
            { "conjecture", new ConjectureBehavior() },
            // Makina (the Strategist's constructs)
            { "summon_makina", new SummonMakinaBehavior() },
            { "crash_down", new CrashDownBehavior() },
            { "massacre", new MassacreBehavior() },
            { "omen", new OmenBehavior() },
            { "outburst", new OutburstBehavior() },
            { "grasp", new GraspBehavior() },
            { "impel", new ImpelBehavior() },
            { "fissure", new FissureBehavior() },
            { "apotheosis", new ApotheosisBehavior() },
            { "beckon", new BeckonBehavior() },
            { "splinter", new SplinterBehavior() },
            { "baneful_domain", new BanefulDomainBehavior() },
            { "mindshatter", new MindshatterBehavior() },
            { "crippling_pursuit", new CripplingPursuitBehavior() },
            { "mental_weapon", new MentalWeaponBehavior() },
        };

        public static void Register(string id, ActionBehavior behavior) { map[id] = behavior; }

        public static ActionBehavior Get(string id)
        {
            ActionBehavior b;
            return (id != null && map.TryGetValue(id, out b)) ? b : map["basic_attack"];
        }
    }
}
