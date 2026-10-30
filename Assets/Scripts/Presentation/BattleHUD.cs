using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Bare-bones IMGUI readout for test scenes: health/strain bars over each unit, a combat log and the outcome.
    /// Driven only by dispatched timeline events (never the simulation, which runs ahead of playback),
    /// so it stays in sync during slow motion and replays. Replace with real UI later.
    /// </summary>
    public class BattleHUD : MonoBehaviour
    {
        public BattleController Controller;
        public PlaybackDirector Director;
        public Camera Camera;
        public int LogLines = 10;
        public float BarHeightAboveUnit = 2.3f;

        private class UnitReadout
        {
            public string Name;
            public Team Team;
            public float Health, MaxHealth, Strain, MaxStrain, Energy, MaxEnergy;
            public readonly List<string> Statuses = new List<string>();
            public bool Dead;
            public string Activity = "";
            // Windup in progress (empty name = none): drawn as a filling bar with the ability name and time left.
            public string WindupName, WindupId;
            public float WindupEnd, WindupFull;
            public bool WindupHeavy;
            public string Order;   // out-of-range order in progress: "Slash > Brawler (chasing)" / "Snipe > Gunner (queued)"
        }

        private readonly Dictionary<string, UnitReadout> units = new Dictionary<string, UnitReadout>();
        private readonly Dictionary<string, ActionDefinition> defs = new Dictionary<string, ActionDefinition>();
        private readonly List<string> log = new List<string>();
        private bool battleOver;

        private void OnEnable()
        {
            if (Director == null) return;
            Director.OnPlaybackReset += Rebuild;
            Director.OnEventDispatched += OnEvent;
        }

        private void OnDisable()
        {
            if (Director == null) return;
            Director.OnPlaybackReset -= Rebuild;
            Director.OnEventDispatched -= OnEvent;
        }

        private void Rebuild()
        {
            units.Clear();
            defs.Clear();
            log.Clear();
            battleOver = false;
            if (Director.Timeline == null) return;
            foreach (var u in Director.Timeline.InitialState.Units)
            {
                units[u.Id] = new UnitReadout
                {
                    Name = u.DisplayName, Team = u.Team,
                    Health = u.Health, MaxHealth = u.MaxHealth, Strain = u.Strain, MaxStrain = u.MaxStrain,
                    Energy = u.Energy, MaxEnergy = u.MaxEnergy,
                };
                foreach (var a in u.Actions)
                    for (var d = a; d != null && !defs.ContainsKey(d.Id); d = d.LinkedAction) defs[d.Id] = d;
            }
        }

        private string NameOf(string id)
        {
            UnitReadout r;
            return id != null && units.TryGetValue(id, out r) ? r.Name : "?";
        }

        private UnitReadout Get(string id)
        {
            UnitReadout r;
            return id != null && units.TryGetValue(id, out r) ? r : null;
        }

        private void OnEvent(TimelineEvent evt)
        {
            var actor = Get(evt.ActorId);
            var target = Get(evt.TargetId);
            switch (evt.Type)
            {
                case TacticsGame.Core.EventType.ActionStarted:
                {
                    var a = (ActionStartedInfo)evt.Payload;
                    if (actor == null) break;
                    if (a.ActionId == "wait") { actor.Activity = "waiting"; break; }
                    actor.Activity = a.ActionId + " (windup)";
                    ActionDefinition def;
                    if (a.Windup > 0f && defs.TryGetValue(a.ActionId, out def))
                    {
                        // A retry after a dodge keeps the original length, so the bar carries on from where it was.
                        bool resumed = actor.WindupId == a.ActionId && actor.WindupName == null && actor.WindupFull >= a.Windup;
                        actor.WindupFull = resumed ? actor.WindupFull : a.Windup;
                        actor.WindupEnd = evt.SimTime + a.Windup;
                        actor.WindupId = a.ActionId;
                        actor.WindupName = def.DisplayName;
                        actor.WindupHeavy = def.Impact == ImpactType.Heavy;
                    }
                    Log(string.Format("{0} winds up {1} on {2}", actor.Name, a.ActionId, NameOf(evt.TargetId)));
                    break;
                }
                case TacticsGame.Core.EventType.ActionResolved:
                    if (actor == null) break;
                    actor.Activity = "recovering";
                    actor.WindupName = actor.WindupId = null;
                    break;
                case TacticsGame.Core.EventType.ActionInterrupted:
                    if (actor == null) break;
                    actor.Activity = "";
                    actor.WindupName = null;
                    if (((InterruptInfo)evt.Payload).Reason != "dodge") actor.WindupId = null;   // a dodge retries it: remember which
                    Log(string.Format("{0}'s action is interrupted ({1})", actor.Name, ((InterruptInfo)evt.Payload).Reason));
                    break;
                case TacticsGame.Core.EventType.MoveStarted:
                    if (actor != null) actor.Activity = "moving";
                    break;
                case TacticsGame.Core.EventType.UnitReady:
                    if (actor != null) actor.Activity = "";
                    break;
                case TacticsGame.Core.EventType.Damage:
                {
                    var d = (DamageInfo)evt.Payload;
                    if (target != null) target.Health = d.RemainingHealth;
                    Log(string.Format("{0} hits {1} with {2} for {3:0}{4}{5}", NameOf(evt.ActorId), NameOf(evt.TargetId), d.ActionId, d.Amount,
                        d.BlockedAmount > 0.5f ? string.Format(" ({0:0} blocked)", d.BlockedAmount) : "",
                        d.Impact == ImpactType.Heavy ? " [HEAVY]" : ""));
                    break;
                }
                case TacticsGame.Core.EventType.Miss:
                    Log(string.Format("{0} misses {1} ({2})", NameOf(evt.ActorId), NameOf(evt.TargetId), ((MissInfo)evt.Payload).Reason));
                    break;
                case TacticsGame.Core.EventType.Dodge:
                    Log(string.Format("{0} dodges {1}", NameOf(evt.TargetId), NameOf(evt.ActorId)));
                    break;
                case TacticsGame.Core.EventType.Stun:
                    if (target != null) target.Activity = "stunned";
                    Log(string.Format("{0} is stunned for {1:0.0}s", NameOf(evt.TargetId), ((StunInfo)evt.Payload).Duration));
                    break;
                case TacticsGame.Core.EventType.Unravel:
                    Log(string.Format("{0} UNRAVELS", NameOf(evt.TargetId)));
                    break;
                case TacticsGame.Core.EventType.StrainChanged:
                    if (actor != null) actor.Strain = ((StrainInfo)evt.Payload).Total;
                    break;
                case TacticsGame.Core.EventType.Heal:
                    if (target != null) target.Health = Mathf.Min(target.MaxHealth, target.Health + (float)evt.Payload);
                    Log(string.Format("{0} heals {1} for {2:0}", NameOf(evt.ActorId), NameOf(evt.TargetId), (float)evt.Payload));
                    break;
                case TacticsGame.Core.EventType.EnergyChanged:
                    if (actor != null) actor.Energy = ((EnergyInfo)evt.Payload).Total;
                    break;
                case TacticsGame.Core.EventType.StatusApplied:
                {
                    var id = (string)evt.Payload;
                    if (target != null && !target.Statuses.Contains(id)) target.Statuses.Add(id);
                    if (id != StatusIds.RapidChain) Log(string.Format("{0} is {1}", NameOf(evt.TargetId), id.Replace('_', ' ')));
                    break;
                }
                case TacticsGame.Core.EventType.StatusExpired:
                    if (actor != null) actor.Statuses.Remove((string)evt.Payload);
                    break;
                case TacticsGame.Core.EventType.TrapPlaced:
                    Log(string.Format("{0} sets a trap", NameOf(evt.ActorId)));
                    break;
                case TacticsGame.Core.EventType.TrapTriggered:
                    Log(string.Format("{0} springs {1}'s trap", NameOf(evt.TargetId), NameOf(evt.ActorId)));
                    break;
                case TacticsGame.Core.EventType.QueuedActionChanged:
                {
                    if (actor == null) break;
                    var q = (QueuedActionInfo)evt.Payload;
                    ActionDefinition qd;
                    if (evt.TargetId == null) { actor.Order = null; break; }
                    string name = defs.TryGetValue(q.ActionId ?? "", out qd) ? qd.DisplayName : q.ActionId;
                    actor.Order = string.Format("{0} > {1} ({2})", name, NameOf(evt.TargetId), q.Chase ? "chasing" : "queued");
                    Log(string.Format("{0} {1} {2} for {3}", actor.Name, q.Chase ? "closes in to" : "queues", name, NameOf(evt.TargetId)));
                    break;
                }
                case TacticsGame.Core.EventType.Death:
                    if (target != null) { target.Dead = true; target.Health = 0f; target.Activity = ""; target.WindupName = null; target.Order = null; }
                    Log(string.Format("{0} is down", NameOf(evt.TargetId)));
                    break;
                case TacticsGame.Core.EventType.BattleEnd:
                    battleOver = true;
                    break;
            }
        }

        private void Log(string line)
        {
            log.Add(line);
            if (log.Count > LogLines) log.RemoveAt(0);
        }

        private void OnGUI()
        {
            UiScale.Begin();
            if (Director == null || Director.Timeline == null) return;
            if (units.Count == 0) Rebuild();
            var cam = Camera != null ? Camera : Camera.main;

            if (cam != null && Controller != null && Controller.Context != null && Controller.Context.Registry != null)
            {
                foreach (var pair in Controller.Context.Registry)
                {
                    var r = Get(pair.Key);
                    if (r == null || pair.Value == null) continue;
                    Vector2 at;
                    if (!UiScale.WorldToGui(cam, pair.Value.transform.position + Vector3.up * BarHeightAboveUnit, out at)) continue;
                    DrawUnit(r, at);
                }
            }

            GUILayout.BeginArea(new Rect(10, 10, 460, 40 + LogLines * 20), GUI.skin.box);
            GUILayout.Label(string.Format("t = {0:0.00}s   playback x{1:0.00}", Director.PlaybackClock, Director.playbackSpeed));
            foreach (var line in log) GUILayout.Label(line);
            GUILayout.EndArea();

            if (battleOver && Director.ReachedTarget && Controller != null && Controller.Sim != null)
            {
                var style = new GUIStyle(GUI.skin.box) { fontSize = 28, alignment = TextAnchor.MiddleCenter, richText = true };
                GUI.Box(new Rect(UiScale.Width / 2f - 220, 40, 440, 90), Controller.Sim.Outcome + "\n<size=16>Press R to replay</size>", style);
            }
        }

        private void DrawUnit(UnitReadout r, Vector2 at)
        {
            const float width = 90f;
            float x = at.x - width / 2f;
            var label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 13 };
            label.normal.textColor = r.Team == Team.Player ? new Color(0.6f, 0.8f, 1f) : new Color(1f, 0.6f, 0.6f);
            string title = r.Dead ? r.Name + " (down)" : r.Name;
            bool winding = !r.Dead && r.WindupName != null;
            GUI.Label(new Rect(x - 40, at.y - (winding ? 48 : 36), width + 80, 18), title, label);
            if (winding) DrawWindup(r, x, at.y, width);
            else if (!string.IsNullOrEmpty(r.Activity) && !r.Dead)
                GUI.Label(new Rect(x - 40, at.y - 20, width + 80, 18), r.Activity, label);

            Bar(new Rect(x, at.y, width, 7), r.MaxHealth > 0f ? r.Health / r.MaxHealth : 0f, new Color(0.3f, 0.9f, 0.3f));
            Bar(new Rect(x, at.y + 9, width, 4), r.MaxStrain > 0f ? r.Strain / r.MaxStrain : 0f, new Color(1f, 0.7f, 0.2f));
            float below = at.y + 15f;
            if (r.MaxEnergy > 0f)
            {
                Bar(new Rect(x, below, width, 4), r.Energy / r.MaxEnergy, new Color(0.35f, 0.6f, 1f));
                below += 6f;
            }
            if (r.Statuses.Count > 0 && !r.Dead)
            {
                var small = new GUIStyle(label) { alignment = TextAnchor.UpperCenter, fontSize = 12 };
                var shown = r.Statuses.FindAll(s => s != StatusIds.RapidChain);
                GUI.Label(new Rect(x - 40, below, width + 80, 16), string.Join(" · ", shown.ToArray()).Replace('_', ' '), small);
                below += 12f;
            }
            if (r.Order != null && !r.Dead)
            {
                var order = new GUIStyle(label) { alignment = TextAnchor.UpperCenter, fontSize = 12, fontStyle = FontStyle.Bold };
                order.normal.textColor = new Color(1f, 0.85f, 0.4f);
                GUI.Label(new Rect(x - 60, below, width + 120, 16), r.Order, order);
            }
        }

        /// <summary>
        /// Windup in progress: "Snipe 0.6s" over a bar that fills to the moment it lands. Orange for Heavy (it will break
        /// windups), blue for Light; it blinks for the last 20% so an incoming hit is easy to spot.
        /// </summary>
        private void DrawWindup(UnitReadout r, float x, float y, float width)
        {
            float left = Mathf.Max(0f, r.WindupEnd - Director.PlaybackClock);
            float fill = r.WindupFull > 0f ? Mathf.Clamp01(1f - left / r.WindupFull) : 1f;
            Color color = r.WindupHeavy ? new Color(1f, 0.55f, 0.15f) : new Color(0.45f, 0.85f, 1f);
            if (fill > 0.8f && Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.5f) color = Color.Lerp(color, Color.white, 0.7f);

            var text = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            text.normal.textColor = color;
            GUI.Label(new Rect(x - 40, y - 32, width + 80, 18), string.Format("{0}  {1:0.0}s", r.WindupName, left), text);

            var bar = new Rect(x - 5f, y - 13f, width + 10f, 8f);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(new Rect(bar.x - 1f, bar.y - 1f, bar.width + 2f, bar.height + 2f), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * fill, bar.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void Bar(Rect rect, float fill, Color color)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
