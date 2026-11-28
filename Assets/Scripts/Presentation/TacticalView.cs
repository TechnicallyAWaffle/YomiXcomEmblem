using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// The tactical view: Alt in a live battle (UiDeclutter, not a replay). Besides hiding the UI, it shows the battle as a
    /// see-through diagram: every terrain piece and model is drawn translucent (TacticalGhost shader) with only its hard
    /// edges solid, and every effect is hidden (particles, trails, CombatVfx's own drawing, the screen overlays). CombatVfx
    /// draws what the view is for on top: arrows running along each unit's targeting line, and the weakpoints, highlighted.
    ///
    /// Renderers get their materials swapped for a ghost of each (same colour) and are put back on leaving. Edges are a line
    /// mesh of the mesh's hard edges (faces meeting at more than EdgeAngle, and open borders), made once per mesh: a child
    /// object for a plain mesh, drawn each frame from a baked pose for a skinned one. Renderers that appear while it is on
    /// (summons, debris) are picked up every RescanSeconds.
    /// </summary>
    public class TacticalView : MonoBehaviour
    {
        public PlaybackDirector Director;
        [Tooltip("Faces this solid (0..1) in the tactical view.")]
        [Range(0f, 1f)] public float FaceAlpha = 0.1f;
        [Tooltip("Faces meeting at more than this many degrees draw an edge.")]
        public float EdgeAngle = 25f;
        public float RescanSeconds = 0.5f;
        [Tooltip("The backdrop behind everything while it is up (sky and fog give way to it): a dark grey, so the see-through scene and its edges read.")]
        public Color Backdrop = new Color(0.19f, 0.195f, 0.21f, 1f);
        [Tooltip("Seconds the switch takes: the scene fades from solid to see-through under a veil and vignette, with a flash (as a Transcend takes).")]
        public float TransitionSeconds = 0.4f;
        public Color FlashColor = new Color(0.55f, 0.75f, 0.9f, 1f);

        private static TacticalView instance;

        /// <summary>Is the tactical view up right now?</summary>
        public static bool On { get { return instance != null && instance.active; } }

        private class Ghosted { public Renderer Renderer; public Material[] Original; public Transform Edges; public Mesh EdgeMesh, Baked; public Material EdgeMaterial; }

        private readonly List<Ghosted> ghosted = new List<Ghosted>();
        private readonly HashSet<Renderer> seen = new HashSet<Renderer>();
        private readonly List<Behaviour> hiddenEffects = new List<Behaviour>();
        private readonly List<Renderer> hiddenParticles = new List<Renderer>();
        private static readonly Dictionary<Mesh, Mesh> edgeMeshes = new Dictionary<Mesh, Mesh>();
        private static readonly Dictionary<Material, Material> faceGhosts = new Dictionary<Material, Material>();
        private static readonly Dictionary<Color32, Material> edgeGhosts = new Dictionary<Color32, Material>();
        private static Shader shader;
        private bool active;
        private float nextScan;
        private float switchedAt = -10f;
        private bool switchedOn;
        // The camera and fog as they were, to put back on leaving.
        private Camera cam;
        private CameraClearFlags oldClear;
        private Color oldBackground;
        private bool oldFog, fogKept;

        private void Awake() { instance = this; }

        private void OnDestroy()
        {
            if (active) Exit();
            if (instance == this) instance = null;
        }

        private bool Wanted { get { return Director != null && Director.Timeline != null && !Director.IsReplay && UiDeclutter.On; } }

        private void LateUpdate()
        {
            bool want = Wanted;
            if (want != active) { if (want) Enter(); else Exit(); switchedAt = Time.unscaledTime; switchedOn = want; }
            if (!active) return;
            FadeIn(Mathf.Clamp01((Time.unscaledTime - switchedAt) / Mathf.Max(0.01f, TransitionSeconds)));
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + RescanSeconds; Scan(); }
            for (int i = ghosted.Count - 1; i >= 0; i--)
            {
                var g = ghosted[i];
                if (g.Renderer == null) { if (g.Edges != null) Destroy(g.Edges.gameObject); seen.Remove(g.Renderer); ghosted.RemoveAt(i); continue; }
                bool shown = g.Renderer.enabled && !g.Renderer.forceRenderingOff && g.Renderer.gameObject.activeInHierarchy;
                if (g.Edges != null) { if (g.Edges.gameObject.activeSelf != shown) g.Edges.gameObject.SetActive(shown); continue; }
                var skinned = g.Renderer as SkinnedMeshRenderer;
                if (!shown || skinned == null || g.EdgeMesh == null) continue;
                // A skinned body: this frame's pose, its edges drawn from it (the edge mesh shares its vertex order).
                skinned.BakeMesh(g.Baked, true);
                g.EdgeMesh.SetVertices(g.Baked.vertices);
                g.EdgeMesh.RecalculateBounds();
                var t = skinned.transform;
                Graphics.DrawMesh(g.EdgeMesh, Matrix4x4.TRS(t.position, t.rotation, Vector3.one), g.EdgeMaterial, g.Renderer.gameObject.layer);
            }
        }

        private void Enter()
        {
            if (shader == null) shader = Resources.Load<Shader>("Tactical/TacticalGhost");
            if (shader == null) shader = Shader.Find("Apotheom/TacticalGhost");
            if (shader == null) { Debug.LogWarning("TacticalView: the TacticalGhost shader is missing (Resources/Tactical)."); return; }
            active = true;
            nextScan = Time.unscaledTime + RescanSeconds;
            Scan();
            cam = Camera.main;
            if (cam != null)
            {
                oldClear = cam.clearFlags; oldBackground = cam.backgroundColor;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Backdrop;
            }
            oldFog = RenderSettings.fog; fogKept = true;
            RenderSettings.fog = false;
            FadeIn(0f);
        }

        private void Exit()
        {
            active = false;
            if (cam != null) { cam.clearFlags = oldClear; cam.backgroundColor = oldBackground; }
            if (fogKept) { RenderSettings.fog = oldFog; fogKept = false; }
            cam = null;
            foreach (var g in ghosted)
            {
                if (g.Renderer != null) g.Renderer.sharedMaterials = g.Original;
                if (g.Edges != null) Destroy(g.Edges.gameObject);
                if (g.Baked != null) Destroy(g.Baked);
            }
            ghosted.Clear();
            seen.Clear();
            foreach (var r in hiddenParticles) if (r != null) r.enabled = true;
            foreach (var b in hiddenEffects) if (b != null) b.enabled = true;
            hiddenParticles.Clear();
            hiddenEffects.Clear();
        }

        /// <summary>The way in: k 0..1 over TransitionSeconds, faces fading from solid to FaceAlpha and edges coming up.</summary>
        private float faded = -1f;
        private void FadeIn(float k)
        {
            if (Mathf.Approximately(k, faded)) return;
            faded = k;
            float e = k * k * (3f - 2f * k);
            foreach (var m in faceGhosts.Values)
            {
                if (m == null) continue;
                var c = m.GetColor("_GhostColor"); c.a = Mathf.Lerp(0.9f, FaceAlpha, e); m.SetColor("_GhostColor", c);
            }
            foreach (var m in edgeGhosts.Values)
            {
                if (m == null) continue;
                var c = m.GetColor("_GhostColor"); c.a = e; m.SetColor("_GhostColor", c);
            }
        }

        /// <summary>
        /// The switch, drawn over everything as a Transcend's is (PlayerOrdersUI.DrawTranscendOverlay): a dark veil and a
        /// vignette that lift as it settles, and a flash of FlashColor (fainter on the way out).
        /// </summary>
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            float t = (Time.unscaledTime - switchedAt) / Mathf.Max(0.01f, TransitionSeconds);
            if (t < 0f || t >= 1f) return;
            float k = 1f - t;
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            var old = GUI.color;
            GUI.color = new Color(0.03f, 0.04f, 0.06f, (switchedOn ? 0.45f : 0.3f) * k);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.9f * k);
            GUI.DrawTexture(screen, Vignette(), ScaleMode.StretchToFill);
            float flash = Mathf.Clamp01(1f - t * 2.5f);
            GUI.color = new Color(FlashColor.r, FlashColor.g, FlashColor.b, (switchedOn ? 0.35f : 0.2f) * flash);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static Texture2D vignette;
        /// <summary>A screen vignette: clear in the middle, black toward the corners (also DodgeMoment's).</summary>
        public static Texture2D Vignette()
        {
            if (vignette != null) return vignette;
            const int size = 64;
            vignette = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.41421f;
                    px[y * size + x] = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1f, d)));
                }
            vignette.SetPixels(px);
            vignette.Apply();
            return vignette;
        }

        /// <summary>Ghost every model and terrain piece not ghosted yet, and hide every effect showing.</summary>
        private void Scan()
        {
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer)
                {
                    if (r.enabled) { r.enabled = false; hiddenParticles.Add(r); }
                    continue;
                }
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || seen.Contains(r)) continue;
                seen.Add(r);
                if (Skip(r)) continue;
                Ghost(r);
            }
            foreach (var d in FindObjectsByType<DeliveranceDistortion>(FindObjectsSortMode.None))
                if (d.enabled) { d.enabled = false; hiddenEffects.Add(d); }
        }

        private bool Skip(Renderer r)
        {
            if (r.gameObject.layer == 5) return true;   // UI
            string n = r.gameObject.name;
            if (n == "HighlightShell" || n == "TacticalEdges" || n == "VfxQuad") return true;   // highlights, edges, the VFX's quads
            foreach (var m in r.sharedMaterials)
                if (m != null && m.shader != null && (m.shader == shader || m.shader.name == "Apotheom/VFX" || m.shader.name.StartsWith("Hidden/"))) return true;
            return false;
        }

        private void Ghost(Renderer r)
        {
            var original = r.sharedMaterials;
            var ghosts = new Material[original.Length];
            Color edge = new Color(0.85f, 0.85f, 0.85f);
            for (int i = 0; i < original.Length; i++)
            {
                Color c = ColorOf(r, original[i]);
                if (i == 0) edge = c;
                ghosts[i] = FaceGhost(original[i], c);
            }
            var g = new Ghosted { Renderer = r, Original = original };
            r.sharedMaterials = ghosts;

            var skinned = r as SkinnedMeshRenderer;
            var src = skinned != null ? skinned.sharedMesh : (r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null);
            var lines = src != null ? EdgesOf(src) : null;
            if (lines != null)
            {
                g.EdgeMaterial = EdgeGhost(Color.Lerp(edge, Color.white, 0.35f));
                if (skinned != null)
                {
                    g.EdgeMesh = Instantiate(lines);   // its own copy: its vertices follow this body's pose
                    g.EdgeMesh.MarkDynamic();
                    g.Baked = new Mesh();
                }
                else
                {
                    var go = new GameObject("TacticalEdges");
                    go.layer = r.gameObject.layer;
                    go.transform.SetParent(r.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = lines;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = g.EdgeMaterial;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    g.Edges = go.transform;
                }
            }
            ghosted.Add(g);
        }

        /// <summary>The colour a renderer shows for one of its materials: a property block's colour (a tint) aside, the material's own.</summary>
        private static Color ColorOf(Renderer r, Material m)
        {
            if (m == null) return new Color(0.8f, 0.8f, 0.8f);
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return new Color(0.8f, 0.8f, 0.8f);
        }

        private Material FaceGhost(Material original, Color c)
        {
            Material g;
            if (original != null && faceGhosts.TryGetValue(original, out g) && g != null) return g;
            g = new Material(shader) { name = "Ghost " + (original != null ? original.name : "none"), hideFlags = HideFlags.DontSave };
            g.SetColor("_GhostColor", new Color(c.r, c.g, c.b, FaceAlpha));
            g.SetFloat("_Rim", 0.25f);
            if (original != null) faceGhosts[original] = g;
            return g;
        }

        private Material EdgeGhost(Color c)
        {
            c.a = 1f;
            Color32 key = c;
            Material g;
            if (edgeGhosts.TryGetValue(key, out g) && g != null) return g;
            g = new Material(shader) { name = "Ghost edges", hideFlags = HideFlags.DontSave };
            g.SetColor("_GhostColor", c);
            g.SetFloat("_Rim", 0f);
            g.renderQueue = 3010;   // over the faces
            edgeGhosts[key] = g;
            return g;
        }

        /// <summary>
        /// A line mesh of 'src's hard edges: an edge two faces meet at more than EdgeAngle, or with one face only (an open
        /// border). It keeps 'src's vertices (and their order), so a skinned body's baked pose can be poured straight in.
        /// Corners split for flat shading are welded by position first. Null for a mesh that can't be read.
        /// </summary>
        private Mesh EdgesOf(Mesh src)
        {
            Mesh lines;
            if (edgeMeshes.TryGetValue(src, out lines)) return lines;
            if (!src.isReadable) { edgeMeshes[src] = null; return null; }
            var verts = src.vertices;
            var tris = src.triangles;
            var weld = new int[verts.Length];
            var byPos = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < verts.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(verts[i].x * 1000f), Mathf.RoundToInt(verts[i].y * 1000f), Mathf.RoundToInt(verts[i].z * 1000f));
                int w;
                if (!byPos.TryGetValue(k, out w)) byPos[k] = w = i;
                weld[i] = w;
            }
            float cos = Mathf.Cos(EdgeAngle * Mathf.Deg2Rad);
            var edges = new Dictionary<long, Edge>();
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 n = Vector3.Cross(verts[tris[t + 1]] - verts[tris[t]], verts[tris[t + 2]] - verts[tris[t]]);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[t + e], b = tris[t + (e + 1) % 3];
                    int wa = weld[a], wb = weld[b];
                    if (wa == wb) continue;
                    long key = wa < wb ? ((long)wa << 32) | (uint)wb : ((long)wb << 32) | (uint)wa;
                    Edge rec;
                    if (!edges.TryGetValue(key, out rec)) { edges[key] = new Edge { A = a, B = b, Normal = n, Faces = 1 }; continue; }
                    rec.Faces++;
                    if (Vector3.Dot(rec.Normal, n) < cos) rec.Sharp = true;
                    edges[key] = rec;
                }
            }
            var idx = new List<int>();
            foreach (var e in edges.Values)
                if (e.Faces == 1 || e.Sharp || e.Faces > 2) { idx.Add(e.A); idx.Add(e.B); }
            lines = new Mesh { name = src.name + " edges", hideFlags = HideFlags.DontSave };
            if (verts.Length > 65000) lines.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            lines.vertices = verts;
            lines.normals = src.normals.Length == verts.Length ? src.normals : null;
            lines.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
            lines.bounds = src.bounds;
            edgeMeshes[src] = lines;
            return lines;
        }

        private struct Edge { public int A, B, Faces; public Vector3 Normal; public bool Sharp; }
    }
}
