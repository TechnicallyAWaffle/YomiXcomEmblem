using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // Melee Fighter abilities that need rules beyond data. The Fighter runs on strain, not energy. The rest are plain data:
    //   Evasive Strike - basic_attack with an "evasive" dodge status applied on windup.
    //   Battle Trance  - self_buff: relieves strain (negative StrainOnSelf) and grants the "trance" status Final Cut requires.
    //   Wrath          - self_buff: the "wrath" status (StrainCostMult 0, DamageDealtMult, ForbidsActions block + battle_trance);
    //                    the trance forbids wrath in turn.
    //   Strain relief on dodge - passive, see Passives.

    /// <summary>
    /// Chain attack (chain_attack): a plain attack that can be used ChainFollowups more times in a row on the same target.
    /// Each use opens (or extends) a window, the "<id>_chain" status aimed at that target, through the recovery and
    /// ChainWindow seconds after. Inside it, using it on that target again ignores its cooldown and costs ChainCostMultiplier
    /// of its strain, until the follow-ups run out. Using it on someone else starts a fresh chain at full price (off
    /// cooldown only). Flickershots; Rapid Slices is one with fixed numbers.
    /// </summary>
    public class ChainAttackBehavior : BasicAttackBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Chain; }

        protected virtual int Followups(ActionDefinition def) { return def.ChainFollowups; }
        protected virtual float CostMultiplier(ActionDefinition def) { return def.ChainCostMultiplier; }
        protected virtual float Window(ActionDefinition def) { return def.ChainWindow; }
        public virtual string ChainStatus(ActionDefinition def) { return def.Id + "_chain"; }

        /// <summary>Would the next use on 'targetId' (on anyone, if null) be a follow-up?</summary>
        public bool InChain(Unit actor, ActionDefinition def, string targetId)
        {
            var chain = actor.GetStatus(ChainStatus(def));
            return chain != null && chain.Stacks < Followups(def) && (targetId == null || chain.TargetId == targetId);
        }

        public override float StrainCost(BattleSimulation sim, Unit actor, ActionDefinition def, string targetId)
        {
            return targetId != null && InChain(actor, def, targetId) ? actor.ActionStat(def, ActionStat.StrainOnSelf) * CostMultiplier(def) : actor.ActionStat(def, ActionStat.StrainOnSelf);
        }

        public override bool IgnoresCooldown(BattleSimulation sim, Unit actor, ActionDefinition def, string targetId) { return InChain(actor, def, targetId); }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            base.OnResolve(sim, a, effects);
            if (a.IsFree) return;
            var actor = a.Actor;
            var def = a.Definition;
            string id = ChainStatus(def);
            float windowEnds = sim.Now + actor.ActionStat(def, ActionStat.Recovery) / actor.EffectiveActionSpeed() + Window(def);
            var chain = actor.GetStatus(id);
            if (chain == null || chain.TargetId != a.TargetUnitId)
            {
                if (Followups(def) > 0) sim.ApplyStatus(actor, new StatusEffect { Id = id, SourceId = actor.Id, TargetId = a.TargetUnitId, ExpiresAt = windowEnds  }, actor);
                return;
            }
            chain.Stacks++;
            if (chain.Stacks >= Followups(def)) sim.RemoveStatus(actor, id);
            else chain.ExpiresAt = windowEnds;
        }
    }

    /// <summary>
    /// Rapid Slices: a quick melee hit and a follow-up ability (Space re-casts it): a chain attack of MaxFollowups more
    /// slices at followup_cost of the strain ("rapid_chain"), its own Params rather than its asset's chain fields.
    /// </summary>
    public class RapidSlicesBehavior : ChainAttackBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("followups", 4f);
                yield return new BehaviorParam("followup_cost", 0.5f);
                yield return new BehaviorParam("followup_window", 1f);   // seconds after recovery ends
            }
        }

        protected override int Followups(ActionDefinition def) { return Mathf.RoundToInt(Param(def, "followups")); }
        protected override float CostMultiplier(ActionDefinition def) { return Param(def, "followup_cost"); }
        protected override float Window(ActionDefinition def) { return Param(def, "followup_window"); }
        public override string ChainStatus(ActionDefinition def) { return StatusIds.RapidChain; }
        // Rapid Slices has no cooldown to skip.
        public override bool IgnoresCooldown(BattleSimulation sim, Unit actor, ActionDefinition def, string targetId) { return false; }
    }

    /// <summary>
    /// Steady Strikes: a deliberate melee strike. Transcended (hold its button; ActionDefinition.TranscendStrain extra
    /// strain) it is the empowered blow: transcend_accuracy more hit chance (80% -> 100%) and it pierces transcend_pierce
    /// of block (its Params).
    /// </summary>
    public class SteadyStrikesBehavior : BasicAttackBehavior
    {
        public override float PreviewAccuracyBonus(ActionDefinition def, bool transcended) { return transcended ? Param(def, "transcend_accuracy") : 0f; }

        public const string EmpoweredTag = "steady_empowered";

        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("transcend_accuracy", 0.2f);
                yield return new BehaviorParam("transcend_pierce", 0.25f);
            }
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            if (a.Transcended) a.Tag = EmpoweredTag;
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            bool empowered = a.Tag == EmpoweredTag;
            effects.Add(new AttackEffect
            {
                Source = a.Actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                AccuracyBonus = empowered ? Param(a.Definition, "transcend_accuracy") : 0f,
                BlockPierceBonus = empowered ? Param(a.Definition, "transcend_pierce") : 0f,
            });
        }
    }

    /// <summary>
    /// Final Cut (shown as Certainty): needs Battle Trance or Wrath (RequiredStatus "trance|wrath"), and keeps it. Execute:
    /// damage grows with the target's missing health, up to double. A kill extends the trance or Wrath by kill_extends seconds.
    /// </summary>
    public class FinalCutBehavior : ActionBehavior
    {
        public const string PrimaryTag = "final_cut";

        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("kill_extends", 3f); } }
        private static readonly string[] Extends = { StatusIds.Trance, StatusIds.Wrath };

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.Killed, ctx =>
            {
                var attacker = ctx.Other;
                if (ctx.Tag != PrimaryTag || attacker == null || !attacker.IsAlive) return;
                foreach (var id in Extends)
                {
                    var status = attacker.GetStatus(id);
                    if (status == null) continue;
                    var extended = status.Copy();
                    extended.ExpiresAt += Param(ctx.Action, "kill_extends");
                    sim.ApplyStatus(attacker, extended, attacker);   // refresh: same status, later end
                }
            });
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            float missing = 1f - Mathf.Clamp01(target.Health / Mathf.Max(1f, target.MaxHealth));
            effects.Add(new AttackEffect
            {
                Source = a.Actor, TargetId = target.Id, Definition = a.Definition,
                DamageMultiplier = a.DamageMultiplier * (1f + missing),
                Tag = a.Tag ?? PrimaryTag,
            });
        }
    }

    /// <summary>
    /// Phantom Strike: dash to the target (Range is the reach of the dash) and slash it, marking it (TargetStatusesOnHit).
    /// The dash phases through walls and cover: it lands beside the target on the target's side of anything in between
    /// (clamped outward from the target, so it never ends up inside a wall).
    /// </summary>
    public class PhantomStrikeBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Opener; }

        public override bool MovesUser { get { return true; } }   // not while airborne

        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("strike_reach", 2f); } }   // the slash lands if the dash ends this close

        public override bool ClosesDistance { get { return true; } }   // a chase goes the moment the dash can reach

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            var actor = a.Actor;
            Vector3 from = target.Position - actor.Position; from.y = 0f;
            from = from.sqrMagnitude > 1e-6f ? from.normalized : Vector3.forward;
            Vector3 beside = target.Position - from * (actor.BodyRadius + target.BodyRadius + 0.3f);
            Vector3 landed = sim.Paths.ClampDash(target.Position, beside);   // from the target out: walls between don't matter
            sim.SlideUnitTo(actor, landed, BattleSimulation.DashDuration, target.Position);
            if (Vector3.Distance(landed, target.Position) > Param(a.Definition, "strike_reach")) return;
            effects.Add(new AttackEffect { Source = actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true });
        }
    }

    /// <summary>
    /// Fatebreak: Trance or Wrath only (RequiredStatus "trance|wrath", consumed when it lands). While it winds up the target
    /// is heavily slowed (TargetStatusesDuringWindup, "fated"); then a massive blow that knocks the target DisplaceDistance
    /// straight back (off ledges too, like Bash, but no wall bonus). Wrath's extra damage counts: it is taken as the blow lands.
    /// </summary>
    public class FatebreakBehavior : ActionBehavior
    {
        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            Vector3 dir = target.Position - a.Actor.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 landed = sim.Paths.ClampDash(target.Position, target.Position + dir * a.Stat(ActionStat.DisplaceDistance));
            effects.Add(new AttackEffect
            {
                Source = a.Actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                HasPush = a.Stat(ActionStat.DisplaceDistance) > 0f, PushTo = landed,
            });
        }
    }

    /// <summary>
    /// Mesmerize: the "mesmerized" status (a big dodge bonus, SelfStatuses). The first attack dodged while it lasts ends it
    /// and blinks the Fighter to the attacker for CounterStrikes rapid strikes (the LinkedAction), see
    /// BattleSimulation.StartMesmerCounter. That replaces the usual dodge dash.
    /// </summary>
    public class MesmerizeBehavior : SelfBuffBehavior
    {
        public const int CounterStrikes = 4;

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.Dodged, ctx =>
            {
                var stance = ctx.Subject.GetStatus(StatusIds.Mesmerized);
                if (stance == null || stance.LinkedAction == null || ctx.Other == null || !ctx.Other.IsAlive) return;
                sim.RemoveStatus(ctx.Subject, StatusIds.Mesmerized);
                sim.StartMesmerCounter(ctx.Subject, ctx.Other, stance.LinkedAction, CounterStrikes);
            });
        }
    }
}
