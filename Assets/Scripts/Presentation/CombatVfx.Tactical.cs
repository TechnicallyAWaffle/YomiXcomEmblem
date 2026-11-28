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
    ///   Areas:     every area effect's hitbox (an area windup's circle or lane, a dome or zone, a delayed blast) as a
    ///              see-through shape with a translucent outline: red for the enemy's, blue for the squad's; with a
    ///              targeting line from its source to its centre when that isn't where the source stands.
    ///   Projectiles: an arrowhead at each one, and its predicted course in small running arrows (ProjectilePath.Predict):
    ///              a homing one's toward where its target is now, as if it stays there; any other straight on, until it
    ///              would meet the ground or a wall, or fall apart.
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

        [Tooltip("Tactical view areas: how see-through their fill is (alpha) and how strong their outline.")]
        public float AreaFillAlpha = 0.12f;
        public float AreaOutlineAlpha = 0.6f;

        /// <summary>The tactical view's frame: area hitboxes, targeting arrows, projectile courses and weakpoints (no effects).</summary>
        private void DrawTactical()
        {
            DrawTacticalAreas();
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

        /// <summary>
        /// Every area effect's hitbox: area windups (a circle, a lane, a charge's run), domes and zones up now, and delayed
        /// blasts waiting to go off. Each a see-through fill in its team's colour with a translucent outline, and a targeting
        /// line (running arrows) from its source to its centre when it isn't centred on the source.
        /// </summary>
        private void DrawTacticalAreas()
        {
            float now = Clock;
            foreach (var w in windups)
            {
                var actor = Director.FindView(w.ActorId);
                if (actor == null || actor.IsDead) continue;
                var def = w.Def;
                Color c = TeamColor(TeamOf(w.ActorId));
                float range = RangeOr(w.Range, def), radius = RadiusOr(w.Radius, def);
                if (def.Shape == AreaShape.Circle && radius > 0f)
                {
                    bool onSelf = def.Targeting == TargetType.Self;
                    Vector3 center = onSelf ? actor.transform.position : w.TargetPos;
                    TacticalDisc(center, radius, c);   // the windup's own arrow already runs to its centre (DrawTactical)
                }
                else if (def.Shape == AreaShape.Cone && radius > 0f)
                {
                    Vector3 from = actor.transform.position;
                    Vector3 dir = Flat(w.TargetPos - from);
                    if (dir.sqrMagnitude < 1e-4f) dir = actor.transform.forward;
                    TacticalShape(WorldMarkers.ConeOutline(from + Vector3.up * 0.05f, dir, radius, def.ConeAngle), c);
                }
                else if (def.Shape == AreaShape.Line || def.BehaviorId == "charge")
                {
                    Vector3 from = actor.transform.position;
                    Vector3 dir = Flat(w.TargetPos - from);
                    if (dir.sqrMagnitude < 1e-4f) dir = actor.transform.forward;
                    dir.Normalize();
                    bool charge = def.Shape != AreaShape.Line;
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * (charge ? 0.6f : def.LineWidth / 2f);
                    Vector3 bend, end;
                    if (charge) { bend = from + dir * (range * 0.5f); end = from + dir * range; }
                    else
                    {
                        float aimDist = Mathf.Min(Flat(w.TargetPos - from).magnitude, range);
                        bend = LinePoint(def, from, w.TargetPos, dir, aimDist); end = LinePoint(def, from, w.TargetPos, dir, range);
                    }
                    var outline = new[] { from - side, bend - side, end - side, end + side, bend + side, from + side };
                    TacticalShape(outline, c);
                }
                else if (def.BehaviorId == "trap" && radius > 0f) TacticalDisc(w.TargetPos, radius, c);
            }
            foreach (var d in domes.Values)
            {
                var view = Director.FindView(d.ActorId);
                if ((view == null && !d.Anchored) || now < d.Start || now > d.EndsAt) continue;
                Vector3 center = d.Anchored ? d.Center : view.transform.position;
                Color c = TeamColor(TeamOf(d.ActorId));
                TacticalDisc(center, d.Radius, c);
                if (d.Anchored && view != null && !view.IsDead) TacticalSourceLine(view, center, c);
            }
            foreach (var b in blasts.Values)
            {
                if (now < b.Start || now > b.At) continue;
                Color c = TeamColor(TeamOf(b.ActorId));
                TacticalDisc(b.Center, b.Radius, c);
                var view = b.ActorId != null ? Director.FindView(b.ActorId) : null;
                if (view != null && !view.IsDead) TacticalSourceLine(view, b.Center, c);
            }
        }

        private void TacticalDisc(Vector3 center, float radius, Color c)
        {
            markers.Disc(center, radius, WithAlpha(c, AreaFillAlpha));
            markers.Circle(center, radius, WithAlpha(c, AreaOutlineAlpha), 0.07f, 64);
        }

        private void TacticalShape(Vector3[] outline, Color c)
        {
            markers.Fill(outline, WithAlpha(c, AreaFillAlpha));
            var loop = new Vector3[outline.Length + 1];
            System.Array.Copy(outline, loop, outline.Length);
            loop[outline.Length] = outline[0];
            markers.Polyline(loop, WithAlpha(c, AreaOutlineAlpha), 0.07f);
        }

        /// <summary>A targeting line from an area's source to its centre, unless the source stands in the middle of it.</summary>
        private void TacticalSourceLine(UnitView source, Vector3 center, Color c)
        {
            if (Flat(center - source.transform.position).sqrMagnitude < 1f) return;
            TacticalArrow(Chest(source), center + Vector3.up * 0.1f, c, 0.6f, 0.8f);
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
