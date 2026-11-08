using UnityEngine;
using UnityEngine.SceneManagement;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Developer tools, shown and hidden with ~ (the backquote key): end the battle at once, heal or weaken a side,
    /// god mode and one-hit kills, auto-play and playback speed; in a campaign, ability points, Etching rerolls and
    /// skipping missions. The cheats live in the simulation (BattleSimulation.Dev) so the views and HUD follow them.
    /// </summary>
    public class DevMenu : MonoBehaviour
    {
        public KeyCode ToggleKey = KeyCode.BackQuote;
        public BattleController Controller;
        public PlaybackDirector Director;
        public PlayerOrdersUI Orders;
        public CampaignScreens Campaign;

        private bool open;
        private bool godMode, oneHitKills;
        private bool confirmRestart;
        private bool jumpOpen;      // the list of missions to jump to is showing
        private Vector2 jumpScroll;
        private Rect rect, jumpRect;
        private GUIStyle head, small;

        // Put on any scene with a battle, without a Build Scene: on the first scene and every one loaded after it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            AddToScene();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { AddToScene(); }

        private static void AddToScene()
        {
            var controller = FindFirstObjectByType<BattleController>();
            if (controller != null && controller.GetComponent<DevMenu>() == null) controller.gameObject.AddComponent<DevMenu>();
        }

        private void Awake()
        {
            if (Controller == null) Controller = GetComponent<BattleController>();
            if (Director == null) Director = FindFirstObjectByType<PlaybackDirector>();
            if (Orders == null) Orders = FindFirstObjectByType<PlayerOrdersUI>();
            if (Campaign == null) Campaign = FindFirstObjectByType<CampaignScreens>();
        }

        private BattleSimulation Sim { get { return Controller != null ? Controller.Sim : null; } }
        private bool Fighting { get { return Sim != null && Sim.Outcome == BattleOutcome.InProgress; } }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey)) { open = !open; confirmRestart = false; jumpOpen = false; }
            // A new battle gets a new simulation: keep the toggles on it.
            if (Sim != null) { Sim.DevInvulnerablePlayers = godMode; Sim.DevOneHitKills = oneHitKills; }
        }

        private void OnGUI()
        {
            if (!open) return;
            UiScale.Begin();
            GUI.depth = -20;   // in front of the order panel and the campaign screens, and gets their clicks first
            if (head == null)
            {
                head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };
                head.normal.textColor = new Color(1f, 0.75f, 0.4f);
                small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
                small.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            }

            rect = new Rect(10f, 10f, 260f, Mathf.Min(640f, UiScale.Height - 20f));
            GUI.Box(rect, GUIContent.none);
            GUI.Box(rect, GUIContent.none);   // doubled for a more opaque background with the default skin
            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f));
            GUILayout.Label("Dev tools   (" + ToggleKey + " to close)", head);
            DrawBattle();
            DrawCampaign();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload scene")) { SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); GUIUtility.ExitGUI(); }
            GUILayout.EndArea();
            bool listing = jumpOpen && Campaign != null && Campaign.DevRoster != null && Campaign.DevCanJump;
            if (listing) DrawMissionList();

            // Clicks on the panels stop here, so they don't also give orders or pick targets behind them.
            var e = Event.current;
            if (e.isMouse && (rect.Contains(e.mousePosition) || (listing && jumpRect.Contains(e.mousePosition)))) e.Use();
        }

        /// <summary>Beside the panel: every mission of the campaign; picking one goes straight to its preparation.</summary>
        private void DrawMissionList()
        {
            var missions = Campaign.DevMissions;
            if (missions == null) return;
            jumpRect = new Rect(rect.xMax + 6f, rect.y, 280f, rect.height);
            GUI.Box(jumpRect, GUIContent.none);
            GUI.Box(jumpRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(jumpRect.x + 8f, jumpRect.y + 6f, jumpRect.width - 16f, jumpRect.height - 12f));
            GUILayout.Label("Jump to the preparation of:", head);
            jumpScroll = GUILayout.BeginScrollView(jumpScroll);
            int current = Campaign.DevRoster.MissionIndex;
            for (int i = 0; i < missions.Count; i++)
            {
                if (missions[i] == null) continue;
                string label = (i + 1) + ". " + missions[i].Title + (i == current ? "  (current)" : "");
                if (GUILayout.Button(label))
                {
                    Campaign.DevJumpToMission(i);
                    jumpOpen = false;
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawBattle()
        {
            GUILayout.Space(4f);
            GUILayout.Label("Battle", head);
            var sim = Sim;
            if (sim == null) { GUILayout.Label("No battle running.", small); }
            else
            {
                GUILayout.Label(string.Format("{0}   t = {1:0.00}s", sim.Outcome, sim.Now), small);
                GUI.enabled = Fighting;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Win", GUILayout.Height(30f))) { sim.DevKillTeam(Team.Enemy); sim.DevKillTeam(Team.Rogue); Flush(); }
                if (GUILayout.Button("Lose", GUILayout.Height(30f))) { sim.DevKillTeam(Team.Player); Flush(); }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Heal squad")) { sim.DevRestoreTeam(Team.Player); Flush(); }
                if (GUILayout.Button("Heal enemies")) { sim.DevRestoreTeam(Team.Enemy); Flush(); }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Enemies to 1 health")) { sim.DevWeakenTeam(Team.Enemy); Flush(); }
                GUI.enabled = true;
                GUILayout.Label("Heal: full health, no strain, cooldowns ready.", small);
            }
            godMode = GUILayout.Toggle(godMode, " God mode (squad takes no damage)");
            oneHitKills = GUILayout.Toggle(oneHitKills, " One-hit kills (squad's damage)");
            if (Orders != null) Orders.AutoPlay = GUILayout.Toggle(Orders.AutoPlay, " Auto-play (AI gives the squad's orders)");
            if (Director != null)
            {
                GUILayout.Label(string.Format("Playback speed: {0:0.##}x", Director.playbackSpeed), small);
                GUILayout.BeginHorizontal();
                float speed = GUILayout.HorizontalSlider(Director.playbackSpeed, 0.25f, 4f);
                if (GUILayout.Button("1x", GUILayout.Width(32f))) speed = 1f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(speed, Director.playbackSpeed)) Director.SetPlaybackSpeed(speed);
            }
        }

        private void DrawCampaign()
        {
            var roster = Campaign != null ? Campaign.DevRoster : null;
            if (roster == null) return;
            GUILayout.Space(8f);
            GUILayout.Label("Campaign", head);
            GUILayout.Label(string.Format("Mission {0}, screen: {1}", roster.MissionIndex + 1, Campaign.DevScreenName), small);
            if (GUILayout.Button("+5 ability points (everyone)")) Campaign.DevGrantPoints(5);
            GUI.enabled = Campaign.DevUpgrading;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reroll Etchings")) Campaign.DevRerollOffers();
            if (GUILayout.Button("+1 pick")) Campaign.DevExtraPick();
            GUILayout.EndHorizontal();
            string who = Campaign.DevMemberName;
            GUILayout.Label(who != null ? "Abilities: " + who : "Abilities (on the upgrade screen)", small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reroll abilities")) Campaign.DevRerollAbilities();
            if (GUILayout.Button("Gain all")) Campaign.DevUnlockAllAbilities();
            GUILayout.EndHorizontal();
            GUI.enabled = Campaign.DevCanJump;
            if (GUILayout.Button(jumpOpen ? "Close mission list" : "Jump to mission...")) jumpOpen = !jumpOpen;
            GUI.enabled = Campaign.DevPreparing;
            if (GUILayout.Button("Unlock all abilities (squad)")) { Campaign.DevUnlockSquadAbilities(); GUIUtility.ExitGUI(); }
            if (GUILayout.Button("Scale squad to this mission")) { Campaign.DevScaleSquadToMission(); GUIUtility.ExitGUI(); }
            if (GUILayout.Button("Skip mission (count as won)")) { Campaign.DevSkipMission(); GUIUtility.ExitGUI(); }
            GUI.enabled = !Campaign.DevInBattle;
            if (GUILayout.Button(confirmRestart ? "Click again to restart" : "Restart campaign"))
            {
                if (confirmRestart) { Campaign.DevRestartCampaign(); confirmRestart = false; GUIUtility.ExitGUI(); }
                else confirmRestart = true;
            }
            GUI.enabled = true;
        }

        /// <summary>A cheat's events land at the current sim time: play them out even while the world is held for orders.</summary>
        private void Flush()
        {
            if (Director != null && Sim != null) Director.PlayUntil(Sim.Now);
        }
    }
}
