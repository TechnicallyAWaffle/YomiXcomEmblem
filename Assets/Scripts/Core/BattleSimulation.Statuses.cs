using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: Statuses, energy and displacement: the helpers behaviors call.</summary>
    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ helpers for behaviors

        /// <summary>Apply (or refresh: same id replaces) a status. Rooting a walking unit stops it where it stands.</summary>
        public void ApplyStatus(Unit target, StatusEffect status, Unit source)
        {
            if (target == null || !target.IsAlive) return;
            float oldAction = target.EffectiveActionSpeed(), oldMove = target.EffectiveMoveSpeed();
            target.Statuses.RemoveAll(s => s.Id == status.Id);   // refresh instead of stacking
            target.Statuses.Add(status);
            Emit(EventType.StatusApplied, source != null ? source.Id : null, target.Id, status.Id);
            EmitStatusTimer(target, status, source);
            HandleSpeedChange(target, oldAction, oldMove);
            if (status.Has(StatusFlags.Rooted) && target.Phase == UnitPhase.Moving) EndMove(target);
        }

        /// <summary>Statuses lasting at least this long count as "until removed" and get no timer.</summary>
        public const float UntimedStatus = 1e6f;

        /// <summary>Every (re)application of a timed status announces its end time, so the HUD can count it down (StatusTimer).
        /// To lengthen one, re-apply a copy with a later ExpiresAt (as Final Cut does with the trance).</summary>
        private void EmitStatusTimer(Unit target, StatusEffect status, Unit source)
        {
            if (status.ExpiresAt >= UntimedStatus) return;
            Emit(EventType.StatusTimer, source != null ? source.Id : null, target.Id,
                new StatusTimerInfo { Id = status.Id, ExpiresAt = status.ExpiresAt, Duration = Mathf.Max(0f, status.ExpiresAt - Now) });
        }

        /// <summary>A status's ExpiresAt was changed in place (a chain kept going): announce the new end, without re-applying it.</summary>
        public void RefreshStatusTimer(Unit target, StatusEffect status, Unit source) { EmitStatusTimer(target, status, source); }

        /// <summary>Set a status's stack count (Flowing Death's chain) and tell the view (StatusStacks).</summary>
        public void SetStacks(Unit u, StatusEffect status, int stacks)
        {
            if (status.Stacks == stacks) return;
            status.Stacks = stacks;
            Emit(EventType.StatusStacks, u.Id, u.Id, new StatusStacksInfo { Id = status.Id, Stacks = stacks });
        }

        public bool RemoveStatus(Unit u, string id)
        {
            int idx = u.Statuses.FindIndex(s => s.Id == id);
            if (idx < 0) return false;
            float oldAction = u.EffectiveActionSpeed(), oldMove = u.EffectiveMoveSpeed();
            u.Statuses.RemoveAt(idx);
            Emit(EventType.StatusExpired, u.Id, null, id);
            HandleSpeedChange(u, oldAction, oldMove);
            return true;
        }

        public void AddEnergy(Unit u, float delta)
        {
            if (u.MaxEnergy <= 0f) return;
            float before = u.Energy;
            u.Energy = Mathf.Clamp(u.Energy + delta, 0f, u.MaxEnergy);
            if (Mathf.Abs(u.Energy - before) < 1e-4f) return;
            Emit(EventType.EnergyChanged, u.Id, null, new EnergyInfo { Amount = u.Energy - before, Total = u.Energy });
        }

        /// <summary>Shove a unit per def.Displace: away from its nearest enemy, knocked back from 'source', or pulled toward 'source' (stopping at arm's length).</summary>
        public void DisplaceUnit(Unit target, Unit source, ActionDefinition def)
        {
            if (target == null || !target.IsAlive || def.DisplaceDistance <= 0f) return;
            float distance = source != null ? source.ActionStat(def, ActionStat.DisplaceDistance) : def.DisplaceDistance;
            Vector3 from = target.Position, to;
            if (def.Displace == DisplaceMode.TowardSource)
            {
                if (source == null) return;
                Vector3 d = from - source.Position; d.y = 0f;
                float stop = source.BodyRadius + target.BodyRadius + 0.5f;
                if (d.magnitude <= stop) return;
                to = source.Position + d.normalized * Mathf.Max(stop, d.magnitude - distance);
                to.y = from.y;
            }
            else if (def.Displace == DisplaceMode.AwayFromEnemies)
            {
                var threat = NearestEnemy(target);
                if (threat == null) return;
                Vector3 away = from - threat.Position; away.y = 0f;
                if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;
                to = from + away.normalized * distance;
            }
            else if (def.Displace == DisplaceMode.AwayFromSource)
            {
                if (source == null) return;
                Vector3 away = from - source.Position; away.y = 0f;
                if (away.sqrMagnitude < 1e-6f) away = Vector3.forward;
                to = from + away.normalized * distance;
            }
            else return;
            Relocate(target, Paths.ClampDash(from, to), DisplaceDuration, true, source);
        }
    }
}
