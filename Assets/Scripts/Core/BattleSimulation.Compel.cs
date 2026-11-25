using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>A unit made to walk toward another for a while (Beckon): it takes no orders until EndsAt or it arrives.</summary>
    public class CompelledState : ControlState
    {
        public float EndsAt;
        public string TowardId;
        public override void OnDeath(BattleSimulation sim) { Unit.RemoveState(this); }
    }

    /// <summary>
    /// BattleSimulation: compelled walks and always-on abilities that act every tick.
    /// Compelled (Beckon): CompelNextAct puts StatusIds.Beckoned on a unit. The next time it can act (it is free, or already
    /// walking) it is made to walk toward the one who called it for 'seconds' instead of whatever it would have done, then
    /// it is free again. Rooted, Unstoppable, voided or Makina units can't be made to walk; the call is spent all the same.
    /// Innate ticks: an InnatePassive ability's behavior gets OnInnateTick every tick its owner is alive and not VOIDED
    /// (Imperfect Apotheosis: what touches The Infinite).
    /// </summary>
    public partial class BattleSimulation
    {
        /// <summary>Make 'u' walk toward 'toward' for 'seconds' the next time it can act (now, if it is free or walking).</summary>
        public void CompelNextAct(Unit u, Unit toward, float seconds, ActionDefinition from)
        {
            if (u == null || !u.IsAlive || toward == null) return;
            ApplyStatus(u, new StatusEffect
            {
                Id = StatusIds.Beckoned, SourceId = toward.Id, ExpiresAt = Now + CompelWaitSeconds, FromAction = from,
            }, toward);
            compelSeconds[u.Id] = Mathf.Max(TickLength, seconds);
            if (u.Phase == UnitPhase.Ready || u.Phase == UnitPhase.Moving) TryCompel(u);
        }

        private readonly Dictionary<string, float> compelSeconds = new Dictionary<string, float>();   // by unit: how long its waiting call makes it walk

        /// <summary>How long a call waits for its unit to be free before it lapses.</summary>
        public const float CompelWaitSeconds = 10f;

        /// <summary>Is 'u' being made to walk right now?</summary>
        public bool IsCompelled(Unit u) { return u != null && u.GetState<CompelledState>() != null; }

        /// <summary>A unit that can act: a waiting call (Beckoned) starts its walk. True if it did (it takes no order now).</summary>
        private bool TryCompel(Unit u)
        {
            var s = u.GetStatus(StatusIds.Beckoned);
            if (s == null || IsCompelled(u)) return false;
            var toward = GetUnit(s.SourceId);
            float seconds; if (!compelSeconds.TryGetValue(u.Id, out seconds)) seconds = 1.5f;
            if (toward == null || !toward.IsAlive || u.IsRooted || u.IsUnstoppable || u.IsMakina || IsVoided(u))
            {
                RemoveStatus(u, StatusIds.Beckoned);   // spent: nothing to walk to, or it can't be made to
                return false;
            }
            var path = new List<Vector3>();
            if (!PlanMove(u, UnitCommand.MoveTo(u.Id, toward.Position), path)) { RemoveStatus(u, StatusIds.Beckoned); return false; }
            if (u.Phase == UnitPhase.Moving) HaltWalk(u);
            ClearChase(u); ClearQueued(u, null);
            u.Waiting = false;
            s.ExpiresAt = Now + seconds;   // shown for as long as the walk lasts
            u.AddState(new CompelledState { EndsAt = Now + seconds, TowardId = toward.Id });
            StartMove(u, path);
            return true;
        }

        /// <summary>Each tick: compelled walks that are over (time up, arrived, or stopped) hand the unit back.</summary>
        private void TickCompelled()
        {
            foreach (var u in State.Units.ToArray())
            {
                var c = u.GetState<CompelledState>();
                if (c == null) continue;
                bool over = !u.IsAlive || Now + 1e-4f >= c.EndsAt || u.Phase != UnitPhase.Moving;
                if (!over) continue;
                u.RemoveState(c);
                if (u.HasStatus(StatusIds.Beckoned)) RemoveStatus(u, StatusIds.Beckoned);
                if (u.IsAlive && u.Phase == UnitPhase.Moving) EndMove(u);   // free to act again
            }
        }

        /// <summary>Each tick: every always-on ability of a unit that is up acts (ActionBehavior.OnInnateTick).</summary>
        private void TickInnatePassives()
        {
            foreach (var u in State.Units.ToArray())
            {
                if (!u.IsAlive || IsVoided(u)) continue;
                foreach (var def in u.Actions)
                    if (def != null && def.InnatePassive) ActionBehaviors.Get(def.BehaviorId).OnInnateTick(this, u, def);
            }
        }
    }
}
