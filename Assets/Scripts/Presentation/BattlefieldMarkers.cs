using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Ground overlays for battlefield objects that have no GameObject of their own (traps for now), driven by
    /// dispatched timeline events so they appear and vanish in step with playback and replays.
    /// </summary>
    public class BattlefieldMarkers : MonoBehaviour
    {
        public PlaybackDirector Director;
        public Color PlayerTrapColor = new Color(0.4f, 0.85f, 1f, 0.9f);
        public Color EnemyTrapColor = new Color(1f, 0.4f, 0.3f, 0.9f);

        private readonly Dictionary<string, TrapInfo> traps = new Dictionary<string, TrapInfo>();
        private WorldMarkers markers;

        private void Awake() { markers = new WorldMarkers(transform); }

        private void OnEnable()
        {
            if (Director == null) return;
            Director.OnPlaybackReset += traps.Clear;
            Director.OnEventDispatched += OnEvent;
        }

        private void OnDisable()
        {
            if (Director == null) return;
            Director.OnPlaybackReset -= traps.Clear;
            Director.OnEventDispatched -= OnEvent;
        }

        private void OnEvent(TimelineEvent evt)
        {
            switch (evt.Type)
            {
                case TacticsGame.Core.EventType.TrapPlaced:
                {
                    var t = (TrapInfo)evt.Payload;
                    traps[t.Id] = t;
                    break;
                }
                case TacticsGame.Core.EventType.TrapTriggered:
                case TacticsGame.Core.EventType.TrapRemoved:
                    traps.Remove((string)evt.Payload);
                    break;
            }
        }

        private void Update()
        {
            markers.Begin();
            if (Director != null)
            {
                foreach (var t in traps.Values)
                {
                    bool armed = Director.PlaybackClock + 1e-4f >= t.ArmsAt;
                    var color = t.OwnerTeam == Team.Player ? PlayerTrapColor : EnemyTrapColor;
                    if (!armed) color.a *= 0.4f;   // still arming
                    markers.Circle(t.Position, t.Radius, color, armed ? 0.08f : 0.04f, 40);
                    markers.Circle(t.Position, t.Radius * 0.35f, color, 0.04f, 20);
                }
            }
            markers.End();
        }
    }
}
