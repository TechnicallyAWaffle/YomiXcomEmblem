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

        [Tooltip("Easing into a stop (PlayUntil with ease seconds: a dodge): the slowest the clock gets, as a fraction of playbackSpeed, so it still arrives.")]
        [Range(0.01f, 0.5f)]
        public float easeFloor = 0.08f;

        public BattleTimeline Timeline { get; private set; }
        public float PlaybackClock { get { return playbackClock; } }
        public bool ReachedTarget { get; private set; }
        /// <summary>
        /// Freezes playback where it is (a tutorial popup being read): the clock stops and the engine timescale goes to
        /// holdTimescale; clearing it carries on at playbackSpeed.
        /// </summary>
        public bool Paused
        {
            get { return paused; }
            set
            {
                if (paused == value) return;
                paused = value;
                if (!syncEngineTimescale) return;
                if (paused) Time.timeScale = holdTimescale;
                else if (!ReachedTarget) ApplyTimescale();
            }
        }
        private bool paused;

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
            if (IsReplay) { UiDeclutter.Set(false); Paused = false; }   // out of a replay: the UI it hid comes back, and a pause there ends
            IsReplay = false;
            unitRegistry = registry;
            // Status looks set on this battle's abilities (StatusSpec.Look) replace the built-in ones.
            var actions = new List<ActionDefinition>();
            if (timeline != null && timeline.InitialState != null) foreach (var u in timeline.InitialState.Units) actions.AddRange(u.Actions);
            StatusDisplay.Register(actions);
            ResetPlayback();
        }

        /// <summary>
        /// Play at playbackSpeed up to the given simulation time, then hold. 'easeSeconds' above 0: over the last that many
        /// sim seconds the clock slows gradually (with the engine's animations) to a stop at the target, instead of stopping
        /// dead (a dodge: time slows to a halt just as the strike lands).
        /// </summary>
        public void PlayUntil(float simTime, float easeSeconds = 0f)
        {
            targetSimTime = Mathf.Max(targetSimTime, simTime);
            // A stretch shorter than the ease eases over all of it, from full speed, rather than starting half-slowed.
            this.easeSeconds = Mathf.Max(0f, Mathf.Min(easeSeconds, targetSimTime - playbackClock));
            ReachedTarget = false;
            ApplyTimescale();
        }

        private float easeSeconds;

        /// <summary>How far the clock has eased toward its stop: 0 (full speed, or not easing) to 1 (stopped).</summary>
        public float EaseProgress
        {
            get
            {
                if (easeSeconds <= 0f) return 0f;
                if (ReachedTarget) return 1f;
                return 1f - Mathf.Clamp01((targetSimTime - playbackClock) / easeSeconds);
            }
        }

        /// <summary>The clock's speed now: playbackSpeed, slowed in proportion to what is left while easing into a stop.</summary>
        private float CurrentSpeed()
        {
            if (easeSeconds <= 0f) return playbackSpeed;
            float left = targetSimTime - playbackClock;
            if (left >= easeSeconds) return playbackSpeed;
            return playbackSpeed * Mathf.Max(easeFloor, left / easeSeconds);
        }

        /// <summary>
        /// Time creeping on while orders are given (Deliverance's phase two): show the world up to 'simTime' now, with the
        /// engine's animations at 'engineScale' of normal speed, and stay held there.
        /// </summary>
        public void CreepTo(float simTime, float engineScale)
        {
            if (Timeline == null) return;
            targetSimTime = Mathf.Max(targetSimTime, simTime);
            playbackClock = Mathf.Max(playbackClock, targetSimTime);
            DispatchDueEvents();
            ReachedTarget = true;
            if (syncEngineTimescale && !paused)
            {
                Time.timeScale = Mathf.Max(holdTimescale, engineScale);
                Time.fixedDeltaTime = 0.02f * Mathf.Max(0.01f, Time.timeScale);
            }
        }

        /// <summary>Playing the finished battle back (PlayReplay, and scrubbing it), rather than playing it out live between orders.</summary>
        public bool IsReplay { get; private set; }

        /// <summary>Reset the scene to the opening state and play the whole battle back at real-time speed.</summary>
        public void PlayReplay(float speed = 1f)
        {
            playbackSpeed = speed;
            ReplaySpeed = Mathf.Clamp(speed, 0.02f, 4f);
            Paused = false;
            ResetPlayback();
            IsReplay = true;
            UiDeclutter.Set(true);   // a replay is for watching: the UI starts hidden (Alt brings it back)
            PlayUntil(Timeline.Duration);
        }

        /// <summary>Restore normal engine time, e.g. for the results screen after the last stretch has played.</summary>
        public void ReleaseTimescale()
        {
            if (!syncEngineTimescale) return;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        /// <summary>The speed the viewer picked for the replay (ReplayControls). A cinematic moment's slow motion is relative
        /// to it (InteractionCamera), so the clock may run slower than this for a while.</summary>
        public float ReplaySpeed { get; private set; } = 1f;

        /// <summary>The viewer picks a replay speed: the clock runs at it from now (a cinematic slows from it).</summary>
        public void SetReplaySpeed(float speed)
        {
            ReplaySpeed = Mathf.Clamp(speed, 0.02f, 4f);
            SetPlaybackSpeed(ReplaySpeed);
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
            if (paused) { Time.timeScale = holdTimescale; return; }   // frozen for a popup until Paused is cleared
            float speed = CurrentSpeed();   // eased while slowing into a stop, whoever sets the speed meanwhile (a cinematic's slow-mo)
            Time.timeScale = speed;
            Time.fixedDeltaTime = 0.02f * Mathf.Max(0.01f, speed);
        }

        private void ResetPlayback()
        {
            playbackClock = 0f;
            targetSimTime = 0f;
            easeSeconds = 0f;
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
            if (Timeline == null) return;
            if (paused || ReachedTarget) { DispatchDueEvents(); return; }   // held, but what happens at this instant (a unit placed) still shows

            // Custom clock decides WHAT happens WHEN, independent of Time.timeScale.
            float speed = CurrentSpeed();
            playbackClock = Mathf.Min(playbackClock + Time.unscaledDeltaTime * speed, targetSimTime);
            if (targetSimTime - playbackClock < 1e-3f) playbackClock = targetSimTime;   // easing: close enough is there
            DispatchDueEvents();
            if (easeSeconds > 0f && playbackClock < targetSimTime) ApplyTimescale();   // the animations slow with the clock

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

        /// <summary>Every unit view in this battle (null before Load).</summary>
        public IEnumerable<UnitView> Views { get { return unitRegistry != null ? unitRegistry.Values : null; } }

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
                    if (actor != null) { var m = (MoveInfo)evt.Payload; actor.BeginMotion(m.Path, m.Speed, evt.SimTime, m.Fly); }
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
                        // A dash of its own picks its forward / back / side clip from where the unit faces; back and side
                        // ones keep it facing that way.
                        bool ownDash = !d.Forced && !d.Step && !d.Teleport && !d.Fall;
                        bool keepFacing = ownDash && actor.PlayDash(d.From, d.To);
                        if (d.Teleport) actor.EndMotion(d.To);   // gone, and there
                        else if (d.Fall) actor.BeginFall(d.From, d.To, d.Duration, evt.SimTime);
                        else actor.BeginGlide(d.From, d.To, d.Duration, evt.SimTime, d.Arc, keepFacing);
                        if (!ownDash && !d.Forced && !d.Step) actor.PlayDodge();
                    }
                    break;
                case TacticsGame.Core.EventType.Jump:
                    if (actor != null) { var j = (JumpInfo)evt.Payload; actor.BeginJump(j.From, j.To, j.Duration, evt.SimTime); }
                    break;
                case TacticsGame.Core.EventType.Lift:
                    if (target != null) { var l = (LiftInfo)evt.Payload; target.BeginLift(l.From, l.To, l.RiseSeconds, evt.SimTime); }
                    break;
                case TacticsGame.Core.EventType.DropStarted:   // a reinforcement coming down from the sky
                    if (target != null) { var d = (DropInfo)evt.Payload; target.BeginLift(d.From, d.To, d.Seconds, evt.SimTime); }
                    break;
                case TacticsGame.Core.EventType.AerialSlam:
                {
                    var s = (AerialSlamInfo)evt.Payload;
                    if (actor != null) actor.BeginAerialSlam(s, true);
                    if (target != null) target.BeginAerialSlam(s, false);
                    break;
                }
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
                    if (target != null) target.PlayDeath(evt.Payload is DeathInfo && ((DeathInfo)evt.Payload).Team != Team.Player);
                    break;
            }

            // StrainChanged, StatusApplied/Expired, DeployableSpawned/Removed and UnitReady have no unit-view hook:
            // listen on OnEventDispatched for strain bars, status icons, spawning/removing smoke or cover props, etc.
            if (OnEventDispatched != null) OnEventDispatched(evt);
        }
    }
}
