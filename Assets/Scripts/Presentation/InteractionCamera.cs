using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// The replay camera (PlaybackDirector.IsReplay), built on Cinemachine: the CinemachineBrain on the main camera blends
    /// between the player-driven TacticalCam (TacticsCamera) and CinematicCam, which this script poses each frame. Live
    /// play between orders is left alone: the camera is the player's, with no shots, slow motion or shake.
    ///
    /// Two modes (C toggles them during a replay):
    ///   Cinematic: an observer. Most of the replay plays at full speed in an overhead view that pans and zooms to keep every
    ///     living unit in frame. Its important moments (Beats) are known in advance, the whole timeline being simulated:
    ///     major ones (killing blows, deaths, Unravels, signature moves) and minor ones (heavy and big hits, interrupts,
    ///     falls, summons). Just before one (LeadInSeconds) the camera zooms in on it, also keeping in frame what else is
    ///     going on near it and what is about to (beats due within ContextSeconds), as far as it can while still close.
    ///     Beats that land together (within ClusterWindow) are one moment. A major moment slows the replay right down, more
    ///     for each further major beat in it (SpeedFor); a minor one barely (MinorSpeed), and only if MinGap has passed
    ///     since the last. Either way the zoom and the slow motion last MomentSeconds (0.6s) of real time, shared between
    ///     the places it happens in, each held at least MinHoldSeconds (0.5s); both ease in and back out over EaseSeconds. Heavy hits, Unravels and deaths shake it.
    ///   None: no cinematic effects at all: the camera stays the player's, no slow motion, no shake.
    /// </summary>
    public class InteractionCamera : MonoBehaviour
    {
        public enum Mode { Cinematic, None }

        public PlaybackDirector Director;
        public CinemachineCamera TacticalCam;
        public CinemachineCamera CinematicCam;
        public TacticsCamera Tactics;
        public CinemachineImpulseSource Impulse;
        [Tooltip("Static geometry the camera shouldn't end up behind (same as BattleController.EnvironmentMask).")]
        public LayerMask EnvironmentMask = 1;

        [Header("Behavior")]
        public Mode CurrentMode = Mode.Cinematic;
        public KeyCode CycleKey = KeyCode.C;

        [Header("Moments")]
        [Tooltip("A single hit dealing at least this much damage is a moment (as are heavy hits and killing blows).")]
        public float HighDamageThreshold = 30f;
        [Tooltip("Sim seconds: beats this close together land together (one moment).")]
        public float ClusterWindow = 0.35f;
        [Tooltip("Real seconds: a moment (its zoom and its slow motion) lasts at most this long, then the replay is back to the overview at full speed.")]
        public float MomentSeconds = 0.6f;
        [Tooltip("Real seconds each place a moment happens in is held at least (several places: the moment runs longer).")]
        public float MinHoldSeconds = 0.5f;
        [Tooltip("Real seconds the zoom and the slow motion take to ease in, and to ease back out.")]
        public float EaseSeconds = 0.2f;
        [Tooltip("Real seconds into a moment that its first beat lands (the zoom gets there just before).")]
        public float LeadInSeconds = 0.2f;
        [Tooltip("Real seconds after a moment before a minor one is filmed again (major ones always are).")]
        public float MinGap = 1.5f;
        [Tooltip("Beats whose units are this close together are filmed as one place.")]
        public float MergeDistance = 6f;
        [Header("Slow motion")]
        [Tooltip("Playback speed through a major moment of one beat (a killing blow, an Unravel, a signature move).")]
        [Range(0.05f, 1f)] public float SlowMo = 0.3f;
        [Tooltip("Each further beat in the same major moment slows it this much more: speed = SlowMo / (1 + this x (beats - 1)).")]
        public float SlowerPerBeat = 0.6f;
        [Range(0.02f, 1f)] public float MinSpeed = 0.08f;
        [Tooltip("Playback speed through a minor moment (a heavy or big hit, an interrupt, a fall, a summon): barely slowed.")]
        [Range(0.05f, 1f)] public float MinorSpeed = 0.85f;
        [Header("Context")]
        [Tooltip("Sim seconds ahead: beats due this soon near the moment are kept in frame too.")]
        public float ContextSeconds = 1.5f;
        [Tooltip("Metres: only what is this near the moment is pulled into its frame.")]
        public float ContextReach = 14f;
        [Tooltip("Metres: a close-up frames at most this radius (context that would need more is left out).")]
        public float CloseUpMaxRadius = 8f;

        [Header("Framing")]
        public float OverviewPitch = 62f;
        public float CloseUpPitch = 50f;
        [Tooltip("Metres of margin around what is framed.")]
        public float Padding = 2.5f;
        public float MinCloseRadius = 3f, MinOverviewRadius = 7f;
        public float MinDistance = 6f, MaxDistance = 80f;
        [Tooltip("Seconds the camera takes to settle (SmoothDamp easing): close-ups, and the overview.")]
        public float CloseSmooth = 0.12f, OverviewSmooth = 0.7f;

        private const int ActivePriority = 30;
        private const int IdlePriority = 0;

        private class Beat { public float Time; public bool Major; public readonly List<string> Ids = new List<string>(); }
        private class Moment
        {
            public float Start, End; public readonly List<Beat> Beats = new List<Beat>();
            public bool Major { get { return Beats.Exists(b => b.Major); } }
            public int MajorCount { get { return Beats.FindAll(b => b.Major).Count; } }
        }

        private readonly Dictionary<string, ActionDefinition> defs = new Dictionary<string, ActionDefinition>();
        private readonly List<Moment> moments = new List<Moment>();
        private readonly List<Beat> beats = new List<Beat>();
        private bool built;
        private int nextMoment;

        // The moment being filmed: its places (unit ids per place), which one the camera is on, since when (real time).
        private Moment current;
        private readonly List<List<string>> places = new List<List<string>>();
        private int placeIndex;
        private float placeSince, momentSince, lastMomentEnd = -100f;

        private bool driving;            // CinematicCam has the view
        private float yaw;               // the replay's viewing direction, from the tactical camera as it started
        private Vector3 lookPoint, posVelocity, lookVelocity;
        private float baseSpeed = 1f;
        private bool slowed;
        private float slowTarget = 1f;   // the speed factor the current (or last) moment eases to
        private float zoom;              // 0 = overview, 1 = the moment's close-up: eased in and out over EaseSeconds
        private Vector3 closeCentre; private float closeRadius = 1f;   // the last close-up framing (held while easing out)

        private void Awake()
        {
            // Shake and blends keep running while the world is frozen.
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
            Release();
        }

        private bool Filming { get { return Director != null && Director.IsReplay && CurrentMode == Mode.Cinematic && CinematicCam != null; } }

        private void OnReset()
        {
            EndMoment();
            built = false;   // rebuilt on the next frame, once it is known whether this is a replay
            defs.Clear();
            if (Director.Timeline == null || Director.Timeline.InitialState == null) return;
            foreach (var u in Director.Timeline.InitialState.Units)
                foreach (var a in u.Actions)
                    for (var d = a; d != null && !defs.ContainsKey(d.Id); d = d.LinkedAction) defs[d.Id] = d;
        }

        private void OnEvent(TimelineEvent evt)
        {
            if (!Filming) return;
            switch (evt.Type)
            {
                case TacticsGame.Core.EventType.Damage:
                    if (((DamageInfo)evt.Payload).Impact == ImpactType.Heavy) Shake(0.35f);
                    break;
                case TacticsGame.Core.EventType.Unravel: Shake(0.5f); break;
                case TacticsGame.Core.EventType.Death: Shake(0.4f); break;
                case TacticsGame.Core.EventType.UnitSpawned:
                {
                    var s = (SpawnInfo)evt.Payload;   // a summon's abilities, for its signature moves
                    if (s.Unit != null) foreach (var a in s.Unit.Actions) for (var d = a; d != null && !defs.ContainsKey(d.Id); d = d.LinkedAction) defs[d.Id] = d;
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ the replay's beats, known in advance

        /// <summary>Every important moment of the timeline, in order, grouped into moments (beats that land together).</summary>
        private void BuildBeats()
        {
            built = true;
            beats.Clear(); moments.Clear(); nextMoment = 0;
            var tl = Director.Timeline;
            if (tl == null) return;
            foreach (var e in tl.Events)
            {
                if (e.SimTime <= 0.01f) continue;   // the battle's opening (everyone arriving) is no moment
                switch (e.Type)
                {
                    case TacticsGame.Core.EventType.Damage:
                    {
                        var d = (DamageInfo)e.Payload;
                        if (d.Impact == ImpactType.Heavy || d.Amount >= HighDamageThreshold || d.RemainingHealth <= 0f) AddBeat(e, e.ActorId, e.TargetId, d.RemainingHealth <= 0f);   // a killing blow is major
                        break;
                    }
                    case TacticsGame.Core.EventType.Death:
                    case TacticsGame.Core.EventType.Unravel:
                        AddBeat(e, e.ActorId, e.TargetId, true);
                        break;
                    case TacticsGame.Core.EventType.Fall:
                        AddBeat(e, e.ActorId, e.TargetId, false);
                        break;
                    case TacticsGame.Core.EventType.ActionInterrupted:
                    {
                        var reason = ((InterruptInfo)e.Payload).Reason;
                        if (reason == "damage" || reason == "heavy impact" || reason == "stun" || reason == "condition lost" || reason == "stutter" || reason == "charge broken")
                            AddBeat(e, e.ActorId, null, false);
                        break;
                    }
                    case TacticsGame.Core.EventType.ActionResolved:
                    {
                        ActionDefinition def;
                        if (defs.TryGetValue(((ActionResolvedInfo)e.Payload).ActionId, out def) && def.ExtraCinematic) AddBeat(e, e.ActorId, e.TargetId, true);
                        break;
                    }
                    case TacticsGame.Core.EventType.UnitSpawned:
                    {
                        var s = (SpawnInfo)e.Payload;
                        if (s.Unit != null) foreach (var a in s.Unit.Actions) for (var d = a; d != null && !defs.ContainsKey(d.Id); d = d.LinkedAction) defs[d.Id] = d;
                        AddBeat(e, e.TargetId, e.ActorId, false);
                        break;
                    }
                }
            }
            foreach (var b in beats)
            {
                var last = moments.Count > 0 ? moments[moments.Count - 1] : null;
                if (last != null && b.Time - last.Start <= ClusterWindow) { last.Beats.Add(b); last.End = Mathf.Max(last.End, b.Time); continue; }
                var m = new Moment { Start = b.Time, End = b.Time };
                m.Beats.Add(b);
                moments.Add(m);
            }
            // Start from where playback is (a scrub lands mid-replay).
            while (nextMoment < moments.Count && moments[nextMoment].End < Director.PlaybackClock) nextMoment++;
        }

        /// <param name="major">A killing blow, a death, an Unravel or a signature move: it slows the replay right down. The
        /// rest (heavy and big hits, interrupts, falls, summons) are minor: a quick zoom, barely slowed.</param>
        private void AddBeat(TimelineEvent e, string a, string b, bool major)
        {
            var beat = new Beat { Time = e.SimTime, Major = major };
            if (!string.IsNullOrEmpty(a)) beat.Ids.Add(a);
            if (!string.IsNullOrEmpty(b) && b != a) beat.Ids.Add(b);
            if (beat.Ids.Count > 0) beats.Add(beat);
        }

        // ------------------------------------------------------------------ moments: zoom in, slow down, visit each place

        private void BeginMoment(Moment m)
        {
            current = m;
            places.Clear();
            var centres = new List<Vector3>();
            foreach (var b in m.Beats)
            {
                Vector3 c;
                if (!Centre(b.Ids, out c)) continue;
                int at = centres.FindIndex(x => Flat(x - c).magnitude <= MergeDistance);
                if (at < 0) { centres.Add(c); places.Add(new List<string>(b.Ids)); continue; }
                foreach (var id in b.Ids) if (!places[at].Contains(id)) places[at].Add(id);
            }
            if (places.Count == 0) { current = null; return; }
            placeIndex = 0;
            placeSince = momentSince = Time.unscaledTime;
            float f = SpeedFor(m);
            if (f < 0.999f) SlowTo(f);
            Vector3 fc; float fr;
            if (FrameMoment(out fc, out fr)) { closeCentre = fc; closeRadius = fr; }
        }

        /// <summary>
        /// The playback speed factor through moment 'm': a major one is slowed right down, more for each further major beat
        /// in it; a minor one barely (MinorSpeed).
        /// </summary>
        private float SpeedFor(Moment m)
        {
            if (!m.Major) return MinorSpeed;
            return Mathf.Max(MinSpeed, SlowMo / (1f + SlowerPerBeat * Mathf.Max(0, m.MajorCount - 1)));
        }

        private void TickMoment()
        {
            float clock = Director.PlaybackClock;
            if (current == null)
            {
                while (nextMoment < moments.Count && moments[nextMoment].End < clock) nextMoment++;   // gone by unfilmed
                if (nextMoment >= moments.Count) return;
                var m = moments[nextMoment];
                // Begin so its first beat lands LeadInSeconds of real time in, at the speed it will play at.
                if (clock < m.Start - Director.playbackSpeed * SpeedFor(m) * LeadInSeconds) return;
                nextMoment++;
                if (!m.Major && Time.unscaledTime - lastMomentEnd < MinGap) return;   // most of the replay plays as it is
                BeginMoment(m);
                return;
            }
            // MomentSeconds of real time shared out between its places, each held at least MinHoldSeconds; then back to the
            // overview at full speed (easing out).
            float dwell = Mathf.Max(MinHoldSeconds, MomentSeconds / Mathf.Max(1, places.Count));
            if (placeIndex < places.Count - 1 && Time.unscaledTime - placeSince >= dwell) { placeIndex++; placeSince = Time.unscaledTime; }
            if (Time.unscaledTime - momentSince >= dwell * places.Count || Director.ReachedTarget) EndMoment();
        }

        private void EndMoment()
        {
            if (current != null) lastMomentEnd = Time.unscaledTime;
            current = null;
            places.Clear();   // the slow motion eases back out with the zoom (LateUpdate)
        }

        /// <summary>A moment slows the replay to 'factor' of its speed, eased in with the zoom (ApplySpeed).</summary>
        private void SlowTo(float factor)
        {
            if (!slowed) { baseSpeed = Director.playbackSpeed; slowed = true; }
            slowTarget = factor;
        }

        /// <summary>The playback speed for how far the zoom is in: full speed at the overview, the moment's slow motion at
        /// the close-up, eased between. Back at the overview, the replay's own speed is restored exactly.</summary>
        private void ApplySpeed(float eased)
        {
            if (!slowed) return;
            if (current == null && zoom <= 0f) { Unslow(); return; }
            Director.SetPlaybackSpeed(baseSpeed * Mathf.Lerp(1f, slowTarget, eased));
        }

        private void Unslow()
        {
            if (!slowed) return;
            slowed = false;
            if (Director != null) Director.SetPlaybackSpeed(baseSpeed);
        }

        // ------------------------------------------------------------------ framing

        private void LateUpdate()
        {
            if (Director != null && Director.IsReplay && Input.GetKeyDown(CycleKey))
                CurrentMode = CurrentMode == Mode.Cinematic ? Mode.None : Mode.Cinematic;

            if (!Filming) { Release(); return; }
            if (!built) BuildBeats();
            if (!driving) TakeOver();
            TickMoment();

            // The zoom eases in toward a moment and back out after it; the slow motion and the framing follow it.
            float dt = Time.unscaledDeltaTime;
            zoom = Mathf.MoveTowards(zoom, current != null ? 1f : 0f, dt / Mathf.Max(0.01f, EaseSeconds));
            float e = Mathf.SmoothStep(0f, 1f, zoom);
            ApplySpeed(e);
            Vector3 c; float r;
            if (current != null && FrameMoment(out c, out r)) { closeCentre = c; closeRadius = r; }
            Vector3 centre; float radius;
            if (!FrameEveryone(out centre, out radius)) return;
            centre = Vector3.Lerp(centre, closeCentre, e);
            radius = Mathf.Lerp(radius, closeRadius, e);
            float pitch = Mathf.Lerp(OverviewPitch, CloseUpPitch, e), smooth = Mathf.Lerp(OverviewSmooth, CloseSmooth, e);
            Vector3 pos, look;
            Pose(centre, radius, pitch, out pos, out look);
            var t = CinematicCam.transform;
            t.position = Vector3.SmoothDamp(t.position, pos, ref posVelocity, smooth, Mathf.Infinity, dt);
            lookPoint = Vector3.SmoothDamp(lookPoint, look, ref lookVelocity, smooth * 0.8f, Mathf.Infinity, dt);
            if ((lookPoint - t.position).sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(lookPoint - t.position);
        }

        /// <summary>
        /// The moment's current place, plus what else is going on near it: the moment's other places and the beats due
        /// within ContextSeconds, each pulled in only while the frame stays within CloseUpMaxRadius (nearest first).
        /// </summary>
        private bool FrameMoment(out Vector3 centre, out float radius)
        {
            var pts = Positions(places[placeIndex]);
            if (pts.Count == 0) { centre = Vector3.zero; radius = 0f; return false; }
            Fit(pts, MinCloseRadius, out centre, out radius);
            var extra = new List<Vector3>();
            for (int i = 0; i < places.Count; i++) if (i != placeIndex) extra.AddRange(Positions(places[i]));
            float clock = Director.PlaybackClock;
            for (int i = nextMoment; i < moments.Count && moments[i].Start <= clock + ContextSeconds; i++)
                foreach (var b in moments[i].Beats) extra.AddRange(Positions(b.Ids));
            Vector3 c0 = centre;
            extra.RemoveAll(p => Flat(p - c0).magnitude > ContextReach);
            extra.Sort((a, b) => Flat(a - c0).sqrMagnitude.CompareTo(Flat(b - c0).sqrMagnitude));
            foreach (var p in extra)
            {
                pts.Add(p);
                Vector3 c; float r;
                Fit(pts, MinCloseRadius, out c, out r);
                if (r > CloseUpMaxRadius) { pts.RemoveAt(pts.Count - 1); continue; }
                centre = c; radius = r;
            }
            return true;
        }

        /// <summary>The overview: every living unit.</summary>
        private bool FrameEveryone(out Vector3 centre, out float radius)
        {
            var pts = new List<Vector3>();
            var views = Director.Views;
            if (views != null) foreach (var v in views) if (v != null && v.gameObject.activeInHierarchy && !v.IsDead) pts.Add(v.transform.position);
            if (pts.Count == 0) { centre = lookPoint; radius = MinOverviewRadius; return driving; }
            Fit(pts, MinOverviewRadius, out centre, out radius);
            return true;
        }

        private List<Vector3> Positions(List<string> ids)
        {
            var list = new List<Vector3>();
            foreach (var id in ids)
            {
                var v = Director.FindView(id);
                if (v != null && v.gameObject.activeInHierarchy) list.Add(v.transform.position);
            }
            return list;
        }

        private bool Centre(List<string> ids, out Vector3 c)
        {
            var pts = Positions(ids);
            c = Vector3.zero;
            if (pts.Count == 0) return false;
            foreach (var p in pts) c += p;
            c /= pts.Count;
            return true;
        }

        /// <summary>The box around 'pts' on the ground: its centre, and a radius taking it all in plus Padding (at least 'min').</summary>
        private void Fit(List<Vector3> pts, float min, out Vector3 centre, out float radius)
        {
            Vector3 lo = pts[0], hi = pts[0];
            foreach (var p in pts) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            centre = (lo + hi) / 2f;
            centre.y = lo.y;
            radius = Mathf.Max(min, Flat(hi - lo).magnitude / 2f + Padding);
        }

        /// <summary>Where the camera goes to take in a circle of 'radius' at 'centre', looking down at 'pitch' along the replay's yaw.</summary>
        private void Pose(Vector3 centre, float radius, float pitch, out Vector3 pos, out Vector3 look)
        {
            float vfov = CinematicCam.Lens.FieldOfView * Mathf.Deg2Rad;
            float aspect = Camera.main != null ? Camera.main.aspect : 16f / 9f;
            float hfov = 2f * Mathf.Atan(Mathf.Tan(vfov / 2f) * aspect);
            float half = Mathf.Min(vfov, hfov) / 2f;
            float dist = Mathf.Clamp(radius / Mathf.Max(0.05f, Mathf.Sin(half)), MinDistance, MaxDistance);
            Vector3 dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            look = centre + Vector3.up * 0.8f;
            pos = look - dir * dist;
            RaycastHit hit;   // never behind a wall or a cliff
            if (Physics.Linecast(look, pos, out hit, EnvironmentMask, QueryTriggerInteraction.Ignore)) pos = hit.point + hit.normal * 0.3f;
        }

        /// <summary>The replay starts being filmed: the cinematic camera takes over from where the player's one is.</summary>
        private void TakeOver()
        {
            driving = true;
            Transform from = TacticalCam != null ? TacticalCam.transform : CinematicCam.transform;
            yaw = from.eulerAngles.y;
            CinematicCam.transform.SetPositionAndRotation(from.position, from.rotation);
            lookPoint = from.position + from.forward * 10f;
            posVelocity = lookVelocity = Vector3.zero;
            CinematicCam.Priority = ActivePriority;
            if (Tactics != null) Tactics.InputEnabled = false;
        }

        /// <summary>Hand the view back to the player (live play, mode None, or the replay over).</summary>
        private void Release()
        {
            if (current != null) EndMoment();
            Unslow();   // at once: no easing out of a replay that is no longer filmed
            zoom = 0f;
            if (!driving) return;
            driving = false;
            if (CinematicCam != null) CinematicCam.Priority = IdlePriority;
            if (Tactics != null) Tactics.InputEnabled = true;
        }

        private void Shake(float force)
        {
            if (Impulse != null) Impulse.GenerateImpulseWithForce(force);
        }

        private void OnGUI()
        {
            if (Director == null || !Director.IsReplay || UiDeclutter.On) return;
            UiScale.Begin();
            string label = CurrentMode == Mode.Cinematic ? "cinematic" : "none, no cinematic effects";
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontSize = 13 };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(UiScale.Width - 360, UiScale.Height - 26 - PlayerOrdersUI.BottomReserved, 350, 22), CycleKey + ": replay camera (" + label + ")", style);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
