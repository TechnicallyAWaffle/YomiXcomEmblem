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

        /// <summary>A cloud of particles around the unit (shadow, holy light, sparks...): colour, how many a second, how big,
        /// how fast they drift, and whether they rise (else they scatter every way).</summary>
        private struct Aura { public Color Color; public float Rate, Size, Speed, Life; public bool Rises; }

        private class Look
        {
            public Vector3 Body = Vector3.one;
            public readonly List<Prop> Props = new List<Prop>();
            public readonly List<Aura> Auras = new List<Aura>();

            public Look Particles(Color color, float rate, float size, float speed, float life, bool rises)
            {
                Auras.Add(new Aura { Color = color, Rate = rate, Size = size, Speed = speed, Life = life, Rises = rises });
                return this;
            }

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
            { "Exemplar", L(1f, 1.08f)   // a gold-adorned knight with a longsword
                .Add(PrimitiveType.Cube, new Vector3(0.6f, 0.25f, 0.45f), new Vector3(0.08f, 1.7f, 0.16f), Metal, new Vector3(30f, 0f, 0f))           // longsword blade
                .Add(PrimitiveType.Cube, new Vector3(0.6f, -0.5f, 0.02f), new Vector3(0.14f, 0.1f, 0.5f), Gold, new Vector3(30f, 0f, 0f))           // crossguard
                .Add(PrimitiveType.Cube, new Vector3(0.6f, -0.66f, -0.08f), new Vector3(0.09f, 0.28f, 0.09f), Gold, new Vector3(30f, 0f, 0f))       // grip
                .Add(PrimitiveType.Sphere, new Vector3(0f, 0.72f, 0f), new Vector3(0.82f, 0.62f, 0.82f), Metal)                                    // helm
                .Add(PrimitiveType.Cube, new Vector3(0f, 0.7f, 0.4f), new Vector3(0.5f, 0.07f, 0.08f), Dark)                                       // visor slit
                .Add(PrimitiveType.Cube, new Vector3(0f, 1.08f, -0.05f), new Vector3(0.08f, 0.32f, 0.6f), Gold)                                    // crest
                .Add(PrimitiveType.Sphere, new Vector3(0.55f, 0.45f, 0f), new Vector3(0.48f, 0.34f, 0.48f), Gold)                                  // pauldrons
                .Add(PrimitiveType.Sphere, new Vector3(-0.55f, 0.45f, 0f), new Vector3(0.48f, 0.34f, 0.48f), Gold)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, -0.15f, 0f), new Vector3(1.06f, 0.05f, 1.06f), Gold)                                  // belt
                .Add(PrimitiveType.Cube, new Vector3(0f, 0.2f, 0.52f), new Vector3(0.12f, 0.5f, 0.04f), Gold)                                      // chest sigil
                .Particles(new Color(1f, 0.85f, 0.4f, 0.6f), 6f, 0.08f, 0.4f, 1.2f, true) },
            { "Deliverance", L(1.15f, 1.18f)   // one being made of five: a fractured crown, four floating shards (its parts), a torn mantle
                .Add(PrimitiveType.Cube, new Vector3(0.2f, 1.08f, 0f), new Vector3(0.07f, 0.42f, 0.07f), new Color(1f, 0.3f, 0.85f), new Vector3(0f, 0f, -25f), true)   // crown
                .Add(PrimitiveType.Cube, new Vector3(-0.2f, 1.08f, 0f), new Vector3(0.07f, 0.42f, 0.07f), new Color(0.3f, 1f, 0.9f), new Vector3(0f, 0f, 25f), true)
                .Add(PrimitiveType.Cube, new Vector3(0f, 1.16f, 0f), new Vector3(0.07f, 0.5f, 0.07f), Color.white, default(Vector3), true)
                .Add(PrimitiveType.Cube, new Vector3(0.78f, 0.6f, 0.2f), new Vector3(0.22f, 0.36f, 0.06f), new Color(0.7f, 0.85f, 1f), new Vector3(0f, 30f, 15f), true)   // the four shards
                .Add(PrimitiveType.Cube, new Vector3(-0.78f, 0.6f, 0.2f), new Vector3(0.22f, 0.36f, 0.06f), new Color(1f, 0.55f, 0.3f), new Vector3(0f, -30f, -15f), true)
                .Add(PrimitiveType.Cube, new Vector3(0.6f, 0.05f, -0.5f), new Vector3(0.22f, 0.36f, 0.06f), new Color(0.55f, 1f, 0.45f), new Vector3(20f, 10f, 40f), true)
                .Add(PrimitiveType.Cube, new Vector3(-0.6f, 0.05f, -0.5f), new Vector3(0.22f, 0.36f, 0.06f), new Color(0.75f, 0.45f, 1f), new Vector3(20f, -10f, -40f), true)
                .Add(PrimitiveType.Cube, new Vector3(0f, -0.15f, -0.48f), new Vector3(1.05f, 1.6f, 0.05f), new Color(0.1f, 0.05f, 0.14f))   // mantle
                .Particles(new Color(1f, 0.3f, 0.85f, 0.75f), 24f, 0.1f, 0.9f, 1.1f, true)
                .Particles(new Color(0.3f, 1f, 0.9f, 0.75f), 24f, 0.1f, 0.9f, 1.1f, false) },
            { "Syne", L(0.88f, 1.1f)   // the marksman: a long pale rifle and a single eye
                .Add(PrimitiveType.Cube, new Vector3(0.38f, 0.12f, 0.75f), new Vector3(0.07f, 0.07f, 1.8f), new Color(0.85f, 0.9f, 1f))
                .Add(PrimitiveType.Sphere, new Vector3(0f, 0.68f, 0.42f), new Vector3(0.2f, 0.2f, 0.2f), new Color(0.7f, 0.85f, 1f), default(Vector3), true)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 1.2f, 0f), new Vector3(0.7f, 0.02f, 0.7f), new Color(0.7f, 0.85f, 1f), default(Vector3), true)
                .Particles(new Color(0.7f, 0.85f, 1f, 0.6f), 10f, 0.08f, 0.5f, 1.2f, true) },
            { "Alio", L(0.95f, 1.05f)   // still to be designed: a shard of its colour
                .Add(PrimitiveType.Cube, new Vector3(0f, 1.15f, 0f), new Vector3(0.25f, 0.4f, 0.06f), new Color(1f, 0.55f, 0.3f), new Vector3(0f, 30f, 15f), true) },
            { "Revl", L(0.95f, 1.05f)
                .Add(PrimitiveType.Cube, new Vector3(0f, 1.15f, 0f), new Vector3(0.25f, 0.4f, 0.06f), new Color(0.55f, 1f, 0.45f), new Vector3(20f, 10f, 40f), true) },
            { "Wex", L(0.95f, 1.05f)
                .Add(PrimitiveType.Cube, new Vector3(0f, 1.15f, 0f), new Vector3(0.25f, 0.4f, 0.06f), new Color(0.75f, 0.45f, 1f), new Vector3(20f, -10f, -40f), true) },
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
            { "Wraith", L(0.72f, 0.95f)
                .Add(PrimitiveType.Cube, new Vector3(0.5f, -0.25f, 0.42f), new Vector3(0.04f, 0.04f, 0.5f), Bone, new Vector3(-10f, 15f, 0f))     // claws, three to a hand
                .Add(PrimitiveType.Cube, new Vector3(0.58f, -0.3f, 0.4f), new Vector3(0.04f, 0.04f, 0.5f), Bone, new Vector3(-10f, 25f, 0f))
                .Add(PrimitiveType.Cube, new Vector3(0.42f, -0.2f, 0.42f), new Vector3(0.04f, 0.04f, 0.5f), Bone, new Vector3(-10f, 5f, 0f))
                .Add(PrimitiveType.Cube, new Vector3(-0.5f, -0.25f, 0.42f), new Vector3(0.04f, 0.04f, 0.5f), Bone, new Vector3(-10f, -15f, 0f))
                .Add(PrimitiveType.Cube, new Vector3(-0.58f, -0.3f, 0.4f), new Vector3(0.04f, 0.04f, 0.5f), Bone, new Vector3(-10f, -25f, 0f))
                .Add(PrimitiveType.Cube, new Vector3(-0.42f, -0.2f, 0.42f), new Vector3(0.04f, 0.04f, 0.5f), Bone, new Vector3(-10f, -5f, 0f))
                .Add(PrimitiveType.Sphere, new Vector3(0.14f, 0.62f, 0.4f), new Vector3(0.1f, 0.06f, 0.06f), new Color(0.7f, 0.5f, 1f), default(Vector3), true)   // eyes
                .Add(PrimitiveType.Sphere, new Vector3(-0.14f, 0.62f, 0.4f), new Vector3(0.1f, 0.06f, 0.06f), new Color(0.7f, 0.5f, 1f), default(Vector3), true)
                .Particles(new Color(0.12f, 0.06f, 0.18f, 0.7f), 26f, 0.45f, 0.5f, 1.1f, true) },                                                      // shadow
            { "Monarch", L(1f, 1.1f)
                .Add(PrimitiveType.Cube, new Vector3(0.75f, 0.35f, -0.4f), new Vector3(1.3f, 0.7f, 0.04f), new Color(1f, 0.97f, 0.88f), new Vector3(0f, -30f, 20f), true)   // wings
                .Add(PrimitiveType.Cube, new Vector3(-0.75f, 0.35f, -0.4f), new Vector3(1.3f, 0.7f, 0.04f), new Color(1f, 0.97f, 0.88f), new Vector3(0f, 30f, -20f), true)
                .Add(PrimitiveType.Cylinder, new Vector3(0f, 1.25f, 0f), new Vector3(0.8f, 0.02f, 0.8f), Gold, default(Vector3), true)                   // halo
                .Add(PrimitiveType.Cube, new Vector3(0.55f, 0.1f, 0.3f), new Vector3(0.05f, 0.05f, 1.4f), Gold, new Vector3(-20f, 0f, 0f))             // javelin
                .Particles(new Color(1f, 0.9f, 0.55f, 0.8f), 18f, 0.12f, 0.6f, 1.4f, true) },                                                          // heavenly light
            { "Zola", L(1.5f, 1.2f)
                .Add(PrimitiveType.Sphere, new Vector3(0.62f, 0.5f, 0f), new Vector3(0.6f, 0.5f, 0.6f), Dark)                                           // great shoulders
                .Add(PrimitiveType.Sphere, new Vector3(-0.62f, 0.5f, 0f), new Vector3(0.6f, 0.5f, 0.6f), Dark)
                .Add(PrimitiveType.Sphere, new Vector3(0f, 0.85f, 0f), new Vector3(0.5f, 0.35f, 0.5f), new Color(0.45f, 0.8f, 1f), default(Vector3), true)   // the crackling crown
                .Add(PrimitiveType.Cylinder, new Vector3(0f, -0.95f, 0f), new Vector3(1.3f, 0.02f, 1.3f), new Color(0.45f, 0.8f, 1f), default(Vector3), true)
                .Particles(new Color(0.55f, 0.9f, 1f, 1f), 40f, 0.07f, 2.6f, 0.25f, false) },                                                         // sparks
            { "Primavaeran", L(0.9f, 1.12f)
                .Add(PrimitiveType.Cube, new Vector3(0.18f, 1.05f, 0f), new Vector3(0.07f, 0.4f, 0.07f), new Color(0.5f, 0.05f, 0.12f), new Vector3(0f, 0f, -20f))   // crown of spikes
                .Add(PrimitiveType.Cube, new Vector3(-0.18f, 1.05f, 0f), new Vector3(0.07f, 0.4f, 0.07f), new Color(0.5f, 0.05f, 0.12f), new Vector3(0f, 0f, 20f))
                .Add(PrimitiveType.Cube, new Vector3(0f, 1.12f, 0.05f), new Vector3(0.07f, 0.48f, 0.07f), new Color(0.5f, 0.05f, 0.12f))
                .Add(PrimitiveType.Cube, new Vector3(0f, -0.1f, -0.42f), new Vector3(0.95f, 1.7f, 0.06f), new Color(0.12f, 0.03f, 0.08f))               // robe
                .Add(PrimitiveType.Sphere, new Vector3(0.72f, 0.65f, 0.15f), new Vector3(0.22f, 0.22f, 0.22f), new Color(0.85f, 0.15f, 0.35f), default(Vector3), true)   // orbs
                .Add(PrimitiveType.Sphere, new Vector3(-0.72f, 0.45f, 0.15f), new Vector3(0.22f, 0.22f, 0.22f), new Color(0.85f, 0.15f, 0.35f), default(Vector3), true)
                .Particles(new Color(0.45f, 0.02f, 0.12f, 0.75f), 22f, 0.35f, 0.9f, 1.2f, false) },                                                    // menace
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
            foreach (var a in look.Auras) AddAura(root, a, view.gameObject.layer);
        }

        /// <summary>A looping particle cloud on the unit (props' root, so it goes with the look): world space, so it trails.</summary>
        private static void AddAura(Transform root, Aura a, int layer)
        {
            var go = new GameObject("LookAura");
            go.layer = layer;
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = a.Life;
            main.startSpeed = a.Speed;
            main.startSize = a.Size;
            main.startColor = a.Color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = a.Rate;
            var shape = ps.shape;
            shape.shapeType = a.Rises ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere;
            shape.radius = 0.55f;
            if (a.Rises) go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // a ring at the feet, drifting up
            if (a.Rises) go.transform.localPosition = new Vector3(0f, -0.9f, 0f);
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(a.Color, 0f), new GradientColorKey(a.Color, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(a.Color.a, 0.2f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ParticleMaterial();
            ps.Play();
        }

        private static Material particleMaterial;

        private static Material ParticleMaterial()
        {
            if (particleMaterial != null) return particleMaterial;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            particleMaterial = new Material(shader) { name = "LookAura" };
            // A soft round dot, so the cloud reads as smoke and sparks rather than squares.
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
                }
            tex.Apply();
            particleMaterial.mainTexture = tex;
            return particleMaterial;
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
