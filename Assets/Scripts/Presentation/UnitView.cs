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
        private float motionArc;   // a glide that bounds through the air: its peak height
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
        /// <summary>Its death has played (PlayDeath); false again once playback rewinds.</summary>
        public bool IsDead { get { return dead; } }
        // Lunge shapes for strikes in a run, in order: lean and reach scales, and a sideways roll (degrees). The first is the plain one.
        private static readonly Vector3[] Lunges = { new Vector3(1f, 1f, 0f), new Vector3(0.75f, 0.8f, 12f), new Vector3(1.15f, 1.1f, -10f), new Vector3(0.6f, 1.2f, 7f), new Vector3(1f, 0.7f, -14f) };
        private const float LungeRunSeconds = 1.2f;
        private int lungeVariant;

        private void Awake()
        {
            initialRotation = transform.rotation;
            initialScale = transform.localScale;
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
            abilityDashUntil = -1f;
            Uncrumble();
            Unmirror();
            MakinaLooks.Restore(this);
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
        /// arc: a bound through the air, this high at its peak (Opening Theorem).
        /// keepFacing: it stays turned the way it was (a dash back or to the side, PlayDash), rather than facing where it goes.
        public void BeginGlide(Vector3 from, Vector3 to, float seconds, float startSimTime, float arc = 0f, bool keepFacing = false)
        {
            BeginTimed(TimedKind.Glide, from, to, seconds, startSimTime);
            motionArc = arc;
            glideKeepsFacing = keepFacing;
        }
        private bool glideKeepsFacing;

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
            motionArc = 0f;
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
            UpdateAnimator();
            UpdateCrumble();
        }

        // ---- Animator (a real model: UnitDefinition.Model, set up by UnitLooks; its controller made by UnitModelImport)

        private RuntimeAnimatorController paramsFor;
        private readonly HashSet<string> animatorParams = new HashSet<string>();
        private readonly Dictionary<string, float> clipSeconds = new Dictionary<string, float>();
        // An ability's own dash clip is playing (its Dash phase): the glide that goes with it plays no dash of its own.
        private float abilityDashUntil = -1f;
        // A dash to the left plays the side dash mirrored (the model flipped left for right) until this time.
        private float mirrorUntil = -1f;
        private bool mirrored;

        /// <summary>Does the Animator's controller have this parameter? (A controller without it is left alone, no warnings.)</summary>
        private bool HasParam(string name)
        {
            if (Animator == null || Animator.runtimeAnimatorController == null) return false;
            if (paramsFor != Animator.runtimeAnimatorController)
            {
                paramsFor = Animator.runtimeAnimatorController;
                animatorParams.Clear();
                foreach (var p in Animator.parameters) animatorParams.Add(p.name);
                clipSeconds.Clear();
                foreach (var c in paramsFor.animationClips) if (c != null) clipSeconds[c.name] = c.length;
            }
            return animatorParams.Contains(name);
        }

        /// <summary>The Animator state of an ability's own clip ("slash.Windup" plays as state "slash Windup"; see UnitModelImport).</summary>
        private static string AbilityState(string actionId, string phase) { return actionId + " " + phase; }

        /// <summary>Does the controller have a state of this name (an ability's own clip)?</summary>
        private bool HasState(string state)
        {
            if (Animator == null || Animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state)) return false;
            return Animator.HasState(0, Animator.StringToHash(Animator.GetLayerName(0) + "." + state));
        }

        private float ClipSeconds(string clip)
        {
            HasParam("");   // fills the cache for this controller
            float s;
            return clipSeconds.TryGetValue(clip, out s) ? s : -1f;
        }

        private void Play(string state) { Animator.CrossFadeInFixedTime(state, 0.05f, 0); }

        private void Mirror(bool on)
        {
            if (Animator == null || mirrored == on) return;
            var t = Animator.transform;
            var s = t.localScale; s.x = -s.x; t.localScale = s;
            mirrored = on;
        }
        private void Unmirror() { Mirror(false); mirrorUntil = -1f; }

        /// <summary>The states that last as long as something in the battle does: walking (a walk, not a dash or a shove)
        /// and stunned.</summary>
        private void UpdateAnimator()
        {
            if (mirrored && Director != null && (Director.PlaybackClock >= mirrorUntil || Director.PlaybackClock < mirrorUntil - 1f)) Unmirror();
            if (Animator == null || Director == null || dead) return;
            bool walking = motionPath != null && timedKind == TimedKind.None && carrier == null
                           && (Director.PlaybackClock - motionStart) * motionSpeed < motionLength;
            if (HasParam("Moving")) Animator.SetBool("Moving", walking);
            if (HasParam("Stunned")) Animator.SetBool("Stunned", Director.PlaybackClock < stunnedUntil);
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
                float ground = terrain != null ? terrain.HeightAt(TerrainMap.Level(p, transform.position.y)) : p.y;   // the floor it is on
                if (timedKind == TimedKind.Lift) { float e = k * k * (3f - 2f * k); p = Vector3.Lerp(from, to, e); transform.position = p; return; }
                if (timedKind == TimedKind.Jump) p.y = TerrainMap.JumpY(from, to, k);
                else if (timedKind == TimedKind.Fall) p.y = Mathf.Max(ground, from.y + (to.y - from.y) * k * k);
                else if (terrain != null) p.y = ground;
                if (timedKind == TimedKind.Glide && motionArc > 0f) p.y += motionArc * 4f * k * (1f - k);
                transform.position = p;
                // Shoves and falls keep the unit facing whoever hit it; dashes and jumps face where they go.
                Vector3 dir = to - from; dir.y = 0f;
                if (timedKind != TimedKind.Fall && !(timedKind == TimedKind.Glide && glideKeepsFacing) && dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir);
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
                    if (terrain != null) p.y = terrain.HeightAt(TerrainMap.Level(p, transform.position.y));   // the sim jumps the edges (a Jump event takes over there)
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
            // A model's windup clip (the ability's own, else the shared one) is stretched to the windup's length; Block (no
            // windup) raises its guard instead.
            if (windupSeconds > 0f)
            {
                string own = AbilityState(actionId, "Windup");
                bool hasOwn = HasState(own);
                float clip = ClipSeconds(hasOwn ? actionId + ".Windup" : "Windup");
                if (HasParam("WindupSpeed")) Animator.SetFloat("WindupSpeed", clip > 0f ? clip / windupSeconds : 1f);
                if (hasOwn) Play(own);
                else Trigger("Windup");
            }
            else
            {
                // An instant ability with a windup clip of its own (a Transcended Parry) plays it at its own speed.
                string own = AbilityState(actionId, "Windup");
                if (HasState(own))
                {
                    if (HasParam("WindupSpeed")) Animator.SetFloat("WindupSpeed", 1f);
                    Play(own);
                }
                else if (actionId == "block") Trigger("Block");
            }
        }
        public void PlayResolve(string actionId)
        {
            // The ability's own clips: its dash (Phantom Strike's, straight into its strike), else its strike. Without them
            // the shared Strike, for what wound up.
            string dash = AbilityState(actionId, "Dash"), strike = AbilityState(actionId, "Strike");
            if (HasState(dash))
            {
                Play(dash);
                if (Director != null) abilityDashUntil = Director.PlaybackClock + 0.25f;
                lungeOnResolve = false;
            }
            else if (HasState(strike))
            {
                Play(strike);
                lungeOnResolve = false;
            }
            windupStart = -1f;
            if (lungeOnResolve && Director != null)
            {
                // Lunges in quick succession (a run of rapid strikes) each lean a different way.
                float now = Director.PlaybackClock;
                lungeVariant = lungeStart >= 0f && now - lungeStart < LungeRunSeconds && now >= lungeStart ? lungeVariant + 1 : 0;
                lungeStart = now;
            }
            if (lungeOnResolve) Trigger("Resolve");   // only what wound up strikes (not a Block, a wait or an instant)
            lungeOnResolve = false;
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
        /// <summary>A dodge where it stands (a dash to somewhere comes as PlayDash): a quick sway to the side.</summary>
        public void PlayDodge()
        {
            if (HasParam("DashDir")) Animator.SetInteger("DashDir", 2);
            Trigger("Dodge");
        }

        /// <summary>
        /// A dash of its own from 'from' to 'to' (the Dash, a dodge's, an ability's that has no dash clip of its own): the
        /// forward, back or side dash by where it goes against where the unit faces now (a dash to the left is the side
        /// dash mirrored). True: it keeps facing that way during the glide (back and side dashes); false: it turns to face
        /// where it goes (forward ones).
        /// </summary>
        public bool PlayDash(Vector3 from, Vector3 to)
        {
            if (Director != null && Director.PlaybackClock < abilityDashUntil) return false;   // the ability's own dash is playing
            Vector3 dir = to - from; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) { PlayDodge(); return true; }
            Vector3 fwd = transform.forward; fwd.y = 0f;
            float angle = Vector3.SignedAngle(fwd, dir, Vector3.up);   // + = to the right
            int kind = Mathf.Abs(angle) <= 50f ? 0 : Mathf.Abs(angle) >= 130f ? 1 : angle > 0f ? 2 : 3;
            if (HasParam("DashDir")) Animator.SetInteger("DashDir", kind);
            Trigger("Dodge");
            if (kind == 3 && Animator != null && Director != null)
            {
                Mirror(true);
                mirrorUntil = Director.PlaybackClock + BattleSimulation.DashDuration + 0.1f;
            }
            return kind != 0;
        }
        public void PlayStun(float seconds)
        {
            if (Director != null) stunnedUntil = Director.PlaybackClock + seconds;
            Trigger("Stun");
        }
        public void PlayUnravel() { Trigger("Unravel"); }
        /// <summary>Down. crumble: its body crumbles to dust CorpseSeconds later (an enemy's: UpdateCrumble).</summary>
        public void PlayDeath(bool crumble = false)
        {
            crumbles = crumble && Director != null;
            if (crumbles) diedAt = Director.PlaybackClock;
            motionPath = null;
            carrier = null;
            ApplyPose(0f, 0f, 0f, 0f);
            dead = true;
            if (MakinaLooks.Shatter(this)) return;   // a Makina leaves no body: it bursts apart
            if (Animator == null)
            {
                // Placeholder until there is a death clip: tip the unit over so it reads as down.
                transform.rotation = transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
                transform.position += Vector3.up * 0.5f;
            }
            if (HasParam("Dead")) Animator.SetBool("Dead", true);   // nothing plays over the death after this
            Trigger("Death");
        }

        // ---- An enemy's corpse crumbling to dust (CombatVfx throws the dust), on the playback clock so it rewinds and scrubs.

        /// <summary>Seconds a fallen enemy's body lies there before it crumbles, and how long the crumbling takes.</summary>
        public const float CorpseSeconds = 3f, CrumbleSeconds = 1.2f;
        private bool crumbles;
        private float diedAt, crumbled;
        private Vector3 initialScale, crumbleFrom;
        private readonly List<Renderer> crumbleRenderers = new List<Renderer>();

        private void UpdateCrumble()
        {
            if (!dead || !crumbles || Director == null) return;
            SetCrumble(Mathf.Clamp01((Director.PlaybackClock - diedAt - CorpseSeconds) / CrumbleSeconds));
        }

        /// <summary>0: the body as it fell; 1: gone. In between it slumps into the ground and thins out.</summary>
        private void SetCrumble(float k)
        {
            if (Mathf.Approximately(k, crumbled)) return;
            if (crumbled <= 0f)
            {
                crumbleFrom = transform.position;
                // Only what is showing as it starts is crumbled away (and brought back): a model's hidden capsule stays hidden.
                crumbleRenderers.Clear();
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (!(r is ParticleSystemRenderer) && !r.forceRenderingOff && r.gameObject.name != "HighlightShell" && r.gameObject.name != "TacticalEdges") crumbleRenderers.Add(r);
            }
            crumbled = k;
            float e = k * k;
            transform.position = crumbleFrom + Vector3.down * (0.5f * e);
            transform.localScale = new Vector3(initialScale.x * (1f + 0.15f * k), initialScale.y * (1f - 0.7f * e), initialScale.z * (1f + 0.15f * k));
            bool gone = k >= 1f;
            foreach (var r in crumbleRenderers) if (r != null) r.forceRenderingOff = gone;
        }

        private void Uncrumble()
        {
            if (crumbled > 0f)
            {
                crumbled = 0f;
                foreach (var r in crumbleRenderers) if (r != null) r.forceRenderingOff = false;
                crumbleRenderers.Clear();
            }
            crumbles = false;
            transform.localScale = initialScale;
        }

        private void Trigger(string name) { if (Animator != null && (Animator.runtimeAnimatorController == null || HasParam(name))) Animator.SetTrigger(name); }
    }
}
