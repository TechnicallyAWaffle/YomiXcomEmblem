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
        StatusStacks,          // a status on actor (= target) changed stack count (Flowing Death's chain); payload StatusStacksInfo
        AuraStarted,           // a dome went up around actor (Sanctity, Chronosphere); payload AuraInfo. It moves with actor
        AuraEnded,             // that dome came down; payload the AuraInfo.Id
        ChannelStarted,        // actor began channeling (target = its target, if any); payload ChannelInfo
        ChannelEnded,          // actor's channel ran out (its recovery follows) or broke (Broken); payload ChannelInfo
        HitRedirected,         // a hit aimed at target went to actor instead (Sanctity's dome); payload RedirectInfo
        AerialSlam,            // actor carries target up into the air and slams it down (Heretic's Ascent); payload AerialSlamInfo
        AerialSlamEnded,       // that slam is over: landed (payload false), or knocked out of the air (true) and both fall
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
    public struct DashInfo { public Vector3 From; public Vector3 To; public float Duration; public bool Forced; public bool Settle; public bool Fall; public bool Teleport; public bool Blink; }  // Forced: shoved or pulled, not a dodge. Settle: a small shuffle out of another unit's body. Fall: drops to lower ground on the way. Teleport: gone from From, there at To
    // Carrying: the target is held at Offset (metres in front of the carrier, along its facing) until a Carry with Carrying false,
    // which leaves it at Position.
    public struct CarryInfo { public bool Carrying; public float Offset; public Vector3 Position; }
    public struct StatusStacksInfo { public string Id; public int Stacks; }
    // Blink (with Teleport): the unit teleported itself (Polystep), rather than being sent.
    public struct LiftInfo { public Vector3 From; public Vector3 To; public float RiseSeconds; public float DropAt; public bool Hover; }   // Hover: a leap up to act from the air (Skyward Demise), not a stunned lift
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
    public struct AuraInfo { public string Id; public string ActionId; public float Radius; public float Height; public float EndsAt; }
    // Seconds: how long the channel runs at most (ChannelStarted). Recovery: what follows it (0 when Broken). Reason: why it broke.
    public struct ChannelInfo { public string ActionId; public float Seconds; public float Recovery; public bool Broken; public string Reason; }
    public struct RedirectInfo { public string ActionId; public string AttackerId; }

    /// <summary>
    /// Heretic's Ascent's flight, all of it known when it starts: the carrier rises Height metres over Rise seconds, hangs
    /// there for Hold, and comes down over Slam back where it took off; the one it carries swings from in front of it, up
    /// over its head and down behind it to Landing, like a suplex. AerialSlamPath gives both positions at any time, so the
    /// simulation and the view follow the same curve.
    /// </summary>
    public struct AerialSlamInfo
    {
        public Vector3 CarrierStart, HeldStart, Landing, Facing;   // Facing: flat, from the carrier toward the one it grabbed
        public float Height, Radius, PivotHeight, HeldHalfHeight;  // Radius: how far the held body's centre swings around the pivot
        public float Start, Rise, Hold, Slam;
    }

    public static class AerialSlamPath
    {
        public const float HangBob = 0.15f;   // the carrier floats this much higher mid-hang

        public static float End(AerialSlamInfo p) { return p.Start + p.Rise + p.Hold + p.Slam; }

        /// <summary>Still going up or hanging there (the slam down hasn't started).</summary>
        public static bool BeforeSlam(AerialSlamInfo p, float t) { return t < p.Start + p.Rise + p.Hold; }

        /// <summary>The carrier's feet at time t: an easing-out leap, a hang, then an accelerating drop back to the ground.</summary>
        public static Vector3 Carrier(AerialSlamInfo p, float t)
        {
            float s = t - p.Start, h;
            if (s < p.Rise) h = p.Height * Mathf.Sin(Mathf.Clamp01(s / Mathf.Max(1e-4f, p.Rise)) * Mathf.PI * 0.5f);
            else if (s < p.Rise + p.Hold) h = p.Height + HangBob * Mathf.Sin((s - p.Rise) / Mathf.Max(1e-4f, p.Hold) * Mathf.PI);
            else { float k = Mathf.Clamp01((s - p.Rise - p.Hold) / Mathf.Max(1e-4f, p.Slam)); h = p.Height * (1f - k * k); }
            return p.CarrierStart + Vector3.up * h;
        }

        /// <summary>How far the held body has swung over (radians): 0 in front, pi/2 overhead, pi behind (upside down).</summary>
        public static float Swing(AerialSlamInfo p, float t)
        {
            float s = t - p.Start;
            if (s < p.Rise) { float k = Mathf.Clamp01(s / Mathf.Max(1e-4f, p.Rise)); return Mathf.PI * 0.3f * k * k * (3f - 2f * k); }
            if (s < p.Rise + p.Hold) { float k = Mathf.Clamp01((s - p.Rise) / Mathf.Max(1e-4f, p.Hold)); return Mathf.PI * (0.3f + 0.2f * k * k * (3f - 2f * k)); }
            float j = Mathf.Clamp01((s - p.Rise - p.Hold) / Mathf.Max(1e-4f, p.Slam));
            return Mathf.PI * (0.5f + 0.5f * j * j);
        }

        /// <summary>The centre of the held body at time t: on a circle around the carrier's chest, blended from where it was
        /// grabbed at the start and onto Landing at the end, so both ends join up with where it really stands.</summary>
        public static Vector3 HeldCenter(AerialSlamInfo p, float t)
        {
            float a = Swing(p, t);
            Vector3 pivot = Carrier(p, t) + Vector3.up * p.PivotHeight;
            Vector3 c = pivot + (p.Facing * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * p.Radius;
            float s = t - p.Start;
            // Where the circle starts and ends (feet on the ground in front, upside down behind) versus the real spots.
            Vector3 startOff = p.HeldStart + Vector3.up * p.HeldHalfHeight - (p.CarrierStart + Vector3.up * p.PivotHeight + p.Facing * p.Radius);
            Vector3 endOff = p.Landing + Vector3.up * p.HeldHalfHeight - (p.CarrierStart + Vector3.up * p.PivotHeight - p.Facing * p.Radius);
            float rise = Mathf.Clamp01(s / Mathf.Max(1e-4f, p.Rise));
            float slam = Mathf.Clamp01((s - p.Rise - p.Hold) / Mathf.Max(1e-4f, p.Slam));
            return c + startOff * (1f - rise * rise * (3f - 2f * rise)) + endOff * (slam * slam);
        }

        /// <summary>The held unit's feet as the simulation tracks them (upright under its centre).</summary>
        public static Vector3 HeldFeet(AerialSlamInfo p, float t) { return HeldCenter(p, t) - Vector3.up * p.HeldHalfHeight; }
    }
}
