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
        public bool HasPush;        // shove the target to PushTo if this hits (Bash)
        public Vector3 PushTo;
        public bool IgnoreRange;    // area hits: the target only needs to be in the area, not in the action's range
        public bool IgnoreCover;    // area hits land regardless of the target's exposure
        public string Tag;
        /// <summary>Runs once the hit has landed (damage, statuses and stun applied) on a target that is still alive:
        /// for rules that follow a hit (Subjugate's grab, Amblivate's lift).</summary>
        public System.Action<BattleSimulation, Unit> OnLanded;
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
        public virtual float EnergyCost(BattleSimulation sim, Unit actor, ActionDefinition def) { return def.EnergyCost; }

        /// <summary>Strain the user takes for starting it (negative relieves strain). Behaviors may discount it (Rapid Slices).</summary>
        /// <param name="targetId">The unit it would be aimed at, when known (follow-up discounts depend on it).</param>
        public virtual float StrainCost(BattleSimulation sim, Unit actor, ActionDefinition def, string targetId) { return def.StrainOnSelf; }

        /// <summary>True if it may be used even when its strain cost would pass max strain (strain just stops at max).</summary>
        public virtual bool AllowsStrainOverflow { get { return false; } }

        /// <summary>True for abilities that close the distance themselves (Phantom Strike's dash): ordered at a target out of
        /// reach, the unit walks only until the target is within the full Range, not BattleSimulation.ChaseStrikeFraction of it.</summary>
        public virtual bool ClosesDistance { get { return false; } }

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
    }

    /// <summary>Default: one attack against the chosen target.</summary>
    public class BasicAttackBehavior : ActionBehavior
    {
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
        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects) { }
    }

    /// <summary>
    /// Hits every enemy in the definition's area: a Circle around the target point (or the actor, for Self
    /// targeting), or a Line of length Range from the actor toward the target point. Area hits ignore cover.
    /// </summary>
    public class AreaAttackBehavior : ActionBehavior
    {
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

    /// <summary>Heal an ally and relocate them (Reconstruct).</summary>
    public class HealDisplaceBehavior : ActionBehavior
    {
        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            if (a.Definition.Heal > 0f) effects.Add(new HealEffect { Source = a.Actor, TargetId = target.Id, Amount = a.Definition.Heal });
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
        };

        public static void Register(string id, ActionBehavior behavior) { map[id] = behavior; }

        public static ActionBehavior Get(string id)
        {
            ActionBehavior b;
            return (id != null && map.TryGetValue(id, out b)) ? b : map["basic_attack"];
        }
    }
}
