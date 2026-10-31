using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Plays a BattleTimeline into the scene on a custom, unscaled clock, so scheduling,
    /// scrubbing and replay never depend on Time.timeScale. Optionally mirrors playbackSpeed
    /// into Time.timeScale so Animators/particles/physics slow down for free.
    ///
    /// The timeline grows as the simulation advances, so playback is driven in stretches:
    /// PlayUntil(simTime) plays up to that point and stops (ReachedTarget), which is where
    /// the BattleController asks the player for commands.
    /// </summary>
    public class PlaybackDirector : MonoBehaviour
    {
        [Header("Playback")]
        [Tooltip("1 = real time. Lower = slow motion. Lower it for a permanent slow-motion look.")]
        [Range(0.02f, 4f)]
        public float playbackSpeed = 1f;

        [Tooltip("Mirror playbackSpeed into Time.timeScale so Animators/particles slow down for free.")]
        public bool syncEngineTimescale = true;

        [Tooltip("Time.timeScale while the world is held waiting for player orders. 0 freezes it; a little above 0 lets it crawl.")]
        [Range(0f, 0.2f)]
        public float holdTimescale = 0f;

        public BattleTimeline Timeline { get; private set; }
        public float PlaybackClock { get { return playbackClock; } }
        public bool ReachedTarget { get; private set; }

        public event System.Action<TimelineEvent> OnEventDispatched;
        /// <summary>Playback rewound to the opening state (battle load, replay, scrub). UI should rebuild from Timeline.InitialState.</summary>
        public event System.Action OnPlaybackReset;

        private Dictionary<string, UnitView> unitRegistry;
        private float playbackClock;
        private float targetSimTime;
        private int nextEventIndex;

        public void Load(BattleTimeline timeline, Dictionary<string, UnitView> registry)
        {
            Timeline = timeline;
            unitRegistry = registry;
            ResetPlayback();
        }

        /// <summary>Play at playbackSpeed up to the given simulation time, then hold.</summary>
        public void PlayUntil(float simTime)
        {
            targetSimTime = Mathf.Max(targetSimTime, simTime);
            ReachedTarget = false;
            ApplyTimescale();
        }

        /// <summary>Reset the scene to the opening state and play the whole battle back at real-time speed.</summary>
        public void PlayReplay(float speed = 1f)
        {
            playbackSpeed = speed;
            ResetPlayback();
            PlayUntil(Timeline.Duration);
        }

        /// <summary>Restore normal engine time, e.g. for the results screen after the last stretch has played.</summary>
        public void ReleaseTimescale()
        {
            if (!syncEngineTimescale) return;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        public void SetPlaybackSpeed(float speed)
        {
            playbackSpeed = Mathf.Clamp(speed, 0.02f, 4f);
            if (!ReachedTarget) ApplyTimescale();
        }

        /// <summary>Jump to an arbitrary point (replay scrubber). Rebuilds the visible state by replaying from the start.</summary>
        public void ScrubTo(float simTime)
        {
            float speed = playbackSpeed;
            ResetPlayback();
            playbackSpeed = speed;
            targetSimTime = Mathf.Clamp(simTime, 0f, Timeline.Duration);
            playbackClock = targetSimTime;
            DispatchDueEvents();
            ReachedTarget = true;
        }

        private void ApplyTimescale()
        {
            if (!syncEngineTimescale) return;
            Time.timeScale = playbackSpeed;
            Time.fixedDeltaTime = 0.02f * playbackSpeed;
        }

        private void ResetPlayback()
        {
            playbackClock = 0f;
            targetSimTime = 0f;
            nextEventIndex = 0;
            ReachedTarget = true;
            if (Timeline == null || unitRegistry == null) return;
            foreach (var unit in Timeline.InitialState.Units)
            {
                UnitView view;
                if (unitRegistry.TryGetValue(unit.Id, out view)) view.ResetTo(unit);
            }
            if (OnPlaybackReset != null) OnPlaybackReset();
        }

        private void Update()
        {
            if (Timeline == null || ReachedTarget) return;

            // Custom clock decides WHAT happens WHEN, independent of Time.timeScale.
            playbackClock = Mathf.Min(playbackClock + Time.unscaledDeltaTime * playbackSpeed, targetSimTime);
            DispatchDueEvents();

            if (playbackClock >= targetSimTime)
            {
                ReachedTarget = true;
                // The playback clock holds here (scripted movement stays put so it can't run ahead of the simulation),
                // and animators/particles run at holdTimescale (0 = fully frozen) until orders are in.
                if (syncEngineTimescale) Time.timeScale = holdTimescale;
            }
        }

        private void DispatchDueEvents()
        {
            var events = Timeline.Events;
            while (nextEventIndex < events.Count && events[nextEventIndex].SimTime <= playbackClock + 1e-4f)
            {
                Dispatch(events[nextEventIndex]);
                nextEventIndex++;
            }
        }

        public UnitView FindView(string unitId)
        {
            UnitView v;
            return (unitId != null && unitRegistry != null && unitRegistry.TryGetValue(unitId, out v)) ? v : null;
        }

        /// <summary>Presentation only: never mutates simulation state. Hook animation, VFX, camera and UI here.</summary>
        private void Dispatch(TimelineEvent evt)
        {
            var actor = FindView(evt.ActorId);
            var target = FindView(evt.TargetId);

            switch (evt.Type)
            {
                case TacticsGame.Core.EventType.MoveStarted:
                    if (actor != null) { var m = (MoveInfo)evt.Payload; actor.BeginMotion(m.Path, m.Speed, evt.SimTime); }
                    break;
                case TacticsGame.Core.EventType.MoveEnded:
                    if (actor != null) actor.EndMotion(((MoveEndedInfo)evt.Payload).Position);
                    break;
                case TacticsGame.Core.EventType.SpeedChanged:
                    if (actor != null) actor.PlaySpeedChanged(((SpeedInfo)evt.Payload).ActionSpeed);
                    break;
                case TacticsGame.Core.EventType.Dash:
                    if (actor != null)
                    {
                        var d = (DashInfo)evt.Payload;
                        if (d.Teleport) actor.EndMotion(d.To);   // gone, and there
                        else if (d.Fall) actor.BeginFall(d.From, d.To, d.Duration, evt.SimTime);
                        else actor.BeginGlide(d.From, d.To, d.Duration, evt.SimTime);
                        if (!d.Forced) actor.PlayDodge();
                    }
                    break;
                case TacticsGame.Core.EventType.Jump:
                    if (actor != null) { var j = (JumpInfo)evt.Payload; actor.BeginJump(j.From, j.To, j.Duration, evt.SimTime); }
                    break;
                case TacticsGame.Core.EventType.Lift:
                    if (target != null) { var l = (LiftInfo)evt.Payload; target.BeginLift(l.From, l.To, l.RiseSeconds, evt.SimTime); }
                    break;
                case TacticsGame.Core.EventType.Carry:
                    if (target != null)
                    {
                        var c = (CarryInfo)evt.Payload;
                        if (c.Carrying && actor != null) target.BeginCarry(actor, c.Offset);
                        else target.EndCarry(c.Position);
                    }
                    break;
                case TacticsGame.Core.EventType.ActionStarted:
                    if (actor != null) { var a = (ActionStartedInfo)evt.Payload; actor.PlayWindup(a.ActionId, a.Windup, target); }
                    break;
                case TacticsGame.Core.EventType.ActionResolved:
                    if (actor != null) actor.PlayResolve(((ActionResolvedInfo)evt.Payload).ActionId);
                    break;
                case TacticsGame.Core.EventType.ActionInterrupted:
                    if (actor != null) actor.PlayInterrupted();
                    break;
                case TacticsGame.Core.EventType.Damage:
                    if (target != null) target.PlayHit(((DamageInfo)evt.Payload).Impact);
                    break;
                case TacticsGame.Core.EventType.Dodge:
                    if (target != null) target.PlayDodge();
                    break;
                case TacticsGame.Core.EventType.Stun:
                    if (target != null) target.PlayStun(((StunInfo)evt.Payload).Duration);
                    break;
                case TacticsGame.Core.EventType.Unravel:
                    if (target != null) target.PlayUnravel();
                    break;
                case TacticsGame.Core.EventType.Death:
                    if (target != null) target.PlayDeath();
                    break;
            }

            // StrainChanged, StatusApplied/Expired, DeployableSpawned/Removed and UnitReady have no unit-view hook:
            // listen on OnEventDispatched for strain bars, status icons, spawning/removing smoke or cover props, etc.
            if (OnEventDispatched != null) OnEventDispatched(evt);
        }
    }
}
