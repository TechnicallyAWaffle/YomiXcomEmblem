using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// CombatVfx's ink-and-light look (the art direction's references: hard black ink shards and splats with coloured edges,
    /// thin radiant light, brushy rings and rune sigils, chromatic smears for speed), built on VfxKit:
    ///   InkHit:    a hit: shards thrown away from the attacker, a splat of ink, light rays for a heavy or critical one.
    ///   Rays:      a burst of thin radiant lines (crits, parries, Transcends, weakpoints).
    ///   BrushRing: a painted ring growing or closing on the ground (every expanding ring CombatVfx draws).
    ///   Sigil:     a rune circle on the ground (windups, Transcends, summons).
    ///   Smear:     two colour-split streaks (dodges and dashes).
    /// Without the kit's assets (Resources/VFX) everything falls back to the plain lines and dots.
    /// </summary>
    public partial class CombatVfx
    {
        private VfxKit kit;
        private ParticleSystem shards, splats, stains, raysPs;

        /// <summary>The ink look is available (the shader and textures loaded).</summary>
        private bool Inked { get { return kit != null && kit.Ready; } }

        private class DecalFx
        {
            public VfxKit.Tex Tex; public VfxKit.Mode Mode; public Vector3 Pos; public float From, To, Age, Life, Spin, Angle; public Color Color; public bool Billboard;
        }
        private readonly List<DecalFx> decals = new List<DecalFx>();

        private static readonly Color InkEdge = new Color(0.95f, 0.25f, 0.2f);   // ink's default edge: crimson
        private static readonly Color Gold = new Color(1f, 0.78f, 0.35f);
        private static readonly Color SmearA = new Color(0.35f, 1f, 0.95f), SmearB = new Color(1f, 0.3f, 0.75f);

        private void SetUpInk()
        {
            kit = new VfxKit(transform);
            if (!kit.Ready) { Debug.LogWarning("CombatVfx: Resources/VFX (shader or textures) missing: plain effects only."); return; }
            shards = kit.System("InkShards", VfxKit.Tex.Shard, VfxKit.Mode.Ink, VfxKit.Shape.Stretched, 1.4f, 1f, 0.7f, 500);
            splats = kit.System("InkSplats", VfxKit.Tex.Splat, VfxKit.Mode.Ink, VfxKit.Shape.Billboard, 0f, 0.7f, 1.15f, 120);
            stains = kit.System("InkStains", VfxKit.Tex.Splat, VfxKit.Mode.Ink, VfxKit.Shape.Flat, 0f, 0.85f, 1.05f, 60);
            raysPs = kit.System("Rays", VfxKit.Tex.Rays, VfxKit.Mode.Glow, VfxKit.Shape.Billboard, 0f, 0.35f, 1.25f, 80);
            // The old dot systems take the kit's materials: sparks are light (additive), puffs painted smoke.
            var sparkMat = new Material(kit.Material(VfxKit.Tex.Puff, VfxKit.Mode.Glow)) { name = "Vfx Sparks" };
            sparkMat.SetFloat("_Boost", 1.3f);
            sparks.GetComponent<ParticleSystemRenderer>().sharedMaterial = sparkMat;
            puffs.GetComponent<ParticleSystemRenderer>().sharedMaterial = kit.Material(VfxKit.Tex.Puff, VfxKit.Mode.Paint);
            if (glow != null)
            {
                var hot = new Material(kit.Material(VfxKit.Tex.Puff, VfxKit.Mode.Glow)) { name = "Vfx Glow" };
                hot.SetFloat("_Boost", GlowBoost);
                glow.GetComponent<ParticleSystemRenderer>().sharedMaterial = hot;
            }
        }

        private void ResetInk()
        {
            decals.Clear();
            foreach (var ps in new[] { shards, splats, stains, raysPs }) if (ps != null) ps.Clear();
        }

        // ------------------------------------------------------------------ compositions

        /// <summary>
        /// A hit landing at 'at': ink shards thrown along 'away' (from the attacker through the target), a splat, and for a
        /// heavy one more of both and a burst of rays. 'edge' colours the ink's edges and the light.
        /// </summary>
        private void InkHit(Vector3 at, Vector3 away, Color edge, bool heavy, bool critical = false)
        {
            if (!Inked) return;
            Vector3 dir = away.sqrMagnitude > 1e-4f ? away.normalized + Vector3.up * 0.35f : Vector3.up;
            // In front of the body as the camera sees it, so the body doesn't hide it.
            var cam = Cam;
            Vector3 front = cam != null ? (cam.transform.position - at).normalized * 0.55f : Vector3.zero;
            VfxKit.Spray(shards, at + front * 0.5f, dir, heavy ? 55f : 40f, heavy ? 10 : 6, edge, heavy ? 10f : 7f, heavy ? 0.42f : 0.3f, heavy ? 0.45f : 0.32f);
            VfxKit.Burst(splats, at + front, 1, edge, 0f, heavy ? 2.1f : 1.25f, heavy ? 0.4f : 0.28f);
            if (heavy || critical) Rays(at + front, critical ? Gold : Color.Lerp(edge, Color.white, 0.35f), critical ? 3.4f : 2.4f, 0.3f);
        }

        /// <summary>A burst of thin radiant lines at 'at', 'size' across.</summary>
        private void Rays(Vector3 at, Color color, float size, float life)
        {
            if (!Inked) return;
            VfxKit.Burst(raysPs, at, 1, color, 0f, size, life);
        }

        private static readonly Color HowlDark = new Color(0.07f, 0.04f, 0.12f), HowlEdge = new Color(0.32f, 0.22f, 0.5f);

        /// <summary>
        /// Howling Lament ("howl" AbilityFx): a translucent wave of darkness rolling out from the Howler to the howl's reach,
        /// in three layers (a broad dark swell and two thinner crests behind it), with a dark pall welling up around the Howler
        /// and its edge faintly lit. Painted (alpha-blended), so it darkens what it passes over rather than lighting it.
        /// </summary>
        private void PlayHowl(FxInfo f)
        {
            Vector3 c = f.Center;
            float r = Mathf.Max(2f, f.Radius);
            rings.Add(new RingFx { Center = c, From = 0.4f, To = r, Width = 0.45f, Life = 1.1f, Color = WithAlpha(HowlDark, 0.55f), Segments = 72 });
            rings.Add(new RingFx { Center = c, From = 0.2f, To = r * 0.75f, Width = 0.12f, Life = 1.0f, Color = WithAlpha(HowlEdge, 0.5f), Segments = 72 });
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f + Random.Range(-0.15f, 0.15f);
                Vector3 rim = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Burst(puffs, c + rim * Random.Range(0.6f, 1.4f) + Vector3.up * 0.3f, 1, WithAlpha(HowlDark, 0.75f), 2.2f, 0.9f, 1.0f, rim * 1.6f + Vector3.up * 0.4f);
            }
            if (!Inked) return;
            BrushRing(c, 0.5f, r, 1.2f, WithAlpha(HowlDark, 0.6f));                   // the swell
            BrushRing(c, 0.3f, r * 0.85f, 1.0f, WithAlpha(HowlDark, 0.45f));          // a crest behind it
            BrushRing(c, 0.2f, r * 0.6f, 0.8f, WithAlpha(HowlEdge, 0.35f), VfxKit.Mode.Glow);   // the faint lit edge
            VfxKit.Burst(stains, c + Vector3.up * 0.05f, 1, WithAlpha(HowlDark, 0.7f), 0f, 3.5f, 1.4f);   // the pall under it
        }

        /// <summary>A painted ring on the ground at 'center', its radius going 'from' to 'to' over 'life'.</summary>
        private void BrushRing(Vector3 center, float from, float to, float life, Color color, VfxKit.Mode mode = VfxKit.Mode.Paint)
        {
            decals.Add(new DecalFx { Tex = VfxKit.Tex.Ring, Mode = mode, Pos = center, From = from, To = to, Life = life, Color = color, Angle = Random.Range(0f, 360f), Spin = Random.Range(-60f, 60f) });
        }

        /// <summary>A rune circle on the ground at 'center', 'radius' growing to 'to' over 'life', turning 'spin' degrees a second.</summary>
        private void Sigil(Vector3 center, float from, float to, float life, Color color, float spin = 40f)
        {
            decals.Add(new DecalFx { Tex = VfxKit.Tex.Sigil, Mode = VfxKit.Mode.Glow, Pos = center, From = from, To = to, Life = life, Color = color, Spin = spin, Angle = Random.Range(0f, 360f) });
        }

        /// <summary>A halo (a thin bright ring seen from the side) over 'at'.</summary>
        private void HaloFlash(Vector3 at, float from, float to, float life, Color color)
        {
            decals.Add(new DecalFx { Tex = VfxKit.Tex.Halo, Mode = VfxKit.Mode.Glow, Pos = at, From = from, To = to, Life = life, Color = color, Billboard = true });
        }

        /// <summary>Speed: two streaks a hand apart in cyan and magenta (a colour split), from 'a' to 'b' at body heights.</summary>
        private void Smear(Vector3 a, Vector3 b, float life, float width = 0.22f)
        {
            Vector3 side = Vector3.Cross(Vector3.up, Flat(b - a)).normalized * 0.08f;
            foreach (float h in new[] { 0.45f, 1.05f, 1.55f })
            {
                AddStroke(new[] { a + Vector3.up * h + side, b + Vector3.up * h + side }, WithAlpha(SmearA, 0.75f), width, life, width * 0.15f, VfxKit.Mode.Glow);
                AddStroke(new[] { a + Vector3.up * h - side, b + Vector3.up * h - side }, WithAlpha(SmearB, 0.75f), width, life, width * 0.15f, VfxKit.Mode.Glow);
            }
        }

        /// <summary>An ink stroke (dark body, coloured grain) under a thin light stroke along the same path.</summary>
        private void InkStroke(Vector3[] pts, Color color, float width, float life, bool sweep = false)
        {
            AddStroke(pts, WithAlpha(Color.Lerp(color, Color.black, 0.75f), 0.9f), width * 2.2f, life, width * 0.5f, VfxKit.Mode.Paint, sweep);
            AddStroke(pts, color, width, life * 0.8f, width * 0.2f, VfxKit.Mode.Glow, sweep);
        }

        // ------------------------------------------------------------------ per frame

        private void DrawInk(float dt)
        {
            if (!Inked) return;
            var cam = Cam;
            for (int i = decals.Count - 1; i >= 0; i--)
            {
                var d = decals[i];
                d.Age += dt;
                if (d.Age >= d.Life) { decals.RemoveAt(i); continue; }
                float t = d.Age / d.Life;
                float ease = 1f - (1f - t) * (1f - t);
                float r = Mathf.Lerp(d.From, d.To, ease);
                Color c = WithAlpha(d.Color, d.Color.a * (1f - t * t));
                float size = r * 2f / (d.Tex == VfxKit.Tex.Ring ? 0.86f : d.Tex == VfxKit.Tex.Halo ? 0.8f : 0.95f);   // the texture's ring sits inside its square
                if (d.Billboard) kit.Billboard(d.Tex, d.Mode, d.Pos, size, d.Angle + d.Spin * d.Age, c, cam);
                else kit.Decal(d.Tex, d.Mode, d.Pos, size, d.Angle + d.Spin * d.Age, c);
            }
        }

        // ------------------------------------------------------------------ status icons

        /// <summary>Which glyph a status shows over the head (VfxKit.Tex.Glyphs: 0 chevrons up, 1 chevrons down, 2 an eye,
        /// 3 a crossed diamond), or -1 for none.</summary>
        private static readonly Dictionary<string, int> StatusGlyphs = new Dictionary<string, int>
        {
            // Faster: chevrons up.
            { StatusIds.Hastened, 0 }, { StatusIds.Quickened, 0 }, { StatusIds.BeyondThresholds, 0 }, { StatusIds.Festival, 0 },
            { StatusIds.Flurry, 0 }, { StatusIds.Monostep, 0 }, { StatusIds.MentalCurrents, 0 },
            // Slower: chevrons down.
            { StatusIds.Slowed, 1 }, { StatusIds.TimeSlowed, 1 }, { StatusIds.Quelled, 1 }, { StatusIds.Fated, 1 }, { StatusIds.Decayed, 1 },
            { StatusIds.Reduction, 1 }, { StatusIds.Crippled, 1 }, { StatusIds.Withering, 1 }, { StatusIds.Dereliction, 1 },
            // Watching or unseen: an eye.
            { StatusIds.Hidden, 2 }, { StatusIds.TakeAim, 2 },
            // Held in place: a crossed diamond.
            { StatusIds.Rooted, 3 }, { StatusIds.Immobilized, 3 }, { StatusIds.Grasped, 3 }, { StatusIds.Chained, 3 }, { StatusIds.Grappled, 3 },
        };

        /// <summary>
        /// A unit's statuses over its head: a glyph for each kind it has (faster, slower, watching, held; one each, side by
        /// side, bobbing gently), and for an Important status (Trance, Wrath, FATED...) a halo of its colour floating over
        /// the head (NEMESIS has its own sigil instead).
        /// </summary>
        private void DrawStatusIcons(UnitView view, HashSet<string> ids, float now)
        {
            var cam = Cam;
            Vector3 head = Head(view);
            var shown = new Color[4]; var has = new bool[4];
            Color haloColor = Color.clear;
            foreach (var id in ids)
            {
                StatusDisplay.Look look;
                if (!StatusDisplay.TryGet(id, out look)) continue;
                int g;
                if (StatusGlyphs.TryGetValue(id, out g) && !has[g]) { has[g] = true; shown[g] = look.Color; }
                if (look.Important && id != StatusIds.Nemesis && haloColor.a <= 0f) haloColor = look.Color;
            }
            int count = 0;
            for (int g = 0; g < 4; g++) if (has[g]) count++;
            if (count > 0)
            {
                Vector3 right = cam != null ? cam.transform.right : Vector3.right;
                float x = -(count - 1) * 0.22f;
                for (int g = 0; g < 4; g++)
                {
                    if (!has[g]) continue;
                    float bob = 0.05f * Mathf.Sin(now * 3f + g);
                    kit.Glyph(g, head + Vector3.up * (0.95f + bob) + right * x, 0.4f, WithAlpha(shown[g], 0.95f), cam);
                    x += 0.44f;
                }
            }
            if (haloColor.a > 0f) kit.Decal(VfxKit.Tex.Halo, VfxKit.Mode.Glow, head + Vector3.up * 0.2f, 1.05f, now * 30f, WithAlpha(Color.Lerp(haloColor, Color.white, 0.25f), 0.95f));
        }

        // ------------------------------------------------------------------ preview (for tuning: Tools, the CLI)

        /// <summary>Play an event's effects now, as if playback had just dispatched it (a preview: nothing in the battle changes).</summary>
        public void PreviewEvent(TimelineEvent evt) { OnEvent(evt); }

        /// <summary>Know an ability no unit in this battle has (so a previewed windup or resolve of it can be drawn).</summary>
        public void PreviewLearn(ActionDefinition def) { AddDef(def); }

        /// <summary>Play an ability's landing effect from 'actor' at 'target' (or 'targetPos'), as if it had resolved now.</summary>
        public void PreviewResolve(ActionDefinition def, UnitView actor, UnitView target, Vector3 targetPos)
        {
            if (def == null || actor == null) return;
            AddDef(def);
            PlayResolve(def, actor, target, targetPos);
        }
    }
}
