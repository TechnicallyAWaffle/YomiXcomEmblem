using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// What CombatVfx draws in the tactical view (TacticalView: Alt in a live battle), in place of its effects:
    ///   Targeting: every unit's targeting line, with arrows running along it from the unit toward what it is aiming at:
    ///              its windup (solid, brighter as it charges), a channel it holds (a beam), and an order it is walking or
    ///              waiting to carry out (fainter). In the unit's team colour.
    ///   Weakpoints: each open one marked large and bright: the ground triangle, a diamond at chest height on that side,
    ///              and a ring about the unit, all pulsing.
    /// Arrows move on real time, so they keep running while the world is held for orders.
    /// </summary>
    public partial class CombatVfx
    {
        [Header("Tactical view (CombatVfx.Tactical)")]
        [Tooltip("Metres between the arrows on a targeting line, and how fast they run (m/s).")]
        public float ArrowSpacing = 1.1f;
        public float ArrowSpeed = 2.6f;

        private static Color TeamColor(Team? team)
        {
            Team t = team ?? Team.Enemy;
            return t == Team.Player ? Color.Lerp(Palette.Squad, Color.white, 0.25f) : t == Team.Enemy ? Color.Lerp(Palette.Enemy, Color.white, 0.15f) : new Color(0.8f, 0.45f, 1f);
        }

        /// <summary>The tactical view's frame: targeting arrows and weakpoints only (no effects).</summary>
        private void DrawTactical()
        {
            var aimed = new HashSet<string>();
            float now = Clock;
            foreach (var w in windups)
            {
                var actor = Director.FindView(w.ActorId);
                if (actor == null || actor.IsDead) continue;
                Vector3 to;
                if (!AimPoint(actor, w.TargetId, w.Def, w.TargetPos, out to)) continue;
                float p = Mathf.Clamp01((now - w.Start) / Mathf.Max(0.01f, w.Duration));
                TacticalArrow(Chest(actor), to, TeamColor(TeamOf(w.ActorId)), 0.65f + 0.35f * p, 1f);
                aimed.Add(w.ActorId);
            }
            foreach (var pair in beams)
            {
                if (aimed.Contains(pair.Key)) continue;
                var actor = Director.FindView(pair.Key);
                var target = Director.FindView(pair.Value.Key);
                if (actor == null || target == null || actor.IsDead) continue;
                TacticalArrow(Chest(actor), Chest(target), TeamColor(TeamOf(pair.Key)), 1f, 1f);
                aimed.Add(pair.Key);
            }
            foreach (var pair in orders)
            {
                if (aimed.Contains(pair.Key)) continue;
                var actor = Director.FindView(pair.Key);
                var target = Director.FindView(pair.Value.Key);
                if (actor == null || target == null || actor == target || actor.IsDead || target.IsDead) continue;
                TacticalArrow(Chest(actor), Chest(target), TeamColor(TeamOf(pair.Key)), 0.45f, 0.6f);
            }
            DrawTacticalProjectiles();
            DrawTacticalWeakpoints();
        }

        /// <summary>Where a windup is aimed: its target unit's chest, or the ground point of one aimed at a point (an area).
        /// False for one on the user itself.</summary>
        private bool AimPoint(UnitView actor, string targetId, ActionDefinition def, Vector3 targetPos, out Vector3 to)
        {
            to = Vector3.zero;
            var target = Director.FindView(targetId);
            if (target != null && target != actor) { to = Chest(target); return true; }
            if (def == null || def.Targeting == TargetType.Self) return false;
            if (Flat(targetPos - actor.transform.position).sqrMagnitude < 0.25f) return false;
            to = targetPos + Vector3.up * 0.1f;
            return true;
        }

        /// <summary>A line from 'from' to 'to' with chevrons running along it toward 'to', and a head at the end.</summary>
        private void TacticalArrow(Vector3 from, Vector3 to, Color c, float alpha, float scale)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.4f) return;
            Vector3 dir = d / len;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(Vector3.forward, dir);
            side.Normalize();
            Color line = WithAlpha(c, 0.35f * alpha);
            markers.Line(new[] { from, to }, line, line, 0.035f * scale, 0.035f * scale);
            float size = 0.3f * scale, spacing = Mathf.Max(0.3f, ArrowSpacing);
            float offset = Mathf.Repeat(Time.unscaledTime * ArrowSpeed, spacing);
            for (float s = offset; s < len - 0.45f; s += spacing)
            {
                Vector3 tip = from + dir * s;
                float fade = Mathf.Clamp01(s / 0.6f) * Mathf.Clamp01((len - 0.45f - s) / 0.6f);
                if (fade <= 0.02f) continue;
                Color cc = WithAlpha(c, alpha * fade);
                markers.Line(new[] { tip - dir * size + side * size * 0.75f, tip, tip - dir * size - side * size * 0.75f }, cc, cc, 0.07f * scale, 0.07f * scale);
            }
            Color head = WithAlpha(c, alpha);
            Vector3 end = to - dir * 0.15f;
            markers.Line(new[] { end - dir * size * 1.3f + side * size, end, end - dir * size * 1.3f - side * size }, head, head, 0.09f * scale, 0.09f * scale);
        }

        private void DrawTacticalWeakpoints()
        {
            if (weakpoints.Count == 0) return;
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 7f);
            foreach (var pair in weakpoints)
            {
                var view = Director.FindView(pair.Key);
                if (view == null || view.IsDead) continue;
                var w = pair.Value;
                Vector3 feet = view.transform.position + Vector3.up * 0.06f, chest = Chest(view);
                markers.Circle(view.transform.position, 0.95f, WithAlpha(Palette.Accent, 0.35f + 0.4f * pulse), 0.06f, 32);
                for (int i = 0; i < w.Directions.Length; i++)
                {
                    Vector3 d = w.Directions[i], side = new Vector3(d.z, 0f, -d.x);
                    float left = w.ExpiresAt != null && i < w.ExpiresAt.Length ? w.ExpiresAt[i] - Clock : 9f;
                    float alpha = left < 0.4f ? 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 22f)) : 0.7f + 0.3f * pulse;
                    Color c = WithAlpha(Palette.Accent, alpha);
                    // On the ground, larger than the battle's own marker.
                    Vector3 a = feet + d * 0.8f + side * 0.4f, b = feet + d * 0.8f - side * 0.4f, tip = feet + d * 1.55f;
                    markers.Polyline(new[] { a, tip, b, a }, c, 0.1f);
                    // At chest height on that side: a diamond, and a line in to the body.
                    Vector3 at = chest + d * 0.85f;
                    float r = 0.22f + 0.06f * pulse;
                    markers.Line(new[] { at + Vector3.up * r, at + side * r, at - Vector3.up * r, at - side * r, at + Vector3.up * r }, c, c, 0.06f, 0.06f);
                    markers.Line(new[] { chest + d * 0.35f, at - d * 0.05f }, WithAlpha(c, alpha * 0.6f), WithAlpha(c, alpha * 0.6f), 0.04f, 0.04f);
                }
            }
        }
    }
}
