using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// The replay's transport bar (once a battle is over and R plays it back): play / pause, a scrub bar to jump anywhere
    /// in the battle (deaths marked on it), and the playback speed. Keys: Space play / pause, comma / period back / ahead
    /// SeekStep seconds, [ / ] slower / faster, Home back to the start.
    /// While the UI is hidden (Alt; a replay starts that way) the bar shows only as a thin progress line until the
    /// cursor comes near the bottom of the screen, or while paused. The BattleController adds it to itself.
    /// </summary>
    public class ReplayControls : MonoBehaviour
    {
        public PlaybackDirector Director;

        [Tooltip("The playback speeds the bar offers, slowest first ([ and ] step through them).")]
        public float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };
        [Tooltip("Seconds of battle comma / period jump back / ahead.")]
        [Min(0.5f)] public float SeekStep = 5f;
        [Tooltip("While scrubbing: the fewest real seconds between two seeks (each one replays the battle up to that point).")]
        [Min(0f)] public float SeekInterval = 0.05f;

        private const float BarHeight = 58f, BarMaxWidth = 900f, RevealHeight = 190f;
        /// <summary>GUI units kept free under the bar: the bottom-left readouts (playback clock, the hidden-UI hint).</summary>
        private const float BottomClear = 62f;

        private float lastSeekAt = -1f;
        private float pendingSeek = -1f;
        private GUIStyle timeStyle, hintStyle;
        private static Texture2D white;

        private bool Active { get { return Director != null && Director.IsReplay && Director.Timeline != null; } }
        private float Duration { get { return Director.Timeline.Duration; } }
        private bool AtEnd { get { return Director.ReachedTarget && Director.PlaybackClock >= Duration - 1e-3f; } }
        /// <summary>Playing right now: not paused, and not run out at the end.</summary>
        private bool Playing { get { return !Director.Paused && !AtEnd; } }

        private void Update()
        {
            if (!Active) return;
            if (Input.GetKeyDown(KeyCode.Space)) TogglePlay();
            if (Input.GetKeyDown(KeyCode.Comma)) SeekTo(Director.PlaybackClock - SeekStep);
            if (Input.GetKeyDown(KeyCode.Period)) SeekTo(Director.PlaybackClock + SeekStep);
            if (Input.GetKeyDown(KeyCode.Home)) SeekTo(0f);
            if (Input.GetKeyDown(KeyCode.LeftBracket)) StepSpeed(-1);
            if (Input.GetKeyDown(KeyCode.RightBracket)) StepSpeed(1);
            // A scrub held back by SeekInterval lands once the interval is up, so the last drag position always shows.
            if (pendingSeek >= 0f && Time.unscaledTime - lastSeekAt >= SeekInterval) SeekTo(pendingSeek);
        }

        private void OnDisable()
        {
            // Leaving the replay paused would leave the next battle's clock frozen.
            if (Director != null && Director.IsReplay) Director.Paused = false;
        }

        /// <summary>Pause or carry on; at the end, play it again from the start.</summary>
        public void TogglePlay()
        {
            if (AtEnd) { Director.Paused = false; SeekTo(0f); return; }
            Director.Paused = !Director.Paused;
        }

        /// <summary>Jump to 'simTime' (clamped to the battle) and carry on from there, paused or not as before.</summary>
        public void SeekTo(float simTime)
        {
            pendingSeek = -1f;
            lastSeekAt = Time.unscaledTime;
            Director.ScrubTo(Mathf.Clamp(simTime, 0f, Duration));
            Director.PlayUntil(Duration);
        }

        /// <summary>The next speed up (+1) or down (-1) from the one playing.</summary>
        public void StepSpeed(int dir)
        {
            int i = NearestSpeed();
            Director.SetReplaySpeed(Speeds[Mathf.Clamp(i + dir, 0, Speeds.Length - 1)]);
        }

        private int NearestSpeed()
        {
            int best = 0;
            for (int i = 1; i < Speeds.Length; i++)
                if (Mathf.Abs(Speeds[i] - Director.ReplaySpeed) < Mathf.Abs(Speeds[best] - Director.ReplaySpeed)) best = i;
            return best;
        }

        private void Styles()
        {
            if (timeStyle != null) return;
            timeStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 13, fontStyle = FontStyle.Bold };
            timeStyle.normal.textColor = Color.white;
            hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = 10 };
            hintStyle.normal.textColor = new Color(0.75f, 0.8f, 0.9f, 0.85f);
            white = Texture2D.whiteTexture;
        }

        private static string Clock(float t)
        {
            int s = Mathf.FloorToInt(Mathf.Max(0f, t));
            return string.Format("{0}:{1:00}.{2}", s / 60, s % 60, Mathf.FloorToInt((t - s) * 10f) % 10);
        }

        private void OnGUI()
        {
            if (!Active) return;
            UiScale.Begin();
            Styles();
            GUI.depth = -10;   // over the battle's own panels
            float width = Mathf.Min(BarMaxWidth, UiScale.Width - 40f);
            var bar = new Rect((UiScale.Width - width) / 2f, UiScale.Height - BarHeight - BottomClear, width, BarHeight);
            float share = Duration > 0f ? Mathf.Clamp01(Director.PlaybackClock / Duration) : 0f;

            bool reveal = !UiDeclutter.On || Director.Paused || AtEnd || pendingSeek >= 0f
                          || (UiScale.HoverIsReal() && Event.current.mousePosition.y > UiScale.Height - RevealHeight);
            if (!reveal)
            {
                // Hidden UI: just a thin line of how far the replay has got.
                Fill(new Rect(bar.x, UiScale.Height - 4f, width, 3f), new Color(1f, 1f, 1f, 0.15f));
                Fill(new Rect(bar.x, UiScale.Height - 4f, width * share, 3f), new Color(1f, 0.85f, 0.45f, 0.6f));
                return;
            }

            GUI.Box(bar, GUIContent.none);
            GUI.Box(bar, GUIContent.none);   // doubled for a more opaque background with the default skin
            float x = bar.x + 8f, y = bar.y + 6f;

            if (GUI.Button(new Rect(x, y, 70f, 22f), AtEnd ? "Replay" : Playing ? "Pause" : "Play")) TogglePlay();
            x += 76f;
            if (GUI.Button(new Rect(x, y, 30f, 22f), "<<")) SeekTo(Director.PlaybackClock - SeekStep);
            x += 34f;
            if (GUI.Button(new Rect(x, y, 30f, 22f), ">>")) SeekTo(Director.PlaybackClock + SeekStep);
            x += 40f;
            GUI.Label(new Rect(x, y, 150f, 22f), Clock(Director.PlaybackClock) + " / " + Clock(Duration), timeStyle);

            // Speeds on the right, the one playing picked out.
            float sw = 46f, sx = bar.xMax - 8f - Speeds.Length * (sw + 3f) + 3f;
            int now = NearestSpeed();
            for (int i = 0; i < Speeds.Length; i++)
            {
                GUI.backgroundColor = i == now ? new Color(1f, 0.8f, 0.3f) : Color.white;
                if (GUI.Button(new Rect(sx + i * (sw + 3f), y, sw, 22f), Speeds[i].ToString("0.##") + "x")) Director.SetReplaySpeed(Speeds[i]);
            }
            GUI.backgroundColor = Color.white;
            GUI.Label(new Rect(x + 150f, y, Mathf.Max(0f, sx - x - 158f), 22f), "Space  ,  .  [  ]", hintStyle);

            // The scrub bar: deaths marked along it; dragging it jumps the replay there.
            var track = new Rect(bar.x + 10f, bar.y + 36f, width - 20f, 14f);
            if (Event.current.type == UnityEngine.EventType.Repaint && Duration > 0f)
                foreach (var e in Director.Timeline.Events)
                    if (e.Type == TacticsGame.Core.EventType.Death)
                    {
                        bool foe = e.Payload is DeathInfo && ((DeathInfo)e.Payload).Team != Team.Player;
                        Fill(new Rect(track.x + track.width * Mathf.Clamp01(e.SimTime / Duration) - 1f, track.y - 3f, 2f, 5f),
                            foe ? new Color(1f, 0.85f, 0.45f, 0.9f) : new Color(1f, 0.35f, 0.3f, 0.95f));
                    }
            float shown = pendingSeek >= 0f ? pendingSeek : Director.PlaybackClock;
            float picked = GUI.HorizontalSlider(track, shown, 0f, Mathf.Max(0.01f, Duration));
            if (Mathf.Abs(picked - shown) > 1e-3f)
            {
                if (Time.unscaledTime - lastSeekAt >= SeekInterval) SeekTo(picked);
                else pendingSeek = picked;   // Update lands it
            }
        }

        private static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white != null ? white : Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
