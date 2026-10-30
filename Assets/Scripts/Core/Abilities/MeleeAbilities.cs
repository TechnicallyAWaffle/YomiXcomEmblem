using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // Melee Fighter abilities that need rules beyond data. The Fighter runs on strain, not energy. The rest are plain data:
    //   Evasive Strike - basic_attack with an "evasive" dodge status applied on windup.
    //   Battle Trance  - self_buff: relieves strain (negative StrainOnSelf) and grants the "trance" status Final Cut requires.
    //   Strain relief on dodge - passive, see Passives.

    /// <summary>
    /// Rapid Slices: a quick melee hit and a follow-up ability (Space re-casts it). Using one opens a short window
    /// ("rapid_chain", aimed at that target) during which slicing the SAME target again costs FollowupCostMultiplier of
    /// its strain, up to MaxFollowups more times. Slicing someone else starts a fresh chain at full price.
    /// </summary>
    public class RapidSlicesBehavior : BasicAttackBehavior
    {
        public const int MaxFollowups = 4;
        public const float FollowupCostMultiplier = 0.5f;
        public const float FollowupWindow = 1.0f;   // seconds after recovery ends

        public override float StrainCost(BattleSimulation sim, Unit actor, ActionDefinition def, string targetId)
        {
            var chain = actor.GetStatus(StatusIds.RapidChain);
            bool discounted = chain != null && chain.Stacks < MaxFollowups && targetId != null && chain.TargetId == targetId;
            return discounted ? def.StrainOnSelf * FollowupCostMultiplier : def.StrainOnSelf;
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            base.OnResolve(sim, a, effects);
            if (a.IsFree) return;
            var actor = a.Actor;
            float windowEnds = sim.Now + a.Definition.Recovery / actor.EffectiveActionSpeed() + FollowupWindow;
            var chain = actor.GetStatus(StatusIds.RapidChain);
            if (chain == null || chain.TargetId != a.TargetUnitId)
            {
                sim.ApplyStatus(actor, new StatusEffect { Id = StatusIds.RapidChain, SourceId = actor.Id, TargetId = a.TargetUnitId, ExpiresAt = windowEnds }, actor);
                return;
            }
            chain.Stacks++;
            if (chain.Stacks >= MaxFollowups) sim.RemoveStatus(actor, StatusIds.RapidChain);
            else chain.ExpiresAt = windowEnds;
        }
    }

    /// <summary>
    /// Steady Strikes: a deliberate melee strike and a follow-up ability. Striking opens a short window ("steady_chain",
    /// aimed at that target); striking the SAME target again inside it is the empowered follow-up: it costs
    /// FollowupStrain, gains FollowupAccuracyBonus and pierces FollowupBlockPierce of block. The window closes after one
    /// follow-up, so the next strike is a plain one again.
    /// </summary>
    public class SteadyStrikesBehavior : BasicAttackBehavior
    {
        public const string EmpoweredTag = "steady_empowered";
        public const float FollowupStrain = 20f;
        public const float FollowupAccuracyBonus = 0.2f;   // 80% -> 100%
        public const float FollowupBlockPierce = 0.25f;
        public const float FollowupWindow = 1.0f;          // seconds after recovery ends

        private static bool IsFollowup(Unit actor, string targetId)
        {
            var chain = actor.GetStatus(StatusIds.SteadyChain);
            return chain != null && targetId != null && chain.TargetId == targetId;
        }

        public override float StrainCost(BattleSimulation sim, Unit actor, ActionDefinition def, string targetId)
        {
            return IsFollowup(actor, targetId) ? FollowupStrain : def.StrainOnSelf;
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            if (a.IsFree || !IsFollowup(a.Actor, a.TargetUnitId)) return;
            a.Tag = EmpoweredTag;
            sim.RemoveStatus(a.Actor, StatusIds.SteadyChain);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            bool empowered = a.Tag == EmpoweredTag;
            effects.Add(new AttackEffect
            {
                Source = a.Actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                AccuracyBonus = empowered ? FollowupAccuracyBonus : 0f,
                BlockPierceBonus = empowered ? FollowupBlockPierce : 0f,
            });
            if (empowered || a.IsFree) return;
            float windowEnds = sim.Now + a.Definition.Recovery / a.Actor.EffectiveActionSpeed() + FollowupWindow;
            sim.ApplyStatus(a.Actor, new StatusEffect { Id = StatusIds.SteadyChain, SourceId = a.Actor.Id, TargetId = target.Id, ExpiresAt = windowEnds }, a.Actor);
        }
    }

    /// <summary>
    /// Final Cut: needs Battle Trance, and keeps it. Execute: damage grows with the target's missing health, up to double.
    /// A kill extends the trance by TranceExtension seconds.
    /// </summary>
    public class FinalCutBehavior : ActionBehavior
    {
        public const string PrimaryTag = "final_cut";
        public const float TranceExtension = 3f;

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.Killed, ctx =>
            {
                var attacker = ctx.Other;
                if (ctx.Tag != PrimaryTag || attacker == null || !attacker.IsAlive) return;
                var trance = attacker.GetStatus(StatusIds.Trance);
                if (trance == null) return;
                var extended = trance.Copy();
                extended.ExpiresAt += TranceExtension;
                sim.ApplyStatus(attacker, extended, attacker);   // refresh: same status, later end
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
}
