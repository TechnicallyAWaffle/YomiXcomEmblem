using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// The Makina's placeholder looks, built in code (UnitLooks hands these over by the definition's asset name): no capsule,
    /// a shape of their own with glowing patterns.
    ///   Monolith: a tall stone slab, its faces cut with glowing bands and squares.
    ///   Hedron:   an inverted pyramid hanging over the ground, banded and tipped with light; it turns slowly (MakinaSpin),
    ///             and through Massacre's windup it points its tip the way it will charge and spins up.
    /// Model space as UnitLooks': the capsule's centre at the origin, 2 tall; feet at y = -1.
    /// </summary>
    public static class MakinaLooks
    {
        private const string RootName = "LookProps";
        private static readonly Color Stone = new Color(0.36f, 0.38f, 0.43f), Glyph = new Color(0.45f, 0.85f, 1f),
                                      HedronBody = new Color(0.2f, 0.18f, 0.27f), HedronGlyph = new Color(0.8f, 0.55f, 1f),
                                      StellationBody = new Color(0.26f, 0.2f, 0.3f), StellationGlyph = new Color(0.95f, 0.5f, 0.95f),
                                      InfiniteWire = new Color(0.6f, 0.95f, 1f);
        private static Mesh pyramid;

        /// <summary>Give 'view' the look its Makina definition has. False: not a Makina with a look here.</summary>
        public static bool Apply(UnitView view, UnitDefinition def, System.Func<Color, bool, Material> material)
        {
            string name = def.name.Replace("(Clone)", "").Trim();
            if (name != "Monolith" && name != "Hedron" && name != "Stellation" && name != "Infinite") return false;
            var body = view.Model.GetComponent<Renderer>();
            if (body != null) body.forceRenderingOff = true;   // the shape replaces the capsule (forced off: a flash or meld toggling renderers on won't bring it back)
            var root = new GameObject(RootName).transform;
            root.SetParent(view.Model, false);
            root.gameObject.AddComponent<MakinaShell>();   // marks it: a Makina breaks apart when it dies (Shatter)
            var s = view.Model.localScale;
            root.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));
            int layer = view.gameObject.layer;
            if (name == "Monolith") BuildMonolith(root, layer, material);
            else if (name == "Hedron") BuildHedron(view, root, layer, material);
            else if (name == "Stellation") BuildStellation(view, root, layer, material);
            else BuildInfinite(view, root, layer, material);
            return true;
        }

        private static GameObject Part(PrimitiveType shape, Transform parent, Vector3 pos, Vector3 size, Material mat, int layer, Vector3 euler = default(Vector3))
        {
            var go = GameObject.CreatePrimitive(shape);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void BuildMonolith(Transform root, int layer, System.Func<Color, bool, Material> material)
        {
            var stone = material(Stone, false); var glyph = material(Glyph, true);
            const float w = 1.1f, h = 2.3f, d = 0.55f, bottom = -1f;
            float mid = bottom + h / 2f;
            Part(PrimitiveType.Cube, root, new Vector3(0f, mid, 0f), new Vector3(w, h, d), stone, layer);
            Part(PrimitiveType.Cube, root, new Vector3(0f, bottom + h + 0.06f, 0f), new Vector3(w * 0.8f, 0.12f, d * 0.8f), stone, layer);   // a capstone
            // The same pattern on both broad faces: three bands, a spine, and two squares turned to diamonds.
            foreach (float side in new[] { 1f, -1f })
            {
                float z = side * (d / 2f + 0.01f);
                foreach (float y in new[] { -0.55f, 0.15f, 0.85f }) Part(PrimitiveType.Cube, root, new Vector3(0f, y, z), new Vector3(w * 0.78f, 0.05f, 0.02f), glyph, layer);
                Part(PrimitiveType.Cube, root, new Vector3(0f, 0.15f, z), new Vector3(0.05f, h * 0.7f, 0.02f), glyph, layer);
                foreach (float y in new[] { -0.2f, 0.5f }) Part(PrimitiveType.Cube, root, new Vector3(0f, y, z), new Vector3(0.22f, 0.22f, 0.02f), glyph, layer, new Vector3(0f, 0f, 45f));
            }
        }

        private static void BuildHedron(UnitView view, Transform root, int layer, System.Func<Color, bool, Material> material)
        {
            var body = material(HedronBody, false); var glyph = material(HedronGlyph, true);
            // The spinning part: an inverted pyramid hanging with its tip near the ground, banded and tipped with light.
            var spin = new GameObject("Hedron").transform;
            spin.SetParent(root, false);
            spin.localPosition = new Vector3(0f, 0.15f, 0f);
            var pyr = new GameObject("Pyramid");
            pyr.layer = layer;
            pyr.transform.SetParent(spin, false);
            pyr.AddComponent<MeshFilter>().sharedMesh = Pyramid();
            pyr.AddComponent<MeshRenderer>().sharedMaterial = body;
            pyr.transform.localScale = new Vector3(1.3f, 1.7f, 1.3f);
            Part(PrimitiveType.Cube, spin, new Vector3(0f, 0.45f, 0f), new Vector3(1.05f, 0.05f, 1.05f), glyph, layer, new Vector3(0f, 45f, 0f));   // a band near the top
            Part(PrimitiveType.Cube, spin, new Vector3(0f, -0.1f, 0f), new Vector3(0.62f, 0.05f, 0.62f), glyph, layer, new Vector3(0f, 45f, 0f));  // a band lower down
            Part(PrimitiveType.Sphere, spin, new Vector3(0f, -0.83f, 0f), new Vector3(0.14f, 0.14f, 0.14f), glyph, layer);                         // the tip
            var spinner = spin.gameObject.AddComponent<MakinaSpin>();
            spinner.View = view;
        }

        private static void BuildStellation(UnitView view, Transform root, int layer, System.Func<Color, bool, Material> material)
        {
            var body = material(StellationBody, false); var glyph = material(StellationGlyph, true);
            // The turning part: a small stellated dodecahedron floating at chest height, every spike's edges traced in light,
            // and a glowing heart.
            var spin = new GameObject("Stellation").transform;
            spin.SetParent(root, false);
            spin.localPosition = new Vector3(0f, 0.2f, 0f);
            const float size = 0.75f;   // the spikes' reach from the centre
            var star = new GameObject("Star");
            star.layer = layer;
            star.transform.SetParent(spin, false);
            star.transform.localScale = Vector3.one * size;
            List<Vector3[]> spikes;
            star.AddComponent<MeshFilter>().sharedMesh = Stellated(out spikes);
            star.AddComponent<MeshRenderer>().sharedMaterial = body;
            // The pattern: each spike's edges from its base to its tip, as thin glowing bars sitting just proud of the faces.
            foreach (var spike in spikes)
            {
                Vector3 tip = spike[0] * size;
                for (int i = 1; i < spike.Length; i++)
                {
                    Vector3 b = spike[i] * size * 1.03f, mid = (tip + b) / 2f, along = tip - b;
                    var bar = Part(PrimitiveType.Cube, spin, mid, new Vector3(0.025f, 0.025f, along.magnitude), glyph, layer);
                    bar.transform.localRotation = Quaternion.LookRotation(along.normalized, mid.normalized);
                }
            }
            Part(PrimitiveType.Sphere, spin, Vector3.zero, Vector3.one * 0.32f, glyph, layer);   // its heart, seen between the spikes
            var jitter = spin.gameObject.AddComponent<MakinaJitter>();
            jitter.View = view;
        }

        private static void BuildInfinite(UnitView view, Transform root, int layer, System.Func<Color, bool, Material> material)
        {
            // A wireframe sphere floating at chest height: rings of latitude and longitude traced in light, nothing inside.
            var spin = new GameObject("Infinite").transform;
            spin.SetParent(root, false);
            spin.localPosition = new Vector3(0f, 0.25f, 0f);
            spin.localRotation = Quaternion.Euler(18f, 0f, 12f);   // a slight tilt, so the turn reads
            var wire = new GameObject("Wire");
            wire.layer = layer;
            wire.transform.SetParent(spin, false);
            wire.transform.localScale = Vector3.one * 0.8f;
            wire.AddComponent<MeshFilter>().sharedMesh = WireSphere();
            wire.AddComponent<MeshRenderer>().sharedMaterial = material(InfiniteWire, true);
            var turn = spin.gameObject.AddComponent<MakinaTurn>();
            turn.View = view;
        }

        private static Mesh wireSphere;

        /// <summary>A unit wire sphere: 5 rings of latitude and 6 of longitude, each 32 thin square bars, as one mesh.</summary>
        private static Mesh WireSphere()
        {
            if (wireSphere != null) return wireSphere;
            const int segments = 32; const float thick = 0.03f;
            var verts = new List<Vector3>(); var tris = new List<int>();
            System.Action<Vector3, Vector3> bar = (a, b) =>
            {
                Vector3 along = b - a, n = ((a + b) * 0.5f).normalized;
                Vector3 side = Vector3.Cross(along, n).normalized * thick * 0.5f, up = n * thick * 0.5f;
                int o = verts.Count;
                foreach (var p in new[] { a, b })
                {
                    verts.Add(p - side - up); verts.Add(p + side - up); verts.Add(p + side + up); verts.Add(p - side + up);
                }
                int[] quads = { 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7 };   // the four long faces (the ends meet the next bar)
                for (int q = 0; q < quads.Length; q += 4)
                {
                    tris.Add(o + quads[q]); tris.Add(o + quads[q + 2]); tris.Add(o + quads[q + 1]);
                    tris.Add(o + quads[q]); tris.Add(o + quads[q + 3]); tris.Add(o + quads[q + 2]);
                }
            };
            for (int r = 1; r <= 5; r++)   // latitude: -60..60 degrees
            {
                float lat = (r * 30f - 90f) * Mathf.Deg2Rad, y = Mathf.Sin(lat), rad = Mathf.Cos(lat);
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                    bar(new Vector3(Mathf.Cos(a0) * rad, y, Mathf.Sin(a0) * rad), new Vector3(Mathf.Cos(a1) * rad, y, Mathf.Sin(a1) * rad));
                }
            }
            for (int m = 0; m < 6; m++)   // longitude: great circles through the poles, 30 degrees apart
            {
                float lon = m * Mathf.PI / 6f;
                Vector3 axis = new Vector3(Mathf.Cos(lon), 0f, Mathf.Sin(lon));
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                    bar(axis * Mathf.Cos(a0) + Vector3.up * Mathf.Sin(a0), axis * Mathf.Cos(a1) + Vector3.up * Mathf.Sin(a1));
                }
            }
            wireSphere = new Mesh { name = "WireSphere" };
            wireSphere.SetVertices(verts);
            wireSphere.SetTriangles(tris, 0);
            wireSphere.RecalculateNormals();
            wireSphere.RecalculateBounds();
            return wireSphere;
        }

        private static Mesh stellated;
        private static List<Vector3[]> stellatedSpikes;

        /// <summary>
        /// A small stellated dodecahedron, tips at radius 1, flat-shaded: a five-sided spike on each face of a dodecahedron.
        /// The spikes' tips are an icosahedron's vertices; each one's base is the five dodecahedron corners around it (the
        /// centres of the icosahedron's faces that meet there). 'spikes': each spike as {tip, its base corners in order}.
        /// </summary>
        private static Mesh Stellated(out List<Vector3[]> spikes)
        {
            if (stellated != null) { spikes = stellatedSpikes; return stellated; }
            float g = (1f + Mathf.Sqrt(5f)) / 2f;
            var ico = new List<Vector3>
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
            };
            for (int i = 0; i < ico.Count; i++) ico[i] = ico[i].normalized;
            // The icosahedron's faces: three vertices each an edge apart (an edge is the shortest distance between vertices).
            float edge = (ico[0] - ico[1]).magnitude + 1e-3f;
            var faces = new List<int[]>();
            for (int a = 0; a < 12; a++)
                for (int b = a + 1; b < 12; b++)
                    for (int c = b + 1; c < 12; c++)
                        if ((ico[a] - ico[b]).magnitude < edge && (ico[b] - ico[c]).magnitude < edge && (ico[a] - ico[c]).magnitude < edge) faces.Add(new[] { a, b, c });
            // How far out a spike's base sits: in the true solid, where the spike's faces meet the dodecahedron's edges.
            const float baseRadius = 0.45f;
            var v = new List<Vector3>(); var t = new List<int>();
            spikes = new List<Vector3[]>();
            for (int i = 0; i < 12; i++)
            {
                Vector3 n = ico[i];
                var ring = new List<Vector3>();
                foreach (var f in faces) if (System.Array.IndexOf(f, i) >= 0) ring.Add((ico[f[0]] + ico[f[1]] + ico[f[2]]).normalized * baseRadius);
                // In order around the tip.
                Vector3 u0 = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized, u1 = Vector3.Cross(n, u0);
                ring.Sort((p, q) => Mathf.Atan2(Vector3.Dot(p, u1), Vector3.Dot(p, u0)).CompareTo(Mathf.Atan2(Vector3.Dot(q, u1), Vector3.Dot(q, u0))));
                var spike = new Vector3[ring.Count + 1];
                spike[0] = n;
                for (int k = 0; k < ring.Count; k++) spike[k + 1] = ring[k];
                spikes.Add(spike);
                for (int k = 0; k < ring.Count; k++)
                {
                    Vector3 p0 = n, p1 = ring[k], p2 = ring[(k + 1) % ring.Count];
                    if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), p0 + p1 + p2) < 0f) { var s = p1; p1 = p2; p2 = s; }   // facing out
                    int at = v.Count;
                    v.Add(p0); v.Add(p1); v.Add(p2);
                    t.Add(at); t.Add(at + 1); t.Add(at + 2);
                }
            }
            stellated = new Mesh { name = "Stellation" };
            stellated.SetVertices(v); stellated.SetTriangles(t, 0);
            stellated.RecalculateNormals(); stellated.RecalculateBounds();
            stellatedSpikes = spikes;
            return stellated;
        }

        // ------------------------------------------------------------------ breaking apart

        /// <summary>Does 'view' wear a Makina's look?</summary>
        public static bool Has(UnitView view) { return view != null && view.Model != null && view.Model.GetComponentInChildren<MakinaShell>(true) != null; }

        /// <summary>
        /// A Makina dies: no corpse, it bursts into bits that fly apart and fade. Its look is hidden (not destroyed: a replay
        /// that rewinds past the death shows it again, Restore). False if 'view' isn't a Makina.
        /// </summary>
        public static bool Shatter(UnitView view)
        {
            if (!Has(view)) return false;
            var shell = view.Model.GetComponentInChildren<MakinaShell>(true);
            var parts = shell.GetComponentsInChildren<Renderer>(true);
            int made = 0;
            foreach (var r in parts)
            {
                if (r.forceRenderingOff) continue;
                var b = r.bounds;
                var mat = r.sharedMaterial;
                int pieces = Mathf.Clamp(Mathf.RoundToInt(b.size.magnitude * 3f), 1, 4);
                for (int i = 0; i < pieces && made < MaxDebris; i++, made++)
                {
                    float s = Mathf.Clamp(Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z)) * 0.8f + Random.Range(0.06f, 0.16f), 0.05f, 0.3f);
                    var bit = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.Destroy(bit.GetComponent<Collider>());
                    bit.layer = view.gameObject.layer;
                    bit.transform.position = b.center + Vector3.Scale(b.extents, Random.insideUnitSphere);
                    bit.transform.rotation = Random.rotation;
                    bit.transform.localScale = Vector3.one * s;
                    bit.GetComponent<Renderer>().sharedMaterial = mat;
                    var fly = bit.AddComponent<MakinaDebris>();
                    Vector3 out_ = bit.transform.position - view.transform.position - Vector3.up * 0.3f;
                    fly.Velocity = (out_.sqrMagnitude > 1e-4f ? out_.normalized : Random.onUnitSphere) * Random.Range(3f, 7f) + Vector3.up * Random.Range(1.5f, 4f);
                    fly.Spin = Random.onUnitSphere * Random.Range(360f, 900f);
                }
                r.forceRenderingOff = true;
            }
            return true;
        }

        /// <summary>Show a Makina's look again (the view is reset to a time before it broke).</summary>
        public static void Restore(UnitView view)
        {
            if (!Has(view)) return;
            foreach (var r in view.Model.GetComponentInChildren<MakinaShell>(true).GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = false;
        }

        private const int MaxDebris = 40;

        /// <summary>A square pyramid, tip down: base 1x1 at y = 0.5, tip at y = -0.5. Flat-shaded.</summary>
        private static Mesh Pyramid()
        {
            if (pyramid != null) return pyramid;
            Vector3 tip = new Vector3(0f, -0.5f, 0f);
            Vector3[] b = { new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f) };
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                int k = v.Count;
                v.Add(b[i]); v.Add(tip); v.Add(b[(i + 1) % 4]);   // a side, facing out
                t.Add(k); t.Add(k + 1); t.Add(k + 2);
            }
            int top = v.Count;
            v.Add(b[0]); v.Add(b[1]); v.Add(b[2]); v.Add(b[3]);
            t.Add(top); t.Add(top + 2); t.Add(top + 1); t.Add(top); t.Add(top + 3); t.Add(top + 2);   // the flat top, facing up
            pyramid = new Mesh { name = "HedronPyramid" };
            pyramid.SetVertices(v); pyramid.SetTriangles(t, 0);
            pyramid.RecalculateNormals(); pyramid.RecalculateBounds();
            return pyramid;
        }
    }

    /// <summary>
    /// The Hedron's turning: a slow spin at rest; through Massacre's windup it tips toward the charge and spins up to full
    /// speed, holds it through the charge, then settles back over SettleSeconds. Read off CombatVfx's windups, so it follows
    /// playback (slow motion, replays) rather than the simulation.
    /// </summary>
    public class MakinaSpin : MonoBehaviour
    {
        public UnitView View;
        public float RestSpeed = 30f, TopSpeed = 1080f, SettleSeconds = 1f, TipDegrees = 90f;

        private CombatVfx vfx;
        private float angle, speed, lastActive = -99f;
        private Quaternion tilt = Quaternion.identity;

        private void Update()
        {
            if (View == null || View.Director == null) return;
            if (vfx == null) vfx = Object.FindFirstObjectByType<CombatVfx>();
            float clock = View.Director.PlaybackClock;
            float target = RestSpeed;
            Quaternion wantTilt = Quaternion.identity;
            if (vfx != null)
                foreach (var w in vfx.Windups)
                {
                    if (w.ActorId != View.UnitId || w.Def == null || w.Def.BehaviorId != "massacre") continue;
                    float p = Mathf.Clamp01((clock - w.Start) / Mathf.Max(0.01f, w.ResolveAt - w.Start));
                    target = Mathf.Lerp(RestSpeed, TopSpeed, p * p);   // faster and faster
                    Vector3 dir = w.TargetPos - View.transform.position; dir.y = 0f;
                    if (dir.sqrMagnitude > 1e-4f)
                    {
                        // Tip toward the charge: the pyramid's down axis swings to point along it.
                        Vector3 local = View.transform.InverseTransformDirection(dir.normalized);
                        wantTilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.down, local), p * TipDegrees / 90f);
                    }
                    lastActive = Time.time;
                    break;
                }
            if (target <= RestSpeed && Time.time - lastActive < SettleSeconds)
            {
                // Just charged: top speed, easing back to rest.
                float k = (Time.time - lastActive) / SettleSeconds;
                target = Mathf.Lerp(TopSpeed, RestSpeed, k);
            }
            speed = Mathf.MoveTowards(speed, target, TopSpeed * 4f * Time.deltaTime);
            angle = (angle + speed * Time.deltaTime) % 360f;
            tilt = Quaternion.Slerp(tilt, wantTilt, 1f - Mathf.Exp(-10f * Time.deltaTime));
            transform.localRotation = tilt * Quaternion.Euler(0f, angle, 0f);
        }
    }

    /// <summary>Marks a Makina's look (MakinaLooks.Has): it breaks apart when its unit dies.</summary>
    public class MakinaShell : MonoBehaviour { }

    /// <summary>
    /// The Stellation's turning: while it isn't doing anything it snaps between random orientations in quick, jerky jolts,
    /// holding each for a moment; while it winds up or uses an ability it holds still. Read off CombatVfx's windups, like
    /// MakinaSpin.
    /// </summary>
    public class MakinaJitter : MonoBehaviour
    {
        public UnitView View;
        public float HoldMin = 0.15f, HoldMax = 0.7f, SnapSeconds = 0.07f, TurnMin = 25f, TurnMax = 110f;

        private CombatVfx vfx;
        private Quaternion from = Quaternion.identity, to = Quaternion.identity;
        private float snapStart = -99f, nextSnap;

        private void Update()
        {
            if (View == null || View.Director == null) return;
            if (vfx == null) vfx = Object.FindFirstObjectByType<CombatVfx>();
            bool busy = false;
            if (vfx != null) foreach (var w in vfx.Windups) if (w.ActorId == View.UnitId) { busy = true; break; }
            if (!busy && Time.time >= nextSnap)
            {
                from = transform.localRotation;
                to = Quaternion.AngleAxis(Random.Range(TurnMin, TurnMax) * (Random.value < 0.5f ? -1f : 1f), Random.onUnitSphere) * from;
                snapStart = Time.time;
                nextSnap = Time.time + SnapSeconds + Random.Range(HoldMin, HoldMax);
            }
            float k = Mathf.Clamp01((Time.time - snapStart) / SnapSeconds);
            transform.localRotation = Quaternion.Slerp(from, to, 1f - (1f - k) * (1f - k));   // fast out, a hard stop
        }
    }

    /// <summary>
    /// The Infinite's turning: a steady spin about its own (tilted) up axis while it isn't doing anything, easing to a stop
    /// while it winds up or uses an ability. Read off CombatVfx's windups, like MakinaSpin.
    /// </summary>
    public class MakinaTurn : MonoBehaviour
    {
        public UnitView View;
        public float DegreesPerSecond = 40f;

        private CombatVfx vfx;
        private float speed;

        private void Update()
        {
            if (View == null || View.Director == null) return;
            if (vfx == null) vfx = Object.FindFirstObjectByType<CombatVfx>();
            bool busy = false;
            if (vfx != null) foreach (var w in vfx.Windups) if (w.ActorId == View.UnitId) { busy = true; break; }
            speed = Mathf.MoveTowards(speed, busy ? 0f : DegreesPerSecond, DegreesPerSecond * 3f * Time.deltaTime);
            transform.localRotation = transform.localRotation * Quaternion.Euler(0f, speed * Time.deltaTime, 0f);
        }
    }

    /// <summary>A bit of a broken Makina: flies out under gravity, tumbling, and shrinks away.</summary>
    public class MakinaDebris : MonoBehaviour
    {
        public Vector3 Velocity, Spin;
        public float Life = 1.1f, Gravity = 14f;

        private float age;
        private Vector3 size;

        private void Start() { size = transform.localScale; }

        private void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            Velocity += Vector3.down * Gravity * dt;
            transform.position += Velocity * dt;
            if (transform.position.y < 0.02f && Velocity.y < 0f) { Velocity = new Vector3(Velocity.x * 0.5f, -Velocity.y * 0.3f, Velocity.z * 0.5f); Spin *= 0.5f; }   // a bounce off the ground
            transform.rotation = Quaternion.Euler(Spin * dt) * transform.rotation;
            float fade = Mathf.Clamp01((Life - age) / (Life * 0.4f));
            transform.localScale = size * fade;
            if (age >= Life) Destroy(gameObject);
        }
    }
}
