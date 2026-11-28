using UnityEngine;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// A dodge, as the player sees it: while the stretch playing ends in a unit's dodge (BattleController.DodgingUnitId, the
    /// clock easing to its stop: PlaybackDirector.PlayUntil with ease seconds) and while that dodge is being ordered, the
    /// screen darkens toward the edges and the camera glides onto the dodger. The darkening follows the slowdown in and
    /// fades out once the dodge is ordered. Drawn under the HUD and the orders panel, so bars and buttons stay clear.
    /// </summary>
    public class DodgeMoment : MonoBehaviour
    {
        public BattleController Controller;

        [Tooltip("How dark the whole screen gets at the full moment (0..1).")]
        [Range(0f, 1f)] public float Dim = 0.35f;
        [Tooltip("How dark the vignette at the edges gets at the full moment (0..1).")]
        [Range(0f, 1f)] public float EdgeDark = 0.85f;
        [Tooltip("Real seconds the darkening takes to fade out after the dodge is ordered.")]
        [Min(0.01f)] public float FadeOutSeconds = 0.3f;

        private float strength;     // 0..1, what is drawn
        private string focusedOn;   // the dodger the camera was sent to (once per dodge)
        private TacticsCamera rig;

        private void Update()
        {
            string id = Controller != null ? Controller.DodgingUnitId : null;
            var director = Controller != null ? Controller.Director : null;
            if (id == null || director == null)
            {
                focusedOn = null;
                strength = Mathf.MoveTowards(strength, 0f, Time.unscaledDeltaTime / FadeOutSeconds);
                return;
            }
            // In with the slowdown (it is already full if the stop came at once), so the moment builds as time does.
            strength = Mathf.Max(strength, director.ReachedTarget ? 1f : Mathf.SmoothStep(0f, 1f, director.EaseProgress));
            if (focusedOn == id) return;
            var view = director.FindView(id);
            if (view == null) return;
            if (rig == null) rig = FindFirstObjectByType<TacticsCamera>();
            if (rig != null) rig.FocusOn(view.transform.position, false);
            focusedOn = id;
        }

        private void OnGUI()
        {
            if (strength <= 0.001f || Event.current.type != EventType.Repaint || UiDeclutter.On) return;
            GUI.depth = 10;   // behind the HUD and the orders panel
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0.02f, Dim * strength);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, EdgeDark * strength);
            GUI.DrawTexture(screen, TacticalView.Vignette(), ScaleMode.StretchToFill);
            GUI.color = old;
        }
    }
}
