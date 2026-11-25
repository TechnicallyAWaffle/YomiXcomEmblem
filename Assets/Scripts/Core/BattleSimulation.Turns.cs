using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// BattleSimulation: when each unit is next free to act, and the order they come up in (the tactical view numbers units
    /// by it). As things stand now: nothing that hasn't happened yet (a stun to come, a haste running out) is foreseen.
    /// </summary>
    public partial class BattleSimulation
    {
        /// <summary>
        /// The sim time 'u' is next free to take an order: now if it is ready (or charging, deciding as it goes); the end of
        /// its walk; the end of its windup plus the recovery after it (and a channel, if the ability has one); the end of
        /// its channel plus the recovery; the end of its recovery, wait or stun. float.MaxValue for the fallen.
        /// </summary>
        public float NextFreeAt(Unit u)
        {
            if (u == null || !u.IsAlive) return float.MaxValue;
            float speed = Mathf.Max(0.01f, u.EffectiveActionSpeed());
            switch (u.Phase)
            {
                case UnitPhase.Ready:
                case UnitPhase.Charging:
                    return Now;
                case UnitPhase.Moving:
                {
                    if (u.Move == null || u.Move.Path == null) return Now;
                    float left = 0f;
                    Vector3 at = u.Position;
                    for (int i = u.Move.NextIndex; i < u.Move.Path.Count; i++) { left += FlatDistance(at, u.Move.Path[i]); at = u.Move.Path[i]; }
                    return Now + left / Mathf.Max(0.1f, u.Move.Speed);
                }
                case UnitPhase.Windup:
                {
                    var a = u.ActiveAction;
                    if (a == null) return u.PhaseEndsAt;
                    float recovery = a.NoRecovery ? 0f : a.Stat(ActionStat.Recovery) * a.RecoveryScale / speed;
                    return a.ResolveAt + Mathf.Max(0f, a.Definition.ChannelSeconds) + recovery;
                }
                case UnitPhase.Channeling:
                {
                    var c = u.Channel;
                    if (c == null) return u.PhaseEndsAt;
                    return c.EndsAt + c.Action.Stat(ActionStat.Recovery) / speed;
                }
                default:
                    return u.PhaseEndsAt;
            }
        }

        /// <summary>The living units, soonest free first (ties: the order they are listed in the battle).</summary>
        public List<Unit> ActOrder()
        {
            var list = new List<Unit>();
            foreach (var u in State.Units) if (u.IsAlive) list.Add(u);
            var at = new Dictionary<Unit, float>();
            foreach (var u in list) at[u] = NextFreeAt(u);
            var index = new Dictionary<Unit, int>();
            for (int i = 0; i < list.Count; i++) index[list[i]] = i;
            list.Sort((a, b) => { int c = at[a].CompareTo(at[b]); return c != 0 ? c : index[a].CompareTo(index[b]); });
            return list;
        }
    }
}
