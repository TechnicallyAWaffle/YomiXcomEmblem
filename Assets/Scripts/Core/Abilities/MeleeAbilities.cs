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
    /// Steady Strikes: a deliberate melee strike. Transcended (hold its button; ActionDefinition.TranscendStrain extra
    /// strain) it is the empowered blow: TranscendAccuracyBonus more hit chance (80% -> 100%) and it pierces
    /// TranscendBlockPierce of block.
    /// </summary>
    public class SteadyStrikesBehavior : BasicAttackBehavior
    {
        public const string EmpoweredTag = "steady_empowered";
        public const float TranscendAccuracyBonus = 0.2f;
        public const float TranscendBlockPierce = 0.25f;

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
                AccuracyBonus = empowered ? TranscendAccuracyBonus : 0f,
                BlockPierceBonus = empowered ? TranscendBlockPierce : 0f,
            });
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

    /// <summary>
    /// Phantom Strike: dash to the target (Range is the reach of the dash) and slash it, marking it (TargetStatusesOnHit).
    /// The dash phases through walls and cover: it lands beside the target on the target's side of anything in between
    /// (clamped outward from the target, so it never ends up inside a wall).
    /// </summary>
    public class PhantomStrikeBehavior : ActionBehavior
    {
        public const float StrikeReach = 2f;

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
            if (Vector3.Distance(landed, target.Position) > StrikeReach) return;
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
            Vector3 landed = sim.Paths.ClampDash(target.Position, target.Position + dir * a.Definition.DisplaceDistance);
            effects.Add(new AttackEffect
            {
                Source = a.Actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                HasPush = a.Definition.DisplaceDistance > 0f, PushTo = landed,
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
