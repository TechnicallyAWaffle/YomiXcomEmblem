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
        private Quaternion initialRotation;

        // Placeholder poses: start times on the playback clock (-1 = not playing).
        private const float LungeSeconds = 0.25f, FlinchSeconds = 0.2f, WobbleSeconds = 0.35f;
        private Vector3 modelPos, modelScale;
        private Quaternion modelRot;
        private float windupStart = -1f, windupEnd = -1f, lungeStart = -1f, flinchStart = -1f, wobbleStart = -1f, stunnedUntil = -1f;
        private float flinchStrength = 1f;
        private bool lungeOnResolve, dead;

        private void Awake()
        {
            initialRotation = transform.rotation;
            if (Model == null) Model = transform.Find("Model");
            if (Model != null) { modelPos = Model.localPosition; modelRot = Model.localRotation; modelScale = Model.localScale; }
        }

        public void ResetTo(Unit unit)
        {
            motionPath = null;
            transform.position = unit.Position;
            transform.rotation = initialRotation;
            windupStart = windupEnd = lungeStart = flinchStart = wobbleStart = stunnedUntil = -1f;
            dead = false;
            ApplyPose(0f, 0f, 0f, 0f);
            if (Animator != null) { Animator.Rebind(); Animator.Update(0f); }
        }

        public void BeginMotion(List<Vector3> path, float speed, float startSimTime)
        {
            motionPath = path;
            motionSpeed = speed;
            motionStart = startSimTime;
            motionLength = 0f;
            for (int i = 1; i < path.Count; i++) motionLength += Vector3.Distance(path[i - 1], path[i]);
        }

        /// <summary>Stop scripted movement where the simulation says the unit ended up (a cancelled walk stops mid-path).</summary>
        public void EndMotion(Vector3 finalPosition)
        {
            transform.position = finalPosition;
            motionPath = null;
        }

        private void Update()
        {
            UpdateMotion();
            UpdatePose();
        }

        private void UpdateMotion()
        {
            if (motionPath == null || Director == null) return;
            float traveled = Mathf.Clamp((Director.PlaybackClock - motionStart) * motionSpeed, 0f, motionLength);

            for (int i = 1; i < motionPath.Count; i++)
            {
                float seg = Vector3.Distance(motionPath[i - 1], motionPath[i]);
                if (traveled <= seg || i == motionPath.Count - 1)
                {
                    float t = seg > 1e-5f ? Mathf.Clamp01(traveled / seg) : 1f;
                    Vector3 dir = motionPath[i] - motionPath[i - 1];
                    transform.position = Vector3.Lerp(motionPath[i - 1], motionPath[i], t);
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
                lean += 20f * s;
                forward += 0.35f * s;
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
            if (lungeOnResolve && Director != null) lungeStart = Director.PlaybackClock;
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
