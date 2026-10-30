using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Immediate-mode ground overlays (range circles, selection rings, path lines) built from pooled LineRenderers.
    /// Call Begin(), draw what this frame needs, then End(): anything not drawn this frame is hidden.
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

        public void Begin() { used = 0; }

        public void End()
        {
            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
        }

        public void Circle(Vector3 center, float radius, Color color, float width = 0.05f, int segments = 48)
        {
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
        public void Line(IList<Vector3> points, Color start, Color end, float startWidth, float endWidth)
        {
            if (points == null || points.Count < 2) return;
            var line = Next(start, startWidth);
            line.loop = false;
            line.endColor = end;
            line.endWidth = endWidth;
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
        }

        private LineRenderer Next(Color color, float width)
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
            l.widthMultiplier = 1f;
            l.startWidth = l.endWidth = width;
            l.startColor = l.endColor = color;
            return l;
        }
    }
}
