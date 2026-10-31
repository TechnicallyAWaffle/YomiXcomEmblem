using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>A raised, flat-topped block of ground (a ledge, a ridge, a crate stack): axis-aligned in XZ, its top at Height.</summary>
    public class TerrainBlock
    {
        public string Id;
        public float MinX, MinZ, MaxX, MaxZ;
        public float Height;

        public bool Contains(float x, float z) { return x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ; }
    }

    /// <summary>
    /// The arena's elevation as pure data, so the headless simulation knows how high the ground is anywhere. The ground
    /// is 0 except on top of TerrainBlocks (the highest one wins where they overlap). There are no ramps: crossing a
    /// height change of more than StepHeight takes a jump (see BattleSimulation's movement). Fixed for the whole
    /// battle, so clones share it.
    /// </summary>
    public class TerrainMap
    {
        /// <summary>Height changes up to this are just walked over.</summary>
        public const float StepHeight = 0.3f;
        /// <summary>A jump takes JumpBaseSeconds plus JumpSecondsPerMeter for every meter of height it changes.</summary>
        public const float JumpBaseSeconds = 0.25f;
        public const float JumpSecondsPerMeter = 0.12f;
        private const float SampleStep = 0.1f;   // how finely paths are scanned for edges

        public List<TerrainBlock> Blocks = new List<TerrainBlock>();

        public bool IsFlat { get { return Blocks.Count == 0; } }

        public float HeightAt(Vector3 p)
        {
            float h = 0f;
            foreach (var b in Blocks)
                if (b.Height > h && b.Contains(p.x, p.z)) h = b.Height;
            return h;
        }

        /// <summary>The point with its height set to the ground there.</summary>
        public Vector3 OnGround(Vector3 p) { return new Vector3(p.x, HeightAt(p), p.z); }

        public static float JumpSeconds(float heightChange) { return JumpBaseSeconds + JumpSecondsPerMeter * Mathf.Abs(heightChange); }

        /// <summary>How far a jump's arc rises above a straight line between its ends.</summary>
        public static float JumpArc(float heightChange) { return 0.35f + 0.5f * Mathf.Abs(heightChange); }

        /// <summary>A jump's height at k (0..1) from 'from' to 'to'.</summary>
        public static float JumpY(Vector3 from, Vector3 to, float k)
        {
            return Mathf.Lerp(from.y, to.y, k) + 4f * JumpArc(to.y - from.y) * k * (1f - k);
        }

        /// <summary>
        /// Distance along a-&gt;b (XZ) to the first point whose ground differs from baseHeight by more than StepHeight
        /// (risesOnly: only higher ground counts), or -1 if there is none. Accurate to about a centimetre.
        /// </summary>
        public float FirstEdge(Vector3 a, Vector3 b, float baseHeight, bool risesOnly = false)
        {
            if (IsFlat) return -1f;
            Vector3 d = b - a; d.y = 0f;
            float length = d.magnitude;
            if (length < 1e-5f) return -1f;
            d /= length;
            float prev = 0f;
            for (float s = Mathf.Min(SampleStep, length); ; s = Mathf.Min(s + SampleStep, length))
            {
                if (Differs(HeightAt(a + d * s), baseHeight, risesOnly))
                {
                    float lo = prev, hi = s;
                    for (int i = 0; i < 8; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Differs(HeightAt(a + d * mid), baseHeight, risesOnly)) hi = mid; else lo = mid;
                    }
                    return hi;
                }
                if (s >= length) return -1f;
                prev = s;
            }
        }

        private static bool Differs(float h, float baseHeight, bool risesOnly)
        {
            return risesOnly ? h > baseHeight + StepHeight : Mathf.Abs(h - baseHeight) > StepHeight;
        }

        /// <summary>Seconds of jumping a walk along this path takes (on top of the walking itself).</summary>
        public float JumpSecondsAlong(IList<Vector3> path)
        {
            if (IsFlat || path == null) return 0f;
            float seconds = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 a = OnGround(path[i - 1]);
                for (int guard = 0; guard < 16; guard++)
                {
                    float edge = FirstEdge(a, path[i], a.y);
                    if (edge < 0f) break;
                    Vector3 dir = path[i] - a; dir.y = 0f; dir.Normalize();
                    Vector3 over = OnGround(a + dir * edge);
                    seconds += JumpSeconds(over.y - a.y);
                    a = over;
                }
            }
            return seconds;
        }

        /// <summary>A copy of the path for drawing: every corner on the ground, with a rise or drop at each edge it crosses.</summary>
        public List<Vector3> Drape(IList<Vector3> path)
        {
            var result = new List<Vector3>();
            if (path == null || path.Count == 0) return result;
            Vector3 a = OnGround(path[0]);
            result.Add(a);
            for (int i = 1; i < path.Count; i++)
            {
                for (int guard = 0; guard < 16; guard++)
                {
                    float edge = FirstEdge(a, path[i], a.y);
                    if (edge < 0f) break;
                    Vector3 dir = path[i] - a; dir.y = 0f; dir.Normalize();
                    Vector3 at = a + dir * edge;
                    result.Add(new Vector3(at.x, a.y, at.z));
                    a = OnGround(at);
                    result.Add(a);
                }
                a = OnGround(path[i]);
                result.Add(a);
            }
            return result;
        }
    }

    /// <summary>
    /// Wraps the flat path service (the navmesh only covers the base ground: raised blocks are left out of the bake)
    /// with the terrain: queries go in at ground level and come back with real heights. Walking paths cross edges freely
    /// (the walk jumps them); dashes, shoves and pulls (ClampDash) can drop off a ledge but stop short of higher ground.
    /// </summary>
    public class TerrainPaths : IPathService
    {
        /// <summary>How far short of a higher ledge a dash or shove stops.</summary>
        public const float LedgeMargin = 0.35f;

        public readonly IPathService Flat;
        private readonly TerrainMap terrain;

        public TerrainPaths(IPathService flat, TerrainMap terrain)
        {
            Flat = flat;
            this.terrain = terrain;
        }

        public bool TryGetPath(Vector3 from, Vector3 to, List<Vector3> pathOut)
        {
            if (!Flat.TryGetPath(Base(from), Base(to), pathOut)) return false;
            for (int i = 0; i < pathOut.Count; i++) pathOut[i] = terrain.OnGround(pathOut[i]);
            return true;
        }

        public Vector3 ClampDash(Vector3 from, Vector3 desired)
        {
            Vector3 to = Flat.ClampDash(Base(from), Base(desired));
            float edge = terrain.FirstEdge(from, to, terrain.HeightAt(from), true);
            if (edge >= 0f)
            {
                Vector3 dir = to - from; dir.y = 0f; dir.Normalize();
                to = from + dir * Mathf.Max(0f, edge - LedgeMargin);
            }
            return terrain.OnGround(to);
        }

        /// <summary>Like ClampDash, but walking: only real walls stop it, not height.</summary>
        public Vector3 ClampWalk(Vector3 from, Vector3 desired) { return terrain.OnGround(Flat.ClampDash(Base(from), Base(desired))); }

        private static Vector3 Base(Vector3 p) { return new Vector3(p.x, 0f, p.z); }
    }
}
