using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// CombatVfx's accents, laid over its own effects for the big moments, from the same timeline events:
    ///   Glow:   a third particle system whose material tint is HDR (GlowBoost), so its sparks pass the Volume's bloom
    ///           threshold. Only heavy hits, critical strikes, Unravels, deaths and Transcends use it: one thing glowing
    ///           at a time reads as an accent.
    ///   Flash:  a pooled point light at the same moments, lighting the ground and the bodies around it for FlashSeconds.
    ///   Dust:   a puff of earth where a unit dashes off from (Palette.Dust).
    ///   Motes:  dust drifting in the sunlight over the field (the one looping effect: the air has a light story).
    /// Pooled throughout: three particle systems and FlashLights lights, made once.
    /// </summary>
    public partial class CombatVfx
    {
        [Header("Accents (CombatVfx.Glow)")]
        [Tooltip("How far past 1 the glow particles' colour goes: above the bloom threshold (1.1) so they bloom.")]
        public float GlowBoost = 2.6f;
        [Tooltip("Impact flash lights alive at once (the oldest is reused).")]
        public int FlashLights = 6;
        public float FlashSeconds = 0.2f;
        [Tooltip("Drifting motes over the field: the area they fill (centred on the origin) and how many are alive.")]
        public Vector3 MoteArea = new Vector3(40f, 5f, 50f);
        public int MoteCount = 24;

        private ParticleSystem glow, motes;
        private class Flash { public Light Light; public float Age, Life, Peak; }
        private readonly List<Flash> flashPool = new List<Flash>();
        private int nextFlash;
        private readonly Dictionary<string, WeakpointsInfo> weakpoints = new Dictionary<string, WeakpointsInfo>();   // unit id -> its open weakpoints

        private void SetUpAccents(Material dot)
        {
            var glowMat = new Material(dot) { name = "VfxGlow" };
            glowMat.color = new Color(GlowBoost, GlowBoost, GlowBoost, 1f);   // Sprites/Default multiplies the particle's colour by this
            glow = MakeSystem("Glow", glowMat, 0.6f, true);
            motes = MakeMotes(dot);
            for (int i = 0; i < Mathf.Max(1, FlashLights); i++)
            {
                var go = new GameObject("ImpactFlash");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point; l.shadows = LightShadows.None; l.range = 5f; l.intensity = 0f; l.enabled = false;
                flashPool.Add(new Flash { Light = l });
            }
        }

        private void ResetAccents()
        {
            if (glow != null) glow.Clear();
            weakpoints.Clear();
            foreach (var f in flashPool) { f.Light.enabled = false; f.Age = f.Life = 0f; }
        }

        private void TickAccents(float dt)
        {
            foreach (var f in flashPool)
            {
                if (!f.Light.enabled) continue;
                f.Age += dt;
                float t = f.Life > 0f ? f.Age / f.Life : 1f;
                if (t >= 1f) { f.Light.enabled = false; continue; }
                f.Light.intensity = f.Peak * (1f - t) * (1f - t);
            }
        }

        /// <summary>The accents an event earns, alongside CombatVfx's own effects for it.</summary>
        private void Embellish(TimelineEvent evt, UnitView actor, UnitView target)
        {
            switch (evt.Type)
            {
                case TacticsGame.Core.EventType.Damage:
                {
                    if (target == null) break;
                    var d = (DamageInfo)evt.Payload;
                    if (d.Amount < 0.5f && d.BlockedAmount < 0.5f) break;
                    if (d.Critical) { Glow(Chest(target), Palette.Accent, 26, 7f); FlashAt(Chest(target), Palette.Accent, 6f, 5f); }
                    else if (d.Impact == ImpactType.Heavy) { Glow(Chest(target), HitColor(d.ActionId, HeavyColor), 16, 6f); FlashAt(Chest(target), HeavyColor, 4f, 4f); }
                    break;
                }
                case TacticsGame.Core.EventType.Unravel:
                    if (target == null) break;
                    Glow(Chest(target), new Color(0.8f, 0.4f, 1f), 30, 7f);
                    FlashAt(Chest(target), new Color(0.75f, 0.4f, 1f), 7f, 6f);
                    break;
                case TacticsGame.Core.EventType.Weakpoints:
                {
                    var w = (WeakpointsInfo)evt.Payload;
                    if (w.Directions == null || w.Directions.Length == 0) weakpoints.Remove(evt.ActorId ?? "");
                    else weakpoints[evt.ActorId] = w;
                    if (actor == null || w.Broken <= 0) break;
                    // Broken: a gold crack of sparks (inked: gold rays and shards); the last one, a ring and the word.
                    Glow(Chest(actor), Palette.Accent, 14 * w.Broken, 5f);
                    if (Inked)
                    {
                        Rays(Chest(actor), Gold, w.Shattered ? 2.6f : 1.5f, 0.3f);
                        VfxKit.Burst(shards, Chest(actor), 3 + 2 * w.Broken, Gold, 6f, 0.16f, 0.4f);
                    }
                    AddText(Head(actor) + Vector3.up * 0.3f, w.Shattered ? "EXPOSED!" : "WEAKPOINT", Palette.Accent, w.Shattered ? 24f : 17f, w.Shattered ? 1.8f : 1f);
                    if (w.Shattered) rings.Add(new RingFx { Center = actor.transform.position, From = 0.3f, To = 2.2f, Width = 0.14f, Life = 0.4f, Color = Palette.Accent });
                    break;
                }
                case TacticsGame.Core.EventType.Death:
                    if (target == null) break;
                    Glow(Chest(target), new Color(1f, 0.35f, 0.2f), 24, 5f);
                    FlashAt(Chest(target), new Color(1f, 0.4f, 0.25f), 5f, 5f);
                    break;
                case TacticsGame.Core.EventType.ActionStarted:
                    if (actor != null && ((ActionStartedInfo)evt.Payload).Transcended) Glow(Chest(actor), TranscendColor, 10, 3f, Vector3.up);
                    break;
                case TacticsGame.Core.EventType.Dash:
                {
                    if (actor == null) break;
                    var dash = (DashInfo)evt.Payload;
                    if (dash.Settle || dash.Step || dash.Teleport || dash.Fall || dash.Forced) break;   // shoves already kick up dust where they land
                    Burst(puffs, dash.From + Vector3.up * 0.1f, 7, Palette.Dust, 1.1f, 0.35f, 0.5f, Vector3.up * 0.3f);
                    break;
                }
            }
        }

        /// <summary>Each open weakpoint: a gold triangle on the ground at that side of the unit, pointing out; it flickers in
        /// its last 0.4s.</summary>
        private void DrawWeakpoints()
        {
            if (weakpoints.Count == 0) return;
            foreach (var pair in weakpoints)
            {
                var view = Director.FindView(pair.Key);
                if (view == null) continue;
                Vector3 feet = view.transform.position + Vector3.up * 0.06f;
                var w = pair.Value;
                for (int i = 0; i < w.Directions.Length; i++)
                {
                    Vector3 d = w.Directions[i], side = new Vector3(d.z, 0f, -d.x);
                    float left = w.ExpiresAt != null && i < w.ExpiresAt.Length ? w.ExpiresAt[i] - Clock : 9f;
                    float alpha = left < 0.4f ? 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 22f)) : 1f;
                    Color c = WithAlpha(Palette.Accent, alpha);
                    Vector3 a = feet + d * 0.72f + side * 0.26f, b = feet + d * 0.72f - side * 0.26f, tip = feet + d * 1.2f;
                    markers.Polyline(new[] { a, tip, b, a }, c, 0.07f);
                    markers.Segment(feet + d * 0.86f, tip, c, 0.05f);
                }
            }
        }

        // A fallen enemy's body crumbling (UnitView.SetCrumble): dust thrown up in waves over UnitView.CrumbleSeconds.
        private class CorpseFx { public string UnitId; public float At; public int Waves; }
        private readonly List<CorpseFx> corpses = new List<CorpseFx>();
        private const int CorpseWaves = 4;

        private void DrawCorpses()
        {
            float now = Clock;
            for (int i = corpses.Count - 1; i >= 0; i--)
            {
                var c = corpses[i];
                if (now < c.At) continue;
                var view = Director.FindView(c.UnitId);
                if (view == null) { corpses.RemoveAt(i); continue; }
                while (c.Waves < CorpseWaves && now >= c.At + c.Waves * (UnitView.CrumbleSeconds / CorpseWaves))
                {
                    float h = HeightOf(view) * (0.6f - 0.15f * c.Waves);
                    Vector3 at = view.transform.position + Vector3.up * Mathf.Max(0.15f, h);
                    Burst(puffs, at, 14, new Color(Palette.Dust.r, Palette.Dust.g, Palette.Dust.b, 0.75f), 1.4f, 0.45f, 1.3f, Vector3.up * 0.6f);
                    Burst(sparks, at, 6, new Color(0.55f, 0.5f, 0.42f), 1.8f, 0.06f, 0.8f, Vector3.down * 0.5f);   // grit falling away
                    c.Waves++;
                }
                if (c.Waves >= CorpseWaves) corpses.RemoveAt(i);
            }
        }

        /// <summary>The colour an ability's hit sparks use (its Visuals), or 'fallback'.</summary>
        private Color HitColor(string actionId, Color fallback)
        {
            ActionDefinition def;
            return actionId != null && defs.TryGetValue(actionId, out def) && def.Visuals != null && def.Visuals.IsSet ? def.Visuals.Color : fallback;
        }

        private void Glow(Vector3 at, Color color, int count, float speed, Vector3 bias = default(Vector3))
        {
            if (glow != null) Burst(glow, at, count, color, speed, 0.07f, 0.35f, bias);
        }

        /// <summary>A short burst of light at 'at' (the oldest flash is taken when all are busy).</summary>
        private void FlashAt(Vector3 at, Color color, float intensity, float range)
        {
            if (flashPool.Count == 0) return;
            var f = flashPool[nextFlash];
            nextFlash = (nextFlash + 1) % flashPool.Count;
            f.Light.transform.position = at + Vector3.up * 0.3f;
            f.Light.color = color; f.Light.range = range;
            f.Peak = intensity; f.Light.intensity = intensity;
            f.Age = 0f; f.Life = FlashSeconds;
            f.Light.enabled = true;
        }

        /// <summary>Dust in the sunlight: a slow, always-on drift over the field, too faint to compete with combat.</summary>
        private ParticleSystem MakeMotes(Material dot)
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, MoteArea.y * 0.5f + 0.3f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            float life = 9f;
            main.startLifetime = life;
            main.startSpeed = 0.15f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = Palette.Mote;
            main.maxParticles = Mathf.Max(1, MoteCount * 2);
            var emission = ps.emission; emission.rateOverTime = MoteCount / life;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = MoteArea;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.15f; noise.frequency = 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;   // fade in and out: none pops
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = dot;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }
    }
}
