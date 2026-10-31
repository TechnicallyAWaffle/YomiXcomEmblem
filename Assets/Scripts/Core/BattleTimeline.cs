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
        ChargeStarted,         // a charge ability began charging; payload ChargeInfo
        Jump,                  // a walking unit jumps across a height change; payload JumpInfo (the walk resumes with a MoveStarted on landing)
        Fall,                  // target was shoved off a ledge or knocked out of a jump (actor = who did it, may be null); payload FallInfo.
                               // A Fall-flagged Dash plays the fall itself; the landing's fall damage is a Damage event with ActionId "fall"
        StatusTimer,           // a timed status on target started, refreshed or was extended (actor = who applied it); payload StatusTimerInfo
        Carry,                 // actor started (or stopped) carrying target (Subjugate); payload CarryInfo
        Lift,                  // target is raised into the air and held there (Amblivate; actor = who did it); payload LiftInfo. A Fall drops it
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
    public struct ActionStartedInfo { public string ActionId; public float Windup; public float Recovery; public Vector3 TargetPos; public bool Free; public bool Transcended; }   // Free: a reaction alongside the unit's own action
    public struct ActionResolvedInfo { public string ActionId; public float Recovery; }
    public struct InterruptInfo { public string Reason; }
    public struct MoveInfo { public List<Vector3> Path; public float Speed; }
    public struct DamageInfo
    {
        public string ActionId; public float Amount; public float BlockedAmount;
        public ImpactType Impact; public float RemainingHealth; public bool CausedUnravel;
    }
    // Reason: "cover" (would have hit in the open), "miss", "out_of_range", "hidden". HitCover: the shot struck the cover that
    // saved the target, at CoverPoint (a point where a sight line to the target first meets that cover).
    public struct MissInfo { public string Reason; public string ActionId; public bool HitCover; public Vector3 CoverPoint; }
    // A timed status (re)started or was extended: it now runs out at ExpiresAt, Duration seconds after this event.
    public struct StatusTimerInfo { public string Id; public float ExpiresAt; public float Duration; }
    public struct DashInfo { public Vector3 From; public Vector3 To; public float Duration; public bool Forced; public bool Settle; public bool Fall; public bool Teleport; }  // Forced: shoved or pulled, not a dodge. Settle: a small shuffle out of another unit's body. Fall: drops to lower ground on the way. Teleport: gone from From, there at To
    // Carrying: the target is held at Offset (metres in front of the carrier, along its facing) until a Carry with Carrying false,
    // which leaves it at Position.
    public struct CarryInfo { public bool Carrying; public float Offset; public Vector3 Position; }
    public struct LiftInfo { public Vector3 From; public Vector3 To; public float RiseSeconds; public float DropAt; }
    public struct JumpInfo { public Vector3 From; public Vector3 To; public float Duration; }   // the arc's height: TerrainMap.JumpY
    public struct FallInfo { public float Height; public float Duration; public bool FromJump; }   // Duration: until it lands (the stun lasts a little longer)
    public struct StunInfo { public float Duration; }
    public struct MoveEndedInfo { public Vector3 Position; }
    public struct StrainInfo { public float Amount; public float Total; }
    public struct SpeedInfo { public float ActionSpeed; public float MoveSpeed; }
    public struct DeployableInfo { public string Id; public Vector3 Position; public float Radius; public float Height; public float ExpiresAt; }
    public struct EnergyInfo { public float Amount; public float Total; }
    public struct ChargeInfo { public string ActionId; public float MaxSeconds; }
    public struct QueuedActionInfo { public string ActionId; public bool Chase; }   // Chase: melee, walking at the target
    public struct TrapInfo { public string Id; public Vector3 Position; public float Radius; public float ArmsAt; public Team OwnerTeam; }
}
