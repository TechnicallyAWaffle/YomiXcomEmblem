using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // Abilities of the Wraith, Monarch, Zola and Primavaeran that need rules beyond data. The rest are data on existing
    // behaviors: Rend and Divine Javelin (basic_attack), Mental Overload and Recurrent Repel (area_attack: Recurrent Repel
    // pushes with Displace AwayFromSource), Abstract Decay and Wasteful Thought (aura: a zone at the target point, and an
    // always-on dome; their damage and strain per second are the definition's Aura fields).

    /// <summary>
    /// Flying for the length of an ability: up DisplaceDistance (and 'forward' toward a target) over the windup, then back
    /// down over the recovery once the ability is done. Behaviors that fly derive from this. A unit already in the air (the
    /// Monarch after its Ascension) just stays where it is.
    /// </summary>
    public abstract class FlyingBehavior : ActionBehavior
    {
        /// <summary>Take off as the windup starts, toward 'to' (in the air); the landing comes when the ability ends (Land).</summary>
        protected static void TakeOff(BattleSimulation sim, ActionInstance a, Vector3 to)
        {
            float windup = Mathf.Max(BattleSimulation.TickLength, a.ResolveAt - sim.Now);
            float recovery = Mathf.Max(BattleSimulation.TickLength, a.Stat(ActionStat.Recovery) / a.Actor.EffectiveActionSpeed());
            a.OwnsHover = sim.HoverUnit(a.Actor, to, float.MaxValue, null, windup, recovery);
        }

        /// <summary>Come back down, if this ability took the user up.</summary>
        protected static void Land(BattleSimulation sim, ActionInstance a)
        {
            if (!a.OwnsHover) return;
            a.OwnsHover = false;
            sim.EndHover(a.Actor, false, null);
        }

        public override void OnInterrupted(BattleSimulation sim, ActionInstance a) { Land(sim, a); }

        protected override void DescribeMovement(AbilityTip tip)
        {
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Flies", AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)) + " up");
        }
    }

    /// <summary>
    /// Horde of Memories (summon): fly up over the windup, then channel (ChannelSeconds), bringing in SummonCount SummonUnit
    /// evenly through it on the ground around the spot below, on the user's side, then fly back down over the recovery.
    /// Without a channel they all come at once. A broken channel stops the rest.
    /// </summary>
    public class SummonBehavior : FlyingBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters { get { yield return new BehaviorParam("spread", 2.5f); } }

        public override void Describe(AbilityTip tip)
        {
            base.Describe(tip);
            if (tip.Def.SummonUnit != null)
                tip.Add("Summons", string.Format("{0} {1}{2}", tip.Def.SummonCount, tip.Def.SummonUnit.DisplayName, tip.Def.ChannelSeconds > 0f ? " over " + AbilityTip.Secs(tip.Def.ChannelSeconds) : ""));
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            float h = a.Stat(ActionStat.DisplaceDistance);
            if (h > 0f) TakeOff(sim, a, a.Actor.Position + Vector3.up * h);
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            a.Steps = 0;
            a.NextStepAt = sim.Now;
            if (a.Definition.ChannelSeconds > 0f) return;   // the channel brings them
            while (a.Steps < a.Definition.SummonCount) Summon(sim, a);
            Land(sim, a);
        }

        public override bool OnChannelTick(BattleSimulation sim, ActionInstance a)
        {
            int count = a.Definition.SummonCount;
            float interval = a.Definition.ChannelSeconds / Mathf.Max(1, count);
            while (a.Steps < count && sim.Now + 1e-4f >= a.NextStepAt) { Summon(sim, a); a.NextStepAt += interval; }
            return true;
        }

        public override void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken) { Land(sim, a); }

        private void Summon(BattleSimulation sim, ActionInstance a)
        {
            int count = Mathf.Max(1, a.Definition.SummonCount);
            float angle = (a.Steps + 0.5f) / count * Mathf.PI * 2f;
            Vector3 below = sim.Terrain.OnGround(a.Actor.Position);
            Vector3 at = below + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Param(a.Definition, "spread");
            sim.SpawnUnit(a.Definition.SummonUnit, a.Actor.Team, at, a.Actor, a.Definition.SummonAIStyle);
            a.Steps++;
        }
    }

    /// <summary>
    /// Incineration (beam): over the windup, fly DisplaceDistance up and forward until within beam_range of the target, then
    /// channel a beam at it (ChannelSeconds): the ability's Damage per second, in hits_per_second hits that each carry its
    /// stun. The beam ends if the target dies or gets out of beam_range; then the flight down over the recovery. Losing
    /// sight of the target (unless the ability pierces terrain) interrupts the windup or breaks the beam.
    /// </summary>
    public class BeamBehavior : FlyingBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("beam_range", 12f); yield return new BehaviorParam("hits_per_second", 4f); }
        }

        public override bool RollsToHit(ActionDefinition def) { return true; }

        public override void Describe(AbilityTip tip)
        {
            DescribeHit(tip, "Damage / s", "Stun per hit", true);
            DescribeMovement(tip);
            tip.Add("Beam", string.Format("{0}, {1:0}m from where it flies to", AbilityTip.Secs(tip.Def.ChannelSeconds), Param(tip.Def, "beam_range")));
            if (!tip.Def.PiercesTerrain) tip.Add("Sight", "lost: interrupted");
        }

        /// <summary>The user can still see its target (always, for a beam that pierces terrain).</summary>
        private static bool Sees(BattleSimulation sim, ActionInstance a, Unit target)
        {
            return target != null && sim.HasSightOf(a.Actor, a.Definition, target);
        }

        public override bool OnWindupTick(BattleSimulation sim, ActionInstance a)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            return target == null || !target.IsAlive || Sees(sim, a, target);   // a dead target is the resolve's business
        }

        public override void OnWindupStart(BattleSimulation sim, ActionInstance a)
        {
            var actor = a.Actor;
            var target = sim.GetUnit(a.TargetUnitId);
            Vector3 to = actor.Position;
            if (target != null)
            {
                Vector3 d = target.Position - actor.Position; d.y = 0f;
                float closer = Mathf.Max(0f, d.magnitude - Param(a.Definition, "beam_range") * 0.75f);
                if (d.sqrMagnitude > 1e-6f) to += d.normalized * closer;
            }
            to = sim.Paths.ClampDash(actor.Position, to);   // over the ground it could reach, not through walls
            TakeOff(sim, a, sim.Terrain.OnGround(to) + Vector3.up * a.Stat(ActionStat.DisplaceDistance));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            a.Steps = 0;
            a.NextStepAt = sim.Now;
            if (a.Definition.ChannelSeconds <= 0f) Land(sim, a);
        }

        public override bool OnChannelTick(BattleSimulation sim, ActionInstance a)
        {
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive) return false;
            if (Vector3.Distance(a.Actor.Position, target.Position) > Param(a.Definition, "beam_range") * BattleSimulation.RangeTolerance) return false;
            if (!Sees(sim, a, target)) { sim.EndChannel(a.Actor, "sight lost"); return true; }
            float hps = Mathf.Max(0.5f, Param(a.Definition, "hits_per_second"));
            while (sim.Now + 1e-4f >= a.NextStepAt)
            {
                sim.StrikeNow(new AttackEffect { Source = a.Actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier / hps, Tag = a.Tag, IgnoreRange = true, IgnoreCover = true });
                a.NextStepAt += 1f / hps;
                a.Steps++;
            }
            return true;
        }

        public override void OnChannelEnd(BattleSimulation sim, ActionInstance a, bool broken) { Land(sim, a); }
    }

    /// <summary>
    /// Stomp (area_lift): hits every enemy in the area (AreaAttackBehavior), and each one it stuns is flung DisplaceDistance up,
    /// to drop and take fall damage (BattleSimulation.LiftUnit, as Amblivate).
    /// </summary>
    public class AreaLiftBehavior : AreaAttackBehavior
    {
        protected override void DescribeMovement(AbilityTip tip)
        {
            if (tip.V(ActionStat.DisplaceDistance) > 0f) tip.Add("Knock-up", AbilityTip.Metres(tip.V(ActionStat.DisplaceDistance)));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            float height = a.Stat(ActionStat.DisplaceDistance);
            foreach (var target in sim.UnitsInArea(actor, a.Definition, a.TargetPos))
                effects.Add(new AttackEffect
                {
                    Source = actor, TargetId = target.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                    IgnoreRange = true, IgnoreCover = true,
                    OnLanded = height > 0f ? (s, t) => s.LiftUnit(t, height, actor) : (System.Action<BattleSimulation, Unit>)null,
                });
        }
    }

    /// <summary>
    /// Distortion (distortion): teleport to the target point (as far as Range that way), leaving a rift where the user stood:
    /// every enemy within rift_radius (4) of it is hit (the ability's damage) and pulled up to 'pull' (4) metres into it.
    /// </summary>
    public class DistortionBehavior : BlinkBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get { yield return new BehaviorParam("rift_radius", 4f); yield return new BehaviorParam("pull", 4f); }
        }

        public override bool RollsToHit(ActionDefinition def) { return true; }

        public override void Describe(AbilityTip tip)
        {
            DescribeHit(tip, "Rift damage", "Stun", true);
            tip.Add("Rift", string.Format("{0:0}m where you were, pulls {1:0}m in", Param(tip.Def, "rift_radius"), Param(tip.Def, "pull")));
            tip.Add("Teleport", "up to " + AbilityTip.Metres(tip.V(ActionStat.Range)));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor;
            if (actor.IsRooted) return;
            Vector3 rift = actor.Position;
            float radius = Param(a.Definition, "rift_radius"), pull = Param(a.Definition, "pull");
            foreach (var u in sim.State.Units)
            {
                if (!u.IsAlive || u.Team == actor.Team) continue;
                Vector3 d = rift - u.Position; d.y = 0f;
                if (d.magnitude - u.BodyRadius > radius) continue;
                float move = Mathf.Min(pull, Mathf.Max(0f, d.magnitude - u.BodyRadius));
                effects.Add(new AttackEffect
                {
                    Source = actor, TargetId = u.Id, Definition = a.Definition, DamageMultiplier = a.DamageMultiplier, Tag = a.Tag,
                    IgnoreRange = true, IgnoreCover = true,
                    HasPush = move > 0.05f, PushTo = u.Position + (d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.zero) * move,
                });
            }
            sim.EmitFx(actor, null, "rift", a.Definition, rift, radius);
            base.OnResolve(sim, a, effects);   // the teleport
        }
    }

    /// <summary>
    /// Tear Apart Reality (reality_tear): a long windup on a point, then a channel (ChannelSeconds) that places 'explosions'
    /// (10) delayed blasts through it, each 'explosion_delay' (0.5s) before it goes off, explosion_radius (3) wide, for the
    /// ability's damage and stun. The points fall within AreaRadius of the target point, picked to spread out: each the best
    /// of a few random candidates for distance from the points already used. A broken channel places no more.
    /// </summary>
    public class RealityTearBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("explosions", 10f);
                yield return new BehaviorParam("explosion_delay", 0.5f);
                yield return new BehaviorParam("explosion_radius", 3f);
                yield return new BehaviorParam("candidates", 8f);
            }
        }

        public override bool RollsToHit(ActionDefinition def) { return true; }

        public override void Describe(AbilityTip tip)
        {
            DescribeHit(tip, "Each explosion", "Stun", true);
            tip.Add("Explosions", string.Format("{0:0} over {1}, {2:0}m wide, {3} after they appear", Param(tip.Def, "explosions"), AbilityTip.Secs(tip.Def.ChannelSeconds),
                Param(tip.Def, "explosion_radius") * 2f, AbilityTip.Secs(Param(tip.Def, "explosion_delay"))));
            tip.Add("Area", AbilityTip.Metres(tip.V(ActionStat.AreaRadius)) + " around the point");
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            a.Steps = 0;
            a.NextStepAt = sim.Now;
            a.Points = new List<Vector3>();
            if (a.Definition.ChannelSeconds <= 0f) while (a.Steps < Count(a)) Place(sim, a);
        }

        public override bool OnChannelTick(BattleSimulation sim, ActionInstance a)
        {
            float interval = a.Definition.ChannelSeconds / Mathf.Max(1, Count(a));
            while (a.Steps < Count(a) && sim.Now + 1e-4f >= a.NextStepAt) { Place(sim, a); a.NextStepAt += interval; }
            return true;
        }

        private int Count(ActionInstance a) { return Mathf.Max(1, Mathf.RoundToInt(Param(a.Definition, "explosions"))); }

        private void Place(BattleSimulation sim, ActionInstance a)
        {
            float area = a.Stat(ActionStat.AreaRadius);
            Vector3 best = a.TargetPos;
            float bestScore = -1f;
            int candidates = Mathf.Max(1, Mathf.RoundToInt(Param(a.Definition, "candidates")));
            for (int i = 0; i < candidates; i++)
            {
                // Uniform over the disc (sqrt for the radius), then kept if it is furthest from the explosions so far.
                float r = Mathf.Sqrt(sim.RandomValue()) * area, ang = sim.RandomValue() * Mathf.PI * 2f;
                Vector3 p = a.TargetPos + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                float score = float.MaxValue;
                foreach (var q in a.Points) score = Mathf.Min(score, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z)));
                if (a.Points.Count == 0) score = -r;   // the first one: nearest the middle of the candidates
                if (score > bestScore) { bestScore = score; best = p; }
            }
            a.Points.Add(best);
            sim.ScheduleBlast(a.Actor, a.Definition, best, Param(a.Definition, "explosion_radius"), Param(a.Definition, "explosion_delay"));
            a.Steps++;
        }
    }
}
