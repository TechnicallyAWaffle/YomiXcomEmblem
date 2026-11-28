using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>Whom a projectile steers toward once its homing starts.</summary>
    public enum ProjectileHoming
    {
        None,          // flies straight
        Target,        // the unit it was fired at (straight on if that one is gone)
        ClosestEnemy,  // whichever enemy of its owner is nearest to it, picked afresh each tick (the Matriarch's barbs)
    }

    /// <summary>How a projectile flies: its speed and how it gains it, how hard it turns, how big it is and how long it lasts.
    /// Behaviors build one from their Params (ProjectileBehavior.SpecFor), so each ability tunes its own.</summary>
    public class ProjectileSpec
    {
        public float Speed = 8f;           // m/s as it leaves
        public float Accel = 0f;           // m/s gained every second
        public float MaxSpeed = 40f;
        public ProjectileHoming Homing = ProjectileHoming.None;
        public float HomingDelay = 0f;     // seconds flying as launched before it starts to steer
        public float TurnRate = 180f;      // degrees a second it can turn once steering
        public float TurnGrowth = 0f;      // degrees a second added to that every second it steers (it locks on harder)
        public float HitRadius = 0.3f;     // metres: touching a body this close hits it
        public float Lifetime = 4f;        // seconds before it falls apart unspent
    }

    /// <summary>
    /// One projectile's flight as the view needs it: sampled points with their times (filled in by the simulation as it
    /// flies, so a projectile still in flight when playback reaches it is drawn up to the playback clock), how it ended
    /// (EndsAt, HitId), and what fired it. The payload of ProjectileLaunched and ProjectileEnded (the same object).
    /// </summary>
    public class ProjectilePath
    {
        public int Id;
        public string OwnerId, ActionId, HitId;
        public readonly List<float> Times = new List<float>();
        public readonly List<Vector3> Points = new List<Vector3>();
        public float EndsAt = -1f;
        // How it flies (its ProjectileSpec), for the view's estimates: the tactical view's time to impact and predicted course.
        public float Accel, MaxSpeed, Radius, Lifetime;
        public ProjectileHoming Homing;
        public float HomingDelay, TurnRate, TurnGrowth;
        public string TargetId;

        /// <summary>
        /// The course it will fly from 'time' on, as the simulation would fly it (Steer, speed gain, the ground and raised
        /// terrain ending it), added to 'into' every 'step' seconds: toward 'aimAt' if it homes (where its target is now, as
        /// if it stays there), straight on if it doesn't (aimAt ignored). It ends where it would reach aimAt, meet solid ground
        /// ('solid': under the terrain there), or fall apart (Lifetime). 'into' starts with where it is now.
        /// </summary>
        public void Predict(float time, Vector3? aimAt, System.Func<Vector3, bool> solid, List<Vector3> into, float step = 0.05f, int maxPoints = 400)
        {
            Vector3 p, dir;
            if (!PositionAt(time, out p, out dir) || Times.Count == 0) return;
            float age = time - Times[0], speed = Mathf.Max(0.1f, SpeedAt(time));
            into.Add(p);
            bool homes = Homing != ProjectileHoming.None && aimAt.HasValue;
            for (int n = 0; n < maxPoints && age < Lifetime; n++)
            {
                if (homes && age >= HomingDelay)
                {
                    Vector3 want = aimAt.Value - p;
                    if (want.sqrMagnitude > 1e-6f) dir = Turn(dir, want.normalized, (TurnRate + TurnGrowth * (age - HomingDelay)) * Mathf.Deg2Rad * step);
                }
                speed = Mathf.Min(Mathf.Max(MaxSpeed, speed), speed + Accel * step);
                Vector3 next = p + dir * (speed * step);
                age += step;
                if (homes && Vector3.Distance(next, aimAt.Value) <= Radius + 0.4f) { into.Add(next); return; }   // it gets there
                if (age > 0.05f && solid != null && solid(next)) { into.Add(next); return; }   // into the ground or a wall
                p = next;
                into.Add(p);
            }
        }

        /// <summary>'dir' turned toward 'want' (both unit length) by at most 'maxTurn' radians (spherical interpolation): how a
        /// homing projectile steers (BattleSimulation.Steer, Predict).</summary>
        public static Vector3 Turn(Vector3 dir, Vector3 want, float maxTurn)
        {
            float dot = Mathf.Clamp(Vector3.Dot(dir, want), -1f, 1f);
            float angle = Mathf.Acos(dot);
            if (angle <= maxTurn || angle < 1e-4f) return want;
            float sin = Mathf.Sin(angle);
            if (sin < 1e-4f) { want = (want + Vector3.up * 0.01f).normalized; sin = 1e-3f; }   // dead behind: tip it to pick a way round
            return (dir * (Mathf.Sin(angle - maxTurn) / sin) + want * (Mathf.Sin(maxTurn) / sin)).normalized;
        }

        /// <summary>Where it is at 'time' (its last point once it has ended; false before it was fired or after it ended).</summary>
        public bool PositionAt(float time, out Vector3 p, out Vector3 dir)
        {
            p = Vector3.zero; dir = Vector3.forward;
            int n = Times.Count;
            if (n == 0 || time < Times[0] || (EndsAt >= 0f && time > EndsAt)) return false;
            if (n == 1 || time >= Times[n - 1]) { p = Points[n - 1]; dir = n > 1 ? Points[n - 1] - Points[n - 2] : Vector3.forward; Norm(ref dir); return true; }
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (Times[mid] <= time) lo = mid; else hi = mid; }
            float k = (time - Times[lo]) / Mathf.Max(1e-5f, Times[hi] - Times[lo]);
            p = Vector3.Lerp(Points[lo], Points[hi], k);
            dir = Points[hi] - Points[lo];
            Norm(ref dir);
            return true;
        }

        /// <summary>Its speed (m/s) around 'time', from the samples either side (0 with fewer than two).</summary>
        public float SpeedAt(float time)
        {
            int n = Times.Count;
            if (n < 2) return 0f;
            int hi = 1;
            while (hi < n - 1 && Times[hi] < time) hi++;
            float dt = Times[hi] - Times[hi - 1];
            return dt > 1e-5f ? Vector3.Distance(Points[hi], Points[hi - 1]) / dt : 0f;
        }

        /// <summary>Seconds to cover 'distance' from 'speed', gaining Accel a second up to MaxSpeed (straight on, no turning).</summary>
        public float SecondsToCover(float distance, float speed)
        {
            if (distance <= 0f) return 0f;
            speed = Mathf.Max(0.1f, speed);
            if (Accel <= 1e-4f || speed >= MaxSpeed) return distance / speed;
            float tMax = (MaxSpeed - speed) / Accel, dMax = speed * tMax + 0.5f * Accel * tMax * tMax;
            if (distance <= dMax) return (-speed + Mathf.Sqrt(speed * speed + 2f * Accel * distance)) / Accel;
            return tMax + (distance - dMax) / MaxSpeed;
        }

        private static void Norm(ref Vector3 d) { float m = d.magnitude; d = m > 1e-5f ? d / m : Vector3.forward; }
    }

    /// <summary>
    /// BattleSimulation: projectiles and emitters, for any ability (enemy or squad) that fires things that fly.
    /// A projectile (LaunchProjectile) is a point moving through the world each tick: it speeds up (Accel, up to MaxSpeed),
    /// steers (Homing, after HomingDelay, turning faster the longer it steers), and lands its ability's hit, through the
    /// full hit pipeline (dodge, block, shields, stun), on the first enemy of its owner whose body it touches. Striking the
    /// ground or running out of Lifetime spends it; a homing one also fizzles on cover and terrain (CoverBlocks). Its flight is recorded in a ProjectilePath for the view.
    /// An emitter (StartEmitter) calls back every few seconds for a while, for its owner, as long as the owner lives: a
    /// volley spread over time (Needlestorm), or a stream that goes on while the owner does other things (Carapace).
    /// </summary>
    public partial class BattleSimulation
    {
        private class Projectile
        {
            public Unit Owner;
            public ActionDefinition Definition;
            public ProjectileSpec Spec;
            public string TargetId, Tag;
            public Vector3 Position, Direction;
            public float Speed, Born;
            public ProjectilePath Path;
            public HashSet<string> Dodged = new HashSet<string>();   // who has rolled their dodge against it (DodgeLead before it touches them)
        }

        private class Emitter
        {
            public Unit Owner;
            public ActionDefinition Definition;
            public float Every, NextAt, EndsAt;
            public int Left;   // shots left (-1: until EndsAt)
            public System.Action<BattleSimulation, Unit, ActionDefinition> Fire;
        }

        private readonly List<Projectile> projectiles = new List<Projectile>();
        private readonly IStaticOcclusion staticOcclusion;

        /// <summary>Does the segment a-b run into cover or terrain: the level's static geometry, or a cover piece or wall
        /// (State.Occluders)? Bodies don't count.</summary>
        public bool CoverBlocks(Vector3 a, Vector3 b)
        {
            if (staticOcclusion != null && staticOcclusion.IsBlocked(a, b)) return true;
            foreach (var o in State.Occluders)
                if (OcclusionMath.SegmentHitsCylinder(a, b, o.Position, o.Radius, o.Height)) return true;
            return false;
        }
        private readonly List<Emitter> emitters = new List<Emitter>();
        private int projectileCounter;

        /// <summary>Projectile flights are recorded every this many ticks (and where they end).</summary>
        public const int ProjectileSampleTicks = 2;

        /// <summary>Fire a projectile for 'owner' from 'from' along 'direction'. Its hit is 'def' (damage, impact, stun,
        /// statuses), tagged 'tag'. targetId: the unit it homes on with ProjectileHoming.Target.</summary>
        public void LaunchProjectile(Unit owner, ActionDefinition def, Vector3 from, Vector3 direction, ProjectileSpec spec, string targetId = null, string tag = null)
        {
            if (owner == null || def == null || spec == null) return;
            if (direction.sqrMagnitude < 1e-8f) direction = Vector3.forward;
            var p = new Projectile
            {
                Owner = owner, Definition = def, Spec = spec, TargetId = targetId, Tag = tag,
                Position = from, Direction = direction.normalized, Speed = Mathf.Max(0.1f, spec.Speed), Born = Now,
                Path = new ProjectilePath
                {
                    Id = ++projectileCounter, OwnerId = owner.Id, ActionId = def.Id, TargetId = targetId,
                    Accel = spec.Accel, MaxSpeed = Mathf.Max(spec.Speed, spec.MaxSpeed), Radius = spec.HitRadius, Lifetime = spec.Lifetime, Homing = spec.Homing,
                    HomingDelay = spec.HomingDelay, TurnRate = spec.TurnRate, TurnGrowth = spec.TurnGrowth,
                },
            };
            p.Path.Times.Add(Now); p.Path.Points.Add(from);
            projectiles.Add(p);
            Emit(EventType.ProjectileLaunched, owner.Id, targetId, p.Path);
        }

        /// <summary>Call 'fire' for 'owner' every 'every' seconds, starting now: 'shots' times (above 0), or until 'seconds'
        /// have passed. It stops early if the owner falls.</summary>
        public void StartEmitter(Unit owner, ActionDefinition def, float every, int shots, float seconds, System.Action<BattleSimulation, Unit, ActionDefinition> fire)
        {
            if (owner == null || fire == null) return;
            emitters.Add(new Emitter
            {
                Owner = owner, Definition = def, Every = shots > 0 && every <= 0f ? 0f : Mathf.Max(TickLength, every), NextAt = Now,   // a burst: all of them at once (Vipers)
                Left = shots > 0 ? shots : -1, EndsAt = shots > 0 ? float.MaxValue : Now + seconds, Fire = fire,
            });
        }

        /// <summary>Is 'owner' still firing from an emitter of 'def' (a Carapace stream, a volley not yet all out)?</summary>
        public bool IsEmitting(Unit owner, ActionDefinition def)
        {
            foreach (var e in emitters) if (e.Owner == owner && (def == null || e.Definition == def)) return true;
            return false;
        }

        private void TickEmitters()
        {
            for (int i = emitters.Count - 1; i >= 0; i--)
            {
                var e = emitters[i];
                if (!e.Owner.IsAlive) { emitters.RemoveAt(i); continue; }
                while (Now + 1e-4f >= e.NextAt && Now <= e.EndsAt + 1e-4f && e.Left != 0)
                {
                    e.Fire(this, e.Owner, e.Definition);
                    e.NextAt += e.Every;
                    if (e.Left > 0) e.Left--;
                }
                if (e.Left == 0 || Now > e.EndsAt + 1e-4f) emitters.RemoveAt(i);
            }
        }

        private void TickProjectiles()
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                float age = Now - p.Born;
                if (age >= p.Spec.Lifetime) { EndProjectile(i, null); continue; }
                Steer(p, age);
                p.Speed = Mathf.Min(Mathf.Max(p.Spec.MaxSpeed, p.Spec.Speed), p.Speed + p.Spec.Accel * TickLength);
                Vector3 from = p.Position, to = from + p.Direction * (p.Speed * TickLength);
                // Sweep the tick's travel in short steps so a fast one can't skip over a body.
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.25f));
                Unit hit = null;
                bool grounded = false;
                for (int s = 1; s <= steps && hit == null && !grounded; s++)
                {
                    Vector3 q = Vector3.Lerp(from, to, s / (float)steps);
                    p.Position = q;
                    hit = Touching(p, q);
                    if (hit == null && age > 0.05f && IsInsideGround(Terrain, q)) grounded = true;
                }
                if (hit != null)
                {
                    StrikeNow(new AttackEffect
                    {
                        Source = p.Owner, TargetId = hit.Id, Definition = p.Definition, IgnoreRange = true, IgnoreCover = true, Tag = p.Tag,
                    }, true);
                    EndProjectile(i, hit.Id);
                    continue;
                }
                if (grounded) { EndProjectile(i, null); continue; }
                RollDodgesAhead(p);
                // A homing one fizzles on cover and terrain it flies into (not in its first moment, leaving its owner's cover).
                if (p.Spec.Homing != ProjectileHoming.None && age > 0.1f && CoverBlocks(from, p.Position)) { p.Position = from; EndProjectile(i, null); continue; }
                if (IsStep(ProjectileSampleTicks)) { p.Path.Times.Add(Now); p.Path.Points.Add(p.Position); }
            }
        }

        /// <summary>Whoever it will touch within DodgeLead, flying on as it is, rolls a dodge against it (once each).</summary>
        private void RollDodgesAhead(Projectile p)
        {
            float ahead = p.Speed * DodgeLead;
            int steps = Mathf.Max(1, Mathf.CeilToInt(ahead / 0.25f));
            for (int s = 1; s <= steps; s++)
            {
                var u = Touching(p, p.Position + p.Direction * (ahead * s / steps));
                if (u == null || p.Dodged.Contains(u.Id)) continue;
                p.Dodged.Add(u.Id);
                RollDodgeAgainst(p.Owner, u, p.Definition);
                return;   // it ends on the first body it touches
            }
        }

        /// <summary>Is a point under the terrain there (the ground, a raised block's side): where a projectile ends. The view's
        /// predicted courses use it too.</summary>
        public static bool IsInsideGround(TerrainMap terrain, Vector3 q) { return terrain != null && q.y < terrain.HeightAt(TerrainMap.Level(q, q.y)) - 0.02f; }

        /// <summary>Turn toward what it homes on, by at most its turn rate this tick (plain spherical interpolation).</summary>
        private void Steer(Projectile p, float age)
        {
            var spec = p.Spec;
            if (spec.Homing == ProjectileHoming.None || age < spec.HomingDelay) return;
            Unit target = spec.Homing == ProjectileHoming.Target ? GetUnit(p.TargetId) : null;
            if (target == null || !target.IsAlive) target = spec.Homing == ProjectileHoming.ClosestEnemy ? ClosestEnemyTo(p.Owner, p.Position) : null;
            if (target == null) return;
            Vector3 want = target.Position + Vector3.up * (target.BodyHeight * 0.55f) - p.Position;
            float m = want.magnitude;
            if (m < 1e-4f) return;
            want /= m;
            p.Direction = ProjectilePath.Turn(p.Direction, want, (spec.TurnRate + spec.TurnGrowth * (age - spec.HomingDelay)) * Mathf.Deg2Rad * TickLength);
        }

        /// <summary>The enemy of 'owner' nearest to 'point' that a projectile can reach (targetable, in the world).</summary>
        public Unit ClosestEnemyTo(Unit owner, Vector3 point)
        {
            Unit best = null;
            float bestD = float.MaxValue;
            foreach (var u in EnemiesOf(owner))
            {
                if (!Reachable(u)) continue;
                float d = (u.Position + Vector3.up * (u.BodyHeight * 0.5f) - point).sqrMagnitude;
                if (d < bestD) { bestD = d; best = u; }
            }
            return best;
        }

        private bool Reachable(Unit u) { return !IsVoided(u) && !u.HasStatus(StatusIds.Melded); }

        /// <summary>The enemy whose body 'q' touches (a vertical capsule from its feet to its height), or null.</summary>
        private Unit Touching(Projectile p, Vector3 q)
        {
            foreach (var u in EnemiesOf(p.Owner))
            {
                if (!Reachable(u)) continue;
                float dx = q.x - u.Position.x, dz = q.z - u.Position.z;
                float feet = u.Position.y, top = u.Position.y + u.BodyHeight;
                float dy = q.y < feet ? feet - q.y : q.y > top ? q.y - top : 0f;
                float r = p.Spec.HitRadius + u.BodyRadius;
                if (dx * dx + dz * dz + dy * dy <= r * r) return u;
            }
            return null;
        }

        private void EndProjectile(int index, string hitId)
        {
            var p = projectiles[index];
            projectiles.RemoveAt(index);
            p.Path.Times.Add(Now); p.Path.Points.Add(p.Position);
            p.Path.EndsAt = Now;
            p.Path.HitId = hitId;
            Emit(EventType.ProjectileEnded, p.Owner.Id, hitId, p.Path);
        }
    }
}
