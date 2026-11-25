using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Highlights units so they read even behind cover. Call Request(view, color) every frame a unit should be highlighted
    /// (a hovered unit's target, enemies a combo would catch); anything not requested in a frame goes back to normal.
    /// Each unit gets a slightly larger copy of its meshes ("shell") drawn with two materials from Unity's built-in
    /// Hidden/Internal-Colored shader:
    ///   x-ray: depth test Greater, so it only shows where something is in front of the unit: an outline through walls;
    ///   glow:  normal depth test, faint, so the unit is tinted where it is in the open.
    /// A unit with a model (UnitDefinition.Model) gets its shell from the model's skinned meshes: a copy of each on the same
    /// bones, so it follows every pose; it can't be scaled up, so its glow is pulled toward the camera (_ZBias) instead. A
    /// placeholder unit's shell is a scaled copy of its plain meshes. Hidden meshes (a model's capsule) get none.
    /// </summary>
    public class UnitHighlighter : MonoBehaviour
    {
        public const float ShellScale = 1.08f;
        public const float XrayAlpha = 0.75f, GlowAlpha = 0.28f;

        private class Shell { public List<Renderer> Renderers = new List<Renderer>(); public Material Xray, Glow; public bool On; }

        private static UnitHighlighter instance;
        private readonly Dictionary<UnitView, Shell> shells = new Dictionary<UnitView, Shell>();
        private readonly Dictionary<UnitView, Color> requested = new Dictionary<UnitView, Color>();
        private readonly HashSet<UnitView> pulsing = new HashSet<UnitView>();
        private Shader shader;

        /// <summary>Glow strength swings this many times a second on a pulsing highlight.</summary>
        public const float PulseHz = 1.4f;

        /// <summary>Highlight this unit in this colour for the current frame; pulse makes the glow throb.</summary>
        public static void Request(UnitView view, Color color, bool pulse = false)
        {
            if (view == null) return;
            if (instance == null) instance = new GameObject("UnitHighlighter").AddComponent<UnitHighlighter>();
            instance.requested[view] = color;   // the latest request this frame wins
            if (pulse) instance.pulsing.Add(view); else instance.pulsing.Remove(view);
        }

        /// <summary>0..1, shared by every pulsing highlight (and anything that wants to pulse in step with them).</summary>
        public static float Pulse { get { return 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PulseHz * Mathf.PI * 2f); } }

        private void LateUpdate()
        {
            foreach (var pair in shells)
                if (pair.Key == null || !requested.ContainsKey(pair.Key)) Show(pair.Value, false);
            foreach (var pair in requested)
            {
                var shell = ShellFor(pair.Key);
                if (shell == null) continue;
                SetColor(shell, pair.Value, pulsing.Contains(pair.Key) ? Pulse : -1f);
                Show(shell, true);
            }
            requested.Clear();
            pulsing.Clear();
        }

        private Shell ShellFor(UnitView view)
        {
            Shell shell;
            if (shells.TryGetValue(view, out shell)) return shell;
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return null;
            shell = new Shell { Xray = MakeMaterial(CompareFunction.Greater), Glow = MakeMaterial(CompareFunction.LessEqual) };
            foreach (var skin in view.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skin.sharedMesh == null || skin.forceRenderingOff || skin.gameObject.name == "HighlightShell") continue;
                foreach (var pass in new[] { shell.Xray, shell.Glow })   // one copy per pass: every part of the model gets both
                {
                    var go = new GameObject("HighlightShell");
                    go.layer = skin.gameObject.layer;
                    go.transform.SetParent(skin.transform.parent, false);
                    go.transform.localPosition = skin.transform.localPosition;
                    go.transform.localRotation = skin.transform.localRotation;
                    go.transform.localScale = skin.transform.localScale;
                    var r = go.AddComponent<SkinnedMeshRenderer>();
                    r.sharedMesh = skin.sharedMesh;
                    r.bones = skin.bones;
                    r.rootBone = skin.rootBone;
                    r.localBounds = skin.localBounds;
                    r.updateWhenOffscreen = true;
                    var mats = new Material[Mathf.Max(1, skin.sharedMesh.subMeshCount)];
                    for (int i = 0; i < mats.Length; i++) mats[i] = pass;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.enabled = false;
                    shell.Renderers.Add(r);
                }
            }
            foreach (var mf in view.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
                if (mf.GetComponent<MeshRenderer>().forceRenderingOff || mf.gameObject.name == "HighlightShell" || mf.gameObject.name == "TacticalEdges") continue;   // a model's hidden capsule
                var go = new GameObject("HighlightShell");
                go.transform.SetParent(mf.transform, false);
                go.transform.localScale = Vector3.one * ShellScale;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = new[] { shell.Xray, shell.Glow };
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;
                shell.Renderers.Add(r);
            }
            shells[view] = shell;
            return shell;
        }

        private Material MakeMaterial(CompareFunction zTest)
        {
            var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_Cull", (int)CullMode.Back);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_ZTest", (int)zTest);
            m.renderQueue = (int)RenderQueue.Overlay;
            if (zTest != CompareFunction.Greater) m.SetFloat("_ZBias", -2f);   // a skinned shell can't be scaled up: its glow sits just in front instead
            return m;
        }

        /// <summary>pulse: -1 for a steady highlight, else 0..1 (dim to bright).</summary>
        private static void SetColor(Shell shell, Color c, float pulse)
        {
            float xray = pulse < 0f ? XrayAlpha : Mathf.Lerp(0.35f, 0.95f, pulse);
            float glow = pulse < 0f ? GlowAlpha : Mathf.Lerp(0.08f, 0.5f, pulse);
            shell.Xray.color = new Color(c.r, c.g, c.b, xray);
            shell.Glow.color = new Color(c.r, c.g, c.b, glow);
        }

        private static void Show(Shell shell, bool on)
        {
            if (shell.On == on) return;
            shell.On = on;
            foreach (var r in shell.Renderers) if (r != null) r.enabled = on;
        }
    }
}
