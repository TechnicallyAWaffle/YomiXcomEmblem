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
    /// Skinned meshes aren't covered (the placeholder units are plain MeshFilters).
    /// </summary>
    public class UnitHighlighter : MonoBehaviour
    {
        public const float ShellScale = 1.08f;
        public const float XrayAlpha = 0.75f, GlowAlpha = 0.28f;

        private class Shell { public List<MeshRenderer> Renderers = new List<MeshRenderer>(); public Material Xray, Glow; public bool On; }

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
            foreach (var mf in view.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
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
