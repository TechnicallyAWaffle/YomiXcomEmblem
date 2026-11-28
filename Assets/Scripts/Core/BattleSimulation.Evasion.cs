using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// Something about to hit wherever a unit is standing, that it could get out of: an area attack winding up (or a blast
    /// about to go off) with the unit inside it, or a projectile flying its way. Catches says whether a spot is still in
    /// it, so an AI can look for a way out (BasicEnemyAI.Evade); LandsAt is when it hits (for a projectile, when it would
    /// reach the unit flying on as it is).
    /// </summary>
    public class Hazard
    {
        public string Id;
        public bool IsProjectile;
        public Unit Source;
        public ActionDefinition Definition;
        public float LandsAt;
        internal System.Func<Vector3, Unit, bool> catches;

        /// <summary>Would 'u' standing at 'p' be caught?</summary>
        public bool Catches(Vector3 p, Unit u) { return catches(p, u); }
    }

    /// <summary>
    /// BattleSimulation: enemies noticing hazards and getting out of the way. An enemy-team unit played by the AI looks for
    /// hazards (Hazards) each time it is ready to act and every EvasionCheckSeconds while it walks. Each new one takes it a
    /// moment to react to: its ReactionSpeed times a roll (x0.5-1.5 for an area, x1-2 for a projectile). Once that has
    /// passed, if it is still inside, it is woken (if only walking or waiting) and its AI decides how to get out
    /// (IHazardEvader). The rolls are a hash of the unit and the hazard: they leave the battle's random numbers alone.
    /// </summary>
    public partial class BattleSimulation
    {
        /// <summary>Seconds between a walking enemy's looks for hazards around it (it also looks each time it is ready).</summary>
        public float EvasionCheckSeconds = 0.5f;
        /// <summary>Reaction roll ranges, as multiples of the unit's ReactionSpeed: area attacks, then projectiles.</summary>
        public float AreaReactionMin = 0.5f, AreaReactionMax = 1.5f, ProjectileReactionMin = 1f, ProjectileReactionMax = 2f;

        private class Noticed { public float ReactAt; public bool Answered; }
        // Per unit: the hazards it has noticed (by Hazard.Id), when it reacts to each, and whether its AI has answered it.
        private readonly Dictionary<string, Dictionary<string, Noticed>> noticed = new Dictionary<string, Dictionary<string, Noticed>>();
        private readonly Dictionary<string, float> nextHazardCheck = new Dictionary<string, float>();

        /// <summary>Does this unit look out for hazards? Enemy-team units the AI plays (not Maddened: MADNESS doesn't care).</summary>
        public bool Evades(Unit u)
        {
            return u != null && u.IsAlive && u.Team != Team.Player && !u.PlayerOrdered && !u.IsMaddened && !u.IsMakina;
        }

        /// <summary>Every hazard from 'u''s enemies that would catch it where it stands now.</summary>
        public List<Hazard> Hazards(Unit u)
        {
            var list = new List<Hazard>();
            foreach (var other in State.Units)
                if (other.IsAlive && other.Phase == UnitPhase.Windup && other.ActiveAction != null) AddAreaHazard(list, u, other.ActiveAction);
            foreach (var f in freeActions) AddAreaHazard(list, u, f);
            foreach (var b in blasts)
            {
                if (b.At <= Now || (!b.World && (b.Source == null || b.Source.Team == u.Team))) continue;   // a world blast (Temperance's) is everyone's
                var blast = b;
                var h = new Hazard { Id = b.Id, Source = b.Source, Definition = b.Definition, LandsAt = b.At,
                    catches = (p, who) => Flat(p, blast.Center) <= blast.Radius + who.BodyRadius };
                if (h.Catches(u.Position, u)) list.Add(h);
            }
            foreach (var p in projectiles) AddProjectileHazard(list, u, p);
            return list;
        }

        /// <summary>An area attack winding up that would catch 'u' (not one aimed at 'u' itself: that follows it wherever it goes,
        /// so it is met as a hit on it instead: BasicEnemyAI.React).</summary>
        private void AddAreaHazard(List<Hazard> list, Unit u, ActionInstance a)
        {
            var def = a.Definition;
            if (a.Actor == null || a.Actor.Team == u.Team || def.Shape == AreaShape.None || a.ResolveAt <= Now) return;
            if (a.TargetUnitId == u.Id || !a.Behavior.RollsToHit(def)) return;
            if (a.Stat(ActionStat.Damage) <= 0f && def.StunDuration <= 0f) return;
            var aimed = GetUnit(a.TargetUnitId);
            Vector3 at = def.Targeting != TargetType.Position && aimed != null ? aimed.Position : a.TargetPos;   // as IncomingTargets lays it out
            var actor = a.Actor;
            var h = new Hazard { Id = "act_" + actor.Id + "_" + a.StartedAt.ToString("0.000"), Source = actor, Definition = def, LandsAt = a.ResolveAt,
                catches = (p, who) => AreaCatches(actor, def, at, p, who.BodyRadius, who.BodyHeight) };
            if (h.Catches(u.Position, u)) list.Add(h);
        }

        /// <summary>A projectile that, flying straight on as it is, would touch 'u' before it falls apart.</summary>
        private void AddProjectileHazard(List<Hazard> list, Unit u, Projectile pr)
        {
            if (pr.Owner == null || pr.Owner.Team == u.Team || pr.Speed <= 0f) return;
            Vector3 from = pr.Position, dir = pr.Direction.normalized;
            float reach = pr.Speed * Mathf.Max(0f, pr.Spec.Lifetime - (Now - pr.Born));
            float radius = pr.Spec.HitRadius;
            float speed = pr.Speed;
            System.Func<Vector3, Unit, float> along = (p, who) =>
            {
                Vector3 mid = p + Vector3.up * (who.BodyHeight * 0.5f);
                float t = Mathf.Clamp(Vector3.Dot(mid - from, dir), 0f, reach);
                Vector3 q = from + dir * t;
                // Touching the body (a capsule-ish column from its feet to its head): see Touching.
                float dx = q.x - p.x, dz = q.z - p.z;
                float dy = q.y < p.y ? p.y - q.y : q.y > p.y + who.BodyHeight ? q.y - p.y - who.BodyHeight : 0f;
                float r = radius + who.BodyRadius;
                return dx * dx + dz * dz + dy * dy <= r * r && Vector3.Dot(mid - from, dir) > -r ? t : -1f;
            };
            float hitAt = along(u.Position, u);
            if (hitAt < 0f) return;
            list.Add(new Hazard { Id = "proj_" + pr.Path.Id, IsProjectile = true, Source = pr.Owner, Definition = pr.Definition, LandsAt = Now + hitAt / speed,
                catches = (p, who) => along(p, who) >= 0f });
        }

        /// <summary>
        /// The hazards 'u' has reacted to (its reaction time is up) that would still catch it where it stands and haven't
        /// landed yet: what it should get out of now. Empty if it doesn't evade.
        /// </summary>
        public List<Hazard> DueHazards(Unit u)
        {
            var due = new List<Hazard>();
            Dictionary<string, Noticed> seen;
            if (!Evades(u) || !noticed.TryGetValue(u.Id, out seen) || seen.Count == 0) return due;
            foreach (var h in Hazards(u))
            {
                Noticed n;
                if (seen.TryGetValue(h.Id, out n) && n.ReactAt <= Now + 1e-4f) due.Add(h);
            }
            return due;
        }

        /// <summary>The AI has been asked about 'u''s due hazards: it isn't woken for them again (it is asked again whenever it is
        /// next ready, and gets out of what is still on it then).</summary>
        private void AnswerHazards(Unit u)
        {
            Dictionary<string, Noticed> seen;
            if (!noticed.TryGetValue(u.Id, out seen)) return;
            foreach (var n in seen.Values) if (n.ReactAt <= Now + 1e-4f) n.Answered = true;
        }

        /// <summary>'u' looks around: each hazard on it that it hasn't seen yet gets its reaction time. Ones that are over are forgotten.</summary>
        private void NoticeHazards(Unit u)
        {
            if (!Evades(u)) return;
            var now = Hazards(u);
            Dictionary<string, Noticed> seen;
            if (!noticed.TryGetValue(u.Id, out seen)) noticed[u.Id] = seen = new Dictionary<string, Noticed>();
            var live = new HashSet<string>();
            foreach (var h in now)
            {
                live.Add(h.Id);
                if (seen.ContainsKey(h.Id)) continue;
                float lo = h.IsProjectile ? ProjectileReactionMin : AreaReactionMin, hi = h.IsProjectile ? ProjectileReactionMax : AreaReactionMax;
                seen[h.Id] = new Noticed { ReactAt = Now + Mathf.Max(0f, u.ReactionSpeed) * Mathf.Lerp(lo, hi, HashRoll(u.Id, h.Id)) };
            }
            // Forget what no longer threatens it (landed, gone, or it is out of it), so a later one is noticed afresh.
            var stale = new List<string>();
            foreach (var id in seen.Keys) if (!live.Contains(id)) stale.Add(id);
            foreach (var id in stale) seen.Remove(id);
        }

        /// <summary>
        /// Each tick: walking enemies look around every EvasionCheckSeconds; and any whose reaction to a hazard still on it
        /// comes due, unanswered, is woken (if it is only walking or waiting) so its AI gets it out of the way.
        /// </summary>
        private void TickEvasion()
        {
            foreach (var u in State.Units)
            {
                if (!Evades(u)) continue;
                if (u.Phase == UnitPhase.Moving)
                {
                    float next;
                    if (!nextHazardCheck.TryGetValue(u.Id, out next) || Now + 1e-4f >= next)
                    {
                        nextHazardCheck[u.Id] = Now + EvasionCheckSeconds;
                        NoticeHazards(u);
                    }
                }
                else nextHazardCheck.Remove(u.Id);   // the next walk looks around at once

                Dictionary<string, Noticed> seen;
                if (!noticed.TryGetValue(u.Id, out seen) || seen.Count == 0) continue;
                bool wake = false;
                foreach (var n in seen.Values) if (!n.Answered && n.ReactAt <= Now + 1e-4f) { wake = true; break; }
                if (!wake || DueHazards(u).Count == 0) continue;
                if (u.Phase == UnitPhase.Recovery && u.Waiting) EndRecoveryNow(u);
                else if (u.Phase == UnitPhase.Moving && !u.IsJumping && !IsGliding(u)) EndMove(u);
            }
        }

        /// <summary>A unit about to decide (ready after an action) looks around first, then its AI may get it out of the way.</summary>
        private UnitCommand EvadeHazards(Unit u, IUnitAI ai)
        {
            if (!Evades(u)) return null;
            NoticeHazards(u);
            var evader = ai as IHazardEvader;
            var cmd = evader != null ? evader.Evade(this, u) : null;
            AnswerHazards(u);   // it has had its say on them: not woken for them again (it looks again when next ready)
            return cmd;
        }

        /// <summary>A stable number in [0, 1) for this pair of ids (FNV-1a, then a final mix).</summary>
        private static float HashRoll(string a, string b)
        {
            uint h = 2166136261u;
            foreach (char c in a) { h ^= c; h *= 16777619u; }
            h ^= 0x9e3779b9u;
            foreach (char c in b) { h ^= c; h *= 16777619u; }
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
