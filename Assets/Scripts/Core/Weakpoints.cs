using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// One weakpoint on a unit: an opening at its feet on one of four sides (Slot 0..3, a quarter turn apart, counted from
    /// the unit's WeakpointBasis: the side its first one opened on). A hit coming from that side breaks it.
    /// </summary>
    public class Weakpoint
    {
        public int Slot;
        public float ExpiresAt;
        public ActionInstance Bound;   // opened by its holder's own windup: gone the moment that windup is over (null: timed only)
        public string SourceId;
    }

    /// <summary>A unit's weakpoints changed (EventType.Weakpoints, actor = the unit): all it has now, as directions on the
    /// ground from its centre. Broken: how many a hit just broke. Shattered: that was the last one (it loses what it was doing).</summary>
    public struct WeakpointsInfo { public Vector3[] Directions; public float[] ExpiresAt; public int Broken; public bool Shattered; }

    /// <summary>
    /// BattleSimulation: weakpoints. Abilities open them on what they hit (ActionDefinition.WeakpointsOnTarget) or on their
    /// user (WeakpointsOnSelfDuringWindup, WeakpointsOnSelf). A single-target hit from a weakpoint's side breaks it and
    /// lands WeakpointBonus harder (damage and stun); an area hit breaks none unless its ability says how many
    /// (BreaksWeakpoints, picked at random). Breaking a unit's last one interrupts whatever it is doing, through any
    /// Unstoppable it gave itself.
    /// </summary>
    public partial class BattleSimulation
    {
        /// <summary>The most a unit can have.</summary>
        public const int MaxWeakpoints = 4;
        /// <summary>How long one lasts when its ability doesn't say (ActionDefinition.WeakpointSeconds).</summary>
        public float WeakpointSeconds = 1.5f;
        /// <summary>How much harder a hit that breaks one lands, per weakpoint broken: damage and stun (0.3: +30%).</summary>
        public float WeakpointBonus = 0.3f;
        /// <summary>A hit breaks the weakpoint whose side it comes from: within this many degrees of it (45: a quarter each).</summary>
        public float WeakpointArc = 45f;

        // ------------------------------------------------------------------ opening them

        /// <summary>
        /// Open up to 'count' weakpoints on 'u' (never past MaxWeakpoints), lasting 'seconds' (0: WeakpointSeconds), or with
        /// 'bound' until that windup of its own is over. The first one a unit gets opens on the side 'firstSide' points to;
        /// the rest take free sides at random. Returns how many opened.
        /// </summary>
        public int AddWeakpoints(Unit u, int count, Vector3 firstSide, float seconds = 0f, Unit source = null, ActionInstance bound = null)
        {
            if (u == null || !u.IsAlive || count <= 0) return 0;
            if (u.Weakpoints.Count == 0)
            {
                firstSide.y = 0f;
                u.WeakpointBasis = firstSide.sqrMagnitude > 1e-6f ? firstSide.normalized : Vector3.forward;
            }
            float until = bound != null ? float.MaxValue : Now + (seconds > 0f ? seconds : WeakpointSeconds);
            int opened = 0;
            for (int i = 0; i < count && u.Weakpoints.Count < MaxWeakpoints; i++)
            {
                int slot = 0;
                if (u.Weakpoints.Count > 0)
                {
                    var free = new List<int>();
                    for (int s = 0; s < MaxWeakpoints; s++) if (!u.Weakpoints.Exists(w => w.Slot == s)) free.Add(s);
                    slot = free[Mathf.Min(free.Count - 1, (int)(rng.NextDouble() * free.Count))];
                }
                u.Weakpoints.Add(new Weakpoint { Slot = slot, ExpiresAt = until, Bound = bound, SourceId = source != null ? source.Id : null });
                opened++;
            }
            if (opened > 0) EmitWeakpoints(u, 0, false);
            return opened;
        }

        /// <summary>An ability's weakpoints for the unit it was used on (WeakpointsOnTarget): the first on the side away from its user.</summary>
        public void AddTargetWeakpoints(Unit source, Unit target, ActionDefinition def, int count = -1)
        {
            if (def == null || target == null || target == source) return;
            int n = count >= 0 ? count : def.WeakpointsOnTarget;
            if (n <= 0) return;
            Vector3 away = source != null ? target.Position - source.Position : Vector3.forward;
            AddWeakpoints(target, n, away, def.WeakpointSeconds, source);
        }

        /// <summary>As 'a' starts: its user's own weakpoints (through the windup, and the timed ones), the first behind it.</summary>
        private void OpenOwnWeakpoints(ActionInstance a)
        {
            var def = a.Definition; var u = a.Actor;
            if (def.WeakpointsOnSelfDuringWindup <= 0 && def.WeakpointsOnSelf <= 0) return;
            Vector3 behind = -FacingFor(a);
            if (def.WeakpointsOnSelfDuringWindup > 0) AddWeakpoints(u, def.WeakpointsOnSelfDuringWindup, behind, 0f, u, a);
            if (def.WeakpointsOnSelf > 0) AddWeakpoints(u, def.WeakpointsOnSelf, behind, def.WeakpointSeconds, u);
        }

        /// <summary>The way 'a''s user faces: at its target, else where it aims, else at its nearest enemy.</summary>
        private Vector3 FacingFor(ActionInstance a)
        {
            var u = a.Actor;
            var t = GetUnit(a.TargetUnitId);
            Vector3 d = t != null && t != u ? t.Position - u.Position : a.Definition.Targeting == TargetType.Position ? a.TargetPos - u.Position : Vector3.zero;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f)
            {
                var near = NearestEnemy(u);
                if (near != null) { d = near.Position - u.Position; d.y = 0f; }
            }
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        // ------------------------------------------------------------------ where they are

        /// <summary>The side of 'u' weakpoint 'w' is on: a direction on the ground from its centre.</summary>
        public Vector3 WeakpointSide(Unit u, Weakpoint w)
        {
            Vector3 b = u.WeakpointBasis.sqrMagnitude > 1e-6f ? u.WeakpointBasis : Vector3.forward;
            switch (((w.Slot % 4) + 4) % 4)
            {
                case 1: return new Vector3(b.z, 0f, -b.x);    // a quarter turn
                case 2: return new Vector3(-b.x, 0f, -b.z);   // opposite
                case 3: return new Vector3(-b.z, 0f, b.x);
                default: return b;
            }
        }

        /// <summary>The weakpoint of 'u' a hit from 'from' would break (the one facing that way), or null.</summary>
        public Weakpoint WeakpointFacing(Unit u, Vector3 from)
        {
            if (u == null || u.Weakpoints.Count == 0) return null;
            Vector3 d = from - u.Position; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return null;
            d.Normalize();
            float cos = Mathf.Cos(WeakpointArc * Mathf.Deg2Rad) - 1e-4f;
            Weakpoint best = null; float bestDot = cos;
            foreach (var w in u.Weakpoints)
            {
                float dot = Vector3.Dot(WeakpointSide(u, w), d);
                if (dot >= bestDot) { bestDot = dot; best = w; }
            }
            return best;
        }

        private void EmitWeakpoints(Unit u, int broken, bool shattered)
        {
            var dirs = new Vector3[u.Weakpoints.Count]; var ends = new float[u.Weakpoints.Count];
            for (int i = 0; i < dirs.Length; i++) { dirs[i] = WeakpointSide(u, u.Weakpoints[i]); ends[i] = u.Weakpoints[i].ExpiresAt; }
            Emit(EventType.Weakpoints, u.Id, null, new WeakpointsInfo { Directions = dirs, ExpiresAt = ends, Broken = broken, Shattered = shattered });
        }

        /// <summary>Every tick: weakpoints that ran out, or whose windup is over, close.</summary>
        /// <summary>Is the action a weakpoint is bound to still going: winding up, or channelling (Withdrawal)?</summary>
        private static bool BoundHolds(Unit u, ActionInstance a)
        {
            return (u.ActiveAction == a && u.Phase == UnitPhase.Windup) || (u.Phase == UnitPhase.Channeling && u.Channel != null && u.Channel.Action == a);
        }

        private void TickWeakpoints()
        {
            foreach (var u in State.Units)
            {
                if (u.Weakpoints.Count == 0) continue;
                int before = u.Weakpoints.Count;
                if (!u.IsAlive) u.Weakpoints.Clear();
                else u.Weakpoints.RemoveAll(w => w.Bound != null ? !BoundHolds(u, w.Bound) : w.ExpiresAt <= Now);
                if (u.Weakpoints.Count != before) EmitWeakpoints(u, 0, false);
            }
        }

        // ------------------------------------------------------------------ breaking them

        /// <summary>
        /// As a hit's damage is worked out: the weakpoints it breaks. A single-target hit breaks the one on the side it
        /// comes from; an area hit only as many as its ability says (BreaksWeakpoints), at random. Each one broken adds
        /// WeakpointBonus to the hit's damage and stun. A hit a Transcended Block stops breaks none.
        /// </summary>
        private void BreakWeakpointsOnHit(HitContext hit)
        {
            var target = hit.Target; var def = hit.Definition;
            if (target.Weakpoints.Count == 0 || hit.Negated || hit.Source == null || hit.Source == target) return;
            int broke = 0;
            int anySide = Mathf.Max(def.BreaksWeakpoints, hit.Effect != null ? hit.Effect.BreakWeakpoints : 0);   // the ability's, or a Transcend's (Brute Force)
            if (anySide > 0)
            {
                for (int i = 0; i < anySide && target.Weakpoints.Count > 0; i++, broke++)
                    target.Weakpoints.RemoveAt(Mathf.Min(target.Weakpoints.Count - 1, (int)(rng.NextDouble() * target.Weakpoints.Count)));
            }
            else if (def.Shape == AreaShape.None)
            {
                var w = WeakpointFacing(target, hit.Source.Position);
                if (w != null) { target.Weakpoints.Remove(w); broke = 1; }
            }
            if (broke == 0) return;
            hit.WeakpointsBroken = broke;
            hit.Shattered = target.Weakpoints.Count == 0;
            hit.DamageMultiplier *= 1f + WeakpointBonus * broke;
            EmitWeakpoints(target, broke, hit.Shattered);
            WeakpointsBrokenBy(hit.Source, target, broke);
        }

        /// <summary>'by' broke 'count' of 'target''s weakpoints: what its statuses give for that (Clear and Steady sheds strain),
        /// and TriggerType.WeakpointsBroken (Karranos Sigil's NEMESIS).</summary>
        private void WeakpointsBrokenBy(Unit by, Unit target, int count)
        {
            if (by == null || !by.IsAlive || count <= 0) return;
            float strain = 0f;
            foreach (var s in by.Statuses) strain += s.StrainOnWeakpointBreak;
            if (Mathf.Abs(strain) > 1e-4f) AddStrain(by, strain * count);
            Triggers.Fire(new TriggerContext { Type = TriggerType.WeakpointsBroken, Sim = this, Subject = by, Other = target, Amount = count });
        }

        /// <summary>Break up to 'count' of 'target''s weakpoints at random, with no hit (Pressure Points). Returns how many broke;
        /// breaking the last one interrupts it.</summary>
        public int BreakWeakpoints(Unit target, int count, Unit by)
        {
            if (target == null || !target.IsAlive || count <= 0 || target.Weakpoints.Count == 0) return 0;
            int broke = 0;
            for (; broke < count && target.Weakpoints.Count > 0; broke++)
                target.Weakpoints.RemoveAt(Mathf.Min(target.Weakpoints.Count - 1, (int)(rng.NextDouble() * target.Weakpoints.Count)));
            bool shattered = target.Weakpoints.Count == 0;
            EmitWeakpoints(target, broke, shattered);
            WeakpointsBrokenBy(by, target, broke);
            if (shattered) Shatter(target);
            return broke;
        }

        /// <summary>
        /// 'u''s last weakpoint was broken: whatever it is doing (a windup, a charge, a channel) is interrupted, through any
        /// Unstoppable or stun immunity it gave itself. One someone else put on it still holds, and so does Split The Minds.
        /// </summary>
        private void Shatter(Unit u)
        {
            if (u == null || !u.IsAlive || IsSplitting(u)) return;
            if (u.Statuses.Exists(s => s.Has(StatusFlags.Unstoppable) && s.SourceId != u.Id)) return;
            if ((u.Phase == UnitPhase.Windup || u.Phase == UnitPhase.Charging) && u.ActiveAction != null) Interrupt(u, "weakpoints broken");
            else if (u.Phase == UnitPhase.Channeling) EndChannel(u, "weakpoints broken");
        }

        // ------------------------------------------------------------------ the AI goes for them

        /// <summary>
        /// An AI-played unit about to use a single-target attack on a foe with weakpoints, from a side that has none: if its
        /// Dash is ready and one of them is within a dash, it dashes round to that side first (and attacks from there on
        /// its next decision). Null: carry on with 'cmd'.
        /// </summary>
        private UnitCommand FlankWeakpoint(Unit unit, UnitCommand cmd)
        {
            if (cmd == null || cmd.Type != CommandType.Action || cmd.Action == null || cmd.TargetUnitId == null) return null;
            var def = cmd.Action;
            if (def.Damage <= 0f || def.Shape != AreaShape.None || def.BreaksWeakpoints > 0) return null;
            var behavior = ActionBehaviors.Get(def.BehaviorId);
            if (behavior == null || behavior.MovesUser || behavior.Dashes) return null;
            var t = GetUnit(cmd.TargetUnitId);
            if (t == null || !t.IsAlive || t.Team == unit.Team || t.Weakpoints.Count == 0) return null;
            if (WeakpointFacing(t, unit.Position) != null) return null;   // already on one's side
            var dash = unit.DashAction();
            if (dash == null || unit.IsRooted || unit.IsLifted || unit.HasFlag(StatusFlags.NoDash) || unit.Phase != UnitPhase.Ready) return null;
            string why;
            if (!CanUse(unit, dash, null, out why)) return null;
            float reach = DashReach(unit), range = unit.ActionStat(def, ActionStat.Range);
            Vector3 gap = t.Position - unit.Position; gap.y = 0f;
            float keep = Mathf.Clamp(gap.magnitude, unit.BodyRadius + t.BodyRadius + MeleeApproachGap, Mathf.Max(unit.BodyRadius + t.BodyRadius + 0.1f, range * 0.9f));
            Vector3 best = Vector3.zero; float bestDist = float.MaxValue;
            foreach (var w in t.Weakpoints)
            {
                Vector3 spot = t.Position + WeakpointSide(t, w) * keep;
                Vector3 d = spot - unit.Position; d.y = 0f;
                if (d.magnitude > reach || d.magnitude >= bestDist) continue;
                Vector3 lands = Paths.ClampDash(unit.Position, spot);
                Vector3 off = lands - spot; off.y = 0f;
                if (off.magnitude > 0.3f) continue;   // a wall in the way
                best = spot; bestDist = d.magnitude;
            }
            return bestDist < float.MaxValue ? UnitCommand.UseActionAt(unit.Id, dash, best) : null;
        }
    }
}
