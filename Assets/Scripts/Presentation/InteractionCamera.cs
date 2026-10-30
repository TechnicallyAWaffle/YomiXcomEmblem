using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Fire Emblem / XCOM style cinematic camera for interactions between two units, built on Cinemachine:
    /// the CinemachineBrain on the main camera blends between the player-driven TacticalCam (TacticsCamera)
    /// and CinematicCam, which this script poses each frame: side-on for melee, over the shoulder for ranged.
    ///
    /// The simulation runs ahead of playback, so when an action's windup starts we can already look ahead in the
    /// timeline to see how it ends. Only actions whose result is already simulated get a shot (so it is never cut
    /// off by the order freeze), and a killing blow gets a short slow-motion beat. Heavy hits, unravels and deaths
    /// shake the camera through a CinemachineImpulseSource.
    /// Abilities flagged ActionDefinition.ExtraCinematic (Tracking Shot, Final Cut) are always filmed, whatever the mode,
    /// with slow motion around the impact; if their windup straddled an order pause, the shot cuts in at the impact.
    /// Moments (an action interrupted by a hit or stun, a unit downed, a hit of HighDamageThreshold or more) briefly slow
    /// time and frame the units involved, in every mode but Off.
    ///   C: cycle Key moments / Every attack / Off.
    /// </summary>
    public class InteractionCamera : MonoBehaviour
    {
        public enum Mode { KeyMoments, EveryAttack, Off }

        public PlaybackDirector Director;
        public CinemachineCamera TacticalCam;
        public CinemachineCamera CinematicCam;
        public TacticsCamera Tactics;
        public CinemachineImpulseSource Impulse;
        [Tooltip("Static geometry the camera shouldn't end up behind (same as BattleController.EnvironmentMask).")]
        public LayerMask EnvironmentMask = 1;

        [Header("Behavior")]
        public Mode CurrentMode = Mode.KeyMoments;
        public KeyCode CycleKey = KeyCode.C;
        [Tooltip("Real seconds between the end of one cinematic and the start of the next.")]
        public float MinGap = 2f;
        [Tooltip("Real seconds the shot holds after the action lands.")]
        public float PostHold = 0.6f;
        public float KillPostHold = 1.1f;
        [Tooltip("Playback speed multiplier for the killing blow's slow-motion beat (1 = none).")]
        [Range(0.05f, 1f)] public float KillSlowMo = 0.3f;
        public float KillSlowMoSeconds = 0.7f;   // real seconds

        [Header("Extra cinematic abilities (ActionDefinition.ExtraCinematic)")]
        [Range(0.05f, 1f)] public float ExtraSlowMo = 0.25f;
        public float ExtraSlowMoSeconds = 1.2f;   // real seconds
        [Tooltip("Sim seconds before the impact that the slow motion kicks in, so the blow itself plays slowed.")]
        public float ExtraLeadIn = 0.2f;
        public float ExtraPostHold = 1.3f;

        [Header("Moments: slow motion and a quick shot on interrupts, downs and big hits")]
        public bool MomentsEnabled = true;
        [Tooltip("A single hit dealing at least this much damage is a high-damage moment.")]
        public float HighDamageThreshold = 30f;
        [Range(0.05f, 1f)] public float MomentSlowMo = 0.35f;
        public float MomentSlowMoSeconds = 0.6f;   // real seconds
        public float DownedSlowMoSeconds = 0.9f;   // real seconds
        [Tooltip("Real seconds a moment's shot holds.")]
        public float MomentHold = 0.9f;

        [Header("Framing")]
        public float MeleeShotDistance = 4.5f;   // closer than this = side-on two-shot
        public float Damping = 6f;

        private const int ActivePriority = 30;
        private const int IdlePriority = 0;

        private readonly Dictionary<string, ActionDefinition> defs = new Dictionary<string, ActionDefinition>();
        private readonly Dictionary<string, KeyValuePair<string, float>> lastHitBy = new Dictionary<string, KeyValuePair<string, float>>();   // target -> (attacker, sim time)

        private bool active;
        private UnitView actor, target;
        private string actionId;
        private bool willKill;
        private bool extra;                 // filming an ExtraCinematic action
        private float impactSimTime = -1f;  // when its impact lands (sim time); -1 once its slow motion has started
        private float side = 1f;
        private float startReal, maxEndReal, endReal = float.MaxValue, lastEndReal = -100f;
        private float slowMoUntil = -1f, restoreSpeed = 1f;

        private void Awake()
        {
            // Shake and blends keep running while the world is frozen for orders.
            CinemachineImpulseManager.Instance.IgnoreTimeScale = true;
            if (CinematicCam != null) CinematicCam.Priority = IdlePriority;
        }

        private void OnEnable()
        {
            if (Director == null) return;
            Director.OnEventDispatched += OnEvent;
            Director.OnPlaybackReset += OnReset;
        }

        private void OnDisable()
        {
            if (Director == null) return;
            Director.OnEventDispatched -= OnEvent;
            Director.OnPlaybackReset -= OnReset;
            End();
        }

        private void OnReset()
        {
            End();
            defs.Clear();
            lastHitBy.Clear();
            if (Director.Timeline == null || Director.Timeline.InitialState == null) return;
            foreach (var u in Director.Timeline.InitialState.Units)
                foreach (var a in u.Actions)
                    for (var d = a; d != null && !defs.ContainsKey(d.Id); d = d.LinkedAction) defs[d.Id] = d;
        }

        private void OnEvent(TimelineEvent evt)
        {
            switch (evt.Type)
            {
                case TacticsGame.Core.EventType.ActionStarted:
                    TryStart(evt);
                    break;
                case TacticsGame.Core.EventType.ActionResolved:
                {
                    var id = ((ActionResolvedInfo)evt.Payload).ActionId;
                    bool filming = active && actor != null && evt.ActorId == actor.UnitId && id == actionId;
                    if (filming)
                    {
                        endReal = Time.unscaledTime + (extra ? ExtraPostHold : willKill ? KillPostHold : PostHold);
                        break;
                    }
                    // A signature move whose windup wasn't filmed (it straddled an order pause): cut in for the impact.
                    ActionDefinition def;
                    var a = Director.FindView(evt.ActorId);
                    var t = Director.FindView(evt.TargetId);
                    if (a == null || t == null || a == t || !defs.TryGetValue(id, out def) || !def.ExtraCinematic || (active && extra)) break;
                    StartShot(a, t, id, false, true, -1f, 0f);
                    BeginSlowMo(ExtraSlowMo, ExtraSlowMoSeconds);
                    endReal = Time.unscaledTime + ExtraPostHold;
                    break;
                }
                case TacticsGame.Core.EventType.ActionInterrupted:
                {
                    if (active && actor != null && evt.ActorId == actor.UnitId) endReal = Mathf.Min(endReal, Time.unscaledTime + 0.4f);
                    // Broken by a hit, a stun or losing the shot: a moment. Dodges, holds and failed follow-ups are not.
                    var reason = ((InterruptInfo)evt.Payload).Reason;
                    if (reason != "damage" && reason != "heavy impact" && reason != "stun" && reason != "condition lost") break;
                    KeyValuePair<string, float> by;
                    string source = lastHitBy.TryGetValue(evt.ActorId, out by) && evt.SimTime - by.Value < 0.01f ? by.Key : null;
                    Moment(Director.FindView(source), Director.FindView(evt.ActorId), MomentSlowMoSeconds);
                    break;
                }
                case TacticsGame.Core.EventType.Damage:
                {
                    var d = (DamageInfo)evt.Payload;
                    if (evt.TargetId != null && evt.ActorId != null) lastHitBy[evt.TargetId] = new KeyValuePair<string, float>(evt.ActorId, evt.SimTime);
                    if (d.Impact == ImpactType.Heavy) Shake(0.35f);
                    if (active && target != null && evt.TargetId == target.UnitId && d.RemainingHealth <= 0f) BeginSlowMo(KillSlowMo, KillSlowMoSeconds);
                    if (d.Amount >= HighDamageThreshold) Moment(Director.FindView(evt.ActorId), Director.FindView(evt.TargetId), MomentSlowMoSeconds);
                    break;
                }
                case TacticsGame.Core.EventType.Stun:
                    if (evt.TargetId != null && evt.ActorId != null) lastHitBy[evt.TargetId] = new KeyValuePair<string, float>(evt.ActorId, evt.SimTime);
                    break;
                case TacticsGame.Core.EventType.Unravel: Shake(0.5f); break;
                case TacticsGame.Core.EventType.Death:
                    Shake(0.4f);
                    Moment(Director.FindView(evt.ActorId), Director.FindView(evt.TargetId), DownedSlowMoSeconds);
                    break;
            }
        }

        private void TryStart(TimelineEvent evt)
        {
            if (CinematicCam == null) return;
            var a = Director.FindView(evt.ActorId);
            var t = Director.FindView(evt.TargetId);
            var info = (ActionStartedInfo)evt.Payload;
            ActionDefinition def;
            if (a == null || t == null || a == t || !defs.TryGetValue(info.ActionId, out def)) return;

            // Signature moves (ExtraCinematic) ignore the mode and the gap, and take over a regular shot in progress.
            bool isExtra = def.ExtraCinematic;
            if (!isExtra && (active || CurrentMode == Mode.Off || Time.unscaledTime - lastEndReal < MinGap)) return;
            if (isExtra && active && extra && actor != a) return;   // never cut away from another signature move

            bool resolved, kills;
            float resolvedAt;
            if (!LookAhead(evt, info.ActionId, out resolved, out kills, out resolvedAt) || !resolved) return;   // outcome not simulated yet
            if (!isExtra && CurrentMode == Mode.KeyMoments &&
                !(kills || def.Impact == ImpactType.Heavy || def.Windup >= 1f || def.EnergyCost > 0f || def.Cooldown > 0f)) return;

            // Same actor chaining its signature move (Final Cut's follow-up): keep the shot and swing to the new target.
            bool chain = active && extra && actor == a;
            StartShot(a, t, info.ActionId, kills, isExtra, resolvedAt, info.Windup, !chain);
        }

        /// <summary>
        /// Interrupt, down or big hit: slow time briefly and frame 'subject' (with 'source', the unit that caused it, if
        /// known). A shot already on either unit just holds longer; a signature move in progress is never cut away from.
        /// </summary>
        private void Moment(UnitView source, UnitView subject, float slowSeconds)
        {
            if (!MomentsEnabled || CurrentMode == Mode.Off || CinematicCam == null || subject == null) return;
            if (source == subject) source = null;
            bool involved = active && (actor == subject || target == subject || (source != null && (actor == source || target == source)));
            if (active && extra && !involved) return;
            BeginSlowMo(MomentSlowMo, slowSeconds);
            if (involved)
            {
                if (endReal < float.MaxValue) endReal = Mathf.Max(endReal, Time.unscaledTime + MomentHold);
                return;
            }
            StartShot(source ?? subject, source != null ? subject : null, null, false, false, -1f, 0f);
            endReal = Time.unscaledTime + MomentHold;
        }

        private void StartShot(UnitView a, UnitView t, string id, bool kills, bool isExtra, float resolvedAt, float windup, bool snap = true)
        {
            active = true;
            actor = a; target = t; actionId = id; willKill = kills;
            extra = isExtra;
            impactSimTime = isExtra ? resolvedAt : -1f;
            startReal = Time.unscaledTime;
            endReal = float.MaxValue;
            maxEndReal = startReal + windup / Mathf.Max(0.05f, Director.playbackSpeed) + 4f + (isExtra ? ExtraSlowMoSeconds : 0f);
            if (!snap) return;

            // Shoot from the same side the player is already looking from, so the cut isn't disorienting.
            Vector3 other = t != null ? t.transform.position : a.transform.position + TacticalForward();
            Vector3 mid = (a.transform.position + other) / 2f;
            Vector3 dir = Flat(other - a.transform.position).normalized;
            Vector3 perp = Vector3.Cross(Vector3.up, dir);
            Vector3 viewer = TacticalCam != null ? TacticalCam.transform.position : mid - dir;
            side = Vector3.Dot(perp, viewer - mid) >= 0f ? 1f : -1f;

            Vector3 pos, look;
            Frame(out pos, out look);
            CinematicCam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            CinematicCam.Priority = ActivePriority;
            if (Tactics != null) Tactics.InputEnabled = false;
        }

        /// <summary>Scans the already-simulated timeline for how this action ends. False if the event can't be located.</summary>
        private bool LookAhead(TimelineEvent start, string id, out bool resolved, out bool kills, out float resolvedAt)
        {
            resolved = kills = false;
            resolvedAt = -1f;
            var events = Director.Timeline.Events;
            int i = start.Sequence;
            if (i < 0 || i >= events.Count || events[i].Sequence != start.Sequence) return false;   // sorted timeline: give up
            for (i++; i < events.Count; i++)
            {
                var e = events[i];
                if (resolvedAt >= 0f)
                {
                    if (e.SimTime > resolvedAt + 0.01f) break;
                    if (e.Type == TacticsGame.Core.EventType.Death && e.TargetId == start.TargetId) kills = true;
                    continue;
                }
                if (e.ActorId != start.ActorId) continue;
                if (e.Type == TacticsGame.Core.EventType.ActionInterrupted) return true;
                if (e.Type == TacticsGame.Core.EventType.ActionResolved && ((ActionResolvedInfo)e.Payload).ActionId == id)
                {
                    resolved = true;
                    resolvedAt = e.SimTime;
                }
            }
            return true;
        }

        private Vector3 TacticalForward()
        {
            Vector3 f = TacticalCam != null ? Flat(TacticalCam.transform.forward) : Vector3.forward;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        private void Frame(out Vector3 pos, out Vector3 look)
        {
            Vector3 a = actor.transform.position;
            if (target == null)
            {
                // A lone subject (an interrupt with no known source): close-up from the player's current viewing direction.
                Vector3 fwd = TacticalForward();
                look = a + Vector3.up * 1.1f;
                pos = look - fwd * 5f + Vector3.Cross(Vector3.up, fwd) * side * 1.2f + Vector3.up * 2.2f;
                RaycastHit clip;
                if (Physics.Linecast(look, pos, out clip, EnvironmentMask, QueryTriggerInteraction.Ignore)) pos = clip.point + clip.normal * 0.3f;
                return;
            }
            Vector3 b = target.transform.position;
            Vector3 d = Flat(b - a);
            float dist = d.magnitude;
            Vector3 dir = dist > 1e-3f ? d / dist : actor.transform.forward;
            Vector3 perp = Vector3.Cross(Vector3.up, dir) * side;

            if (dist < MeleeShotDistance)
            {
                // Side-on two-shot, both fighters in profile.
                Vector3 mid = (a + b) / 2f;
                pos = mid + perp * (3f + dist * 0.7f) + Vector3.up * 1.7f - dir * 0.5f;
                look = mid + Vector3.up * 1.1f;
            }
            else
            {
                // Over the shoulder, looking down the line of fire.
                pos = a - dir * 2.6f + perp * 1.2f + Vector3.up * 2.1f;
                look = Vector3.Lerp(a, b, 0.6f) + Vector3.up * 1f;
            }

            // Don't end up inside or behind cover.
            RaycastHit hit;
            if (Physics.Linecast(look, pos, out hit, EnvironmentMask, QueryTriggerInteraction.Ignore))
                pos = hit.point + hit.normal * 0.3f;
        }

        private void LateUpdate()
        {
            if (Input.GetKeyDown(CycleKey))
            {
                CurrentMode = (Mode)(((int)CurrentMode + 1) % 3);
                if (CurrentMode == Mode.Off) End();
            }

            if (slowMoUntil > 0f && Time.unscaledTime >= slowMoUntil) EndSlowMo();
            if (!active) return;

            if (extra && impactSimTime >= 0f && Director.PlaybackClock >= impactSimTime - ExtraLeadIn)
            {
                impactSimTime = -1f;
                BeginSlowMo(ExtraSlowMo, ExtraSlowMoSeconds);
            }

            // Hand control back the moment the battle freezes for orders, or when the shot is done.
            if (actor == null || Director.ReachedTarget || Time.unscaledTime >= endReal || Time.unscaledTime >= maxEndReal)
            {
                End();
                return;
            }

            Vector3 pos, look;
            Frame(out pos, out look);
            float k = 1f - Mathf.Exp(-Damping * Time.unscaledDeltaTime);
            var t = CinematicCam.transform;
            t.position = Vector3.Lerp(t.position, pos, k);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(look - t.position), k);
        }

        private void End()
        {
            if (active) lastEndReal = Time.unscaledTime;
            active = false;
            extra = false;
            impactSimTime = -1f;
            actor = target = null;
            if (CinematicCam != null) CinematicCam.Priority = IdlePriority;
            if (Tactics != null) Tactics.InputEnabled = true;
            EndSlowMo();
        }

        private void BeginSlowMo(float factor, float seconds)
        {
            if (factor >= 0.999f) return;
            if (slowMoUntil > 0f) { slowMoUntil = Mathf.Max(slowMoUntil, Time.unscaledTime + seconds); return; }   // already slowed: just last longer
            restoreSpeed = Director.playbackSpeed;
            Director.SetPlaybackSpeed(restoreSpeed * factor);
            slowMoUntil = Time.unscaledTime + seconds;
        }

        private void EndSlowMo()
        {
            if (slowMoUntil < 0f) return;
            slowMoUntil = -1f;
            if (Director != null) Director.SetPlaybackSpeed(restoreSpeed);
        }

        private void Shake(float force)
        {
            if (Impulse != null) Impulse.GenerateImpulseWithForce(force);
        }

        private void OnGUI()
        {
            UiScale.Begin();
            string label = CurrentMode == Mode.KeyMoments ? "key moments" : CurrentMode == Mode.EveryAttack ? "every attack" : "off, signature moves only";
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontSize = 13 };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(UiScale.Width - 360, UiScale.Height - 26, 350, 22), CycleKey + ": cinematic camera (" + label + ")", style);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
