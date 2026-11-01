using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Scene representation of a unit. Put this on the unit prefab you place in the Hierarchy.
    /// It never runs combat logic; it only knows how to present events for the Id it was given.
    /// Movement is computed from the playback clock (not accumulated per frame), so it stays
    /// correct while paused, scrubbed, or replayed at a different speed.
    ///
    /// Until there are animation clips, the Model child gets simple procedural poses, also driven by the playback
    /// clock: it leans back and squashes as a windup charges (trembling near the end), lunges forward when the action
    /// lands, flinches when hit (harder for Heavy), wobbles when interrupted and slumps while stunned. They switch off
    /// once the unit has an Animator with a controller.
    /// </summary>
    public class UnitView : MonoBehaviour
    {
        [Tooltip("Assigned at battle setup; matches Core.Unit.Id.")]
        public string UnitId;
        public Animator Animator;
        public Transform VfxAnchor;
        [Tooltip("The visual body the placeholder poses move. Found by name (\"Model\") if left empty.")]
        public Transform Model;
        [HideInInspector] public PlaybackDirector Director;

        // Scripted motion along a path, a pure function of playback time.
        private List<Vector3> motionPath;
        private float motionSpeed;
        private float motionStart;
        private float motionLength;
        // Timed two-point motions (dash/shove, jump, fall) run over motionSeconds instead of at a speed.
        private enum TimedKind { None, Glide, Jump, Fall, Lift, Slam }
        private TimedKind timedKind;
        // Aerial slam (Heretic's Ascent) in progress: the shared flight, and whether this unit carries or is carried.
        private AerialSlamInfo slamPath;
        private bool slamCarrier;
        private float motionSeconds;
        private Quaternion initialRotation;

        // Placeholder poses: start times on the playback clock (-1 = not playing).
        private const float LungeSeconds = 0.25f, FlinchSeconds = 0.2f, WobbleSeconds = 0.35f;
        private Vector3 modelPos, modelScale;
        private Quaternion modelRot;
        private float windupStart = -1f, windupEnd = -1f, lungeStart = -1f, flinchStart = -1f, wobbleStart = -1f, stunnedUntil = -1f;
        private float flinchStrength = 1f;
        // Carried by another unit (Subjugate): held in front of it until EndCarry.
        private UnitView carrier;
        private float carryOffset;
        private const float CarryHeight = 0.45f;
        private bool lungeOnResolve, dead;
        // Lunge shapes for strikes in a run, in order: lean and reach scales, and a sideways roll (degrees). The first is the plain one.
        private static readonly Vector3[] Lunges = { new Vector3(1f, 1f, 0f), new Vector3(0.75f, 0.8f, 12f), new Vector3(1.15f, 1.1f, -10f), new Vector3(0.6f, 1.2f, 7f), new Vector3(1f, 0.7f, -14f) };
        private const float LungeRunSeconds = 1.2f;
        private int lungeVariant;

        private void Awake()
        {
            initialRotation = transform.rotation;
            if (Model == null) Model = transform.Find("Model");
            if (Model != null) { modelPos = Model.localPosition; modelRot = Model.localRotation; modelScale = Model.localScale; }
        }

        /// <summary>Resize the Model (UnitLooks' body proportions); the placeholder poses squash and stretch from this size.</summary>
        public void SetBodyScale(Vector3 scale)
        {
            if (Model == null) return;
            modelScale = scale;
            Model.localScale = scale;
        }

        public void ResetTo(Unit unit)
        {
            motionPath = null;
            timedKind = TimedKind.None;
            carrier = null;
            transform.position = unit.Position;
            transform.rotation = initialRotation;
            windupStart = windupEnd = lungeStart = flinchStart = wobbleStart = stunnedUntil = -1f;
            dead = false;
            ApplyPose(0f, 0f, 0f, 0f);
            if (Animator != null) { Animator.Rebind(); Animator.Update(0f); }
        }

        /// <summary>Walk the path at 'speed' (m/s over the ground, like the sim), feet on the terrain.</summary>
        public void BeginMotion(List<Vector3> path, float speed, float startSimTime)
        {
            Unslam();
            motionPath = path;
            motionSpeed = speed;
            motionStart = startSimTime;
            motionLength = 0f;
            timedKind = TimedKind.None;
            for (int i = 1; i < path.Count; i++) motionLength += FlatDistance(path[i - 1], path[i]);
        }

        /// <summary>A dash, shove or pull from 'from' to 'to' over 'seconds', feet on the terrain (a dash off a ledge steps down).</summary>
        public void BeginGlide(Vector3 from, Vector3 to, float seconds, float startSimTime) { BeginTimed(TimedKind.Glide, from, to, seconds, startSimTime); }

        /// <summary>A jump across a height change, along the sim's arc (TerrainMap.JumpY).</summary>
        public void BeginJump(Vector3 from, Vector3 to, float seconds, float startSimTime) { BeginTimed(TimedKind.Jump, from, to, seconds, startSimTime); }

        /// <summary>Knocked off a ledge or out of a jump: carried toward 'to' while dropping faster and faster onto it.</summary>
        public void BeginFall(Vector3 from, Vector3 to, float seconds, float startSimTime) { BeginTimed(TimedKind.Fall, from, to, seconds, startSimTime); }

        /// <summary>Raised straight up from 'from' to 'to' over 'seconds' (Amblivate), then held there until the next motion (its fall).</summary>
        public void BeginLift(Vector3 from, Vector3 to, float seconds, float startSimTime) { BeginTimed(TimedKind.Lift, from, to, seconds, startSimTime); }

        /// <summary>Held up in front of 'by' (Subjugate), 'offset' metres along its facing, until EndCarry.</summary>
        public void BeginCarry(UnitView by, float offset)
        {
            carrier = by;
            carryOffset = offset;
            motionPath = null;
        }

        /// <summary>Put down where the simulation left it.</summary>
        public void EndCarry(Vector3 position)
        {
            carrier = null;
            transform.position = position;
        }

        /// <summary>
        /// Heretic's Ascent: follow the slam's flight (AerialSlamPath, the same curve the simulation uses). The carrier leaps up,
        /// hangs and drops back, arching back as it slams; the one carried swings over its head, turning over, to land behind it.
        /// </summary>
        public void BeginAerialSlam(AerialSlamInfo path, bool isCarrier)
        {
            Unslam();
            carrier = null;
            slamPath = path;
            slamCarrier = isCarrier;
            motionPath = new List<Vector3> { path.CarrierStart, path.Landing };
            timedKind = TimedKind.Slam;
            motionStart = path.Start;
            motionSeconds = Mathf.Max(0.01f, AerialSlamPath.End(path) - path.Start);
        }

        private void UpdateSlam()
        {
            float t = Mathf.Min(Director.PlaybackClock, AerialSlamPath.End(slamPath));
            Vector3 f = slamPath.Facing;
            Vector3 back = Vector3.Cross(f, Vector3.up);   // turning about this tips a body's top toward the carrier's back
            if (slamCarrier)
            {
                transform.position = AerialSlamPath.Carrier(slamPath, t);
                float s = t - slamPath.Start - slamPath.Rise - slamPath.Hold;
                float arch = s > 0f ? Mathf.Sin(Mathf.Clamp01(s / Mathf.Max(1e-4f, slamPath.Slam)) * Mathf.PI) * 30f : 0f;   // upright again as it lands
                transform.rotation = Quaternion.AngleAxis(arch, back) * Quaternion.LookRotation(f);
                return;
            }
            var tilt = Quaternion.AngleAxis(AerialSlamPath.Swing(slamPath, t) * Mathf.Rad2Deg, back);
            transform.rotation = tilt * Quaternion.LookRotation(-f);   // facing its captor, turning over with the swing
            transform.position = AerialSlamPath.HeldCenter(slamPath, t) - tilt * Vector3.up * slamPath.HeldHalfHeight;
        }

        /// <summary>Leaving a slam's motion: stand upright again, facing as it did.</summary>
        private void Unslam()
        {
            if (timedKind != TimedKind.Slam) return;
            timedKind = TimedKind.None;
            transform.rotation = Quaternion.LookRotation(slamCarrier ? slamPath.Facing : -slamPath.Facing);
        }

        private void BeginTimed(TimedKind kind, Vector3 from, Vector3 to, float seconds, float startSimTime)
        {
            Unslam();
            motionPath = new List<Vector3> { from, to };
            timedKind = kind;
            motionStart = startSimTime;
            motionSeconds = Mathf.Max(0.01f, seconds);
        }

        /// <summary>Stop scripted movement where the simulation says the unit ended up (a cancelled walk stops mid-path).</summary>
        public void EndMotion(Vector3 finalPosition)
        {
            Unslam();
            transform.position = finalPosition;
            motionPath = null;
        }

        private TerrainMap Terrain
        {
            get { return Director != null && Director.Timeline != null && Director.Timeline.InitialState != null ? Director.Timeline.InitialState.Terrain : null; }
        }

        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        private void Update()
        {
            if (carrier == null) UpdateMotion();
            UpdatePose();
        }

        // A carried unit follows its carrier after the carrier has moved this frame.
        private void LateUpdate()
        {
            if (carrier == null) return;
            Vector3 f = carrier.transform.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            transform.position = carrier.transform.position + f * carryOffset + Vector3.up * CarryHeight;
            transform.rotation = Quaternion.LookRotation(-f);   // facing its captor
        }

        private void UpdateMotion()
        {
            if (motionPath == null || Director == null) return;
            var terrain = Terrain;
            if (timedKind == TimedKind.Slam) { UpdateSlam(); return; }
            if (timedKind != TimedKind.None)
            {
                Vector3 from = motionPath[0], to = motionPath[1];
                float k = Mathf.Clamp01((Director.PlaybackClock - motionStart) / motionSeconds);
                Vector3 p = Vector3.Lerp(from, to, k);
                float ground = terrain != null ? terrain.HeightAt(p) : p.y;
                if (timedKind == TimedKind.Lift) { float e = k * k * (3f - 2f * k); p = Vector3.Lerp(from, to, e); transform.position = p; return; }
                if (timedKind == TimedKind.Jump) p.y = TerrainMap.JumpY(from, to, k);
                else if (timedKind == TimedKind.Fall) p.y = Mathf.Max(ground, from.y + (to.y - from.y) * k * k);
                else if (terrain != null) p.y = ground;
                transform.position = p;
                // Shoves and falls keep the unit facing whoever hit it; dashes and jumps face where they go.
                Vector3 dir = to - from; dir.y = 0f;
                if (timedKind != TimedKind.Fall && dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir);
                return;
            }

            float traveled = Mathf.Clamp((Director.PlaybackClock - motionStart) * motionSpeed, 0f, motionLength);
            for (int i = 1; i < motionPath.Count; i++)
            {
                float seg = FlatDistance(motionPath[i - 1], motionPath[i]);
                if (traveled <= seg || i == motionPath.Count - 1)
                {
                    float t = seg > 1e-5f ? Mathf.Clamp01(traveled / seg) : 1f;
                    Vector3 dir = motionPath[i] - motionPath[i - 1];
                    Vector3 p = Vector3.Lerp(motionPath[i - 1], motionPath[i], t);
                    if (terrain != null) p.y = terrain.HeightAt(p);   // the sim jumps the edges (a Jump event takes over there)
                    transform.position = p;
                    if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
                    return;
                }
                traveled -= seg;
            }
        }

        private bool ProceduralPoses { get { return Model != null && (Animator == null || Animator.runtimeAnimatorController == null); } }

        /// <summary>Sums whichever placeholder poses are playing into a lean (degrees, + = forward), squash, forward
        /// offset and roll for the Model, pivoting at the feet.</summary>
        private void UpdatePose()
        {
            if (!ProceduralPoses || Director == null || dead) return;
            float t = Director.PlaybackClock;
            float lean = 0f, squash = 0f, forward = 0f, roll = 0f, tremble = 0f;

            if (windupStart >= 0f && t < windupEnd)
            {
                float p = Mathf.Clamp01((t - windupStart) / Mathf.Max(0.01f, windupEnd - windupStart));
                float e = p * p * (3f - 2f * p);
                lean -= 14f * e;           // coil back
                squash += 0.1f * e;        // and down
                tremble = p * p * p * 2.5f;   // shaking just before it goes
            }
            if (lungeStart >= 0f && t - lungeStart < LungeSeconds)
            {
                float s = Mathf.Sin(Mathf.PI * (t - lungeStart) / LungeSeconds);
                var v = Lunges[lungeVariant % Lunges.Length];
                lean += 20f * v.x * s;
                forward += 0.35f * v.y * s;
                roll += v.z * s;
            }
            if (flinchStart >= 0f && t - flinchStart < FlinchSeconds)
            {
                float s = Mathf.Sin(Mathf.PI * (t - flinchStart) / FlinchSeconds);
                lean -= 12f * flinchStrength * s;
                forward -= 0.12f * flinchStrength * s;
            }
            if (wobbleStart >= 0f && t - wobbleStart < WobbleSeconds)
            {
                float k = (t - wobbleStart) / WobbleSeconds;
                roll += 10f * (1f - k) * Mathf.Sin(k * Mathf.PI * 4f);
            }
            if (t < stunnedUntil)
            {
                lean += 10f;      // slumped forward
                squash += 0.06f;
                roll += 4f * Mathf.Sin(t * 3f);
            }
            if (tremble > 0f) roll += tremble * Mathf.Sin(t * 70f);
            ApplyPose(lean, squash, forward, roll);
        }

        private void ApplyPose(float lean, float squash, float forward, float roll)
        {
            if (Model == null || !ProceduralPoses) return;
            Quaternion r = Quaternion.Euler(lean, 0f, roll);
            Model.localPosition = r * modelPos + new Vector3(0f, 0f, forward);
            Model.localRotation = r * modelRot;
            Model.localScale = new Vector3(modelScale.x * (1f + squash * 0.5f), modelScale.y * (1f - squash), modelScale.z * (1f + squash * 0.5f));
        }

        // ---- Presentation hooks: fill in with your animation controller / VFX ----
        public void PlayWindup(string actionId, float windupSeconds, UnitView target)
        {
            if (target != null)
            {
                Vector3 look = target.transform.position - transform.position; look.y = 0f;
                if (look.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(look);
            }
            // Waits, holds and instant actions (Block) have no windup to show and no lunge to make.
            lungeOnResolve = windupSeconds > 0f;
            if (lungeOnResolve && Director != null) { windupStart = Director.PlaybackClock; windupEnd = windupStart + windupSeconds; }
            Trigger("Windup");   // TODO: pick a clip per actionId and scale it to windupSeconds
        }
        public void PlayResolve(string actionId)
        {
            windupStart = -1f;
            if (lungeOnResolve && Director != null)
            {
                // Lunges in quick succession (a run of rapid strikes) each lean a different way.
                float now = Director.PlaybackClock;
                lungeVariant = lungeStart >= 0f && now - lungeStart < LungeRunSeconds && now >= lungeStart ? lungeVariant + 1 : 0;
                lungeStart = now;
            }
            lungeOnResolve = false;
            Trigger("Resolve");
        }

        /// <summary>Haste/slow: drive an Animator float (e.g. a speed multiplier on windup/recovery states).</summary>
        public void PlaySpeedChanged(float actionSpeed) { if (Animator != null) Animator.SetFloat("ActionSpeed", actionSpeed); }
        public void PlayInterrupted()
        {
            windupStart = -1f;
            lungeOnResolve = false;
            if (Director != null) wobbleStart = Director.PlaybackClock;
            Trigger("Interrupted");
        }
        public void PlayHit(ImpactType impact)
        {
            flinchStrength = impact == ImpactType.Heavy ? 1.7f : 1f;
            if (Director != null) flinchStart = Director.PlaybackClock;
            Trigger(impact == ImpactType.Heavy ? "HitHeavy" : "HitLight");
        }
        public void PlayDodge() { Trigger("Dodge"); }
        public void PlayStun(float seconds)
        {
            if (Director != null) stunnedUntil = Director.PlaybackClock + seconds;
            Trigger("Stun");
        }
        public void PlayUnravel() { Trigger("Unravel"); }
        public void PlayDeath()
        {
            motionPath = null;
            carrier = null;
            ApplyPose(0f, 0f, 0f, 0f);
            dead = true;
            if (Animator == null)
            {
                // Placeholder until there is a death clip: tip the unit over so it reads as down.
                transform.rotation = transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
                transform.position += Vector3.up * 0.5f;
            }
            Trigger("Death");
        }

        private void Trigger(string name) { if (Animator != null) Animator.SetTrigger(name); }
    }
}
