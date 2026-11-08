using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// BattleSimulation: tethers (You.'s chain). A tethered unit is held to its anchor by a chain of some length: once the two
    /// are further apart than that, by any means (it walked, dashed or was shoved away, or the anchor moved off), and stay so
    /// for the snap delay, the chain snaps taut. The unit is yanked back toward the anchor (a forced move: Unstoppable
    /// shrugs it off), and the anchor's hit lands on it, sure to connect: the tether's ability's damage and stun. Each snap
    /// spends one of its snaps; with none left, or once its time is up or either end is gone, the chain comes off.
    /// The view follows "chain_on" / "chain_off" AbilityFx events (Radius: the chain's length).
    /// </summary>
    public partial class BattleSimulation
    {
        /// <summary>Seconds a snapped chain takes to drag its unit back.</summary>
        public float TetherYankSeconds = 0.15f;

        /// <summary>
        /// Chain 'target' to 'anchor' (the anchor's ability 'def' is the hit a snap lands) for 'seconds': 'length' metres of
        /// slack, 'snapDelay' seconds over it before it snaps, 'snaps' snaps before it comes off, and a snap drags the unit
        /// back to 'yankTo' of the length from the anchor. A new chain on the same unit replaces the old one.
        /// </summary>
        public void AttachTether(Unit target, Unit anchor, ActionDefinition def, float length, float snapDelay, float seconds, int snaps = 1, float yankTo = 0.5f)
        {
            if (target == null || anchor == null || !target.IsAlive || !anchor.IsAlive || target == anchor || length <= 0f || seconds <= 0f) return;
            if (target.Tether != null) EndTether(target);
            target.AddState(new TetherState
            {
                AnchorId = anchor.Id, Definition = def, Length = length, SnapDelay = Mathf.Max(0f, snapDelay), EndsAt = Now + seconds,
                SnapsLeft = Mathf.Max(1, snaps), YankTo = Mathf.Clamp01(yankTo),
            });
            ApplyStatus(target, new StatusEffect { Id = StatusIds.Chained, SourceId = anchor.Id, ExpiresAt = Now + seconds }, anchor);
            EmitFx(anchor, target, "chain_on", def, target.Position, length, anchor.Position);
        }

        /// <summary>A miss for a behavior that checks its own target (out of reach, hidden): the Miss event.</summary>
        public void EmitMiss(Unit source, Unit target, string reason, ActionDefinition def)
        {
            Emit(EventType.Miss, source != null ? source.Id : null, target != null ? target.Id : null, new MissInfo { Reason = reason, ActionId = def != null ? def.Id : null });
        }

        /// <summary>Take the chain off 'u' (its time is up, it snapped its last, or an end is gone).</summary>
        public void EndTether(Unit u)
        {
            var t = u != null ? u.Tether : null;
            if (t == null) return;
            u.RemoveState(t);
            if (u.HasStatus(StatusIds.Chained)) RemoveStatus(u, StatusIds.Chained);
            EmitFx(GetUnit(t.AnchorId), u, "chain_off", t.Definition, u.Position);
        }

        /// <summary>Each tick, after everyone has moved: chains past their length count toward their snap; snapped ones yank.</summary>
        private void TickTethers()
        {
            foreach (var u in State.Units.ToArray())
            {
                var t = u.Tether;
                if (t == null) continue;
                var anchor = GetUnit(t.AnchorId);
                if (!u.IsAlive || anchor == null || !anchor.IsAlive || Now + 1e-4f >= t.EndsAt) { EndTether(u); continue; }
                Vector3 d = u.Position - anchor.Position; d.y = 0f;
                if (d.magnitude <= t.Length + 1e-3f) { t.OverSince = -1f; continue; }
                if (t.OverSince < 0f) t.OverSince = Now;
                if (Now - t.OverSince + 1e-4f < t.SnapDelay) continue;
                SnapTether(u, anchor, t, d);
            }
        }

        private void SnapTether(Unit u, Unit anchor, TetherState t, Vector3 away)
        {
            t.OverSince = -1f;
            t.SnapsLeft--;
            float back = Mathf.Max(anchor.BodyRadius + u.BodyRadius + StandingGap, t.Length * t.YankTo);
            Vector3 to = anchor.Position + away.normalized * back;
            to.y = u.Position.y;
            EmitFx(anchor, u, "chain_snap", t.Definition, u.Position, t.Length, anchor.Position);
            PushUnit(u, to, TetherYankSeconds, anchor);   // a forced move: not past an Unstoppable unit
            if (t.Definition != null)
                StrikeNow(new AttackEffect { Source = anchor, TargetId = u.Id, Definition = t.Definition, IgnoreRange = true, IgnoreCover = true, Unerring = true, Tag = "chain_snap" });
            if (t.SnapsLeft <= 0 && u.Tether == t) EndTether(u);
        }
    }
}
