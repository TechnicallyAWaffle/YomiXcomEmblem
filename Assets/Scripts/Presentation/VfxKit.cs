using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// The combat VFX's building blocks (CombatVfx draws with them): the "Apotheom/VFX" shader and its textures from
    /// Resources/VFX, made into materials in three modes, pooled particle systems over them, and pooled textured quads
    /// (ground decals and camera-facing billboards) drawn immediate-mode like WorldMarkers (Begin, draw, End).
    ///
    /// The look (from the art references): hard-edged ink with coloured edges that erodes away instead of fading (Ink),
    /// thin radiant light that blooms (Glow), and brushy painted strokes and rings (Paint). Cheap enough for low-end
    /// machines: one shader, a handful of materials (so they batch), capped particle counts, nothing allocated per effect.
    /// </summary>
    public class VfxKit
    {
        public enum Mode { Ink, Glow, Paint }

        /// <summary>The textures (Resources/VFX/Tex): R edge band, G dissolve noise, A shape.</summary>
        public enum Tex { Puff, Shard, Splat, Rays, Ring, Sigil, Stroke, Halo, Glyphs }

        private static readonly string[] TexNames = { "puff", "shard", "splat", "rays", "ring", "sigil", "stroke_tile", "halo", "glyphs" };

        public readonly bool Ready;
        private readonly Shader shader;
        private readonly Texture2D[] textures = new Texture2D[TexNames.Length];
        private readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
        private readonly Transform root;

        /// <summary>How far past 1 Glow colours go, so they pass the Volume's bloom threshold.</summary>
        public float GlowBoost = 2.2f;

        public VfxKit(Transform parent)
        {
            root = new GameObject("VfxKit").transform;
            root.SetParent(parent, false);
            shader = Resources.Load<Shader>("VFX/ApotheomVfx");
            if (shader == null) shader = Shader.Find("Apotheom/VFX");
            for (int i = 0; i < TexNames.Length; i++) textures[i] = Resources.Load<Texture2D>("VFX/Tex/" + TexNames[i]);
            Ready = shader != null && textures[(int)Tex.Shard] != null;
            quadMesh = MakeQuad();
        }

        // ------------------------------------------------------------------ materials

        /// <summary>The shared material for a texture in a mode (made once).</summary>
        public Material Material(Tex tex, Mode mode)
        {
            int key = (int)tex * 8 + (int)mode;
            Material m;
            if (materials.TryGetValue(key, out m) && m != null) return m;
            if (!Ready) return null;
            m = new Material(shader) { name = "Vfx " + tex + " " + mode, mainTexture = textures[(int)tex] };
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(mode == Mode.Glow ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_TintAll", mode == Mode.Ink ? 0f : 1f);
            m.SetFloat("_Dissolve", mode == Mode.Ink ? 1f : 0f);
            m.SetFloat("_Boost", mode == Mode.Glow ? GlowBoost : 1f);
            m.renderQueue = mode == Mode.Glow ? 3100 : 3050;   // light over ink
            materials[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ particle systems

        /// <summary>How a particle system draws: camera-facing, stretched along its motion, or lying flat on the ground.</summary>
        public enum Shape { Billboard, Stretched, Flat }

        /// <summary>
        /// A pooled particle system (no emission of its own: emit with Burst). Size over its life follows sizeFrom to sizeTo;
        /// its colour's alpha falls from 1 to 0 (for Ink, how much is left as it erodes).
        /// </summary>
        public ParticleSystem System(string name, Tex tex, Mode mode, Shape shape, float gravity, float sizeFrom = 1f, float sizeTo = 0f, int max = 600)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            var emission = ps.emission; emission.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, sizeFrom, 1f, sizeTo));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         mode == Mode.Ink ? new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.45f), new GradientAlphaKey(0f, 1f) }
                                          : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Material(tex, mode);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (shape == Shape.Stretched) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 2.2f; }
            else if (shape == Shape.Flat) { r.renderMode = ParticleSystemRenderMode.HorizontalBillboard; }
            ps.Play();
            return ps;
        }

        /// <summary>'count' particles at 'pos' flung every way at up to 'speed' (plus 'bias'), 'size' each, living 'life'
        /// (each a little more or less, so a burst doesn't move as one).</summary>
        public static void Burst(ParticleSystem ps, Vector3 pos, int count, Color color, float speed, float size, float life, Vector3 bias = default(Vector3), bool spin = true)
        {
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                ep.position = pos;
                ep.velocity = Random.insideUnitSphere * speed + bias;
                ep.startSize = size * Random.Range(0.6f, 1.25f);
                ep.startLifetime = life * Random.Range(0.75f, 1.15f);
                ep.rotation = spin ? Random.Range(0f, 360f) : 0f;
                ps.Emit(ep, 1);
            }
        }

        /// <summary>'count' particles fanned out from 'pos' along 'dir' (within 'spread' degrees), at 'speed' each.</summary>
        public static void Spray(ParticleSystem ps, Vector3 pos, Vector3 dir, float spread, int count, Color color, float speed, float size, float life)
        {
            if (ps == null || dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            var ep = new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector3 d = Quaternion.AngleAxis(Random.Range(-spread, spread), Random.onUnitSphere) * dir;
                ep.position = pos;
                ep.velocity = d * speed * Random.Range(0.6f, 1.2f);
                ep.startSize = size * Random.Range(0.6f, 1.25f);
                ep.startLifetime = life * Random.Range(0.75f, 1.15f);
                ps.Emit(ep, 1);
            }
        }

        // ------------------------------------------------------------------ quads (decals and billboards)

        private readonly Mesh quadMesh;
        private readonly List<MeshRenderer> quads = new List<MeshRenderer>();
        private int usedQuads;
        private MaterialPropertyBlock block;

        public void Begin() { usedQuads = 0; }

        public void End()
        {
            for (int i = usedQuads; i < quads.Count; i++) if (quads[i].enabled) quads[i].enabled = false;
        }

        /// <summary>A textured square lying on the ground at 'pos' (just above it), 'size' across, turned 'angle' degrees.</summary>
        public void Decal(Tex tex, Mode mode, Vector3 pos, float size, float angle, Color color)
        {
            Quad(tex, mode, pos + Vector3.up * 0.06f, Quaternion.Euler(90f, angle, 0f), new Vector3(size, size, 1f), color, 0);
        }

        /// <summary>A textured square facing the camera at 'pos', 'size' across, turned 'angle' degrees in the view.</summary>
        public void Billboard(Tex tex, Mode mode, Vector3 pos, float size, float angle, Color color, Camera cam)
        {
            Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
            Quad(tex, mode, pos, face * Quaternion.Euler(0f, 0f, angle), new Vector3(size, size, 1f), color, 0);
        }

        /// <summary>One cell of the glyph strip (four glyphs side by side) as a camera-facing billboard.</summary>
        public void Glyph(int index, Vector3 pos, float size, Color color, Camera cam)
        {
            Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
            Quad(Tex.Glyphs, Mode.Glow, pos, face, new Vector3(size, size, 1f), color, index + 1);
        }

        /// <summary>A textured quad oriented by 'rotation' (its face is local +Z), scaled by 'scale'. cell: 0 the whole
        /// texture, 1..4 one quarter of the glyph strip.</summary>
        public void Quad(Tex tex, Mode mode, Vector3 pos, Quaternion rotation, Vector3 scale, Color color, int cell)
        {
            var mat = Material(tex, mode);
            if (mat == null) return;
            if (usedQuads == quads.Count)
            {
                var go = new GameObject("VfxQuad");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                quads.Add(mr);
            }
            var q = quads[usedQuads++];
            q.enabled = true;
            q.sharedMaterial = mat;
            q.transform.SetPositionAndRotation(pos, rotation);
            q.transform.localScale = scale;
            if (block == null) block = new MaterialPropertyBlock();
            block.Clear();
            block.SetColor("_Color", color);
            block.SetVector("_MainTex_ST", cell <= 0 ? new Vector4(1f, 1f, 0f, 0f) : new Vector4(0.25f, 1f, (cell - 1) * 0.25f, 0f));
            q.SetPropertyBlock(block);
        }

        private static Mesh MakeQuad()
        {
            var m = new Mesh { name = "VfxQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }
    }
}
