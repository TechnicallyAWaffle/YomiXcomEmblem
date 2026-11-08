using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Bash: hit everyone in a short strip in front of the Tank (a Line area toward the target point) and shove them
    /// DisplaceDistance straight back. Anyone the push drives into a wall (the navmesh edge stops them short) or the face
    /// of higher ground takes wall_multiplier damage and stun (its Params). Pushed off a ledge, they fall (see BattleSimulation.Relocate).
    /// </summary>
    public class BashBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Peel; }   // Repel too

        public override bool RollsToHit(ActionDefinition def) { return true; }

        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("wall_multiplier", WallMultiplierDefault);   // damage and stun on a target driven into a wall (1 = none)
                yield return new BehaviorParam("wall_tolerance", 0.3f);   // a push stopped more than this short of its full distance hit a wall
            }
        }

        /// <summary>The default wall bonus: 2, none (1) for Repel.</summary>
        protected virtual float WallMultiplierDefault { get { return 2f; } }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            Vector3 dir = a.TargetPos - a.Actor.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            foreach (var target in sim.UnitsInArea(a.Actor, a.Definition, a.TargetPos))
            {
                Vector3 full = target.Position + dir * a.Stat(ActionStat.DisplaceDistance);
                Vector3 landed = sim.Paths.ClampDash(target.Position, full);   // higher ground counts as a wall; a ledge drops them off it
                Vector3 shortBy = full - landed; shortBy.y = 0f;
                float wallBonus = Param(a.Definition, "wall_multiplier");
                bool walled = wallBonus > 1f && shortBy.magnitude > Param(a.Definition, "wall_tolerance");
                effects.Add(new AttackEffect
                {
                    Source = a.Actor, TargetId = target.Id, Definition = a.Definition, Tag = walled ? "bash_wall" : a.Tag,
                    DamageMultiplier = a.DamageMultiplier * (walled ? wallBonus : 1f), StunMultiplier = walled ? wallBonus : 1f,
                    IgnoreRange = true, IgnoreCover = true, HasPush = true, PushTo = landed,
                });
            }
        }
    }

    /// <summary>
    /// Subjugate. The grab: stun immune from the start of the windup ("grappling"), a Heavy hit for no damage that stuns
    /// for StunDuration. If it lands and the target is stunned, the Tank holds it (BattleSimulation.StartGrapple): it
    /// carries the target, can only move, headbutts it (the LinkedAction) every GrappleHeadbuttSeconds and is asked after
    /// each one whether to let go. When the stun runs out or it lets go, the throw winds up (this same action, tagged
    /// BattleSimulation.ThrowTag) and hurls the target GrappleThrowDistance the way the Tank faces, for this action's Damage.
    /// </summary>
    public class SubjugateBehavior : ActionBehavior
    {
        // The grab's own numbers read as the hold; Damage is the throw's; the headbutts are the linked action's.
        public override void Describe(AbilityTip tip)
        {
            var headbutt = tip.Def.LinkedAction;
            if (headbutt != null)
                tip.Add("Headbutts", string.Format("{0:0} / {1:0.##}s", tip.Who != null ? tip.Who.ActionStat(headbutt, ActionStat.Damage) : headbutt.Damage, tip.Sim != null ? tip.Sim.GrappleHeadbuttSeconds : 0.5f));
            DescribeHit(tip, "Throw", "Hold", false);
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            if (a.Tag == BattleSimulation.ThrowTag) return;
            sim.ApplyStatus(a.Actor, new StatusEffect { Id = StatusIds.Grappling, SourceId = a.Actor.Id, ExpiresAt = float.MaxValue, Flags = StatusFlags.StunImmune  }, a.Actor);
        }

        // An interrupted grab loses its stun immunity once the windup is over (BattleSimulation.TickGrapples); an
        // interrupted throw drops the target where it is.
        public override void OnInterrupted(BattleSimulation sim, ActionInstance a)
        {
            if (a.Tag == BattleSimulation.ThrowTag) sim.EndGrapple(a.Actor);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            var target = sim.GetUnit(a.TargetUnitId);
            var def = a.Definition;
            if (a.Tag == BattleSimulation.ThrowTag)
            {
                Vector3 dir = sim.GrappleFacing(actor);
                sim.EndGrapple(actor);   // let go first: the throw's push moves it from here
                if (target == null || !target.IsAlive) return;
                Vector3 landed = sim.Paths.ClampDash(target.Position, target.Position + dir * sim.GrappleThrowDistance);
                effects.Add(new AttackEffect
                {
                    Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = BattleSimulation.ThrowTag,
                    StunMultiplier = def.StunDuration > 0f ? sim.GrappleThrowStun / actor.ActionStat(def, ActionStat.StunDuration) : 0f,   // the throw stuns for GrappleThrowStun whatever the grab's stun
                    AccuracyBonus = 1f, IgnoreRange = true, IgnoreCover = true, HasPush = true, PushTo = landed,   // it is in your hands: it can't miss
                });
                return;
            }
            if (target == null || !target.IsAlive) return;
            effects.Add(new AttackEffect
            {
                Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = 0f, Tag = a.Tag,   // the grab itself does no damage
                OnLanded = (s, t) => s.StartGrapple(actor, t, def),
            });
        }
    }

    /// <summary>
    /// Heretic's Ascent (aerial_slam): a grab, a single hit (usually Heavy, so it breaks a windup) that stuns; if it lands and
    /// the target is stunned, the user carries it up into the air and slams it down behind itself (BattleSimulation.StartAerialSlam:
    /// height DisplaceDistance, timing AirRiseSeconds / AirHoldSeconds / AirSlamSeconds, StunDuration again after the landing).
    /// </summary>
    public class AerialSlamBehavior : ActionBehavior
    {
        protected override void DescribeMovement(AbilityTip tip)
        {
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Slam", string.Format("from {0:0.#}m up", tip.V(ActionStat.DisplaceDistance)));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return;
            var actor = a.Actor;
            var def = a.Definition;
            effects.Add(new AttackEffect
            {
                Source = actor, TargetId = target.Id, Definition = def, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                OnLanded = (s, t) => s.StartAerialSlam(actor, t, def),
            });
        }
    }

    /// <summary>
    /// Behemoth's Arrival (arrival): blink to the target point (as far as Range that way, like Polystep), then slam down: a
    /// hit on every enemy in AreaRadius of where it lands (no cover, no range check) that lifts each one it stuns
    /// DisplaceDistance metres into the air for the stun, to drop and take fall damage (BattleSimulation.LiftUnit, as Amblivate).
    /// </summary>
    public class ArrivalBehavior : BlinkBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return true; }

        protected override void DescribeMovement(AbilityTip tip)
        {
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Lift", AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            if (actor.IsRooted) return;
            base.OnResolve(sim, a, effects);   // the blink
            float height = a.Stat(ActionStat.DisplaceDistance);
            foreach (var target in sim.UnitsInArea(actor, a.Definition, actor.Position))
                effects.Add(new AttackEffect
                {
                    Source = actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                    IgnoreRange = true, IgnoreCover = true,
                    OnLanded = height > 0f ? (s, t) => s.LiftUnit(t, height, actor) : (System.Action<BattleSimulation, Unit>)null,
                });
        }
    }

    /// <summary>
    /// Cognitive Burden (strain_burden): take up to 'transfer' (50) of an ally's strain onto yourself and be Burdened for
    /// 'seconds' (3, scaled like a duration). While Burdened, the first hit an enemy lands on you hands that enemy the
    /// strain you took on (as much of it as you still carry; the status's Stacks), and Burdened ends. No hit roll.
    /// </summary>
    public class StrainBurdenBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("transfer", 50f); yield return new BehaviorParam("seconds", 3f); }
        }

        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Takes on", string.Format("up to {0:0} strain", Param(tip.Def, "transfer")));
            tip.Add("Burdened", AbilityTip.Secs(Param(tip.Def, "seconds") * (tip.Who != null ? tip.Who.DurationScale(tip.Def) : 1f)));
        }

        public override void Attach(BattleSimulation sim)
        {
            sim.Triggers.Subscribe(TriggerType.DamageTaken, ctx =>
            {
                var u = ctx.Subject;
                var burden = u.GetStatus(StatusIds.Burdened);
                var enemy = ctx.Other;
                if (burden == null || enemy == null || enemy.Team == u.Team || !enemy.IsAlive) return;
                float amount = Mathf.Min(burden.Stacks, u.Strain);
                sim.RemoveStatus(u, StatusIds.Burdened);
                if (amount <= 0f) return;
                sim.AddStrain(u, -amount);
                sim.AddStrain(enemy, amount);
            });
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            var ally = sim.GetUnit(a.TargetUnitId);
            if (ally == null || ally == actor || !sim.InReach(actor, a.Definition, ally)) return;
            float take = Mathf.Min(ally.Strain, Param(a.Definition, "transfer"));
            if (take > 0f) { sim.AddStrain(ally, -take); sim.AddStrain(actor, take); }
            sim.ApplyStatus(actor, new StatusEffect
            {
                Id = StatusIds.Burdened, SourceId = actor.Id, Stacks = Mathf.RoundToInt(take),
                ExpiresAt = sim.Now + Param(a.Definition, "seconds") * actor.DurationScale(a.Definition),
            }, actor);
        }
    }

    /// <summary>
    /// Death Sentence (telekinetic_pull): winds up on an enemy it must keep in sight (losing it breaks the windup), then
    /// grabs it and channels (ChannelSeconds, at most): the target is held (stunned) and dragged toward the user at
    /// pull_speed (4 m/s) until it arrives, the channel ends, or the user is hit (the pull stops where it is). An airborne
    /// target is instead thrown straight down: air_speed (4) times as fast as a fall, air_fall_damage (2) times the fall
    /// damage, and the channel ends there.
    /// </summary>
    public class TelekineticPullBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("pull_speed", 4f);
                yield return new BehaviorParam("air_speed", 4f);
                yield return new BehaviorParam("air_fall_damage", 2f);
            }
        }

        public override bool RollsToHit(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            tip.Add("Pull", string.Format("{0:0.#} m/s, up to {1}", Param(tip.Def, "pull_speed"), AbilityTip.Secs(tip.Def.ChannelSeconds)));
            tip.Add("Airborne target", string.Format("slammed down x{0:0.#} speed, x{1:0.#} fall damage", Param(tip.Def, "air_speed"), Param(tip.Def, "air_fall_damage")));
            tip.Add("Windup", "breaks if it loses sight of the target");
        }

        public override bool OnWindupTick(BattleSimulation sim, ActionInstance a)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            return target != null && target.IsAlive && sim.HasSightOf(a.Actor, a.Definition, target);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            var target = sim.GetUnit(a.TargetUnitId);
            a.Steps = -1;   // nothing to pull (the channel ends at once) unless set below
            if (target == null || !target.IsAlive || !sim.InReach(actor, a.Definition, target)) return;
            if (sim.SlamToGround(target, actor, Param(a.Definition, "air_speed"), Param(a.Definition, "air_fall_damage"))) return;
            Vector3 dir = actor.Position - target.Position; dir.y = 0f;
            float gap = dir.magnitude - (actor.BodyRadius + target.BodyRadius + 0.3f);
            if (gap <= 0.05f) return;
            float speed = Mathf.Max(0.1f, Param(a.Definition, "pull_speed"));
            float dist = Mathf.Min(gap, speed * a.Definition.ChannelSeconds);
            float seconds = dist / speed;
            sim.StunUnit(target, seconds + BattleSimulation.TickLength, actor);   // held while it is dragged
            sim.PushUnit(target, target.Position + dir.normalized * dist, seconds, actor);
            a.Steps = 1;
            a.NextStepAt = sim.Now + seconds;
        }

        public override bool OnChannelTick(BattleSimulation sim, ActionInstance a)
        {
            sim.CutChannel(a.Actor, a.Steps > 0 ? a.NextStepAt : sim.Now);   // over when the pull arrives, recovery after
            var target = sim.GetUnit(a.TargetUnitId);
            return a.Steps <= 0 || (target != null && target.IsAlive);
        }

        public override void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken)
        {
            if (a.Steps > 0 && broken) sim.StopGlide(sim.GetUnit(a.TargetUnitId));   // let go: it stops where it is
            a.Steps = 0;
        }
    }
}
