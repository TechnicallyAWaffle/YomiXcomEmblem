using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // Enemy-type abilities that need rules beyond data. The rest are data on existing behaviors:
    //   Sniper     - its own Snipe (snipe), Marking Shot and Rifle Shot (basic_attack) assets.
    //   Assassin   - Strike (basic_attack); Hidden Strike (phantom_strike, with SelfStatusesDuringWindup "hidden").
    //   Skirmisher - Strike (basic_attack); Mental Currents (self_buff: dodge and move speed).

    /// <summary>Polystep: blink (teleport) to the target point, as far as Range that way, onto the nearest walkable ground.</summary>
    public class BlinkBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Gapcloser | AbilityRole.Escape; }

        public override bool MovesUser { get { return true; } }   // not while airborne

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = actor.IsRooted ? "immobilized" : null;
            return reason == null;
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            if (actor.IsRooted) return;
            Vector3 d = a.TargetPos - actor.Position; d.y = 0f;
            float reach = a.Stat(ActionStat.Range);
            if (d.magnitude > reach) d = d.normalized * reach;
            sim.TeleportUnit(actor, sim.WalkablePointNear(actor.Position + d), actor);
        }
    }

    /// <summary>
    /// Flowing Death: a melee strike that speeds up as it chains. Each use opens (or extends) a "flow" window that lasts
    /// chain_window seconds past its recovery and counts the uses so far (its Stacks, shown over the unit); while it is open
    /// the next Flowing Death, at any target, winds up windup_step faster per stack, down to min_windup (its Params).
    /// </summary>
    public class FlowingDeathBehavior : BasicAttackBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Chain; }

        public const string FlowStatus = StatusIds.Flow;

        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("windup_step", 0.1f);
                yield return new BehaviorParam("min_windup", 0.1f);
                yield return new BehaviorParam("chain_window", 1f);
            }
        }

        public static int Stacks(Unit actor)
        {
            var flow = actor.GetStatus(FlowStatus);
            return flow != null ? flow.Stacks : 0;
        }

        public override float WindupSeconds(BattleSimulation sim, Unit actor, ActionDefinition def)
        {
            float windup = actor.ActionStat(def, ActionStat.Windup);
            float min = Param(def, "min_windup");
            return Mathf.Max(Mathf.Min(min, windup), windup - Param(def, "windup_step") * Stacks(actor));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            base.OnResolve(sim, a, effects);
            if (a.IsFree) return;
            var actor = a.Actor;
            float windowEnds = sim.Now + a.Stat(ActionStat.Recovery) / actor.EffectiveActionSpeed() + Param(a.Definition, "chain_window");
            var flow = actor.GetStatus(FlowStatus);
            if (flow == null)
            {
                flow = new StatusEffect { Id = FlowStatus, SourceId = actor.Id, ExpiresAt = windowEnds  };
                sim.ApplyStatus(actor, flow, actor);
            }
            else
            {
                flow.ExpiresAt = windowEnds;
                sim.RefreshStatusTimer(actor, flow, actor);
            }
            sim.SetStacks(actor, flow, flow.Stacks + 1);
        }
    }

    /// <summary>Repel: Bash's line push (everyone in the line shoved DisplaceDistance straight away), without its wall bonus.</summary>
    public class RepelBehavior : BashBehavior
    {
        protected override float WallMultiplierDefault { get { return 1f; } }   // it pushes, without the wall bonus
    }

    /// <summary>
    /// Deluge of Creation (a charge ability, released at an ally or the user): heals Heal, growing to ChargeHealMultiplier
    /// times that at full charge. Fizzles if the target died or left its range.
    /// </summary>
    public class ChargedHealBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Heal; }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !sim.InReach(a.Actor, a.Definition, target)) return;
            var def = a.Definition;
            effects.Add(new HealEffect { Source = a.Actor, TargetId = target.Id, Amount = a.Stat(ActionStat.Heal) * Mathf.Lerp(1f, def.ChargeHealMultiplier, a.Charge) });
        }
    }

    /// <summary>
    /// Charge (aimed at a point; Range is how far it runs): after the windup the Brutebeast rushes that way at "speed" and
    /// drags every enemy in its path along in front of it. The first of them driven into a wall (or the face of higher
    /// ground) stops the lot: then each one dragged takes this action's hit (a Heavy impact, Damage and StunDuration),
    /// which can't miss. A charge that is never stopped, or catches no one, deals nothing.
    /// </summary>
    public class ChargeBehavior : ActionBehavior
    {
        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.Rush; }

        public override bool MovesUser { get { return true; } }   // not while airborne

        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("speed", 18f);            // m/s
                yield return new BehaviorParam("wall_tolerance", 0.3f);  // stopped this far short of its full run: it hit a wall
            }
        }

        // Reads its numbers for PlanCharge (static: the AI plans charges too).
        private static readonly ChargeBehavior Numbers = new ChargeBehavior();
        public const string WallTag = "charge_wall";
        private const float Gap = BattleSimulation.StandingGap + 0.05f;   // between the charger and the one it pushes (room to stand)

        public struct Plan
        {
            public Vector3 End;              // where the charger stops
            public List<Unit> Caught;        // dragged along, nearest first
            public List<Vector3> CaughtEnds; // where each one ends up
            public bool Walled;              // stopped short by a wall with someone in front: they take the hit
            public float Seconds;            // how long the rush takes
        }

        /// <summary>What a charge toward 'aim' would do right now (the AI uses this to look for a wall to pin someone on).</summary>
        public static Plan PlanCharge(BattleSimulation sim, Unit actor, ActionDefinition def, Vector3 aim)
        {
            var plan = new Plan { Caught = new List<Unit>(), CaughtEnds = new List<Vector3>() };
            Vector3 start = actor.Position;
            Vector3 dir = aim - start; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            float run = Along(sim.Paths.ClampDash(start, start + dir * actor.ActionStat(def, ActionStat.Range)) - start, dir);

            // Who is in the way (the charger's width), nearest first.
            var along = new List<float>();
            foreach (var e in sim.EnemiesOf(actor))
            {
                if (e.IsLifted) continue;
                Vector3 v = e.Position - start; v.y = 0f;
                float d = Vector3.Dot(v, dir), reach = actor.BodyRadius + e.BodyRadius;
                if (d <= 0f || d > run + reach || (v - dir * d).magnitude > reach) continue;
                int at = along.FindIndex(x => x > d);
                if (at < 0) at = along.Count;
                along.Insert(at, d);
                plan.Caught.Insert(at, e);
            }

            // Each is carried 'offset' ahead of the charger, keeping the spacing it had behind the first. The first one
            // a wall stops (pushed from where it stands) stops the charger, and so everyone.
            float stop = run;
            var offsets = new List<float>();
            for (int i = 0; i < plan.Caught.Count; i++)
            {
                var t = plan.Caught[i];
                float offset = along[i] - along[0] + actor.BodyRadius + t.BodyRadius + Gap;
                offsets.Add(offset);
                float want = Mathf.Max(0f, run + offset - along[i]);
                float got = Along(sim.Paths.ClampDash(t.Position, t.Position + dir * want) - t.Position, dir);
                if (got < want - Numbers.Param(def, "wall_tolerance")) stop = Mathf.Min(stop, along[i] + got - offset);
            }
            stop = Mathf.Max(0f, stop);
            for (int i = 0; i < plan.Caught.Count; i++)
            {
                var t = plan.Caught[i];
                float push = Mathf.Max(0f, stop + offsets[i] - along[i]);
                plan.CaughtEnds.Add(sim.Paths.ClampDash(t.Position, t.Position + dir * push));
            }
            plan.End = start + dir * stop;
            plan.Walled = plan.Caught.Count > 0 && stop < actor.ActionStat(def, ActionStat.Range) - Numbers.Param(def, "wall_tolerance");
            plan.Seconds = Mathf.Max(BattleSimulation.DashDuration, stop / Numbers.Param(def, "speed"));
            return plan;
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            var plan = PlanCharge(sim, actor, a.Definition, a.TargetPos);
            for (int i = plan.Caught.Count - 1; i >= 0; i--) sim.PushUnit(plan.Caught[i], plan.CaughtEnds[i], plan.Seconds, actor);   // the front one first
            sim.SlideUnitTo(actor, plan.End, plan.Seconds);
            if (!plan.Walled) return;
            foreach (var t in plan.Caught)
                effects.Add(new AttackEffect
                {
                    Source = actor, TargetId = t.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = WallTag,
                    AccuracyBonus = 1f, IgnoreRange = true, IgnoreCover = true,   // pinned against the wall: it can't miss
                });
        }

        private static float Along(Vector3 v, Vector3 dir) { v.y = 0f; return Vector3.Dot(v, dir); }
    }
}
