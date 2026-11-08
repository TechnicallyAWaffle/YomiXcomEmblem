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
        public override bool Dashes { get { return true; } }      // a CRIPPLED unit can't

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

    /// <summary>
    /// A Million Cuts (flurry): dash to the target like Phantom Strike (Range is the dash's reach), then channel
    /// (ChannelSeconds): 'strikes' (15) hits spread over it, each its own hit for the ability's Damage and stun. A channel
    /// can't be cancelled by orders; damage on the user still breaks it. Ends with its recovery once the strikes are in,
    /// at once if the dash didn't reach (strike_reach) or the target falls. Before every cut after the first the user
    /// blinks to a random spot around the target, at striking distance.
    /// </summary>
    public partial class FlurryBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Opener; }

        public override bool Dashes { get { return true; } }      // a CRIPPLED unit can't
        public override bool MovesUser { get { return true; } }
        public override bool ClosesDistance { get { return true; } }

        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("strikes", 15f); yield return new BehaviorParam("strike_reach", 2f); }
        }

        public override void Describe(AbilityTip tip)
        {
            DescribeHit(tip, "Per strike", "Stun per strike", true);
            tip.Add("Strikes", string.Format("{0:0} over {1}", Param(tip.Def, "strikes"), AbilityTip.Secs(tip.Def.ChannelSeconds)));
            tip.Add("Once started", "can't be cancelled");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            a.Steps = -1;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            var actor = a.Actor;
            Vector3 from = target.Position - actor.Position; from.y = 0f;
            from = from.sqrMagnitude > 1e-6f ? from.normalized : Vector3.forward;
            Vector3 beside = target.Position - from * (actor.BodyRadius + target.BodyRadius + 0.3f);
            Vector3 landed = sim.Paths.ClampDash(target.Position, beside);
            sim.SlideUnitTo(actor, landed, BattleSimulation.DashDuration, target.Position);
            if (Vector3.Distance(landed, target.Position) > Param(a.Definition, "strike_reach")) return;
            a.Steps = 0;
            a.NextStepAt = sim.Now + BattleSimulation.DashDuration;   // the first cut as it arrives
        }

        public override bool OnChannelTick(BattleSimulation sim, ActionInstance a)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            int strikes = Mathf.Max(1, Mathf.RoundToInt(Param(a.Definition, "strikes")));
            if (a.Steps < 0 || target == null || !target.IsAlive) { sim.CutChannel(a.Actor, sim.Now); return true; }
            float interval = Mathf.Max(BattleSimulation.TickLength, (a.Definition.ChannelSeconds - BattleSimulation.DashDuration) / strikes);
            while (a.Steps < strikes && sim.Now + 1e-4f >= a.NextStepAt && target.IsAlive)
            {
                if (a.Steps > 0) BlinkAround(sim, a.Actor, target);
                sim.StrikeNow(new AttackEffect { Source = a.Actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag, IgnoreRange = true });
                a.Steps++;
                a.NextStepAt += interval;
            }
            if (a.Steps >= strikes) sim.CutChannel(a.Actor, sim.Now);
            return true;
        }
    }

    public partial class FlurryBehavior
    {
        /// <summary>Blink 'actor' to a random point around 'target', just outside both bodies (the battle's seeded random).</summary>
        private static void BlinkAround(BattleSimulation sim, Unit actor, Unit target)
        {
            float angle = sim.RandomValue() * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 spot = target.Position + dir * (actor.BodyRadius + target.BodyRadius + 0.3f);
            sim.TeleportUnit(actor, sim.WalkablePointNear(spot, target.Position), actor);
        }
    }

    /// <summary>
    /// The Endless Hunt (hunt): a long windup on any enemy in Range, seen or not (PiercesTerrain), that slows the target
    /// while it lasts (TargetStatusesDuringWindup: Fated), then a blink to its side.
    /// </summary>
    public class HuntBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser; }

        public override bool MovesUser { get { return true; } }

        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Blink", "to the target's side");
            tip.Add("Sight", "not needed");
        }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || actor.IsRooted) return;
            Vector3 from = target.Position - actor.Position; from.y = 0f;
            from = from.sqrMagnitude > 1e-6f ? from.normalized : Vector3.forward;
            Vector3 beside = target.Position - from * (actor.BodyRadius + target.BodyRadius + 0.3f);
            sim.TeleportUnit(actor, sim.WalkablePointNear(beside, target.Position), actor);
        }
    }
}
