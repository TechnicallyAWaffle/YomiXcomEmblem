using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// A vertical cylinder that blocks sight: deployed cover, smoke, a shield wall. Pure data, lives in
    /// BattleState so the simulation can reason about it without any GameObject. Units are occluders too
    /// (radius = BodyRadius, height = BodyHeight), handled by SimVisibility.
    /// </summary>
    public class Occluder
    {
        public string Id;
        public string OwnerId;
        public Vector3 Position;            // base (feet) of the cylinder
        public float Radius = 0.5f;
        public float Height = 2f;
        public float ExpiresAt = float.MaxValue;   // sim time

        public Occluder Clone()
        {
            return new Occluder { Id = Id, OwnerId = OwnerId, Position = Position, Radius = Radius, Height = Height, ExpiresAt = ExpiresAt };
        }
    }

    /// <summary>Static level geometry test (Unity: Physics.Linecast against environment colliders).</summary>
    public interface IStaticOcclusion
    {
        bool IsBlocked(Vector3 from, Vector3 to);
    }

    public class NoStaticOcclusion : IStaticOcclusion
    {
        public bool IsBlocked(Vector3 from, Vector3 to) { return false; }
    }

    public static class OcclusionMath
    {
        /// <summary>True if the segment a-&gt;b passes through a vertical cylinder (base at center, extending up by height).</summary>
        public static bool SegmentHitsCylinder(Vector3 a, Vector3 b, Vector3 center, float radius, float height)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float fx = a.x - center.x, fz = a.z - center.z;
            float A = dx * dx + dz * dz;
            float tIn, tOut;

            if (A < 1e-8f)
            {
                // Segment is vertical: it either sits inside the circle or it doesn't.
                if (fx * fx + fz * fz > radius * radius) return false;
                tIn = 0f; tOut = 1f;
            }
            else
            {
                float B = 2f * (fx * dx + fz * dz);
                float C = fx * fx + fz * fz - radius * radius;
                float disc = B * B - 4f * A * C;
                if (disc < 0f) return false;
                float sq = (float)System.Math.Sqrt(disc);
                tIn = (-B - sq) / (2f * A);
                tOut = (-B + sq) / (2f * A);
                if (tOut < 0f || tIn > 1f) return false;
                tIn = Mathf.Max(0f, tIn);
                tOut = Mathf.Min(1f, tOut);
            }

            // Height of the segment while it is inside the circle, against the cylinder's vertical extent.
            float y0 = a.y + (b.y - a.y) * tIn;
            float y1 = a.y + (b.y - a.y) * tOut;
            float lo = Mathf.Min(y0, y1), hi = Mathf.Max(y0, y1);
            return hi >= center.y && lo <= center.y + height;
        }
    }

    /// <summary>
    /// The simulation's visibility model. Exposure = fraction of sample points on the target's body that the
    /// attacker can see. A sample is hidden by static geometry (IStaticOcclusion) or by any deployable Occluder.
    /// Other living units (sized by BodyRadius/BodyHeight) are only soft cover: a sample behind a body counts as
    /// UnitCoverVisibility of a sample, and bodies never break line of sight. Units shuffle and crowd around
    /// melee constantly, so letting them hard-block made shots (and Snipe windups) fail on targets in plain view.
    /// Units and deployables are tested with math against sim data, so this stays correct while the scene's
    /// GameObjects lag behind playback.
    /// </summary>
    public class SimVisibility : IVisibilityService
    {
        private static readonly float[] HeightFractions = { 0.1f, 0.35f, 0.6f, 0.85f, 0.97f };
        private static readonly float[] LateralFractions = { -0.8f, 0f, 0.8f };   // of the target's BodyRadius

        /// <summary>How much a sample seen only past other units' bodies still counts toward exposure.</summary>
        public const float UnitCoverVisibility = 0.5f;

        private readonly IStaticOcclusion staticOcclusion;
        private readonly BattleState state;

        public SimVisibility(IStaticOcclusion staticOcclusion, BattleState state)
        {
            this.staticOcclusion = staticOcclusion;
            this.state = state;
        }

        public float GetExposure(Unit attacker, Unit target, bool ignoreTerrain)
        {
            if (ignoreTerrain) return 1f;   // "pierces terrain": ignores static geometry, units and deployables alike
            float visible = 0f;
            int total = 0;
            Vector3 eye = attacker.Position + Vector3.up * attacker.EyeHeight;
            Vector3 side = SideAxis(attacker, target);
            foreach (float h in HeightFractions)
                foreach (float lateral in LateralFractions)
                {
                    total++;
                    Vector3 point = SamplePoint(target, h, lateral, side);
                    if (HardBlocked(eye, point)) continue;
                    visible += BehindBody(eye, point, attacker, target) ? UnitCoverVisibility : 1f;
                }
            return visible / total;
        }

        /// <summary>Only walls and deployables break sight; bodies in the way just lower exposure. Early-outs on
        /// the first visible sample, so it is cheap enough to call every tick.</summary>
        public bool HasLineOfSight(Unit attacker, Unit target)
        {
            Vector3 eye = attacker.Position + Vector3.up * attacker.EyeHeight;
            Vector3 side = SideAxis(attacker, target);
            foreach (float h in HeightFractions)
                foreach (float lateral in LateralFractions)
                    if (!HardBlocked(eye, SamplePoint(target, h, lateral, side))) return true;
            return false;
        }

        /// <summary>
        /// Where a shot that cover stopped hits that cover: one of the target's hidden sample points, picked by 'pick'
        /// (0..1), and the first point along the attacker's sight line to it that walls or deployables block. False if no
        /// sample is hard-blocked (only other bodies were in the way).
        /// </summary>
        public bool TryCoverPoint(Unit attacker, Unit target, float pick, out Vector3 point)
        {
            point = Vector3.zero;
            Vector3 eye = attacker.Position + Vector3.up * attacker.EyeHeight;
            Vector3 side = SideAxis(attacker, target);
            var hidden = new List<Vector3>();
            foreach (float h in HeightFractions)
                foreach (float lateral in LateralFractions)
                {
                    Vector3 p = SamplePoint(target, h, lateral, side);
                    if (HardBlocked(eye, p)) hidden.Add(p);
                }
            if (hidden.Count == 0) return false;
            Vector3 aim = hidden[Mathf.Clamp((int)(pick * hidden.Count), 0, hidden.Count - 1)];
            // Bisect for the first blocked point: blocked(eye -> x) is false before the cover and true from it on.
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 16; i++)
            {
                float mid = (lo + hi) / 2f;
                if (HardBlocked(eye, Vector3.Lerp(eye, aim, mid))) hi = mid; else lo = mid;
            }
            point = Vector3.Lerp(eye, aim, hi);
            return true;
        }

        private static Vector3 SideAxis(Unit attacker, Unit target)
        {
            Vector3 toTarget = target.Position - attacker.Position;
            toTarget.y = 0f;
            return Vector3.Cross(Vector3.up, toTarget.normalized);
        }

        private static Vector3 SamplePoint(Unit target, float heightFraction, float lateralFraction, Vector3 side)
        {
            return target.Position + Vector3.up * (target.BodyHeight * heightFraction) + side * (target.BodyRadius * lateralFraction);
        }

        private bool HardBlocked(Vector3 eye, Vector3 point)
        {
            if (staticOcclusion.IsBlocked(eye, point)) return true;
            foreach (var o in state.Occluders)
                if (OcclusionMath.SegmentHitsCylinder(eye, point, o.Position, o.Radius, o.Height)) return true;
            return false;
        }

        private bool BehindBody(Vector3 eye, Vector3 point, Unit attacker, Unit target)
        {
            foreach (var u in state.Units)
            {
                if (!u.IsAlive || u.Id == attacker.Id || u.Id == target.Id) continue;
                if (OcclusionMath.SegmentHitsCylinder(eye, point, u.Position, u.BodyRadius, u.BodyHeight)) return true;
            }
            return false;
        }
    }
}
