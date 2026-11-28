using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Immediate-mode ground overlays (range circles, selection rings, path lines) built from pooled LineRenderers, and flat
    /// filled shapes (Disc, Fill: the tactical view's area hitboxes) from pooled meshes.
    /// Call Begin(), draw what this frame needs, then End(): anything not drawn this frame is hidden.
    /// A round circle gets more segments as it grows (one every SegmentLength metres at least), so a big area's edge stays round.
    /// </summary>
    public class WorldMarkers
    {
        private const float GroundOffset = 0.05f;

        private readonly Transform parent;
        private readonly Material material;
        private readonly List<LineRenderer> pool = new List<LineRenderer>();
        private int used;

        public WorldMarkers(Transform parent)
        {
            this.parent = parent;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) material = new Material(shader);
        }

        /// <summary>Longest stretch of a round circle between two of its points (metres): big circles get more segments.</summary>
        public const float SegmentLength = 0.5f;

        private readonly List<MeshRenderer> fillPool = new List<MeshRenderer>();
        private int fillsUsed;

        public void Begin() { used = 0; fillsUsed = 0; }

        public void End()
        {
            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
            for (int i = fillsUsed; i < fillPool.Count; i++) fillPool[i].enabled = false;
        }

        /// <summary>A filled flat disc on the ground (see-through with a translucent colour).</summary>
        public void Disc(Vector3 center, float radius, Color color)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(radius * Mathf.PI * 2f / SegmentLength), 24, 256);
            var ring = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                ring[i] = new Vector3(center.x + Mathf.Cos(a) * radius, center.y, center.z + Mathf.Sin(a) * radius);
            }
            Fill(ring, color, center);
        }

        /// <summary>A filled flat shape: a fan from 'middle' (default: the points' average) to the closed outline 'ring'.</summary>
        public void Fill(IList<Vector3> ring, Color color, Vector3? middle = null)
        {
            if (ring == null || ring.Count < 3) return;
            Vector3 mid = Vector3.zero;
            if (middle.HasValue) mid = middle.Value;
            else { foreach (var p in ring) mid += p; mid /= ring.Count; }
            var r = NextFill();
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            mesh.Clear();
            int n = ring.Count;
            var verts = new Vector3[n + 1];
            var colors = new Color[n + 1];
            var tris = new int[n * 3];
            Vector3 lift = Vector3.up * (GroundOffset * 0.6f);   // under the outlines drawn on it
            verts[0] = mid + lift; colors[0] = color;
            for (int i = 0; i < n; i++) { verts[i + 1] = ring[i] + lift; colors[i + 1] = color; }
            for (int i = 0; i < n; i++) { tris[i * 3] = 0; tris[i * 3 + 1] = 1 + (i + 1) % n; tris[i * 3 + 2] = 1 + i; }
            mesh.vertices = verts; mesh.colors = colors; mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        private MeshRenderer NextFill()
        {
            if (fillsUsed == fillPool.Count)
            {
                var go = new GameObject("VfxQuad");   // the name the tactical view leaves alone (TacticalView.Skip)
                go.transform.SetParent(parent, true);   // kept at the world origin: its vertices are world positions, like the lines'
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = new Mesh { name = "MarkerFill" };
                mf.sharedMesh.MarkDynamic();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                if (material != null) mr.sharedMaterial = material;
                fillPool.Add(mr);
            }
            var r = fillPool[fillsUsed++];
            r.enabled = true;
            // At the world origin with no turn or scale, whatever the parent does: its vertices are world positions.
            var t = r.transform;
            t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Vector3 ps = parent != null ? parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(Mathf.Abs(ps.x) > 1e-5f ? 1f / ps.x : 1f, Mathf.Abs(ps.y) > 1e-5f ? 1f / ps.y : 1f, Mathf.Abs(ps.z) > 1e-5f ? 1f / ps.z : 1f);
            return r;
        }

        public void Circle(Vector3 center, float radius, Color color, float width = 0.05f, int segments = 48)
        {
            if (segments > 8) segments = Mathf.Clamp(Mathf.Max(segments, Mathf.CeilToInt(radius * Mathf.PI * 2f / SegmentLength)), 3, 256);   // round: smooth at any size (shaped ones keep their corners)
            var line = Next(color, width);
            line.loop = true;
            line.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * radius, center.y + GroundOffset, center.z + Mathf.Sin(a) * radius));
            }
        }

        public void Polyline(IList<Vector3> points, Color color, float width = 0.08f)
        {
            if (points == null || points.Count < 2) return;
            var line = Next(color, width);
            line.loop = false;
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i] + Vector3.up * GroundOffset);
        }

        public void Segment(Vector3 a, Vector3 b, Color color, float width = 0.05f)
        {
            var line = Next(color, width);
            line.loop = false;
            line.positionCount = 2;
            line.SetPosition(0, a + Vector3.up * GroundOffset);
            line.SetPosition(1, b + Vector3.up * GroundOffset);
        }

        /// <summary>A free line in world space with no ground offset (tracers, slashes, beams), tapering from start to end.</summary>
        /// <summary>A cone's outline on the ground: its tip at 'from', opening 'angle' degrees around 'dir', 'radius' long (the
        /// tip, then the arc). For AreaShape.Cone (Repress).</summary>
        public static Vector3[] ConeOutline(Vector3 from, Vector3 dir, float radius, float angle, int segments = 24)
        {
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            float baseYaw = Mathf.Atan2(dir.x, dir.z), half = angle * 0.5f * Mathf.Deg2Rad;
            var pts = new Vector3[segments + 2];
            pts[0] = from;
            for (int i = 0; i <= segments; i++)
            {
                float yaw = baseYaw - half + 2f * half * i / segments;
                pts[i + 1] = from + new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * radius;
            }
            return pts;
        }

        public void Line(IList<Vector3> points, Color start, Color end, float startWidth, float endWidth)
        {
            Stroke(points, start, end, startWidth, endWidth, null, false);
        }

        /// <summary>
        /// A free line drawn with 'mat' (a VfxKit material: a brushy stroke, ink or glow; null: the plain one). tile: the
        /// texture repeats along the line once per its width, so strokes keep their grain however long they are; otherwise
        /// it is stretched over the whole line once.
        /// </summary>
        public void Stroke(IList<Vector3> points, Color start, Color end, float startWidth, float endWidth, Material mat, bool tile = true)
        {
            if (points == null || points.Count < 2) return;
            var line = Next(start, startWidth, mat);
            line.loop = false;
            line.endColor = end;
            line.endWidth = endWidth;
            line.textureMode = mat != null && tile ? LineTextureMode.Tile : LineTextureMode.Stretch;
            if (mat != null && tile) line.textureScale = new Vector2(1f / Mathf.Max(0.05f, Mathf.Max(startWidth, endWidth) * 4f), 1f);
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
        }

        private LineRenderer Next(Color color, float width, Material mat = null)
        {
            if (used == pool.Count)
            {
                var line = new GameObject("Marker").AddComponent<LineRenderer>();
                line.transform.SetParent(parent, false);
                line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                if (material != null) line.sharedMaterial = material;
                pool.Add(line);
            }
            var l = pool[used++];
            l.enabled = true;
            var want = mat != null ? mat : material;
            if (l.sharedMaterial != want) l.sharedMaterial = want;
            if (mat == null) { l.textureMode = LineTextureMode.Stretch; l.textureScale = Vector2.one; }
            l.widthMultiplier = 1f;
            l.startWidth = l.endWidth = width;
            l.startColor = l.endColor = color;
            return l;
        }
    }
}
