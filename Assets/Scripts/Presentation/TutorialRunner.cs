using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Plays a TutorialDefinition over a battle. CampaignScreens starts it (Begin) just before launching the battle; from
    /// then on it shows one step at a time as a popup and watches for the step's goal:
    ///   orders given        BattleSimulation.CommandSubmitted / MoveCancelled (moves, waypoints, Focus, plans, stopping a walk);
    ///   what happens        the timeline as it plays (PlaybackDirector.OnEventDispatched): abilities starting, hits, dodges and
    ///                       dash, out-of-range orders, enemies falling, so a goal completes when the player sees it happen;
    ///   the camera and keys TacticsCamera's by-hand counters and Input.
    /// A Continue step holds the battle: playback freezes and the order panel greys out (PlayerOrdersUI.Suspended) until it
    /// is dismissed. A goal step leaves the battle running and shows what to do (and a Skip button). As each step starts it
    /// may place enemies (BattleSimulation.SpawnUnitNow), restore the squad, and set whether the enemies fight
    /// (TutorialEnemies: held enemies get IdleAI) and whether the squad dodges every attack.
    /// The battle is scripted (BattleSimulation.HoldVictory): it doesn't end when the enemies are down; once the last step is
    /// done a closing popup ends it as a win, and Exit leaves at any time. Either way OnExit fires.
    /// </summary>
    public class TutorialRunner : MonoBehaviour
    {
        public BattleController Controller;
        public PlaybackDirector Director;
        public PlayerOrdersUI Orders;
        [Tooltip("The tactical camera rig; found automatically if left empty.")]
        public TacticsCamera CameraRig;
        [Tooltip("Width of the step popup, in GUI units.")]
        public float PopupWidth = 430f;

        /// <summary>The player left the tutorial (finished it, or Exit). The battle is over by then.</summary>
        public event System.Action OnExit;

        public bool Running { get { return tutorial != null; } }
        public TutorialStep Current { get { return tutorial != null && index >= 0 && index < tutorial.Steps.Count ? tutorial.Steps[index] : null; } }

        private TutorialDefinition tutorial;
        private BattleSimulation sim;
        private int index = -1;
        private bool done;              // every step finished: the closing popup is up
        private int progress;           // how many times the current goal has happened
        private bool collapsed;         // a goal step's popup shrunk to one line while the player does what it asks
        private float expandedAt;       // when it was last shown in full (unscaled time)
        private float camAtExpand;      // the camera counters' total then
        private float stepStartClock;   // playback time the step started at (Wait)
        private float panAt, rotateAt, zoomAt;   // the camera counters when the step started
        private int frameOfOrders = -1;          // QueueOrders: orders given in one frame, per unit
        private readonly Dictionary<string, int> ordersThisFrame = new Dictionary<string, int>();
        private readonly HashSet<string> justDodged = new HashSet<string>();     // DodgeDash: player units that dodged and may dash
        private readonly HashSet<string> enemiesShown = new HashSet<string>();   // enemies alive as playback shows them
        private readonly HashSet<string> removed = new HashSet<string>();        // enemies a step took off the field (RemoveEnemies)
        private readonly Dictionary<string, UnitDefinition> classOf = new Dictionary<string, UnitDefinition>();   // squad unit id -> its class
        private GUIStyle titleStyle, textStyle, smallStyle, boxStyle;

        // ------------------------------------------------------------------ starting and ending

        /// <summary>
        /// Run 'definition' over the battle about to be launched: call just before BattleLauncher.Launch. Its rules go on the
        /// simulation as soon as that is made (BattleController.OnSimCreated), before it runs.
        /// </summary>
        public void Begin(TutorialDefinition definition)
        {
            if (definition == null) return;
            tutorial = definition;
            index = -1; done = false;
            if (CameraRig == null) CameraRig = FindFirstObjectByType<TacticsCamera>();
            if (Orders == null) Orders = FindFirstObjectByType<PlayerOrdersUI>();
            if (Director == null && Controller != null) Director = Controller.Director;
            Controller.OnSimCreated -= Attach;
            Controller.OnSimCreated += Attach;
        }

        private void Attach(BattleSimulation s)
        {
            Controller.OnSimCreated -= Attach;
            sim = s;
            sim.HoldVictory = true;
            sim.PlayerHealthFloor = tutorial.SquadHealthFloor;
            sim.EnemyAI = new HeldAI(this, sim.EnemyAI);
            sim.CommandSubmitted += OnCommand;
            sim.MoveCancelled += OnMoveCancelled;
            Director.OnEventDispatched -= OnEvent;
            Director.OnEventDispatched += OnEvent;
            classOf.Clear();
            foreach (var p in Controller.Context.Placements)
                if (p.Team == Team.Player && p.View != null && p.Definition != null) classOf[p.View.UnitId] = p.Definition;
            enemiesShown.Clear();
            foreach (var spawn in tutorial.StartingEnemies) Place(spawn);
            StartStep(0);
        }

        /// <summary>Leave: end the battle (a win once every step is done) and tell CampaignScreens.</summary>
        public void Exit()
        {
            if (sim != null)
            {
                sim.CommandSubmitted -= OnCommand;
                sim.MoveCancelled -= OnMoveCancelled;
                sim.FinishBattle(done ? BattleOutcome.PlayerVictory : BattleOutcome.PlayerDefeat);
            }
            if (Director != null) { Director.OnEventDispatched -= OnEvent; Director.Paused = false; }
            if (Orders != null) Orders.Suspended = false;
            tutorial = null;
            sim = null;
            if (OnExit != null) OnExit();
        }

        private void OnDestroy()
        {
            if (Controller != null) Controller.OnSimCreated -= Attach;
            if (Director != null) Director.OnEventDispatched -= OnEvent;
            if (sim != null) { sim.CommandSubmitted -= OnCommand; sim.MoveCancelled -= OnMoveCancelled; }
        }

        // ------------------------------------------------------------------ steps

        private void StartStep(int i)
        {
            index = i;
            progress = 0;
            justDodged.Clear();
            collapsed = false; expandedAt = Time.unscaledTime; camAtExpand = CameraTotal();
            var step = Current;
            if (step == null) { done = true; sim.PlayersAlwaysDodge = false; Hold(true); return; }
            stepStartClock = Director.PlaybackClock;
            if (CameraRig != null) { panAt = CameraRig.PannedByInput; rotateAt = CameraRig.RotatedByInput; zoomAt = CameraRig.ZoomedByInput; }
            if (step.RemoveEnemies)
            {
                foreach (var u in sim.State.Units) if (u.Team == Team.Enemy && u.IsAlive) removed.Add(u.Id);   // their deaths don't count as kills
                sim.DevKillTeam(Team.Enemy);   // HoldVictory: the battle goes on
            }
            if (step.RestoreSquad) sim.DevRestoreTeam(Team.Player);
            foreach (var spawn in step.Spawns) Place(spawn);
            sim.PlayersAlwaysDodge = step.SquadAlwaysDodges;
            Hold(step.Goal == TutorialGoal.Continue);
            Director.PlayUntil(sim.Now);   // so what was just placed or healed shows now, while the battle waits for orders
        }

        private void NextStep() { StartStep(index + 1); }

        /// <summary>Freeze the battle for a popup that waits to be read, or let it run.</summary>
        private void Hold(bool hold)
        {
            Director.Paused = hold;
            if (Orders != null) Orders.Suspended = hold;
        }

        /// <summary>Count of 'spawn.Unit' around its position, a couple of metres apart.</summary>
        private void Place(TutorialSpawn spawn)
        {
            if (spawn == null || spawn.Unit == null) return;
            int n = Mathf.Max(1, spawn.Count);
            for (int k = 0; k < n; k++)
            {
                float offset = (k - (n - 1) / 2f) * 2.5f;
                sim.SpawnUnitNow(spawn.Unit, Team.Enemy, spawn.Position + new Vector3(offset, 0f, 0f));
            }
        }

        /// <summary>The goal happened once more: on to the next step once it has happened Count times.</summary>
        private void Count(int times = 1)
        {
            var step = Current;
            if (step == null || done) return;
            progress += times;
            if (progress >= Mathf.Max(1, step.Count)) NextStep();
        }

        // ------------------------------------------------------------------ watching

        private bool Is(TutorialGoal goal) { var s = Current; return s != null && !done && s.Goal == goal; }

        /// <summary>Does this squad unit's order count for the step (its UnitClass filter)?</summary>
        private bool ClassMatches(string unitId)
        {
            var step = Current;
            if (step == null || string.IsNullOrEmpty(step.UnitClass)) return true;
            UnitDefinition def;
            if (!classOf.TryGetValue(unitId ?? "", out def) || def == null) return false;
            string want = step.UnitClass.Trim();
            return string.Equals(def.Id, want, System.StringComparison.OrdinalIgnoreCase) || string.Equals(def.name, want, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(def.DisplayName, want, System.StringComparison.OrdinalIgnoreCase);
        }

        private bool AbilityMatches(string actionId)
        {
            var step = Current;
            return step == null || string.IsNullOrEmpty(step.AbilityId) || string.Equals(step.AbilityId.Trim(), actionId, System.StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPlayer(string unitId) { var u = sim != null ? sim.GetUnit(unitId) : null; return u != null && u.Team == Team.Player; }
        private bool IsEnemy(string unitId) { var u = sim != null ? sim.GetUnit(unitId) : null; return u != null && u.Team == Team.Enemy; }

        private void OnCommand(UnitCommand cmd)
        {
            if (Running && !done && Current != null && Current.Goal != TutorialGoal.Continue) collapsed = true;   // the player is on it
            if (!Running || done || !ClassMatches(cmd.UnitId)) return;
            if (cmd.Type == CommandType.Move)
            {
                if (Is(TutorialGoal.Move)) Count();
                else if (Is(TutorialGoal.MoveWithWaypoints) && cmd.Waypoints != null && cmd.Waypoints.Count > 0) Count();
            }
            if (cmd.Type == CommandType.Wait && !cmd.NoFocus && Is(TutorialGoal.Focus)) Count();
            // A confirmed plan sends all its orders in the same frame.
            if (frameOfOrders != Time.frameCount) { frameOfOrders = Time.frameCount; ordersThisFrame.Clear(); }
            int n;
            ordersThisFrame.TryGetValue(cmd.UnitId, out n);
            ordersThisFrame[cmd.UnitId] = ++n;
            if (n == 2 && Is(TutorialGoal.QueueOrders)) Count();
        }

        private void OnMoveCancelled(Unit u)
        {
            if (Running && Is(TutorialGoal.StopMove) && ClassMatches(u.Id)) Count();
        }

        private void OnEvent(TimelineEvent e)
        {
            if (!Running) return;
            switch (e.Type)
            {
                case Core.EventType.UnitSpawned:
                    if (IsEnemy(e.TargetId)) enemiesShown.Add(e.TargetId);
                    break;
                case Core.EventType.Death:
                    if (enemiesShown.Remove(e.TargetId) && !removed.Contains(e.TargetId) && Is(TutorialGoal.DefeatEnemies)) Count();
                    break;
                case Core.EventType.ActionStarted:
                    if (!(e.Payload is ActionStartedInfo) || !IsPlayer(e.ActorId) || !ClassMatches(e.ActorId)) break;
                    var started = (ActionStartedInfo)e.Payload;
                    if (started.ActionId == "wait" || started.ActionId == "idle") break;   // Focus and pauses log as actions too
                    if (!AbilityMatches(started.ActionId)) break;
                    if (Is(TutorialGoal.UseAbility)) Count();
                    else if (Is(TutorialGoal.Transcend) && started.Transcended) Count();
                    break;
                case Core.EventType.Damage:
                    if (!(e.Payload is DamageInfo) || !IsPlayer(e.ActorId) || !IsEnemy(e.TargetId) || !ClassMatches(e.ActorId)) break;
                    var hit = (DamageInfo)e.Payload;
                    if (hit.Amount > 0f && AbilityMatches(hit.ActionId) && Is(TutorialGoal.HitEnemy)) Count();
                    break;
                case Core.EventType.Dodge:
                    if (IsPlayer(e.TargetId)) justDodged.Add(e.TargetId);
                    break;
                case Core.EventType.Dash:
                    if (e.Payload is DashInfo && !((DashInfo)e.Payload).Forced && justDodged.Remove(e.ActorId) && ClassMatches(e.ActorId) && Is(TutorialGoal.DodgeDash)) Count();
                    break;
                case Core.EventType.QueuedActionChanged:
                    if (e.TargetId != null && IsPlayer(e.ActorId) && ClassMatches(e.ActorId) && Is(TutorialGoal.ChaseOrQueue)) Count();
                    break;
            }
        }

        private void Update()
        {
            if (!Running || sim == null || done) return;
            var step = Current;
            if (step == null) return;
            AutoCollapse(step);
            switch (step.Goal)
            {
                case TutorialGoal.PressKey:
                    if (step.Key != KeyCode.None && Input.GetKeyDown(step.Key)) Count();
                    break;
                case TutorialGoal.PanCamera:
                    if (CameraRig == null || CameraRig.PannedByInput - panAt >= Amount(step, 4f)) NextStep();
                    break;
                case TutorialGoal.RotateCamera:
                    if (CameraRig == null || CameraRig.RotatedByInput - rotateAt >= Amount(step, 45f)) NextStep();
                    break;
                case TutorialGoal.ZoomCamera:
                    if (CameraRig == null || CameraRig.ZoomedByInput - zoomAt >= Amount(step, 2f)) NextStep();
                    break;
                case TutorialGoal.Wait:
                    if (Director.PlaybackClock - stepStartClock >= Amount(step, 2f)) NextStep();
                    break;
                case TutorialGoal.DefeatAllEnemies:
                    // Every enemy down, as the battle shows it and in the simulation (one just placed has not shown up yet).
                    if (enemiesShown.Count == 0 && !sim.State.Units.Exists(u => u.Team == Team.Enemy && u.IsAlive)) NextStep();
                    break;
            }
        }

        private static float Amount(TutorialStep step, float fallback) { return step.Amount > 0f ? step.Amount : fallback; }

        // ------------------------------------------------------------------ the popup

        private void OnGUI()
        {
            if (!Running || sim == null || UiDeclutter.On) return;   // Alt hides it with the rest of the interface
            GUI.depth = -20;   // over the order panel and its tooltips
            UiScale.Begin();
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true, richText = true };
                titleStyle.normal.textColor = new Color(1f, 0.88f, 0.5f);
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, richText = true };
                textStyle.normal.textColor = new Color(0.93f, 0.93f, 0.93f);
                smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, richText = true };
                smallStyle.normal.textColor = new Color(0.7f, 0.8f, 0.9f);
                boxStyle = new GUIStyle(GUI.skin.box);
            }

            var step = Current;
            if (!done && collapsed && step.Goal != TutorialGoal.Continue) { DrawCollapsed(step); return; }
            string title = done ? tutorial.FinishedTitle : step.Title;
            string text = done ? tutorial.FinishedText : step.Text;
            bool waitsForClick = done || step.Goal == TutorialGoal.Continue;
            string goalLine = done ? null : GoalLine(step);

            float w = Mathf.Min(PopupWidth, UiScale.Width - 20f), inner = w - 24f;
            float h = 12f + titleStyle.CalcHeight(new GUIContent(title), inner) + 6f + textStyle.CalcHeight(new GUIContent(text), inner);
            if (!string.IsNullOrEmpty(goalLine)) h += 6f + smallStyle.CalcHeight(new GUIContent(goalLine), inner);
            h += 6f + 18f + 8f + 28f + 10f;   // step count, buttons
            var rect = Place(done ? TutorialPlace.Center : step.Place, w, h);

            GUI.Box(rect, GUIContent.none, boxStyle);
            GUI.Box(rect, GUIContent.none, boxStyle);
            GUI.Box(rect, GUIContent.none, boxStyle);   // tripled for a nearly opaque background with the default skin
            float x = rect.x + 12f, y = rect.y + 10f;
            float th = titleStyle.CalcHeight(new GUIContent(title), inner);
            GUI.Label(new Rect(x, y, inner, th), title, titleStyle); y += th + 6f;
            float bh = textStyle.CalcHeight(new GUIContent(text), inner);
            GUI.Label(new Rect(x, y, inner, bh), text, textStyle); y += bh + 6f;
            if (!string.IsNullOrEmpty(goalLine))
            {
                float gh = smallStyle.CalcHeight(new GUIContent(goalLine), inner);
                GUI.Label(new Rect(x, y, inner, gh), goalLine, smallStyle); y += gh + 6f;
            }
            GUI.Label(new Rect(x, y, inner, 18f), done ? "" : string.Format("Step {0} of {1}", index + 1, tutorial.Steps.Count), smallStyle);
            y += 26f;

            var e = Event.current;
            bool enter = waitsForClick && e.type == UnityEngine.EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            if (enter) e.Use();
            if (done)
            {
                if (GUI.Button(new Rect(x, y, inner, 28f), string.IsNullOrEmpty(tutorial.FinishedButton) ? "Finish" : tutorial.FinishedButton) || enter) Exit();
                return;
            }
            float exitW = 110f;
            if (waitsForClick)
            {
                if (GUI.Button(new Rect(x, y, inner - exitW - 8f, 28f), "Continue (Enter)") || enter) NextStep();
            }
            else
            {
                // A goal step: "Got it" shrinks it to one line at the side while the player does what it asks.
                if (GUI.Button(new Rect(x, y, 90f, 28f), "Got it")) collapsed = true;
                if (step.Skippable && GUI.Button(new Rect(x + 98f, y, 70f, 28f), "Skip")) NextStep();
            }
            if (GUI.Button(new Rect(x + inner - exitW, y, exitW, 28f), "Exit tutorial")) Exit();
        }

        /// <summary>The camera's by-hand counters added up (any pan, turn or zoom moves it).</summary>
        private float CameraTotal()
        {
            return CameraRig != null ? CameraRig.PannedByInput + CameraRig.RotatedByInput / 20f + CameraRig.ZoomedByInput : 0f;
        }

        /// <summary>
        /// A goal step's popup gets out of the way once the player starts on it: aiming an order, giving one (OnCommand), or
        /// moving the camera, after a moment to read it. "Show" brings it back until the player acts again.
        /// </summary>
        private void AutoCollapse(TutorialStep step)
        {
            if (collapsed || step.Goal == TutorialGoal.Continue) return;
            if (Orders != null && Orders.Aiming) { collapsed = true; return; }
            if (Time.unscaledTime - expandedAt > ReadSeconds && CameraTotal() - camAtExpand > 0.3f) collapsed = true;
        }

        /// <summary>Seconds a goal step's popup stays in full before moving the camera can shrink it.</summary>
        public float ReadSeconds = 1.5f;

        /// <summary>The shrunk popup: one line at the side with what it waits for, and Show / Skip.</summary>
        private void DrawCollapsed(TutorialStep step)
        {
            string line = "<b>" + step.Title + "</b>   " + (GoalLine(step) ?? "") + (step.Count > 1 ? string.Format("  ({0}/{1})", progress, step.Count) : "");
            float w = Mathf.Min(PopupWidth, UiScale.Width - 20f), inner = w - 24f - 120f;
            float h = Mathf.Max(28f, smallStyle.CalcHeight(new GUIContent(line), inner)) + 12f;
            var rect = Place(step.Place == TutorialPlace.Center ? TutorialPlace.Side : step.Place, w, h);
            GUI.Box(rect, GUIContent.none, boxStyle);
            GUI.Box(rect, GUIContent.none, boxStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, inner, h - 12f), line, smallStyle);
            if (GUI.Button(new Rect(rect.xMax - 122f, rect.y + 6f, 56f, 24f), "Show")) { collapsed = false; expandedAt = Time.unscaledTime; camAtExpand = CameraTotal(); }
            if (step.Skippable && GUI.Button(new Rect(rect.xMax - 62f, rect.y + 6f, 52f, 24f), "Skip")) NextStep();
        }

        /// <summary>What the step is waiting for, under its text: "Waiting for: ..." and how far along a counted goal is.</summary>
        private static string GoalLine(TutorialStep step)
        {
            string what;
            switch (step.Goal)
            {
                case TutorialGoal.Continue: return null;
                case TutorialGoal.PressKey: what = "press " + step.Key; break;
                case TutorialGoal.PanCamera: what = "pan the camera"; break;
                case TutorialGoal.RotateCamera: what = "rotate the camera"; break;
                case TutorialGoal.ZoomCamera: what = "zoom the camera"; break;
                case TutorialGoal.Move: what = "a move order"; break;
                case TutorialGoal.MoveWithWaypoints: what = "a move through waypoints"; break;
                case TutorialGoal.UseAbility: what = string.IsNullOrEmpty(step.AbilityId) ? "an ability" : AbilityName(step.AbilityId); break;
                case TutorialGoal.HitEnemy: what = "a hit on an enemy"; break;
                case TutorialGoal.Focus: what = "Focus (hold Space)"; break;
                case TutorialGoal.QueueOrders: what = "a plan of two or more orders, confirmed"; break;
                case TutorialGoal.Transcend: what = "a Transcended ability"; break;
                case TutorialGoal.DodgeDash: what = "a dodge dash"; break;
                case TutorialGoal.StopMove: what = "a walk stopped"; break;
                case TutorialGoal.ChaseOrQueue: what = "an attack ordered out of range"; break;
                case TutorialGoal.DefeatEnemies: what = step.Count == 1 ? "an enemy defeated" : "enemies defeated"; break;
                case TutorialGoal.DefeatAllEnemies: what = "every enemy defeated"; break;
                default: return null;
            }
            return "<i>Waiting for: " + what + "</i>";
        }

        private static string AbilityName(string id)
        {
            var words = id.Replace('_', ' ').Split(' ');
            for (int i = 0; i < words.Length; i++) if (words[i].Length > 0) words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        /// <summary>
        /// Where the popup goes: Left sits top left, above the battle log (bottom left, over the unit panel), Right under the controls list's
        /// corner, Top below the timeline, Center in the middle. Always on screen.
        /// </summary>
        private Rect Place(TutorialPlace place, float w, float h)
        {
            float W = UiScale.Width, H = UiScale.Height;
            float bottom = H - PlayerOrdersUI.BottomReserved - 8f;
            if (UnitPanel.ScreenRect.height > 0f && (place == TutorialPlace.Left || place == TutorialPlace.Side)) bottom = Mathf.Min(bottom, UnitPanel.ScreenRect.y - 8f);
            if (BattleHUD.LogRect.height > 0f && (place == TutorialPlace.Left || place == TutorialPlace.Side)) bottom = Mathf.Min(bottom, BattleHUD.LogRect.y - 8f);
            float x, y;
            switch (place)
            {
                case TutorialPlace.Center: x = (W - w) / 2f; y = (H - h) / 2f; break;
                case TutorialPlace.Right: x = W - w - 10f; y = 200f; break;
                case TutorialPlace.Top: x = (W - w) / 2f; y = 190f; break;
                default: x = 10f; y = 160f; break;   // Side and Left: top left, clear of the timeline
            }
            if (y + h > bottom) y = Mathf.Max(4f, bottom - h);
            return new Rect(Mathf.Clamp(x, 4f, Mathf.Max(4f, W - w - 4f)), Mathf.Clamp(y, 4f, Mathf.Max(4f, H - h - 4f)), w, h);
        }

        // ------------------------------------------------------------------ the enemies' orders

        /// <summary>The enemies' AI while the tutorial runs: they stand still (IdleAI) unless the step lets them fight.</summary>
        private class HeldAI : IUnitAI
        {
            private readonly TutorialRunner runner;
            private readonly IUnitAI fight;
            private readonly IdleAI idle = new IdleAI();

            public HeldAI(TutorialRunner runner, IUnitAI fight) { this.runner = runner; this.fight = fight ?? new AIRouter(); }

            public UnitCommand Decide(BattleSimulation sim, Unit unit)
            {
                var step = runner.Current;
                bool fighting = step != null && step.Enemies == TutorialEnemies.Fight && !runner.done;
                return fighting ? fight.Decide(sim, unit) : idle.Decide(sim, unit);
            }
        }
    }
}
