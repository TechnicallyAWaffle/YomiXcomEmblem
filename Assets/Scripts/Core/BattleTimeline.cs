using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    public enum EventType
    {
        ActionStarted,      // windup begins
        ActionResolved,     // windup finished, effects applied, recovery begins
        ActionInterrupted,
        MoveStarted,
        MoveEnded,
        Damage,
        Miss,
        Dodge,
        Dash,
        Stun,
        Unravel,
        StatusApplied,
        StatusExpired,
        Heal,
        UnitReady,
        Death,
        BattleEnd,
        StrainChanged,
        SpeedChanged,       // haste/slow changed this unit's action or move speed
        DeployableSpawned,
        DeployableRemoved,
        EnergyChanged,
        TrapPlaced,
        TrapTriggered,      // actor = trap owner, target = victim, payload = trap id
        TrapRemoved,        // expired unsprung; payload = trap id
        QueuedActionChanged,   // an out-of-range order was set (target = its target) or cleared (target null); payload QueuedActionInfo
    }

    /// <summary>
    /// One already-resolved thing that happened. SimTime is simulation time
    /// in seconds, NOT wall-clock time; the PlaybackDirector maps it to real time.
    /// </summary>
    public struct TimelineEvent
    {
        public float SimTime;
        public int Sequence;     // causal order tiebreaker, assigned by BattleTimeline.AddEvent
        public string ActorId;
        public string TargetId;
        public EventType Type;
        public object Payload;

        public TimelineEvent(float simTime, string actorId, string targetId, EventType type, object payload = null)
        {
            SimTime = simTime; Sequence = 0; ActorId = actorId; TargetId = targetId; Type = type; Payload = payload;
        }
    }

    /// <summary>
    /// Append-only record of the whole battle. The simulation appends to it in
    /// chronological order segment by segment; the same data drives slow-motion
    /// playback and the real-time replay. Never mutated during playback.
    /// </summary>
    public class BattleTimeline
    {
        public BattleState InitialState;
        public List<TimelineEvent> Events = new List<TimelineEvent>();
        private int nextSequence;

        public void AddEvent(TimelineEvent evt)
        {
            evt.Sequence = nextSequence++;
            Events.Add(evt);
        }

        /// <summary>Only needed if events were added out of order (e.g. in tests).</summary>
        public void SortByTime()
        {
            Events = Events.OrderBy(e => e.SimTime).ThenBy(e => e.Sequence).ToList();
        }

        public float Duration { get { return Events.Count == 0 ? 0f : Events[Events.Count - 1].SimTime; } }
    }

    // ---- Event payloads ----
    public struct ActionStartedInfo { public string ActionId; public float Windup; public float Recovery; public Vector3 TargetPos; public bool Free; }   // Free: a reaction alongside the unit's own action
    public struct ActionResolvedInfo { public string ActionId; public float Recovery; }
    public struct InterruptInfo { public string Reason; }
    public struct MoveInfo { public List<Vector3> Path; public float Speed; }
    public struct DamageInfo
    {
        public string ActionId; public float Amount; public float BlockedAmount;
        public ImpactType Impact; public float RemainingHealth; public bool CausedUnravel;
    }
    public struct MissInfo { public string Reason; } // "cover", "miss", "out_of_range"
    public struct DashInfo { public Vector3 From; public Vector3 To; public float Duration; public bool Forced; public bool Settle; }  // Forced: shoved or pulled, not a dodge. Settle: a small shuffle out of another unit's body
    public struct StunInfo { public float Duration; }
    public struct MoveEndedInfo { public Vector3 Position; }
    public struct StrainInfo { public float Amount; public float Total; }
    public struct SpeedInfo { public float ActionSpeed; public float MoveSpeed; }
    public struct DeployableInfo { public string Id; public Vector3 Position; public float Radius; public float Height; public float ExpiresAt; }
    public struct EnergyInfo { public float Amount; public float Total; }
    public struct QueuedActionInfo { public string ActionId; public bool Chase; }   // Chase: melee, walking at the target
    public struct TrapInfo { public string Id; public Vector3 Position; public float Radius; public float ArmsAt; public Team OwnerTeam; }
}
