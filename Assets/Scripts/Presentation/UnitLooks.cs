using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Placeholder looks until there are real models: each unit type gets its own body proportions and a few primitive
    /// props on its Model (a shield, a rifle, horns, a halo...), so the types are easy to tell apart at a glance. Picked by
    /// the unit definition's asset name (Looks below); a type with no entry keeps the plain capsule. Props have no
    /// colliders and keep their own colours: the team colour stays on the body. BattleContext applies them each battle.
    /// Model space: the capsule is centred on the origin, 2 tall and 1 wide; +z is the way the unit faces.
    /// </summary>
    public static class UnitLooks
    {
        private const string PropsName = "LookProps";

        private struct Prop
        {
            public PrimitiveType Shape; public Vector3 Position, Euler, Size; public Color Color; public bool Glow;
        }

        private class Look
        {
            public Vector3 Body = Vector3.one;
            public readonly List<Prop> Props = new List<Prop>();

            public Look Add(PrimitiveType shape, Vector3 position, Vector3 size, Color color, Vector3 euler = default(Vector3), bool glow = false)
            {
                Props.Add(new Prop { Shape = shape, Position = position, Size = size, Color = color, Euler = euler, Glow = glow });
                return this;
            }
        }

        private static readonly Color Metal = new Color(0.72f, 0.74f, 0.8f), Dark = new Color(0.14f, 0.14f, 0.17f), Wood = new Color(0.45f, 0.3f, 0.18f),
                                      Gold = new Color(1f, 0.82f, 0.3f), Bone = new Color(0.92f, 0.88f, 0.75f), Cloth = new Color(0.2f, 0.32f, 0.22f);

        private static Look L(float width, float height) { return new Look { Body = new Vector3(width, height, width) }; }

        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            // The squad
            { "Sharpshooter", L(0.9f, 1.05f)
                .Add(PrimitiveType.Cube, new Vector3(0.38f, 0.1f, 0.5f), new Vector3(0.08f, 0.08f, 1.2f), Dark)            // rifle
                .Add(PrimitiveType.Cylinder, new Vector3(0.38f, 0.2f, 0.45f), new Vector3(0.07f, 0.15f, 0.07f), Dark, new Vector3(90f, 0f, 0f))
                .Add(PrimitiveType.Cube, new Vector3(0f, 0.68f, 0.42f), new Vector3(0.6f, 0.12f, 0.14f), new Color(0.4f, 0.9f, 1f), default(Vector3), true) },   // goggles
            { "Melee", L(0.95f, 1f)
                .Add(PrimitiveType.Cube, new Vector3(0.58f, 0.05f, 0.35f), new Vector3(0.07f, 1.3f, 0.14f), Metal, new Vector3(35f, 0f, 0f))   // sword
                .Add(PrimitiveType.Cube, new Vector3(0.58f, -0.42f, 0.02f), new Vector3(0.1f, 0.08f, 0.4f), Gold, new Vector3(35f, 0f, 0f))
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 0.72f, 0f), new Vector3(1.04f, 0.05f, 1.04f), new Color(0.85f, 0.15f, 0.15f)) },   // headband
            { "Tank", L(1.25f, 1.05f)
                .Add(PrimitiveType.Cube, new Vector3(0f, -0.05f, 0.62f), new Vector3(0.95f, 1.25f, 0.12f), Metal)          // shield
                .Add(PrimitiveType.Cube, new Vector3(0f, -0.05f, 0.69f), new Vector3(0.18f, 0.9f, 0.04f), Gold)
                .Add(PrimitiveType.Sphere, new Vector3(0.52f, 0.5f, 0f), new Vector3(0.45f, 0.35f, 0.45f), Metal)        // pauldrons
                .Add(PrimitiveType.Sphere, new Vector3(-0.52f, 0.5f, 0f), new Vector3(0.45f, 0.35f, 0.45f), Metal) },
            { "Paradigm", L(0.85f, 1f)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 1.25f, 0f), new Vector3(0.75f, 0.02f, 0.75f), new Color(0.75f, 0.45f, 1f), default(Vector3), true)   // halo
                .Add(PrimitiveType.Sphere, new Vector3(0.62f, 0.75f, 0.25f), new Vector3(0.22f, 0.22f, 0.22f), new Color(0.75f, 0.45f, 1f), default(Vector3), true)
                .Add(PrimitiveType.Sphere, new Vector3(-0.62f, 0.55f, 0.25f), new Vector3(0.18f, 0.18f, 0.18f), new Color(0.45f, 0.85f, 1f), default(Vector3), true) },
            // Enemy types
            { "Sniper", L(0.85f, 1.1f)
                .Add(PrimitiveType.Cube, new Vector3(0.38f, 0.1f, 0.7f), new Vector3(0.07f, 0.07f, 1.7f), Dark)            // long rifle
                .Add(PrimitiveType.Cube, new Vector3(0f, 0.05f, -0.46f), new Vector3(0.8f, 1.5f, 0.06f), Cloth)            // cloak
                .Add(PrimitiveType.Sphere, new Vector3(0f, 0.7f, 0.4f), new Vector3(0.16f, 0.16f, 0.16f), new Color(1f, 0.25f, 0.2f), default(Vector3), true) },   // scope eye
            { "Assassin", L(0.8f, 0.92f)
                .Add(PrimitiveType.Cube, new Vector3(0.5f, -0.15f, 0.25f), new Vector3(0.05f, 0.6f, 0.09f), Metal, new Vector3(60f, 0f, 0f))   // daggers
                .Add(PrimitiveType.Cube, new Vector3(-0.5f, -0.15f, 0.25f), new Vector3(0.05f, 0.6f, 0.09f), Metal, new Vector3(60f, 0f, 0f))
                .Add(PrimitiveType.Cube, new Vector3(0f, 0.55f, 0.42f), new Vector3(0.62f, 0.26f, 0.12f), Dark) },          // mask
            { "Skirmisher", L(0.9f, 0.95f)
                .Add(PrimitiveType.Cube, new Vector3(0.55f, 0f, 0.2f), new Vector3(0.06f, 0.95f, 0.12f), Metal, new Vector3(20f, 0f, -30f))   // twin blades
                .Add(PrimitiveType.Cube, new Vector3(-0.55f, 0f, 0.2f), new Vector3(0.06f, 0.95f, 0.12f), Metal, new Vector3(20f, 0f, 30f))
                .Add(PrimitiveType.Cylinder, new Vector3(0f, -0.1f, 0f), new Vector3(1.06f, 0.06f, 1.06f), new Color(1f, 0.55f, 0.15f), new Vector3(0f, 0f, 20f)) },   // sash
            { "Healer", L(0.9f, 1f)
                .Add(PrimitiveType.Cylinder, new Vector3(0.58f, 0.1f, 0.2f), new Vector3(0.07f, 1.1f, 0.07f), Wood)       // staff
                .Add(PrimitiveType.Sphere, new Vector3(0.58f, 1.25f, 0.2f), new Vector3(0.26f, 0.26f, 0.26f), new Color(0.35f, 1f, 0.55f), default(Vector3), true)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 1.22f, 0f), new Vector3(0.7f, 0.02f, 0.7f), new Color(0.35f, 1f, 0.55f), default(Vector3), true) },   // halo
            { "Caster", L(0.9f, 1f)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 0.98f, 0f), new Vector3(1.15f, 0.03f, 1.15f), new Color(0.2f, 0.2f, 0.45f))   // hat brim
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 1.2f, 0f), new Vector3(0.5f, 0.22f, 0.5f), new Color(0.2f, 0.2f, 0.45f))      // and crown
                .Add(PrimitiveType.Sphere, new Vector3(0.68f, 0.3f, 0.3f), new Vector3(0.28f, 0.28f, 0.28f), new Color(0.55f, 0.3f, 1f), default(Vector3), true)
                .Add(PrimitiveType.Sphere, new Vector3(-0.68f, 0.3f, 0.3f), new Vector3(0.28f, 0.28f, 0.28f), new Color(0.55f, 0.3f, 1f), default(Vector3), true) },
            { "Brutebeast", L(1.35f, 1.12f)
                .Add(PrimitiveType.Cube, new Vector3(0.28f, 1f, 0.15f), new Vector3(0.12f, 0.45f, 0.12f), Bone, new Vector3(0f, 0f, -28f))   // horns
                .Add(PrimitiveType.Cube, new Vector3(-0.28f, 1f, 0.15f), new Vector3(0.12f, 0.45f, 0.12f), Bone, new Vector3(0f, 0f, 28f))
                .Add(PrimitiveType.Sphere, new Vector3(0.62f, -0.3f, 0.3f), new Vector3(0.5f, 0.5f, 0.5f), Dark)          // fists
                .Add(PrimitiveType.Sphere, new Vector3(-0.62f, -0.3f, 0.3f), new Vector3(0.5f, 0.5f, 0.5f), Dark) },
        };

        private static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

        /// <summary>Give 'view' the look for 'def' (replacing any earlier one). Safe to call again on the same view.</summary>
        public static void Apply(UnitView view, UnitDefinition def)
        {
            if (view == null || view.Model == null || def == null) return;
            var old = view.Model.Find(PropsName);
            if (old != null) { old.name = PropsName + "_old"; if (Application.isPlaying) Object.Destroy(old.gameObject); else Object.DestroyImmediate(old.gameObject); }
            Look look;
            if (!Looks.TryGetValue(def.name.Replace("(Clone)", "").Trim(), out look)) return;

            view.SetBodyScale(look.Body);
            var body = view.Model.GetComponent<Renderer>();
            var root = new GameObject(PropsName).transform;
            root.SetParent(view.Model, false);
            // Props keep their real size whatever the body's proportions.
            root.localScale = new Vector3(1f / look.Body.x, 1f / look.Body.y, 1f / look.Body.z);
            foreach (var p in look.Props)
            {
                var go = GameObject.CreatePrimitive(p.Shape);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.layer = view.gameObject.layer;
                go.transform.SetParent(root, false);
                go.transform.localPosition = Vector3.Scale(p.Position, look.Body);
                go.transform.localRotation = Quaternion.Euler(p.Euler);
                go.transform.localScale = p.Size;
                go.GetComponent<Renderer>().sharedMaterial = MaterialFor(p.Color, p.Glow, body != null ? body.sharedMaterial : null);
            }
        }

        private static Material MaterialFor(Color color, bool glow, Material like)
        {
            Color key = glow ? new Color(color.r, color.g, color.b, 0.5f) : color;
            Material m;
            if (materials.TryGetValue(key, out m) && m != null) return m;
            m = like != null ? new Material(like) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "LookProp";
            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);
            if (glow)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 1.5f);
            }
            materials[key] = m;
            return m;
        }
    }
}
