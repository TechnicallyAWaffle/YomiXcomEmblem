using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // The Duelist's tempo kit: Feint, Contempt and Countersign. Clear and Steady and Third Intention are self_buffs whose
    // statuses are spent by the next abilities (StatusFlags.SpentOnAbility); Breakthrough is an area_attack line.

    /// <summary>
    /// Feint (feint): a feint at an enemy within Range. If it can act right now (standing, walking, or waiting out a
    /// recovery) and can use its Block (the shared Block or its class's own: Unsigned Rebuttal), it Blocks at once, dropping
    /// what it was about to do. Otherwise (stunned, winding up, channeling, no Block to use) it gains 'strain' (25) instead.
    /// No roll: a feint isn't dodged.
    /// </summary>
    public class FeintBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("strain", 25f); } }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Strike; }
        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("Feint", "the target Blocks at once");
            tip.Add("If it can't", string.Format("+{0:0} strain instead", Param(tip.Def, "strain")));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || target.Team == actor.Team || !sim.InReach(actor, def, target)) return;
            bool blocked = sim.ForceBlock(target);
            if (!blocked) sim.AddStrain(target, Param(def, "strain") * actor.StrainDealtMult());
            sim.EmitFx(actor, target, "feint", def, target.Position, blocked ? 1f : 0f, actor.Position);
        }
    }

    /// <summary>
    /// Contempt (contempt): dash to an enemy within Range, through walls and cover, and put the ability's
    /// TargetStatusesOnHit (IMMOBILIZED) on it. On the Duelist's NEMESIS (Karranos Sigil) they last 'nemesis_duration'
    /// (1.5) times as long. No roll.
    /// </summary>
    public class ContemptBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("nemesis_duration", 1.5f); } }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser; }
        public override bool MovesUser { get { return true; } }
        public override bool Dashes { get { return true; } }
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool WeakpointsOnHit(ActionDefinition def) { return false; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        protected override void DescribeMovement(AbilityTip tip) { tip.Add("Dash", "to the target, up to " + AbilityTip.Metres(tip.V(ActionStat.Range))); }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Your NEMESIS", string.Format("lasts {0:0.##}x as long", Param(tip.Def, "nemesis_duration")));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || !sim.InReach(actor, def, target)) return;
            DuetBehavior.DashBeside(sim, actor, target);
            float scale = actor.DurationScale(def) * (Passives.NemesisOf(target, actor) != null ? Param(def, "nemesis_duration") : 1f);
            foreach (var spec in def.TargetStatusesOnHit)
                if (spec != null && spec.AppliesToUnit(actor, target))
                    effects.Add(new ApplyStatusEffect { Source = actor, TargetId = target.Id, Status = spec.Create(sim.Now, actor.Id, null, scale) });
            if (target.Team != actor.Team) sim.AddTargetWeakpoints(actor, target, def);
            sim.EmitFx(actor, target, "contempt", def, target.Position);
        }
    }

    /// <summary>
    /// Countersign (countersign): COUNTERSIGN on an enemy within Range for 'seconds' (3). The next time it would damage one
    /// of the Duelist's allies, the Duelist blinks in front of that ally and the hit is the Duelist's instead
    /// (BattleSimulation.InterceptCountersigned). Against the Duelist's NEMESIS the redirected hit meets the parry Karranos
    /// Sigil gives (it is parried, and the NEMESIS mark is spent). No roll.
    /// </summary>
    public class CountersignBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("seconds", 3f); } }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Mark; }
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }
        public override IEnumerable<string> StatusesApplied(ActionDefinition def) { yield return StatusIds.Countersign; }

        public override void Describe(AbilityTip tip)
        {
            tip.Add("COUNTERSIGN", AbilityTip.Secs(Param(tip.Def, "seconds") * (tip.Who != null ? tip.Who.DurationScale(tip.Def) : 1f)));
            tip.Add("Its next hit on an ally", "you blink in and take it");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || target.Team == actor.Team || !sim.InReach(actor, def, target)) return;
            sim.ApplyStatus(target, new StatusEffect { Id = StatusIds.Countersign, SourceId = actor.Id, ExpiresAt = sim.Now + Param(def, "seconds") * actor.DurationScale(def), FromAction = def }, actor);
            sim.EmitFx(actor, target, "countersign", def, target.Position, 0f, actor.Position);
        }
    }

    public partial class BattleSimulation
    {
        /// <summary>
        /// Feint: 'u' Blocks right now if it can act (standing, walking, or waiting out a recovery) and its Block can be used
        /// (the shared Block, or what takes its slot: Unsigned Rebuttal). What it was doing (a walk, a chase, a queued order)
        /// is dropped. False: it couldn't.
        /// </summary>
        public bool ForceBlock(Unit u)
        {
            if (u == null || !u.IsAlive || u.Grapple != null || u.IsLifted) return false;
            bool free = u.Phase == UnitPhase.Ready || u.Phase == UnitPhase.Moving || (u.Phase == UnitPhase.Recovery && u.Waiting);
            if (!free) return false;
            ActionDefinition block = null;
            foreach (var a in u.Actions)
                if (a != null && (a.BehaviorId == "block" || ActionBehaviors.Get(a.BehaviorId).TakesBlockSlot)) { block = a; break; }
            string why;
            if (block == null || !CanAfford(u, block, out why)) return false;
            if (u.Phase == UnitPhase.Moving) EndMove(u, false);
            if (u.Phase == UnitPhase.Recovery) EndRecoveryNow(u);
            ClearChase(u);
            ClearQueued(u, null);
            queues.Remove(u.Id);
            return TryStartCommand(u, UnitCommand.UseAction(u.Id, block, u.Id), out why);
        }

        /// <summary>
        /// Countersign: 'src' is about to hit 'target' with damage. If 'src' carries a COUNTERSIGN from a Duelist on 'target's
        /// side (not 'target' itself), the Duelist blinks in front of 'target', between it and 'src', the mark is spent, and
        /// the Duelist is who the hit reaches. Null: nobody stepped in.
        /// </summary>
        internal Unit InterceptCountersigned(Unit src, Unit target, ActionDefinition def, AttackEffect atk)
        {
            if (src == null || target == null || src.Team == target.Team) return null;
            if (atk.Tag == ReflectedTag || (atk.FlatDamage < 0f && def.Damage <= 0f && def.MissingHealthDamage <= 0f)) return null;
            var mark = src.GetStatus(StatusIds.Countersign);
            if (mark == null) return null;
            var duelist = GetUnit(mark.SourceId);
            if (duelist == null || !duelist.IsAlive || duelist == target || duelist.Team != target.Team || duelist.HasStatus(StatusIds.Voided)) return null;
            RemoveStatus(src, StatusIds.Countersign);
            Vector3 toward = src.Position - target.Position; toward.y = 0f;
            toward = toward.sqrMagnitude > 1e-6f ? toward.normalized : Vector3.forward;
            Vector3 from = duelist.Position;
            TeleportUnit(duelist, target.Position + toward * (target.BodyRadius + duelist.BodyRadius + StandingGap), duelist);
            EmitFx(duelist, src, "countersign_blink", mark.FromAction ?? def, duelist.Position, 0f, from);
            return duelist;
        }
    }
}
