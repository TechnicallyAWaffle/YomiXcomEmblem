using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Orbit-style tactics camera around a focus point on the ground.
    ///   WASD / arrows: pan    Middle-drag: pan    Right-drag: rotate (and tilt)    Scroll: zoom    Q / E: rotate
    /// FocusOn glides to a point and restores the starting rotation (used when a unit is ready for orders). Everything is
    /// eased: input moves a target view and the camera SmoothDamps toward it; the glide follows FocusCurve.
    /// Runs on unscaled time, so it keeps working while the world is frozen waiting for orders.
    /// Starts from wherever it is placed in the scene. Lives on the TacticalCam CinemachineCamera; the brain on the
    /// main camera blends between it and InteractionCamera's cinematic shots.
    /// </summary>
    public class TacticsCamera : MonoBehaviour
    {
        public float PanSpeed = 14f;            // m/s at the reference distance; scales with zoom
        public float DragPanSensitivity = 1.5f;
        public float ZoomStep = 0.12f;          // fraction of distance per scroll notch
        public float MinDistance = 6f;
        public float MaxDistance = 50f;
        public float RotateSpeed = 90f;         // degrees/s
        [Tooltip("Degrees per pixel of right-drag.")]
        public float DragRotateSensitivity = 0.25f;
        public float MinPitch = 20f;
        public float MaxPitch = 85f;
        [Tooltip("Seconds FocusOn takes to glide to its target.")]
        public float FocusTime = 0.45f;
        [Tooltip("Easing of the FocusOn glide (0..1 over FocusTime).")]
        public AnimationCurve FocusCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Seconds the view takes to catch up with pans, zooms and turns (SmoothDamp easing). 0 = immediate.")]
        public float SmoothTime = 0.12f;
        [Tooltip("Ground area the focus point is kept inside (XZ).")]
        public Vector2 BoundsMin = new Vector2(-20f, -25f);
        public Vector2 BoundsMax = new Vector2(20f, 25f);
        [Tooltip("Turned off while a cinematic shot (InteractionCamera) has the view.")]
        public bool InputEnabled = true;

        /// <summary>True once the current (or last) right-button press moved far enough to count as a rotate drag,
        /// so UI can tell a right-click (go back) from a right-drag (rotate).</summary>
        public bool RightDragged { get; private set; }

        private const float ReferenceDistance = 20f;
        private const float ClickSlop = 6f;     // pixels a right-press may wander and still be a click

        private Vector3 focus;
        private float distance;
        private float yaw;
        private float pitch;
        private float homeYaw, homePitch;
        private Vector3 lastMouse;
        private Vector3 rightPressAt;

        // What is on screen: eases toward focus/distance/yaw/pitch above, which input and FocusOn set directly.
        private Vector3 shownFocus, focusVelocity;
        private float shownDistance, shownYaw, shownPitch, distanceVelocity, yawVelocity, pitchVelocity;

        // FocusOn glide
        private bool gliding;
        private float glideT;
        private Vector3 glideFromFocus, glideToFocus;
        private float glideFromYaw, glideToYaw, glideFromPitch, glideToPitch;

        private void Start()
        {
            Vector3 euler = transform.rotation.eulerAngles;
            pitch = homePitch = euler.x;
            yaw = homeYaw = euler.y;
            Vector3 fwd = transform.forward;
            // Focus where the view ray meets the ground plane (y = 0); fall back to a point ahead if looking level.
            float t = fwd.y < -0.01f ? -transform.position.y / fwd.y : ReferenceDistance;
            focus = transform.position + fwd * t;
            focus.y = 0f;
            distance = Mathf.Clamp(t, MinDistance, MaxDistance);
            lastMouse = Input.mousePosition;
            Apply(true);
        }

        /// <summary>Glide to center 'point' and return to the starting rotation (zoom is kept). Any camera input cancels it.</summary>
        public void FocusOn(Vector3 point, bool resetRotation = true)
        {
            gliding = true;
            glideT = 0f;
            glideFromFocus = focus;
            // The point's height counts: a unit on raised ground is centred, not the ground below it.
            glideToFocus = new Vector3(Mathf.Clamp(point.x, BoundsMin.x, BoundsMax.x), point.y, Mathf.Clamp(point.z, BoundsMin.y, BoundsMax.y));
            glideFromYaw = yaw;
            glideToYaw = resetRotation ? yaw + Mathf.DeltaAngle(yaw, homeYaw) : yaw;   // the short way round
            glideFromPitch = pitch;
            glideToPitch = resetRotation ? homePitch : pitch;
        }

        private void LateUpdate()
        {
            // The glide runs even while a cinematic has the view, so the camera is in place when it hands back.
            if (gliding)
            {
                glideT = Mathf.Min(1f, glideT + Time.unscaledDeltaTime / Mathf.Max(0.01f, FocusTime));
                float s = FocusCurve != null && FocusCurve.length > 0 ? FocusCurve.Evaluate(glideT) : Mathf.SmoothStep(0f, 1f, glideT);
                focus = Vector3.Lerp(glideFromFocus, glideToFocus, s);
                yaw = Mathf.Lerp(glideFromYaw, glideToYaw, s);
                pitch = Mathf.Lerp(glideFromPitch, glideToPitch, s);
                if (glideT >= 1f) gliding = false;
            }

            if (!InputEnabled) { lastMouse = Input.mousePosition; Apply(); return; }
            float dt = Time.unscaledDeltaTime;
            Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
            Vector3 forward = flat * Vector3.forward;
            Vector3 right = flat * Vector3.right;
            float zoomScale = distance / ReferenceDistance;
            bool input = false;

            Vector2 keys = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) keys.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) keys.y -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) keys.x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) keys.x -= 1f;
            if (keys.sqrMagnitude > 1f) keys.Normalize();
            if (keys.sqrMagnitude > 0f) input = true;
            focus += (right * keys.x + forward * keys.y) * PanSpeed * zoomScale * dt;

            Vector3 mouse = Input.mousePosition;
            Vector3 delta = mouse - lastMouse;
            if (Input.GetMouseButton(2))
            {
                float metersPerPixel = DragPanSensitivity * distance / Mathf.Max(1f, Screen.height);
                focus -= (right * delta.x + forward * delta.y) * metersPerPixel;
                input = true;
            }

            if (Input.GetMouseButtonDown(1)) { rightPressAt = mouse; RightDragged = false; }
            if (Input.GetMouseButton(1))
            {
                if (!RightDragged && (mouse - rightPressAt).magnitude > ClickSlop) RightDragged = true;
                if (RightDragged)
                {
                    yaw += delta.x * DragRotateSensitivity;
                    pitch = Mathf.Clamp(pitch - delta.y * DragRotateSensitivity, MinPitch, MaxPitch);
                    input = true;
                }
            }
            lastMouse = mouse;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f) distance = Mathf.Clamp(distance * (1f - scroll * ZoomStep), MinDistance, MaxDistance);

            if (Input.GetKey(KeyCode.Q)) { yaw += RotateSpeed * dt; input = true; }
            if (Input.GetKey(KeyCode.E)) { yaw -= RotateSpeed * dt; input = true; }
            if (input) gliding = false;   // the player took over

            focus.x = Mathf.Clamp(focus.x, BoundsMin.x, BoundsMax.x);
            focus.z = Mathf.Clamp(focus.z, BoundsMin.y, BoundsMax.y);
            Apply();
        }

        /// <summary>Ease the shown view toward the target view (or jump straight there) and place the camera.</summary>
        private void Apply(bool snap = false)
        {
            float dt = Time.unscaledDeltaTime;
            if (snap || SmoothTime <= 0f)
            {
                shownFocus = focus; shownDistance = distance; shownYaw = yaw; shownPitch = pitch;
                focusVelocity = Vector3.zero; distanceVelocity = yawVelocity = pitchVelocity = 0f;
            }
            else
            {
                shownFocus = Vector3.SmoothDamp(shownFocus, focus, ref focusVelocity, SmoothTime, Mathf.Infinity, dt);
                shownDistance = Mathf.SmoothDamp(shownDistance, distance, ref distanceVelocity, SmoothTime, Mathf.Infinity, dt);
                shownYaw = Mathf.SmoothDamp(shownYaw, yaw, ref yawVelocity, SmoothTime, Mathf.Infinity, dt);
                shownPitch = Mathf.SmoothDamp(shownPitch, pitch, ref pitchVelocity, SmoothTime, Mathf.Infinity, dt);
            }
            transform.rotation = Quaternion.Euler(shownPitch, shownYaw, 0f);
            transform.position = shownFocus - transform.forward * shownDistance;
        }
    }
}
