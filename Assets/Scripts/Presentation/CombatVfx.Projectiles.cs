using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Projectiles (BattleSimulation.Projectiles.cs) as CombatVfx draws them, from their ProjectilePath: the simulation fills
    /// each one's flight in as it goes, so a projectile is drawn where it is at the playback clock, scrubbing included.
    ///   In flight: a bright head and a streak trailing ProjectileTrail seconds behind it (the ability's Visuals colour).
    ///   Ending:    a burst where it hit someone, a puff of grit where it struck the ground.
    ///   Tactical view: a small arrow at each one, pointing where it flies.
    /// </summary>
    public partial class CombatVfx
    {
        [Header("Projectiles (CombatVfx.Projectiles)")]
        [Tooltip("Seconds of flight the streak behind a projectile shows.")]
        public float ProjectileTrail = 0.09f;

        private readonly Dictionary<int, ProjectilePath> flying = new Dictionary<int, ProjectilePath>();
        private static readonly Color BarbColor = new Color(0.85f, 0.5f, 1f);

        private void OnProjectileEvent(TimelineEvent evt)
        {
            var path = evt.Payload as ProjectilePath;
            if (path == null) return;
            if (evt.Type == TacticsGame.Core.EventType.ProjectileLaunched) { flying[path.Id] = path; return; }
            if (evt.Type != TacticsGame.Core.EventType.ProjectileEnded || TacticalView.On) return;
            Vector3 end = path.Points.Count > 0 ? path.Points[path.Points.Count - 1] : Vector3.zero;
            Color c = ProjectileColor(path);
            if (path.HitId != null) Burst(sparks, end, 8, Color.Lerp(c, Color.white, 0.3f), 3f, 0.08f, 0.3f);
            else if (path.Homing != ProjectileHoming.None && path.EndsAt < path.Times[0] + path.Lifetime - 0.01f)
            {
                // A homing one that fizzled on cover or terrain: a spit of its colour and a little smoke.
                Burst(sparks, end, 6, c, 1.6f, 0.06f, 0.25f);
                Burst(puffs, end, 3, Palette.Dust, 0.5f, 0.2f, 0.45f, Vector3.up * 0.3f);
            }
            else Burst(puffs, end + Vector3.up * 0.05f, 4, Palette.Dust, 0.8f, 0.2f, 0.5f, Vector3.up * 0.3f);
        }

        private Color ProjectileColor(ProjectilePath path)
        {
            ActionDefinition def;
            return path.ActionId != null && defs.TryGetValue(path.ActionId, out def) && def.Visuals != null && def.Visuals.IsSet ? def.Visuals.Color : BarbColor;
        }

        private readonly List<int> landed = new List<int>();

        private void DrawProjectiles()
        {
            if (flying.Count == 0) return;
            float now = Clock;
            landed.Clear();
            foreach (var pair in flying)
            {
                var path = pair.Value;
                if (path.EndsAt >= 0f && now > path.EndsAt + 0.05f) { landed.Add(pair.Key); continue; }
                Vector3 head, dir;
                if (!path.PositionAt(now, out head, out dir)) continue;
                Vector3 tail, tailDir;
                if (!path.PositionAt(Mathf.Max(path.Times[0], now - ProjectileTrail), out tail, out tailDir)) tail = head - dir * 0.2f;
                if ((head - tail).sqrMagnitude < 0.0025f) tail = head - dir * 0.05f;
                Color c = ProjectileColor(path);
                ActionDefinition pdef;
                if (path.ActionId != null && defs.TryGetValue(path.ActionId, out pdef) && pdef.Visuals != null && pdef.Visuals.Orb)
                {
                    // A glowing ball (Divulge, Fray The Mind): a bright core its own size, a soft halo, a fading trail.
                    float r = Mathf.Max(0.2f, path.Radius);
                    Color core = Color.Lerp(c, Color.white, 0.55f);
                    markers.Line(new[] { tail, head }, WithAlpha(c, 0f), WithAlpha(c, 0.55f), 0.02f, r * 1.3f);
                    markers.Line(new[] { head - dir * r * 0.55f, head + dir * r * 0.55f }, WithAlpha(c, 0.45f), WithAlpha(c, 0.45f), r * 2.2f, r * 2.2f);
                    markers.Line(new[] { head - dir * r * 0.4f, head + dir * r * 0.4f }, WithAlpha(core, 0.95f), WithAlpha(core, 0.95f), r * 1.3f, r * 1.3f);
                    if (glow != null && Chance(60f)) Burst(glow, head, 1, c, 0.1f, r * 2.4f, 0.12f);   // its halo, steady at any frame rate, none while time stands still
                    continue;
                }
                float size = Mathf.Max(0.3f, path.Radius / 0.3f);   // drawn to its size (ProjectileRadius; 0.3 is a barb)
                if (path.Homing != ProjectileHoming.None)
                {
                    // A homing barb: bigger (HomingProjectileScale), with a soft glow round its head, so it can be followed.
                    size *= HomingProjectileScale;
                    markers.Line(new[] { head - dir * 0.08f * size, head + dir * 0.05f * size }, WithAlpha(c, 0.35f), WithAlpha(c, 0.35f), 0.16f * size, 0.16f * size);
                }
                markers.Line(new[] { tail, head }, WithAlpha(c, 0f), WithAlpha(Color.Lerp(c, Color.white, 0.5f), 0.95f), 0.015f * size, 0.09f * size);
                markers.Line(new[] { head - dir * 0.12f * size, head + dir * 0.06f * size }, WithAlpha(Color.white, 0.9f), WithAlpha(c, 0.9f), 0.05f * size, 0.01f * size);   // the barb itself
            }
            foreach (var id in landed) flying.Remove(id);
        }

        private GUIStyle etaStyle;

        /// <summary>
        /// The tactical view: over each projectile in flight, how long until it reaches what it is going for if that stays
        /// where it is: its target (homing on one), else the nearest enemy of its owner (homing on whoever is nearest), at the
        /// speed it has now, gaining speed as it does, flying straight in (turning is not counted). A dash if it would fall
        /// apart first (ProjectileLifetime); nothing for one that flies straight at nobody.
        /// </summary>
        private void DrawProjectileEtas(Camera cam)
        {
            if (flying.Count == 0) return;
            if (etaStyle == null) etaStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 13, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            float now = Clock;
            foreach (var path in flying.Values)
            {
                Vector3 p, dir;
                if (!path.PositionAt(now, out p, out dir) || path.Times.Count == 0) continue;
                var target = EtaTarget(path, p);
                if (target == null) continue;
                Vector3 to = target.transform.position + Vector3.up * (HeightOf(target) * 0.55f);
                float gap = Mathf.Max(0f, Vector3.Distance(p, to) - path.Radius - 0.4f);
                float t = path.SecondsToCover(gap, path.SpeedAt(now));
                float lifeLeft = path.Times[0] + path.Lifetime - now;
                string label = t > lifeLeft ? "-" : t.ToString("0.0") + "s";
                Vector2 sp;
                if (!UiScale.WorldToGui(cam, p + Vector3.up * 0.3f, out sp)) continue;
                OutlinedLabel(new Rect(sp.x - 30f, sp.y - 20f, 60f, 18f), label, etaStyle, TeamColor(TeamOf(path.OwnerId)), 1.2f);
            }
        }

        /// <summary>What a projectile is going for: its target, or for one homing on the nearest enemy, the nearest of them.</summary>
        private UnitView EtaTarget(ProjectilePath path, Vector3 p)
        {
            if (path.Homing == ProjectileHoming.Target)
            {
                var t = Director.FindView(path.TargetId);
                return t != null && !t.IsDead ? t : null;
            }
            if (path.Homing != ProjectileHoming.ClosestEnemy) return null;
            var owner = TeamOf(path.OwnerId);
            UnitView best = null;
            float bestD = float.MaxValue;
            if (Director.Timeline == null || Director.Timeline.InitialState == null) return null;
            foreach (var id in UnitIds())
            {
                var v = Director.FindView(id);
                if (v == null || v.IsDead || TeamOf(id) == owner) continue;
                float d = (v.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = v; }
            }
            return best;
        }

        /// <summary>Every unit in the battle so far: those it started with and those that joined.</summary>
        private IEnumerable<string> UnitIds()
        {
            foreach (var u in Director.Timeline.InitialState.Units) yield return u.Id;
            foreach (var id in summoned.Keys) yield return id;
        }

        [Tooltip("Tactical view: metres between the small arrows on a projectile's predicted course.")]
        public float CourseArrowSpacing = 0.55f;

        private readonly List<Vector3> course = new List<Vector3>();

        private void DrawTacticalProjectiles()
        {
            float now = Clock;
            var state = Director.Timeline != null ? Director.Timeline.InitialState : null;
            var terrain = state != null ? state.Terrain : null;
            System.Func<Vector3, bool> solid = q => BattleSimulation.IsInsideGround(terrain, q);
            foreach (var path in flying.Values)
            {
                Vector3 p, dir;
                if (!path.PositionAt(now, out p, out dir)) continue;
                Color c = TeamColor(TeamOf(path.OwnerId));
                // Its course from here: a homing one toward where its target is now (as if it stays there), any other straight on.
                course.Clear();
                Vector3? aim = null;
                if (path.Homing != ProjectileHoming.None)
                {
                    var target = EtaTarget(path, p);
                    if (target != null) aim = target.transform.position + Vector3.up * (HeightOf(target) * 0.55f);
                }
                path.Predict(now, aim, solid, course);
                CourseArrows(course, c);
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
                side.Normalize();
                markers.Line(new[] { p - dir * 0.25f + side * 0.15f, p, p - dir * 0.25f - side * 0.15f }, c, c, 0.05f, 0.05f);
            }
        }

        /// <summary>A predicted course: a faint thread along it and small arrows running along it the way it flies.</summary>
        private void CourseArrows(List<Vector3> pts, Color c)
        {
            if (pts.Count < 2) return;
            markers.Line(pts.ToArray(), WithAlpha(c, 0.25f), WithAlpha(c, 0.05f), 0.02f, 0.02f);
            float spacing = Mathf.Max(0.2f, CourseArrowSpacing), size = 0.13f;
            float next = Mathf.Repeat(Time.unscaledTime * ArrowSpeed * 1.5f, spacing) + 0.3f, walked = 0f;   // the first one clear of the head
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 a = pts[i - 1], b = pts[i];
                float len = Vector3.Distance(a, b);
                if (len < 1e-4f) continue;
                Vector3 d = (b - a) / len;
                Vector3 side = Vector3.Cross(Vector3.up, d);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
                side.Normalize();
                while (next <= walked + len)
                {
                    Vector3 tip = a + d * (next - walked);
                    float fade = Mathf.Clamp01((total - next) / 1.2f);   // fading toward where it ends
                    Color cc = WithAlpha(c, 0.75f * fade);
                    markers.Line(new[] { tip - d * size + side * size * 0.7f, tip, tip - d * size - side * size * 0.7f }, cc, cc, 0.035f, 0.035f);
                    next += spacing;
                }
                walked += len;
            }
        }
    }
}
