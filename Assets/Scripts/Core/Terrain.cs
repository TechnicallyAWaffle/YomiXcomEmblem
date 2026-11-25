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
    /// Ground heights sampled on a grid (a map built from a blockout: MapBuilderWindow's Convert Blockout): the top of the
    /// climbable geometry above each Cell x Cell square, starting at (MinX, MinZ), Width cells along X by Depth along Z, row
    /// by row along X. Outside it the ground is 0. Any shape works: slopes read as small steps, walls as ledges to jump.
    /// A floating platform (with room for a unit to stand under it) gives its cells a second, lower floor: Under, the
    /// ground beneath it (-1: none, the cell is solid down to the ground).
    /// </summary>
    [System.Serializable]
    public class HeightGrid
    {
        public float MinX, MinZ, Cell = 0.25f;
        public int Width, Depth;
        public float[] Heights = new float[0];
        [Tooltip("The floor under a floating platform, per cell (-1: none). Empty: no floating platforms.")]
        public float[] Under = new float[0];

        public bool IsEmpty { get { return Width <= 0 || Depth <= 0 || Heights == null || Heights.Length < Width * Depth; } }
        public bool HasUnder { get { return !IsEmpty && Under != null && Under.Length == Heights.Length; } }

        /// <summary>The cell index at (x, z), or -1 off the grid.</summary>
        public int IndexAt(float x, float z)
        {
            if (IsEmpty) return -1;
            int i = Mathf.FloorToInt((x - MinX) / Cell), j = Mathf.FloorToInt((z - MinZ) / Cell);
            if (i < 0 || j < 0 || i >= Width || j >= Depth) return -1;
            return j * Width + i;
        }

        /// <summary>The top of the cell at (x, z).</summary>
        public float At(float x, float z)
        {
            int k = IndexAt(x, z);
            return k < 0 ? 0f : Heights[k];
        }

        /// <summary>The floor under a floating platform at (x, z), or -1 if there is none.</summary>
        public float UnderAt(float x, float z)
        {
            if (!HasUnder) return -1f;
            int k = IndexAt(x, z);
            return k < 0 ? -1f : Under[k];
        }

        /// <summary>The centre of cell (i, j), at height 0.</summary>
        public Vector3 Center(int i, int j) { return new Vector3(MinX + (i + 0.5f) * Cell, 0f, MinZ + (j + 0.5f) * Cell); }

        /// <summary>Is any of it above the ground?</summary>
        public bool AnyRaised() { if (IsEmpty) return false; foreach (var h in Heights) if (h > 1e-3f) return true; return false; }
    }

    /// <summary>
    /// The arena's elevation as pure data, so the headless simulation knows how high the ground is anywhere. The ground
    /// is 0 except on top of TerrainBlocks and the Grid (a blockout's sampled heights); the highest wins where they overlap. There are no ramps: crossing a
    /// height change of more than StepHeight takes a jump (see BattleSimulation's movement). Fixed for the whole
    /// battle, so clones share it.
    /// </summary>
    public class TerrainMap
    {
        /// <summary>Height changes up to this are just walked over.</summary>
        public const float StepHeight = 0.3f;
        /// <summary>A jump takes JumpBaseSeconds plus JumpSecondsPerMeter for every meter of height it changes
        /// (was 0.25 + 0.12/m; 30% faster now).</summary>
        public const float JumpBaseSeconds = 0.25f / 1.3f;
        public const float JumpSecondsPerMeter = 0.12f / 1.3f;
        private const float SampleStep = 0.1f;   // how finely paths are scanned for edges

        public List<TerrainBlock> Blocks = new List<TerrainBlock>();
        /// <summary>A blockout map's sampled heights (null: none); where it and a block overlap, the higher wins.</summary>
        public HeightGrid Grid;
        /// <summary>The playable ground (XZ), for picking spots on it (reinforcement drops). Defaults to the Arena's 40 x 50 m.</summary>
        public Vector2 AreaMin = new Vector2(-20f, -25f), AreaMax = new Vector2(20f, 25f);

        public bool IsFlat { get { return Blocks.Count == 0 && (Grid == null || Grid.IsEmpty); } }

        /// <summary>A unit this far below a floor (or higher) stands on it; lower, it is on the floor under it.</summary>
        public const float FloorReach = StepHeight + 0.05f;

        /// <summary>
        /// The ground at p's XZ, for something at p's height: where a floating platform leaves a floor under it, a point
        /// at (or near, FloorReach) the platform's top is on the platform, and anything lower is on the floor under it.
        /// Elsewhere there is one ground, whatever p's height.
        /// </summary>
        public float HeightAt(Vector3 p)
        {
            float top = TopAt(p.x, p.z);
            float under = Grid != null ? Grid.UnderAt(p.x, p.z) : -1f;
            return under >= 0f && under < top && p.y + FloorReach < top ? under : top;
        }

        /// <summary>The highest ground at (x, z): a platform's top where there is one.</summary>
        public float TopAt(float x, float z)
        {
            float h = Grid != null ? Grid.At(x, z) : 0f;
            foreach (var b in Blocks)
                if (b.Height > h && b.Contains(x, z)) h = b.Height;
            return h;
        }

        /// <summary>The point with its height set to the ground there (the floor it is at, see HeightAt).</summary>
        public Vector3 OnGround(Vector3 p) { return new Vector3(p.x, HeightAt(p), p.z); }

        /// <summary>
        /// A point the player picked (the cursor's hit on the map): on whichever floor is nearest its height, so the
        /// ground under a floating platform is that ground, not the platform's top, and the platform's side is its top.
        /// </summary>
        public Vector3 NearestFloor(Vector3 p)
        {
            float top = TopAt(p.x, p.z);
            float under = Grid != null ? Grid.UnderAt(p.x, p.z) : -1f;
            float h = under >= 0f && under < top && Mathf.Abs(p.y - under) < Mathf.Abs(p.y - top) ? under : top;
            return new Vector3(p.x, h, p.z);
        }

        /// <summary>Does the map have floating platforms (floors with ground under them)?</summary>
        public bool HasFloating { get { return Grid != null && Grid.HasUnder; } }

        /// <summary>
        /// Where walking straight from a to b's XZ ends up, height and all: it follows the floor it is on, dropping off
        /// ledges and climbing onto solid ground, but passing under floating platforms (onto one takes a jump up from
        /// beside it, see TerrainPaths).
        /// </summary>
        public Vector3 Follow(Vector3 a, Vector3 b)
        {
            if (IsFlat) return new Vector3(b.x, 0f, b.z);
            Vector3 cur = OnGround(a);
            for (int guard = 0; guard < 32; guard++)
            {
                float edge = FirstEdge(cur, b, cur.y);
                if (edge < 0f) break;
                Vector3 dir = b - cur; dir.y = 0f; dir.Normalize();
                cur = OnGround(Level(cur + dir * edge, cur.y));
            }
            return OnGround(Level(b, cur.y));
        }

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
                if (Differs(HeightAt(Level(a + d * s, baseHeight)), baseHeight, risesOnly))
                {
                    float lo = prev, hi = s;
                    for (int i = 0; i < 8; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Differs(HeightAt(Level(a + d * mid, baseHeight)), baseHeight, risesOnly)) hi = mid; else lo = mid;
                    }
                    return hi;
                }
                if (s >= length) return -1f;
                prev = s;
            }
        }

        /// <summary>p at height y (which floor a point is on goes by its height).</summary>
        public static Vector3 Level(Vector3 p, float y) { p.y = y; return p; }

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
                    Vector3 over = OnGround(Level(a + dir * edge, a.y));
                    seconds += JumpSeconds(over.y - a.y);
                    a = over;
                }
                Vector3 end = OnGround(path[i]);
                if (Mathf.Abs(end.y - a.y) > StepHeight) seconds += JumpSeconds(end.y - a.y);   // a jump up onto a floating platform
            }
            return seconds;
        }

        /// <summary>A spot on a floor's edge: Inside on the floor, Outside just over its edge on the lower ground below.</summary>
        public struct FloorEdge { public Vector3 Inside, Outside; }

        /// <summary>
        /// The edges of the floor 'start' stands on (a blockout's cells connected to it within StepHeight): every place
        /// where that floor ends over lower ground, with a point a little way in from the edge and one a little way past it,
        /// down on the ground there. Up to 'maxCells' cells are searched.
        /// </summary>
        public List<FloorEdge> EdgesOf(Vector3 start, int maxCells = 40000)
        {
            var result = new List<FloorEdge>();
            if (Grid == null || Grid.IsEmpty) return result;
            int k0 = Grid.IndexAt(start.x, start.z);
            if (k0 < 0) return result;
            int w = Grid.Width, d = Grid.Depth;
            var level = new Dictionary<int, float> { { k0, HeightAt(start) } };
            var queue = new Queue<int>();
            queue.Enqueue(k0);
            int[] di = { 1, -1, 0, 0 }, dj = { 0, 0, 1, -1 };
            while (queue.Count > 0 && level.Count < maxCells)
            {
                int k = queue.Dequeue();
                int i = k % w, j = k / w;
                float h = level[k];
                Vector3 c = Grid.Center(i, j);
                for (int n = 0; n < 4; n++)
                {
                    int ni = i + di[n], nj = j + dj[n];
                    if (ni < 0 || nj < 0 || ni >= w || nj >= d) continue;
                    int nk = nj * w + ni;
                    if (level.ContainsKey(nk)) continue;
                    Vector3 nc = Grid.Center(ni, nj);
                    float nh = HeightAt(Level(nc, h));
                    if (Mathf.Abs(nh - h) <= StepHeight) { level[nk] = nh; queue.Enqueue(nk); continue; }
                    if (nh > h) continue;   // a wall up: not a way off
                    Vector3 inward = c - nc; inward.y = 0f; inward.Normalize();
                    Vector3 inside = Level(c + inward * 0.25f, h);
                    if (Mathf.Abs(HeightAt(inside) - h) > StepHeight) inside = Level(c, h);
                    Vector3 outside = OnGround(Level(nc - inward * 0.3f, nh));
                    result.Add(new FloorEdge { Inside = OnGround(inside), Outside = outside });
                }
            }
            return result;
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
                    a = OnGround(Level(at, a.y));
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

        /// <summary>
        /// A walk from 'from' to 'to', each corner on the floor the walk is on there (it follows its floor: see
        /// TerrainMap.Follow). 'to' is on the floor its height says (TerrainMap.HeightAt): the ground under a floating
        /// platform, or the platform's top. When walking straight there would end on the other one, the route goes by the
        /// nearest edge of the platform: up onto it, a jump from beside it (the walk's climb, BattleSimulation.WalkStep),
        /// or down off it, a drop over its edge.
        /// </summary>
        public bool TryGetPath(Vector3 from, Vector3 to, List<Vector3> pathOut) { return Route(from, to, pathOut, 3); }

        private bool Route(Vector3 from, Vector3 to, List<Vector3> pathOut, int depth)
        {
            if (!Flat.TryGetPath(Base(from), Base(to), pathOut)) return false;
            pathOut[0] = terrain.OnGround(from);
            for (int i = 1; i < pathOut.Count; i++) pathOut[i] = terrain.Follow(pathOut[i - 1], pathOut[i]);
            if (!terrain.HasFloating || depth <= 0) return true;
            float want = terrain.HeightAt(to);
            Vector3 arrive = pathOut[pathOut.Count - 1];
            if (Mathf.Abs(arrive.y - want) <= TerrainMap.StepHeight) return true;

            // The walk ends on the wrong floor. Up: by an edge of the platform 'to' is on. Down: by an edge of the one it is on.
            bool up = want > arrive.y;
            var edges = terrain.EdgesOf(up ? terrain.OnGround(to) : terrain.OnGround(from));
            if (edges.Count == 0) return true;   // no way between them: as close as walking gets
            Vector3 f = Base(from), t = Base(to);
            edges.Sort((a, b) => Score(a, f, t, up).CompareTo(Score(b, f, t, up)));
            var first = new List<Vector3>();
            var rest = new List<Vector3>();
            for (int k = 0; k < edges.Count && k < 6; k++)
            {
                var e = edges[k];
                // Up: walk to beside the edge, jump onto the platform, walk on. Down: walk to the edge, over it, walk on.
                Vector3 leave = up ? e.Outside : e.Inside, land = up ? e.Inside : e.Outside;
                if (!Route(from, leave, first, depth - 1)) continue;
                if (Mathf.Abs(first[first.Count - 1].y - leave.y) > TerrainMap.StepHeight) continue;   // it couldn't get there
                if (!Route(land, to, rest, depth - 1)) continue;
                pathOut.Clear();
                pathOut.AddRange(first);
                pathOut.AddRange(rest);   // rest starts at 'land': the step over the edge
                return true;
            }
            return true;
        }

        private static float Score(TerrainMap.FloorEdge e, Vector3 from, Vector3 to, bool up)
        {
            Vector3 leave = up ? e.Outside : e.Inside, land = up ? e.Inside : e.Outside;
            return Vector3.Distance(from, Base(leave)) + Vector3.Distance(Base(land), to);
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
            return terrain.Follow(from, to);
        }

        /// <summary>Like ClampDash, but walking: only real walls stop it, not height.</summary>
        public Vector3 ClampWalk(Vector3 from, Vector3 desired) { return terrain.Follow(from, Flat.ClampDash(Base(from), Base(desired))); }

        private static Vector3 Base(Vector3 p) { return new Vector3(p.x, 0f, p.z); }
    }
}
