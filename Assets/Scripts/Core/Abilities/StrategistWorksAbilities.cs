using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // The Strategist's works: Wrathful Reflection, Obstruction and Compositions of Destruction. Strange Axis is an aura
    // left at the target point whose status turns damage into strain (StatusSpec.DamageAsStrain).

    /// <summary>
    /// Wrathful Reflection (reflect_shield): an ally within Range (or the user) gets the ability's TargetStatusesOnHit: a
    /// shield (StatusSpec.Shield) that reflects (ReflectShare of the damage and stun each attack on it deals, back at the
    /// attacker ReflectDelay later, with the attack's impact: BattleSimulation.Reflect). Used up, the shield and its
    /// reflection end. No roll.
    /// </summary>
    public class ReflectShieldBehavior : ActionBehavior
    {
        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        public override void Describe(AbilityTip tip)
        {
            foreach (var s in tip.Def.TargetStatusesOnHit)
            {
                if (s == null) continue;
                if (s.Shield > 0f) tip.Add("Shield", string.Format("{0:0} for {1}", s.Shield, AbilityTip.Secs(s.Duration * (tip.Who != null ? tip.Who.DurationScale(tip.Def) : 1f))));
                if (s.ReflectShare > 0f) tip.Add("Reflects", string.Format("{0:0}% of damage and stun, {1} later", s.ReflectShare * 100f, AbilityTip.Secs(s.ReflectDelay)));
            }
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            var target = sim.GetUnit(a.TargetUnitId);
            if (target == null || !target.IsAlive || target.Team != actor.Team || (target != actor && !sim.InReach(actor, def, target))) return;
            foreach (var spec in def.TargetStatusesOnHit)
            {
                if (spec == null) continue;
                var s = spec.Create(sim.Now, actor.Id, null, actor.DurationScale(def));
                s.FromAction = def;   // the reflected hits are this ability's (BattleSimulation.Reflect)
                sim.ApplyStatus(target, s, actor);
            }
            sim.EmitFx(actor, target, "wrathful_reflection", def, target.Position, 0f, actor.Position);
        }
    }

    /// <summary>
    /// Obstruction (wall): a wall 'length' (10m) long and 'height' (5m) tall at the target point for 'seconds' (4s). It
    /// blocks line of sight (a row of occluders); units walk through it, and an enemy that does gets the ability's
    /// TargetStatusesOnHit (CRIPPLED) as it enters, kept up while it stays inside. The player turns it before placing it
    /// (hold R: UnitCommand.Facing); unturned (the AI), it stands across the line from the user to the point.
    /// </summary>
    public class WallBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("length", 10f);
                yield return new BehaviorParam("height", 5f);
                yield return new BehaviorParam("seconds", 4f);
                yield return new BehaviorParam("thickness", 0.6f);
            }
        }

        public override bool RollsToHit(ActionDefinition def) { return false; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }
        public override bool Rotatable(ActionDefinition def) { return true; }

        public override void Describe(AbilityTip tip)
        {
            var d = tip.Def;
            tip.Add("Wall", string.Format("{0:0.#}m long, {1:0.#}m tall, for {2}", Param(d, "length"), Param(d, "height"), AbilityTip.Secs(Param(d, "seconds") * (tip.Who != null ? tip.Who.DurationScale(d) : 1f))));
            tip.Add("Blocks", "line of sight");
            tip.Add("Placing", "hold R to turn it");
        }

        /// <summary>Which way the wall runs (flat, unit length): 'facing' in degrees if set, else across the line from 'from' to 'at'.</summary>
        public static Vector3 Direction(Vector3 from, Vector3 at, float facing)
        {
            if (!float.IsNaN(facing))
            {
                float r = facing * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
            }
            Vector3 d = at - from; d.y = 0f;
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            return new Vector3(-d.z, 0f, d.x);
        }

        /// <summary>The default facing (degrees) for a wall placed at 'at' from 'from': across the line between them.</summary>
        public static float DefaultFacing(Vector3 from, Vector3 at)
        {
            var d = Direction(from, at, float.NaN);
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            Vector3 at = a.TargetPos;
            Vector3 flat = at - actor.Position; flat.y = 0f;
            float reach = a.Stat(ActionStat.Range);
            if (flat.magnitude > reach) at = actor.Position + flat.normalized * reach;
            sim.RaiseWall(actor, def, at, Direction(actor.Position, at, a.Facing), Param(def, "length"), Param(def, "height"), Param(def, "thickness"),
                          Param(def, "seconds") * actor.DurationScale(def));
        }
    }

    /// <summary>
    /// Compositions of Destruction (composition): a slow projectile, its SummonUnit (a small unit enemies can't target: so
    /// the Strategist's own displacements reach it), sent toward the target point at 'speed' (3 m/s) for up to 'lifetime'
    /// (8s). The first enemy within 'impact_radius' (2m) of it takes the ability's hit, and it is gone. Teleported, pushed or
    /// pulled (Theory Of Displacement, Wrinkles Of Reality, a knockback), it stops, and once the move is over it explodes
    /// after 'blast_delay' (0.5s), hitting every enemy within 'blast_radius' (8m) with the same hit. Into a wall, it fizzles.
    /// </summary>
    public class CompositionBehavior : ActionBehavior
    {
        public override IEnumerable<BehaviorParam> Parameters
        {
            get
            {
                yield return new BehaviorParam("speed", 3f);
                yield return new BehaviorParam("impact_radius", 2f);
                yield return new BehaviorParam("blast_radius", 8f);
                yield return new BehaviorParam("blast_delay", 0.5f);
                yield return new BehaviorParam("lifetime", 8f);
            }
        }

        public override AbilityRole DefaultRoles(ActionDefinition def) { return base.DefaultRoles(def) | AbilityRole.AreaBlast; }
        public override bool RollsToHit(ActionDefinition def) { return true; }
        public override bool ApproachesTarget(ActionDefinition def) { return false; }

        public override bool CanStart(BattleSimulation sim, Unit actor, UnitCommand cmd, out string reason)
        {
            reason = cmd.Action.SummonUnit == null ? "nothing to send" : null;
            return reason == null;
        }

        public override void Describe(AbilityTip tip)
        {
            var d = tip.Def;
            DescribeHit(tip, "Damage", "Stun", true);
            tip.Add("Projectile", string.Format("{0:0.#} m/s, hits within {1:0.#}m", Param(d, "speed"), Param(d, "impact_radius")));
            tip.Add("Displaced", string.Format("explodes {0} after, {1:0.#}m wide", AbilityTip.Secs(Param(d, "blast_delay")), Param(d, "blast_radius")));
        }

        public override void OnResolve(BattleSimulation sim, ActionInstance a, List<SimEffect> effects)
        {
            var actor = a.Actor; var def = a.Definition;
            if (def.SummonUnit == null) return;
            Vector3 dir = a.TargetPos - actor.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 spot = sim.Paths.ClampDash(actor.Position, actor.Position + dir * (actor.BodyRadius + 0.7f));
            var state = new CompositionState
            {
                Source = actor, Definition = def, Direction = dir, Speed = Param(def, "speed"), ImpactRadius = Param(def, "impact_radius"),
                BlastRadius = a.Stat(ActionStat.AreaRadius) > 0f ? a.Stat(ActionStat.AreaRadius) : Param(def, "blast_radius"), BlastDelay = Param(def, "blast_delay"),
                EndsAt = sim.Now + Param(def, "lifetime"),
            };
            sim.SpawnUnit(def.SummonUnit, actor.Team, spot, actor, InertAI.Style, u =>
            {
                u.AddState(state);
                state.EndsAt = sim.Now + Param(def, "lifetime");
                sim.ApplyStatus(u, new StatusEffect { Id = StatusIds.Composition, SourceId = actor.Id, ExpiresAt = float.MaxValue, Flags = StatusFlags.Untargetable }, actor);
            });
            sim.EmitFx(actor, null, "composition", def, spot, 0f, actor.Position);
        }
    }

    /// <summary>Compositions of Destruction's projectile, on its unit (BattleSimulation.TickCompositions).</summary>
    public class CompositionState : ControlState
    {
        public Unit Source;
        public ActionDefinition Definition;
        public Vector3 Direction;
        public float Speed, ImpactRadius, BlastRadius, BlastDelay, EndsAt;
        public bool Armed;   // displaced: it explodes once the move is over

        public override bool BlocksDodgeDash { get { return true; } }

        public override void OnDisplaced(BattleSimulation sim)
        {
            if (Armed) return;
            Armed = true;
            sim.EmitFx(Unit, null, "composition_armed", Definition, Unit.Position, BlastRadius);
        }
    }

    /// <summary>A unit that never acts by itself (AIProfile "inert": Compositions of Destruction's projectile).</summary>
    public class InertAI : IUnitAI
    {
        public const string Style = "inert";
        public UnitCommand Decide(BattleSimulation sim, Unit unit) { return UnitCommand.Idle(unit.Id, 1f); }
    }

    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ walls (Obstruction)

        private class Wall
        {
            public string Id; public Unit Source; public ActionDefinition Definition;
            public Vector3 A, B; public float Thickness, EndsAt;
            public readonly HashSet<string> Inside = new HashSet<string>();
        }

        private readonly List<Wall> walls = new List<Wall>();
        private int wallCounter;

        /// <summary>
        /// Raise a wall centred on 'center', running along 'dir', for 'seconds': a row of sight-blocking occluders 'height' tall,
        /// and a strip 'thickness' wide that puts 'def's TargetStatusesOnHit on the owner's enemies passing through it.
        /// </summary>
        public void RaiseWall(Unit source, ActionDefinition def, Vector3 center, Vector3 dir, float length, float height, float thickness, float seconds)
        {
            center = Terrain.OnGround(center);
            dir.y = 0f; dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            var w = new Wall { Id = "wall_" + (++wallCounter), Source = source, Definition = def, A = center - dir * (length / 2f), B = center + dir * (length / 2f), Thickness = thickness, EndsAt = Now + seconds };
            walls.Add(w);
            float radius = Mathf.Max(0.2f, thickness / 2f);
            int posts = Mathf.Max(2, Mathf.CeilToInt(length / (radius * 1.5f)) + 1);
            for (int i = 0; i < posts; i++)
            {
                Vector3 p = Vector3.Lerp(w.A, w.B, i / (float)(posts - 1));
                State.Occluders.Add(new Occluder { Id = w.Id + "_" + i, OwnerId = source != null ? source.Id : null, Position = Terrain.OnGround(p), Radius = radius, Height = height, ExpiresAt = w.EndsAt });
            }
            Emit(EventType.WallRaised, source != null ? source.Id : null, null, new WallInfo { Id = w.Id, ActionId = def != null ? def.Id : null, From = w.A, To = w.B, Height = height, Thickness = thickness, EndsAt = w.EndsAt });
        }

        /// <summary>Walls running out, and enemies inside them (Obstruction's CRIPPLE as they enter, kept up while they stay).</summary>
        private void TickWalls()
        {
            for (int i = walls.Count - 1; i >= 0; i--)
            {
                var w = walls[i];
                if (Now + 1e-4f >= w.EndsAt)
                {
                    walls.RemoveAt(i);
                    Emit(EventType.WallEnded, w.Source != null ? w.Source.Id : null, null, new WallInfo { Id = w.Id, From = w.A, To = w.B, EndsAt = w.EndsAt });
                    continue;
                }
                if (w.Definition == null || w.Definition.TargetStatusesOnHit == null || w.Source == null) continue;
                foreach (var u in State.Units)
                {
                    if (!u.IsAlive || u.Team == w.Source.Team) continue;
                    bool inside = DistanceToSegmentFlat(u.Position, w.A, w.B) <= u.BodyRadius + w.Thickness / 2f;
                    if (!inside) { w.Inside.Remove(u.Id); continue; }
                    bool entering = w.Inside.Add(u.Id);
                    foreach (var spec in w.Definition.TargetStatusesOnHit)
                    {
                        if (spec == null || !spec.AppliesToUnit(w.Source, u)) continue;
                        if (!entering && u.HasStatus(spec.Id)) continue;   // inside still: only kept up, not re-announced every tick
                        ApplyStatus(u, spec.Create(Now, w.Source.Id, null, w.Source.DurationScale(w.Definition)), w.Source);
                    }
                }
            }
        }

        private static float DistanceToSegmentFlat(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x, abz = b.z - a.z, apx = p.x - a.x, apz = p.z - a.z;
            float len = abx * abx + abz * abz;
            float t = len > 1e-6f ? Mathf.Clamp01((apx * abx + apz * abz) / len) : 0f;
            float dx = apx - abx * t, dz = apz - abz * t;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------ Compositions of Destruction

        /// <summary>
        /// Each composition drifts on (unless it is being moved), strikes the first enemy that comes within its impact radius,
        /// fizzles into a wall or when its time is up, and, once displaced and the move is over, explodes after its delay.
        /// </summary>
        private void TickCompositions()
        {
            foreach (var u in State.Units.ToArray())
            {
                if (!u.IsAlive) continue;
                var c = u.GetState<CompositionState>();
                if (c == null) continue;
                bool moving = u.GlideEnd >= 0f || u.FallLandAt >= 0f || u.IsJumping;
                if (moving) continue;
                if (c.Armed)
                {
                    ScheduleBlast(c.Source, c.Definition, u.Position, c.BlastRadius, c.BlastDelay);
                    EndComposition(u, "composition_blast");
                    continue;
                }
                if (Now >= c.EndsAt) { EndComposition(u, "composition_fizzle"); continue; }
                Unit struck = null; float best = float.MaxValue;
                foreach (var e in State.Units)
                {
                    if (!e.IsAlive || e.Team == u.Team || e.IsHidden || e.GetState<CompositionState>() != null) continue;
                    float d = FlatDistance(e.Position, u.Position) - e.BodyRadius;
                    if (d <= c.ImpactRadius && d < best) { best = d; struck = e; }
                }
                if (struck != null)
                {
                    StrikeNow(new AttackEffect { Source = c.Source, TargetId = struck.Id, Definition = c.Definition, IgnoreRange = true, IgnoreCover = true }, true);
                    EndComposition(u, "composition_impact");
                    continue;
                }
                Vector3 want = u.Position + c.Direction * c.Speed * TickLength;
                Vector3 to = Paths.ClampDash(u.Position, want);
                if (FlatDistance(to, want) > 0.05f) { EndComposition(u, "composition_fizzle"); continue; }   // into a wall or off the map
                u.Position = Terrain.OnGround(to);
            }
        }

        private void EndComposition(Unit u, string fx)
        {
            var c = u.GetState<CompositionState>();
            EmitFx(u, null, fx, c != null ? c.Definition : null, u.Position, c != null ? c.BlastRadius : 0f);
            u.RemoveState<CompositionState>();
            Vanish(u);
        }
    }
}
