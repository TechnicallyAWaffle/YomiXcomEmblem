using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: Paths around bodies, free standing spots, and relocating units.</summary>
    public partial class BattleSimulation
    {
        /// <summary>
        /// A walking path from 'from' to 'to' that steps around units standing in the way (the navmesh only knows about
        /// cover). Units already walking are ignored: they will have moved by the time we get there. False if unreachable.
        /// The orders panel previews moves with this too, so the drawn route is the one the unit takes.
        /// </summary>
        public bool PlanPath(Unit unit, Vector3 from, Vector3 to, List<Vector3> path)
        {
            if (!Paths.TryGetPath(from, to, path) || path.Count < 2) return false;
            path[0] = from;
            RouteAroundBodies(unit, path);
            return true;
        }

        private void RouteAroundBodies(Unit unit, List<Vector3> path)
        {
            var skip = new HashSet<string>();   // blockers we couldn't find a way around
            for (int pass = 0; pass < 16; pass++)
            {
                bool changed = false;
                for (int i = 0; i + 1 < path.Count && !changed; i++)
                {
                    Vector3 a = path[i], b = path[i + 1];
                    foreach (var u in State.Units)
                    {
                        if (!u.IsAlive || u == unit || u.Phase == UnitPhase.Moving || skip.Contains(u.Id) || Holds(unit, u)) continue;
                        float clearance = unit.BodyRadius + u.BodyRadius + 0.1f;
                        Vector3 closest;
                        if (!SegmentPassesWithin(a, b, u.Position, clearance, out closest)) continue;
                        if (TryDetour(unit, path, i, u, clearance, closest)) changed = true;
                        else skip.Add(u.Id);
                        break;
                    }
                }
                if (!changed) return;
            }
        }

        /// <summary>True if the segment a-b passes closer than 'radius' to c (XZ), with both ends outside that circle.</summary>
        private static bool SegmentPassesWithin(Vector3 a, Vector3 b, Vector3 c, float radius, out Vector3 closest)
        {
            Vector3 ab = b - a; ab.y = 0f;
            Vector3 ac = c - a; ac.y = 0f;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ac, ab) / len2) : 0f;
            closest = a + ab * t;
            Vector3 d = c - closest; d.y = 0f;
            Vector3 da = c - a, db = c - b; da.y = 0f; db.y = 0f;
            return d.sqrMagnitude < radius * radius && da.sqrMagnitude >= radius * radius && db.sqrMagnitude >= radius * radius;
        }

        /// <summary>Inserts two waypoints that skirt 'blocker' after path[i], on whichever side is walkable and clear.</summary>
        private bool TryDetour(Unit unit, List<Vector3> path, int i, Unit blocker, float clearance, Vector3 closest)
        {
            Vector3 a = path[i], b = path[i + 1];
            Vector3 dir = b - a; dir.y = 0f; dir.Normalize();
            Vector3 n = closest - blocker.Position; n.y = 0f;
            n = n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.Cross(Vector3.up, dir);
            float r = clearance * 1.15f;
            for (int side = 0; side < 2; side++, n = -n)
            {
                Vector3 p1 = Terrain.OnGround(blocker.Position + n * r - dir * r * 0.75f);
                Vector3 p2 = Terrain.OnGround(blocker.Position + n * r + dir * r * 0.75f);
                if (!Walkable(a, p1) || !Walkable(p1, p2) || !Walkable(p2, b)) continue;
                if (InsideStandingBody(unit, p1) || InsideStandingBody(unit, p2)) continue;
                path.Insert(i + 1, p2);
                path.Insert(i + 1, p1);
                return true;
            }
            return false;
        }

        private bool Walkable(Vector3 a, Vector3 b)
        {
            Vector3 gap = terrainPaths.ClampWalk(a, b) - b; gap.y = 0f;   // walks jump height changes: only walls stop them
            return gap.sqrMagnitude < 0.01f;
        }

        private bool InsideStandingBody(Unit unit, Vector3 p)
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u == unit || u.Phase == UnitPhase.Moving || Holds(unit, u)) continue;
                Vector3 d = u.Position - p; d.y = 0f;
                float min = unit.BodyRadius + u.BodyRadius;
                if (d.sqrMagnitude < min * min) return true;
            }
            return false;
        }

        /// <summary>
        /// The nearest point to 'desired' where 'unit' can stand without overlapping another unit's body, counting
        /// where walking units are headed as taken. Searches outward in rings, trying the side facing 'from' first, so
        /// melee units crowding one target fan out around it instead of piling onto the same spot. 'walk' checks
        /// candidates with a path (move orders); otherwise with ClampDash (dashes and shoves).
        /// </summary>
        public Vector3 FreeSpotNear(Unit unit, Vector3 desired, Vector3 from, bool walk)
        {
            if (IsSpotFree(unit, desired)) return desired;
            Vector3 back = from - desired; back.y = 0f;
            float baseAngle = back.sqrMagnitude > 1e-6f ? Mathf.Atan2(back.z, back.x) : 0f;
            float step = unit.BodyRadius + StandingGap;
            var scratch = walk ? new List<Vector3>() : null;
            for (int ring = 1; ring <= 6; ring++)
            {
                float radius = ring * step;
                int count = 6 * ring;
                for (int k = 0; k < count; k++)
                {
                    // 0, +1, -1, +2, -2 ... steps away from the approach side.
                    int offset = (k + 1) / 2 * (k % 2 == 1 ? 1 : -1);
                    float a = baseAngle + offset * Mathf.PI * 2f / count;
                    var c = new Vector3(desired.x + Mathf.Cos(a) * radius, desired.y, desired.z + Mathf.Sin(a) * radius);
                    if (!IsSpotFree(unit, c)) continue;
                    if (walk)
                    {
                        if (!Paths.TryGetPath(from, c, scratch) || scratch.Count < 2) continue;
                        c = scratch[scratch.Count - 1];
                        if (!IsSpotFree(unit, c)) continue;
                    }
                    else
                    {
                        var clamped = Paths.ClampDash(from, c);
                        float gx = clamped.x - c.x, gz = clamped.z - c.z;
                        if (gx * gx + gz * gz > 0.01f) continue;
                        c = clamped;
                    }
                    return c;
                }
            }
            return desired;   // nowhere free nearby: overlap rather than refuse the order
        }

        /// <summary>
        /// Where 'unit' should walk to fight 'target' from 'distance' away: the free, reachable point on that circle
        /// closest to the unit's own side. Attackers ganging up on one target surround it at striking distance instead
        /// of queueing behind each other (which FreeSpotNear alone would do, pushing them out of reach).
        /// </summary>
        public Vector3 ApproachSpot(Unit unit, Unit target, float distance)
        {
            Vector3 dir = unit.Position - target.Position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            var scratch = new List<Vector3>();
            for (int k = 0; k < 24; k++)
            {
                int offset = (k + 1) / 2 * (k % 2 == 1 ? 1 : -1);   // 0, +15, -15, +30 ... degrees from straight on
                float a = offset * 15f * Mathf.Deg2Rad, cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                Vector3 c = target.Position + new Vector3(dir.x * cos - dir.z * sin, 0f, dir.x * sin + dir.z * cos) * distance;
                if (!IsSpotFree(unit, c)) continue;
                if (!Paths.TryGetPath(unit.Position, c, scratch) || scratch.Count < 2) continue;
                c = scratch[scratch.Count - 1];
                if (IsSpotFree(unit, c)) return c;
            }
            return target.Position + dir * distance;   // fully surrounded: the move order falls back to FreeSpotNear
        }

        /// <summary>Is 'unit' holding 'other' (Subjugate)? A captive is carried along, so it is never in its captor's way.</summary>
        private static bool Holds(Unit unit, Unit other) { return unit.Grapple != null && unit.Grapple.TargetId == other.Id; }

        private bool IsSpotFree(Unit unit, Vector3 p)
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u == unit || Holds(unit, u) || Holds(u, unit)) continue;
                Vector3 spot = IsGliding(u) ? u.GlideTo
                    : u.Phase == UnitPhase.Moving && u.Move != null && u.Move.Path.Count > 0 ? u.Move.Path[u.Move.Path.Count - 1] : u.Position;
                float dx = spot.x - p.x, dz = spot.z - p.z, min = unit.BodyRadius + u.BodyRadius + StandingGap;
                if (dx * dx + dz * dz < min * min) return false;
            }
            return true;
        }

        /// <summary>
        /// A walk that stops partway (cancelled, rooted, stunned) or arrives where someone else got stopped can leave the
        /// unit inside another body: slide it to the nearest free spot. Shown as a short shuffle, not a dodge or shove.
        /// </summary>
        private void SettleOutOfBodies(Unit u)
        {
            if (!u.IsAlive || IsSpotFree(u, u.Position)) return;
            Vector3 from = u.Position, to = FreeSpotNear(u, from, from, false);
            if ((to - from).sqrMagnitude < 1e-6f) return;
            u.Position = to;
            Emit(EventType.Dash, u.Id, null, new DashInfo { From = from, To = to, Duration = SettleDuration, Forced = true, Settle = true });
        }

        /// <summary>
        /// Slide a unit (dash, shove, pull) to 'to' over 'duration' (AdvanceGlides moves it; the view plays the same slide).
        /// It can't start anything until it lands. A new relocation mid-slide starts from where it is now, replacing the
        /// old destination (dodging again mid-dodge). Ends a walk in progress.
        /// Elevation: a forced move (shove, pull) that carries the unit off a ledge becomes a fall (StartFall); one that
        /// catches it mid-jump knocks it out of the jump instead; a unit already falling just keeps falling.
        /// </summary>
        private void Relocate(Unit unit, Vector3 to, float duration, bool forced, Unit source = null, Vector3? phaseFrom = null)
        {
            if (unit.FallLandAt >= 0f || unit.IsLifted) return;   // falling or held up in the air: nothing moves it
            foreach (var s in unit.States.ToArray()) s.OnShoved(this);   // shoved out of a grip (HeldState)
            if (unit.IsJumping)
            {
                if (forced) { BreakJump(unit, source); return; }
                HaltWalk(unit);
            }
            Vector3 from = unit.Position;
            to = Terrain.OnGround(FreeSpotNear(unit, to, phaseFrom ?? from, false));
            if (unit.Phase == UnitPhase.Moving) EndMove(unit, false);   // assumption: being relocated ends a walk in progress
            float drop = from.y - to.y;
            if (forced && drop > TerrainMap.StepHeight)
            {
                StartFall(unit, to, drop, duration, source, false);
                return;
            }
            unit.GlideFrom = from;
            unit.GlideTo = to;
            unit.GlideStart = Now;
            unit.GlideEnd = Now + Mathf.Max(TickLength, duration);
            Emit(EventType.Dash, unit.Id, null, new DashInfo { From = from, To = to, Duration = duration, Forced = forced });
        }
    }
}
