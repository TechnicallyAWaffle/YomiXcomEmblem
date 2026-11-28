using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Runs a campaign (CampaignDefinition) as a loop of screens over the squad roster (saved between sessions):
    ///   New campaign: pick how the squad starts: the tuned default loadouts or random ones (ProgressionConfig).
    ///   Preparation:  the next mission, its map set out behind the panel with the enemies on it (BattleLauncher.Preview),
    ///                 who they are, what it pays, and the squad with its Etchings.
    ///   Battle:       the launcher fights it. Afterwards the result (and what was earned); Continue goes on.
    ///   Upgrades:     after each win (and before the first mission if the squad starts with Grasp): every member, in any
    ///                 order (a tab each), spends Grasp on Form (stat rituals), Cognition (Etchings) and Knowledge
    ///                 (abilities), and can destroy what it has (Progression). Then the next mission.
    /// A win moves on to the next mission (built by the campaign's mission builder, or hand-made); a loss ends the run
    /// (CampaignDefinition.LossEndsRun) or retries it. After the last mission the campaign is complete. The scene reloads between battles, so each one starts clean; the roster's Stage says
    /// which screen comes up. While a campaign is set, the custom-battle screen (PreBattleSetup) stays off.
    /// Uses GUILayout: anything that changes the set of controls ends the GUI pass with GUIUtility.ExitGUI.
    /// </summary>
    public class CampaignScreens : MonoBehaviour
    {
        public BattleController Controller;
        public BattleContext Context;
        public BattleLauncher Launcher;
        public CampaignDefinition Campaign;
        [Tooltip("Resolves the roster's classes. Empty: Resources/ContentCatalog, else the campaign's squad and the missions' enemies.")]
        public ContentCatalog Catalog;
        [Tooltip("The tutorial offered on the new-campaign and preparation screens (empty: none). Its steps and text are edited on its asset.")]
        public TutorialDefinition Tutorial;
        [Tooltip("Plays the tutorial over its battle; added to this object if empty.")]
        public TutorialRunner TutorialRunner;
        public const string SaveFile = "campaign.json";
        private const string TutorialSeenKey = "TacticsGame.TutorialPlayed";

        private enum Screen { NewCampaign, Preparation, Battle, Results, Upgrades, Finished, Tutorial, ClassSelect }

        private SquadRoster roster;
        private Screen screen;
        private EncounterResult result;
        private int memberIndex;
        private string etchingId;   // a One Ability Etching clicked: its ability is being picked
        private string firstAbilityId;   // a Two Abilities Etching: the first of its pair, picked (the second is being picked)
        private Vector2 scroll;
        private GUIStyle head, title, small, card, wrapped, tipStyle, tabStyle, graspStyle, ritualStyle, holdStyle, formRowStyle;
        private static readonly Color OneAbilityCard = new Color(0.45f, 1f, 0.95f), UnitCard = new Color(1f, 0.9f, 0.7f);   // card tints
        private const float AbilityColumn = 330f;   // the upgrade panel's right column (names wrap rather than cut off)
        private string message;

        private ProgressionConfig Config { get { return Campaign != null ? Campaign.Progression : null; } }
        // The mission being prepared or fought, made once per mission (a built one is a new EncounterDefinition each time it is made).
        private EncounterDefinition missionCache;
        private int missionCacheIndex = -1;
        private MissionPlan missionCachePlan;

        private EncounterDefinition Mission
        {
            get
            {
                if (Campaign == null || roster == null) return null;
                int i = roster.MissionIndex;
                var plan = roster.Run != null ? roster.Run.Plan : null;
                if (missionCacheIndex != i || (Campaign.IsBuilt(i) && (plan == null || plan != missionCachePlan || missionCache == null)))
                {
                    missionCache = Campaign.Mission(i, roster, Catalog);
                    missionCacheIndex = i;
                    missionCachePlan = roster.Run != null ? roster.Run.Plan : null;
                }
                return missionCache;
            }
        }

        private void Start()
        {
            if (Campaign == null || Controller == null || Context == null || Launcher == null) { enabled = false; return; }
            var custom = GetComponent<PreBattleSetup>();
            if (custom != null) custom.enabled = false;   // the campaign runs the battles
            if (Catalog == null) Catalog = Resources.Load<ContentCatalog>(ContentCatalog.ResourceName);
            if (Catalog == null) Catalog = CampaignCatalog();
            Launcher.OnEncounterFinished += Finished;

            roster = RosterSave.Load(SaveFile);
            if (roster == null || roster.Members.Count == 0) { screen = Screen.NewCampaign; return; }
            if (roster.Run == null) roster.Run = new MissionRunState();
            Show(roster.Stage == CampaignStage.Upgrading ? Screen.Upgrades : roster.Stage == CampaignStage.Finished || roster.Stage == CampaignStage.Lost ? Screen.Finished : Screen.Preparation);
        }

        private void OnDestroy() { if (Launcher != null) Launcher.OnEncounterFinished -= Finished; }

        private ContentCatalog CampaignCatalog()
        {
            var c = ScriptableObject.CreateInstance<ContentCatalog>();
            foreach (var u in Campaign.Squad) if (u != null && !c.Units.Contains(u)) c.Units.Add(u);
            if (Campaign.Classes != null) foreach (var u in Campaign.Classes) if (u != null && !c.Units.Contains(u)) c.Units.Add(u);
            return c;
        }

        private void Save() { RosterSave.Save(roster, SaveFile); }

        // ------------------------------------------------------------------ dev menu (DevMenu)

        public SquadRoster DevRoster { get { return enabled ? roster : null; } }
        public bool DevInBattle { get { return screen == Screen.Battle; } }
        public bool DevUpgrading { get { return screen == Screen.Upgrades; } }
        public bool DevPreparing { get { return screen == Screen.Preparation; } }
        public string DevScreenName { get { return screen.ToString(); } }

        /// <summary>Every member's max Grasp grows by 'grasp'.</summary>
        public void DevGrantGrasp(int grasp)
        {
            if (roster == null) return;
            foreach (var m in roster.Members) m.GraspEarned += grasp;
            Save();
        }

        /// <summary>A fresh draw of every member's Etching offer.</summary>
        public void DevRerollOffers()
        {
            if (roster == null || screen != Screen.Upgrades) return;
            foreach (var m in roster.Members) Progression.RedrawEtchings(roster, m, Config, Catalog != null ? Catalog.Unit(m.Class) : null);
            etchingId = null;
            Save();
        }

        /// <summary>The member being upgraded now, and its class (null off the upgrade screen).</summary>
        private RosterMember DevMember(out UnitDefinition def)
        {
            def = null;
            if (roster == null || screen != Screen.Upgrades || memberIndex < 0 || memberIndex >= roster.Members.Count) return null;
            var m = roster.Members[memberIndex];
            def = Catalog != null ? Catalog.Unit(m.Class) : null;
            return def != null ? m : null;
        }

        public string DevMemberName { get { UnitDefinition def; var m = DevMember(out def); return m != null ? UnitName(m) : null; } }

        /// <summary>A fresh random draw of the member's abilities to learn (not the seeded one).</summary>
        public void DevRerollAbilities()
        {
            UnitDefinition def;
            var m = DevMember(out def);
            if (m == null || Config == null) return;
            Progression.DrawAbilityOffer(m, def, Config, new System.Random(System.Environment.TickCount));
            Save();
        }

        /// <summary>The member learns every ability of its class, free.</summary>
        public void DevUnlockAllAbilities()
        {
            UnitDefinition def;
            var m = DevMember(out def);
            if (m == null) return;
            foreach (var a in Progression.Locked(m, def)) m.Unlocked.Add(a.Id);
            m.AbilityOffer.Clear();
            message = UnitName(m) + " knows every ability.";
            Save();
        }

        /// <summary>How many missions the campaign has, and each one's name, for the dev menu's list.</summary>
        public int DevMissionCount { get { return Campaign != null ? Campaign.MissionCount : 0; } }
        public string DevMissionTitle(int index) { return Campaign != null ? Campaign.MissionTitle(index) : ""; }

        /// <summary>The screens a jump to another mission can be made from.</summary>
        public bool DevCanJump { get { return screen == Screen.Preparation || screen == Screen.Upgrades; } }

        /// <summary>Straight to the preparation of mission 'index' (a built one is built afresh). Open offers are dropped; nothing is earned.</summary>
        public void DevJumpToMission(int index)
        {
            if (roster == null || !DevCanJump || index < 0 || index >= Campaign.MissionCount) return;
            roster.MissionIndex = index;
            roster.Stage = CampaignStage.Preparation;
            if (roster.Run != null) roster.Run.Plan = null;
            Progression.EndUpgrades(roster);
            Save();
            Show(Screen.Preparation);
        }

        /// <summary>Every squad member learns every ability of its class, free (from preparation).</summary>
        public void DevUnlockSquadAbilities()
        {
            if (roster == null || screen != Screen.Preparation) return;
            foreach (var m in roster.Members)
            {
                var def = Catalog != null ? Catalog.Unit(m.Class) : null;
                if (def == null) continue;
                foreach (var a in Progression.Locked(m, def)) m.Unlocked.Add(a.Id);
            }
            Save();
            Show(Screen.Preparation);
            message = "The squad knows every ability.";
        }

        /// <summary>
        /// Bring the squad up to the mission it is about to fight (from preparation): the Grasp a member would have by now,
        /// had it won every mission before this one, spent at random on abilities and Etchings (Progression.CatchUp).
        /// </summary>
        public void DevScaleSquadToMission()
        {
            if (roster == null || screen != Screen.Preparation || Config == null) return;
            int grasp = Campaign.GraspBefore(roster.MissionIndex);
            var rng = new System.Random(System.Environment.TickCount);
            int learned = 0, etched = 0;
            foreach (var m in roster.Members)
            {
                var def = Catalog != null ? Catalog.Unit(m.Class) : null;
                if (def == null) continue;
                var got = Progression.CatchUp(m, def, Config, grasp, rng);
                learned += got.Key; etched += got.Value;
            }
            Save();
            Show(Screen.Preparation);
            message = string.Format("Scaled to mission {0}: {1} Grasp each, spent on +{2} Etchings and +{3} abilities across the squad.", roster.MissionIndex + 1, grasp, etched, learned);
        }

        /// <summary>Counts the coming mission as won without fighting it (from preparation): its Grasp, then its upgrades.</summary>
        public void DevSkipMission()
        {
            if (roster == null || screen != Screen.Preparation || Mission == null) return;
            Progression.AwardGrasp(roster, Mission.Spec.Rewards != null ? Mission.Spec.Rewards.Grasp : 0);
            roster.MissionIndex++;
            roster.Stage = roster.MissionIndex >= Campaign.MissionCount ? CampaignStage.Finished : CampaignStage.Upgrading;
            if (roster.Stage == CampaignStage.Upgrading) Progression.BeginUpgrades(roster, Config, Catalog);
            Save();
            Show(roster.Stage == CampaignStage.Upgrading ? Screen.Upgrades : Screen.Finished);
        }

        /// <summary>Back to the new-campaign screen (the old save is replaced once a new campaign starts).</summary>
        public void DevRestartCampaign()
        {
            if (screen == Screen.Battle) return;
            screen = Screen.NewCampaign;
        }

        private void Show(Screen s)
        {
            screen = s;
            message = null;
            scroll = Vector2.zero;
            if (s == Screen.Preparation && Mission != null)
            {
                Save();   // a mission just built is kept, so a reload fights the same one
                Launcher.Preview(roster, Catalog, Mission, Config);   // the map and who is on it
            }
            if (s == Screen.Upgrades)
            {
                // A save cut off before the draw has nothing offered yet: draw now.
                if (roster.Members.TrueForAll(m => m.Offer.Count == 0 && m.AbilityOffer.Count == 0)) { Progression.BeginUpgrades(roster, Config, Catalog); Save(); }
                memberIndex = Mathf.Clamp(memberIndex, 0, Mathf.Max(0, roster.Members.Count - 1));
                etchingId = null;
            }
        }

        // ------------------------------------------------------------------ the loop

        private void NewCampaign(LoadoutStart start, List<UnitDefinition> classes = null)
        {
            roster = Progression.NewCampaign(classes ?? Campaign.Squad, Config, start, System.Environment.TickCount);
            missionCacheIndex = -1;
            // An upgrade phase before the first mission only if the squad starts with Grasp to spend.
            bool upgradeFirst = Config != null && Config.StartingGrasp > 0;
            roster.Stage = upgradeFirst ? CampaignStage.Upgrading : CampaignStage.Preparation;
            if (upgradeFirst) Progression.BeginUpgrades(roster, Config, Catalog);
            Save();
            Show(upgradeFirst ? Screen.Upgrades : Screen.Preparation);
        }

        private void StartMission()
        {
            var mission = Mission;
            if (mission == null) return;
            foreach (var m in roster.Members) m.Wounds = 0f;   // health and strain never carry over in a campaign
            Save();
            screen = Screen.Battle;
            Launcher.Launch(roster, Catalog, mission, Config);
        }

        // ------------------------------------------------------------------ the tutorial

        /// <summary>The tutorial's button (when there is one): bigger on the new-campaign screen, marked until it has been played once.</summary>
        private bool DrawTutorialButton(bool big)
        {
            if (Tutorial == null) return false;
            bool played = PlayerPrefs.GetInt(TutorialSeenKey, 0) != 0;
            string label = "Play the tutorial" + (big && !played ? "  (new players start here)" : "");
            if (big) GUILayout.Space(6f);
            return TipButton(new GUIContent(label, Tutorial.Description), GUILayout.Height(big ? 36f : 28f));
        }

        /// <summary>
        /// Fight the tutorial's battle with its own squad (a roster made for it, never saved: the campaign is untouched) while
        /// the TutorialRunner plays its steps. Leaving it reloads the scene, back to the screen the campaign was on.
        /// </summary>
        private void StartTutorial()
        {
            if (Tutorial == null) return;
            if (TutorialRunner == null) TutorialRunner = GetComponent<TutorialRunner>();
            if (TutorialRunner == null) TutorialRunner = gameObject.AddComponent<TutorialRunner>();
            TutorialRunner.Controller = Controller;
            if (TutorialRunner.Director == null) TutorialRunner.Director = Controller.Director;
            TutorialRunner.OnExit -= TutorialExited;
            TutorialRunner.OnExit += TutorialExited;

            var squad = new SquadRoster();
            var catalog = ScriptableObject.CreateInstance<ContentCatalog>();
            for (int i = 0; i < Tutorial.Squad.Count; i++)
            {
                var t = Tutorial.Squad[i];
                if (t == null || t.Unit == null) continue;
                if (!catalog.Units.Contains(t.Unit)) catalog.Units.Add(t.Unit);
                var m = new RosterMember { Id = "t" + (i + 1), Class = ContentCatalog.IdOf(t.Unit), HasLoadout = true };
                foreach (var a in t.Abilities) if (a != null) m.Loadout.Add(a.Id);
                squad.Members.Add(m);
            }
            message = null;
            screen = Screen.Tutorial;
            PlayerPrefs.SetInt(TutorialSeenKey, 1);
            TutorialRunner.Begin(Tutorial);
            Launcher.Launch(squad, catalog, Tutorial.Map, new EncounterSpec { Id = "tutorial", Temperance = new TemperanceSettings { Enabled = false } }, "tutorial");
        }

        private void TutorialExited()
        {
            TutorialRunner.OnExit -= TutorialExited;
            Time.timeScale = 1f;   // the battle may have been held (frozen) when it was left
            Time.fixedDeltaTime = 0.02f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void Finished(EncounterResult r)
        {
            if (screen == Screen.Tutorial) return;   // the tutorial's battle: nothing goes into the campaign (TutorialExited reloads)
            result = r;
            foreach (var m in roster.Members) m.Wounds = 0f;
            if (r.Won)
            {
                roster.MissionIndex++;   // its Grasp came with the result (Campaign.Finish)
                roster.Stage = roster.MissionIndex >= Campaign.MissionCount ? CampaignStage.Finished : CampaignStage.Upgrading;
                if (roster.Stage == CampaignStage.Upgrading) Progression.BeginUpgrades(roster, Config, Catalog);
            }
            else roster.Stage = Campaign.LossEndsRun ? CampaignStage.Lost : CampaignStage.Preparation;   // the run is over, or try again
            Save();
            screen = Screen.Results;
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            UiScale.Begin();
            if (head == null)
            {
                head = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                small.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
                card = new GUIStyle(GUI.skin.button) { fontSize = 12, wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 8, 8) };
                wrapped = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, richText = true };
                graspStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, alignment = TextAnchor.MiddleLeft };
                ritualStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, richText = true };
                formRowStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, richText = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 4, 4) };
                holdStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, wordWrap = true, richText = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 6, 4, 4) };
                holdStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
                tipStyle = new GUIStyle(small) { richText = true };   // the hover text: ability details
                tabStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, richText = true };   // the upgrade screen's member tabs
                popupStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
                popupStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
            }
            hoverTip = null;
            hoverReal = UiScale.HoverIsReal();   // before any area or scroll view, where the event mouse is in plain GUI units
            switch (screen)
            {
                case Screen.NewCampaign: Panel(new Vector2(520f, 310f), true, DrawNewCampaign); break;
                case Screen.ClassSelect: Panel(new Vector2(940f, 620f), true, DrawClassSelect); break;
                case Screen.Preparation: Panel(new Vector2(460f, 620f), false, DrawPreparation); break;
                case Screen.Results: DrawResults(); break;
                case Screen.Upgrades: Panel(new Vector2(1000f, 640f), true, DrawUpgrades); break;
                case Screen.Finished: Panel(new Vector2(520f, 220f), true, DrawFinished); break;
            }
            DrawHoverPopup();
        }

        // The hover text of what is under the cursor on the upgrade screen (an ability's details, an Etching's description).
        private string hoverTip;
        private bool hoverReal;   // UiScale.HoverIsReal this pass: off when the GUI event's mouse is stale (touchpads) or the cursor is away
        private GUIStyle popupStyle;
        private const float PopupWidth = 360f;

        /// <summary>
        /// The hover text of the control under the cursor (GUI.tooltip), taken only while repainting: that is when IMGUI works out
        /// what is hovered, and on other events GUI.tooltip can still hold an earlier frame's text.
        /// </summary>
        private void TakeTooltip() { }   // the hover text is taken by the controls themselves now (TrackTip): GUI.tooltip stuck in builds

        /// <summary>A label with hover text: drawn without Unity's tooltip (GUI.tooltip can keep the last hovered control's
        /// text in a build), its text taken for the popup only while the real cursor is over it (TrackTip).</summary>
        private void TipLabel(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            GUILayout.Label(content.text, style, options);
            TrackTip(content.tooltip);
        }

        /// <summary>A button with hover text, as TipLabel.</summary>
        private bool TipButton(GUIContent content, params GUILayoutOption[] options)
        {
            return TipButton(content, GUI.skin.button, options);
        }

        private bool TipButton(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            bool clicked = GUILayout.Button(content.text, style, options);
            TrackTip(content.tooltip);
            return clicked;
        }

        /// <summary>
        /// The control just drawn shows 'tip' if it is under the cursor right now: only while repainting (when its rect is
        /// final), only when the hover is real (UiScale.HoverIsReal: focused window, the GUI's mouse where the OS cursor is), and
        /// the text is cleared at the start of every pass, so nothing lingers once the cursor leaves.
        /// </summary>
        private void TrackTip(string tip)
        {
            if (Event.current.type != UnityEngine.EventType.Repaint || string.IsNullOrEmpty(tip) || !hoverReal) return;
            if (GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) hoverTip = tip;
        }

        /// <summary>The hover text in a popup just above the cursor (below it when there is no room above), kept on screen.</summary>
        private void DrawHoverPopup()
        {
            if (string.IsNullOrEmpty(hoverTip) || (screen != Screen.Upgrades && screen != Screen.ClassSelect) || !hoverReal) return;
            var content = new GUIContent(hoverTip);
            float h = popupStyle.CalcHeight(content, PopupWidth);
            Vector2 mouse = UiScale.CursorGui;
            float x = Mathf.Clamp(mouse.x - PopupWidth / 2f, 4f, UiScale.Width - PopupWidth - 4f);
            float y = mouse.y - h - 14f;
            if (y < 4f) y = Mathf.Min(mouse.y + 24f, UiScale.Height - h - 4f);
            var rect = new Rect(x, y, PopupWidth, h);
            GUI.Box(rect, GUIContent.none);
            GUI.Box(rect, GUIContent.none);   // doubled for a more opaque background with the default skin
            GUI.Box(rect, content, popupStyle);
        }

        /// <summary>A panel: centred, or (for preparation) on the left so the map stays in view.</summary>
        private void Panel(Vector2 size, bool centred, System.Action draw)
        {
            float w = Mathf.Min(size.x, UiScale.Width - 40f), h = Mathf.Min(size.y, UiScale.Height - 40f);
            var area = centred ? new Rect((UiScale.Width - w) / 2f, (UiScale.Height - h) / 2f, w, h) : new Rect(20f, (UiScale.Height - h) / 2f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);   // doubled for a more opaque background with the default skin
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 10f, area.width - 28f, area.height - 20f));
            draw();
            GUILayout.EndArea();
        }

        private void DrawNewCampaign()
        {
            GUILayout.Label(Campaign.DisplayName, title);
            GUILayout.Label(GameText.Format("campaign.intro", "A run of {0} missions. After each win every member gains Grasp, spent on stat rituals, Etchings (permanent passives) and abilities. Lose a battle and the run is over.", Campaign.MissionCount), small);
            GUILayout.Space(10f);
            GUILayout.Label(GameText.Get("campaign.start_question", "How does the squad start?"), head);
            bool def = GUILayout.Button(GameText.Get("campaign.default_loadouts", "Default loadouts") + (Config != null && Config.DefaultStart == LoadoutStart.Default ? GameText.Get("campaign.recommended", "  (recommended)") : ""), GUILayout.Height(36f));
            bool rnd = GUILayout.Button(GameText.Get("campaign.random_loadouts", "Random loadouts") + (Config != null && Config.DefaultStart == LoadoutStart.Random ? GameText.Get("campaign.recommended", "  (recommended)") : ""), GUILayout.Height(36f));
            bool tutorial = DrawTutorialButton(true);
            if (def) { ChooseStart(LoadoutStart.Default); GUIUtility.ExitGUI(); }
            if (rnd) { ChooseStart(LoadoutStart.Random); GUIUtility.ExitGUI(); }
            if (tutorial) { StartTutorial(); GUIUtility.ExitGUI(); }
        }

        // ------------------------------------------------------------------ picking the squad's classes

        private LoadoutStart pendingStart;
        private readonly List<UnitDefinition> pickedClasses = new List<UnitDefinition>();
        private UnitDefinition inspectedClass;

        /// <summary>The classes a squad can be picked from: the campaign's pool, or its fixed squad without one.</summary>
        private List<UnitDefinition> ClassPool()
        {
            var pool = new List<UnitDefinition>();
            if (Campaign.Classes != null) foreach (var u in Campaign.Classes) if (u != null && !pool.Contains(u)) pool.Add(u);
            if (pool.Count == 0) foreach (var u in Campaign.Squad) if (u != null && !pool.Contains(u)) pool.Add(u);
            return pool;
        }

        /// <summary>The loadout start is chosen: on to picking classes if there are more than a squad's worth, else straight in.</summary>
        private void ChooseStart(LoadoutStart start)
        {
            var pool = ClassPool();
            int size = Mathf.Max(1, Campaign.SquadSize);
            if (pool.Count <= size) { NewCampaign(start); return; }
            pendingStart = start;
            pickedClasses.Clear();
            foreach (var u in Campaign.Squad) if (u != null && pool.Contains(u) && pickedClasses.Count < size && !pickedClasses.Contains(u)) pickedClasses.Add(u);   // the usual squad, to start from
            inspectedClass = pool[0];
            Show(Screen.ClassSelect);
        }

        /// <summary>
        /// Pick the squad: the classes on the left (click one to inspect it), the inspected one's description, stats and
        /// starting abilities on the right, with a button to put it in or take it out. Begins once the squad is full.
        /// </summary>
        private void DrawClassSelect()
        {
            var pool = ClassPool();
            int size = Mathf.Max(1, Campaign.SquadSize);
            if (inspectedClass == null || !pool.Contains(inspectedClass)) inspectedClass = pool.Count > 0 ? pool[0] : null;
            GUILayout.Label("Choose your squad", title);
            GUILayout.Label(string.Format("Pick {0} classes. Click a class to inspect it, then add it to the squad.", size), small);
            GUILayout.Space(8f);
            bool begin = false, back = false;
            GUILayout.BeginHorizontal();

            // ---- the classes
            GUILayout.BeginVertical(GUILayout.Width(230f));
            foreach (var c in pool)
            {
                bool inSquad = pickedClasses.Contains(c);
                var old = GUI.backgroundColor;
                if (c == inspectedClass) GUI.backgroundColor = OneAbilityCard;
                if (GUILayout.Button((inSquad ? "[x]  " : "[  ]  ") + c.DisplayName, GUILayout.Height(40f))) inspectedClass = c;
                GUI.backgroundColor = old;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format("Squad: {0} / {1}", pickedClasses.Count, size), head);
            foreach (var c in pickedClasses) GUILayout.Label(c.DisplayName, wrapped);
            GUILayout.EndVertical();
            GUILayout.Space(14f);

            // ---- the inspected class
            GUILayout.BeginVertical();
            var u = inspectedClass;
            if (u != null)
            {
                scroll = GUILayout.BeginScrollView(scroll);
                GUILayout.Label(u.DisplayName, title);
                if (!string.IsNullOrEmpty(u.Description)) { GUILayout.Label(u.Description, wrapped); GUILayout.Space(6f); }
                GUILayout.Label("Stats", head);
                GUILayout.Label(string.Format("Health {0:0}     Max strain {1:0}     Move speed {2:0.#} m/s", u.MaxHealth, u.MaxStrain, u.MoveSpeed), wrapped);
                GUILayout.Label(string.Format("Dodge {0:0}%     Block {1:0}%     Dash {2:0.#}m", u.BaseDodge * 100f, u.BaseBlock * 100f, u.DashDistance), wrapped);
                GUILayout.Space(6f);
                GUILayout.Label("Starting abilities", head);
                var start = Config != null ? Config.StartFor(u) : null;
                if (u.BasicAttack != null) TipLabel(new GUIContent(PairName(u.BasicAttack) + "  (basic attack)", AbilityTip(u.BasicAttack, null)), wrapped);
                if (pendingStart == LoadoutStart.Default && start != null && start.DefaultAbilities.Count > 0)
                {
                    foreach (var a in start.DefaultAbilities) if (a != null) TipLabel(new GUIContent(PairName(a), AbilityTip(a, null)), wrapped);
                }
                else GUILayout.Label(string.Format("{0} of its abilities, at random.", start != null ? start.RandomCount : 3), small);
                var later = u.Actions.FindAll(a => a != null && a != u.BasicAttack && (pendingStart != LoadoutStart.Default || start == null || !start.DefaultAbilities.Contains(a)));
                if (later.Count > 0)
                {
                    GUILayout.Space(6f);
                    GUILayout.Label(pendingStart == LoadoutStart.Default ? "Can unlock later" : "All its abilities", head);
                    foreach (var a in later) TipLabel(new GUIContent(PairName(a), AbilityTip(a, null)), wrapped);
                }
                GUILayout.EndScrollView();
                GUILayout.Space(6f);
                bool inSquad = pickedClasses.Contains(u);
                bool full = pickedClasses.Count >= size;
                GUI.enabled = inSquad || !full;
                if (GUILayout.Button(inSquad ? "Remove from squad" : full ? "Squad is full" : "Add to squad", GUILayout.Height(34f)))
                {
                    if (inSquad) pickedClasses.Remove(u); else pickedClasses.Add(u);
                }
                GUI.enabled = true;
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            back = GUILayout.Button("Back", GUILayout.Width(120f), GUILayout.Height(36f));
            GUI.enabled = pickedClasses.Count == size;
            begin = GUILayout.Button(pickedClasses.Count == size ? "Begin campaign" : string.Format("Pick {0} more", size - pickedClasses.Count), GUILayout.Height(36f));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            TakeTooltip();
            if (back) { screen = Screen.NewCampaign; GUIUtility.ExitGUI(); }
            if (begin) { NewCampaign(pendingStart, new List<UnitDefinition>(pickedClasses)); GUIUtility.ExitGUI(); }
        }

        private void DrawPreparation()
        {
            var mission = Mission;
            if (mission == null) { GUILayout.Label(GameText.Get("prep.no_mission", "No mission set up."), head); return; }
            Line(GameText.Format("prep.mission_number", "Mission {0} of {1}", roster.MissionIndex + 1, Campaign.MissionCount), small);
            GUILayout.Label(mission.Title, title);
            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message, small);   // what a dev tool just did
            if (!string.IsNullOrEmpty(mission.Description)) GUILayout.Label(mission.Description, small);
            Line(GameText.Format("prep.map", "Map: {0}", mission.Map != null ? mission.Map.Title : GameText.Get("prep.map_scene", "(the scene as it is)")), small);
            var plan = Campaign.IsBuilt(roster.MissionIndex) && roster.Run != null ? roster.Run.Plan : null;
            if (plan != null) Line(GameText.Format("prep.points", "Threat: {0} points", plan.Points), small);
            GUILayout.Space(6f);
            scroll = GUILayout.BeginScrollView(scroll);
            Line(GameText.Get("prep.enemies", "Enemies"), head);
            foreach (var e in mission.Spec.Enemies)
            {
                if (e == null || e.Unit == null) continue;
                string arrives = e.ArriveAfter > 0f ? GameText.Format("prep.enemy_drops_in", "   (drops in at {0:0}s)", e.ArriveAfter) : "";
                Line(GameText.Format("prep.enemy", "{0} x{1}   {2:0} health{3}", e.Unit.DisplayName, Mathf.Max(1, e.Count), e.Unit.MaxHealth, arrives), wrapped);
                Line(GameText.Format("prep.enemy_abilities", "{0}", string.Join(", ", UnitAbilityNames(e.Unit).ToArray())), small);
            }
            if (mission.Spec.Objectives != null && mission.Spec.Objectives.Count > 0)
            {
                GUILayout.Space(6f);
                Line(GameText.Get("prep.objectives", "Objectives"), head);
                foreach (var o in mission.Spec.Objectives)
                {
                    if (o == null || o.Unit == null) continue;
                    Line(GameText.Format("prep.objective", "{0}   {1:0} health", o.Unit.DisplayName, o.Unit.MaxHealth), wrapped);
                    if (!string.IsNullOrEmpty(o.Unit.Description)) Line(o.Unit.Description, small);
                }
            }
            GUILayout.Space(6f);
            Line(GameText.Get("prep.rewards", "Rewards"), head);
            Line(mission.Spec.Rewards != null && mission.Spec.Rewards.Grasp > 0 ? GameText.Format("prep.rewards_text", "+{0} max Grasp for every squad member, then upgrades", mission.Spec.Rewards.Grasp)
                                : GameText.Get("prep.rewards_none", "none"), small);
            GUILayout.Space(6f);
            Line(GameText.Get("prep.squad", "Your squad"), head);
            foreach (var m in roster.Members)
            {
                var def = Catalog.Unit(m.Class);
                if (def == null) continue;
                Line(GameText.Format("prep.member", "{0}   {1}", def.DisplayName, GraspText(m)), wrapped);
                Line(GameText.Format("prep.member_abilities", "{0}", string.Join(", ", AbilityNames(def, m).ToArray())), small);
                if (m.Etchings.Count > 0) Line(GameText.Format("prep.member_etchings", "Etchings: {0}", string.Join(", ", EtchingNames(m).ToArray())), small);
            }
            GUILayout.EndScrollView();
            GUILayout.Space(6f);
            bool go = GUILayout.Button(GameText.Get("prep.start", "Start mission"), GUILayout.Height(40f));
            bool restart = GUILayout.Button(GameText.Get("prep.new_campaign", "New campaign"), GUILayout.Height(28f));
            bool tutorialPrep = DrawTutorialButton(false);
            if (go) { StartMission(); GUIUtility.ExitGUI(); }
            if (restart) { screen = Screen.NewCampaign; GUIUtility.ExitGUI(); }
            if (tutorialPrep) { StartTutorial(); GUIUtility.ExitGUI(); }
        }

        private static List<string> UnitAbilityNames(UnitDefinition u)
        {
            var names = new List<string>();
            if (u.BasicAttack != null) names.Add(u.BasicAttack.DisplayName);
            foreach (var a in u.Actions) if (a != null && a != u.BasicAttack) names.Add(a.DisplayName);
            return names;
        }

        /// <summary>"Grasp 140 / 200", or with too much held "Grasp 230 / 200  OVERBURDENED".</summary>
        private string GraspText(RosterMember m)
        {
            int used = Progression.UsedGrasp(m), max = Progression.MaxGrasp(m, Config);
            return used > max ? GameText.Format("prep.grasp_over", "Grasp {0} / {1}  <color=#FF7A6A>OVERBURDENED</color>", used, max) : GameText.Format("prep.grasp", "Grasp {0} / {1}", used, max);
        }

        /// <summary>A label, unless its GameText line was emptied (hidden).</summary>
        private static void Line(string text, GUIStyle style)
        {
            if (GameText.Shown(text)) GUILayout.Label(text, style);
        }

        private List<string> AbilityNames(UnitDefinition def, RosterMember m)
        {
            var names = new List<string>();
            foreach (var a in def.Actions) if (a != null && m.Unlocked.Contains(a.Id)) names.Add(a.DisplayName);
            return names;
        }

        private List<string> EtchingNames(RosterMember m)
        {
            var names = new List<string>();
            foreach (var s in EtchingStacks.Of(m.Etchings)) { var e = Config != null ? Config.Etching(s.Key.Id) : null; names.Add((e != null ? e.Name : s.Key.Id) + EtchingStacks.Times(s.Value)); }
            return names;
        }

        private void DrawResults()
        {
            if (result == null || Controller.Sim == null || Controller.Sim.Outcome == BattleOutcome.InProgress) return;
            // One row at the top, under the battle's own banner (BattleHUD), so the replay plays in the clear.
            float w = 640f, x = UiScale.Width / 2f - w / 2f, y = 78f;
            var box = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
            string text = result.Won
                ? GameText.Format("results.victory", "Victory   <size=14>every member +{0} max Grasp</size>", result.GraspEarned)
                : roster.Stage == CampaignStage.Lost ? GameText.Get("results.defeat_run_over", "Defeat   <size=14>the run is over</size>") : GameText.Get("results.defeat_retry", "Defeat   <size=14>try the mission again</size>");
            GUI.Box(new Rect(x, y, w - 230f, 42f), text, box);
            string next = roster.Stage == CampaignStage.Upgrading ? GameText.Get("results.to_upgrades", "Continue to upgrades") : roster.Stage == CampaignStage.Finished || roster.Stage == CampaignStage.Lost ? GameText.Get("results.continue", "Continue") : GameText.Get("results.to_preparation", "Back to preparation");
            if (GUI.Button(new Rect(x + w - 220f, y + 3f, 220f, 36f), next)) SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void DrawFinished()
        {
            bool lost = roster != null && roster.Stage == CampaignStage.Lost;
            GUILayout.Label(lost ? GameText.Get("finished.lost_title", "The run is over") : GameText.Get("finished.won_title", "Campaign complete"), title);
            GUILayout.Label(lost ? GameText.Format("finished.lost_text", "The squad fell on mission {0} of {1}.", roster.MissionIndex + 1, Campaign.MissionCount)
                                 : GameText.Format("finished.won_text", "All {0} missions won.", Campaign.MissionCount), small);
            GUILayout.Space(10f);
            if (GUILayout.Button(lost ? GameText.Get("finished.new_run", "New run") : GameText.Get("finished.new_campaign", "New campaign"), GUILayout.Height(36f))) { screen = Screen.NewCampaign; GUIUtility.ExitGUI(); }
        }

        // ------------------------------------------------------------------ upgrades: Form, Cognition, Knowledge

        private enum UpgradeTab { Form, Cognition, Knowledge }
        private UpgradeTab upgradeTab = UpgradeTab.Cognition;
        private static readonly Color OverColor = new Color(1f, 0.48f, 0.42f);

        /// <summary>
        /// The upgrade phase: a tab per member, in any order, and for the shown one its Grasp (used / max, and the strain an
        /// overburdened unit gains in battle). Three kinds of upgrade, a tab each: Form (stat rituals), Cognition (the offered
        /// Etchings: taking one draws a fresh set) and Knowledge (abilities to learn). Beside them what the member has; holding
        /// the mouse on an ability or Etching destroys it. Done whenever the player says so.
        /// </summary>
        private void DrawUpgrades()
        {
            var config = Config;
            if (config == null) { GUILayout.Label(GameText.Get("upgrades.no_config", "This campaign has no progression config."), small); return; }
            if (memberIndex < 0 || memberIndex >= roster.Members.Count) memberIndex = 0;
            var m = roster.Members[memberIndex];
            var def = Catalog.Unit(m.Class);

            DrawMemberTabs();
            if (def == null) { GUILayout.Label(GameText.Get("upgrades.no_class", "This member's class is missing from the catalog."), small); DrawFinishButton(); return; }
            GUILayout.BeginHorizontal();
            GUILayout.Label(def.DisplayName, title, GUILayout.Width(260f));
            DrawGraspLine(m);
            GUILayout.EndHorizontal();
            GUILayout.Label(message ?? "", small);

            if (etchingId != null) { DrawAbilityPicker(m, def, config.Etching(etchingId)); return; }

            GUILayout.BeginHorizontal();
            foreach (UpgradeTab t in System.Enum.GetValues(typeof(UpgradeTab)))
            {
                var old = GUI.backgroundColor;
                if (t == upgradeTab) GUI.backgroundColor = new Color(1f, 0.85f, 0.4f);
                if (GUILayout.Button(TabName(t), tabStyle, GUILayout.Height(38f), GUILayout.Width(150f)) && t != upgradeTab)
                {
                    upgradeTab = t; scroll = Vector2.zero; message = null;
                    GUI.backgroundColor = old;
                    GUIUtility.ExitGUI();
                }
                GUI.backgroundColor = old;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(600f));
            switch (upgradeTab)
            {
                case UpgradeTab.Form: DrawForm(m, def, config); break;
                case UpgradeTab.Cognition: DrawCognition(m, def, config); break;
                case UpgradeTab.Knowledge: DrawKnowledge(m, def, config); break;
            }
            GUILayout.EndVertical();
            GUILayout.Space(12f);
            GUILayout.BeginVertical(GUILayout.Width(AbilityColumn));
            DrawHeld(m, def, config);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            DrawFinishButton();
        }

        /// <summary>An upgrade tab's button: its name and what it upgrades.</summary>
        private static string TabName(UpgradeTab t)
        {
            switch (t)
            {
                case UpgradeTab.Form: return GameText.Get("upgrades.tab_form", "Form\n<size=11>stats</size>");
                case UpgradeTab.Cognition: return GameText.Get("upgrades.tab_cognition", "Cognition\n<size=11>Etchings</size>");
                default: return GameText.Get("upgrades.tab_knowledge", "Knowledge\n<size=11>abilities</size>");
            }
        }

        /// <summary>"Grasp 140 / 200 (60 free)", or how far over it is and the strain that costs in battle.</summary>
        private void DrawGraspLine(RosterMember m)
        {
            int used = Progression.UsedGrasp(m), max = Progression.MaxGrasp(m, Config);
            string text = used > max
                ? GameText.Format("upgrades.grasp_over", "Grasp {0} / {1}   <color=#FF7A6A><b>OVERBURDENED</b>: {2} over, +{3:0.#} strain per second in battle</color>", used, max, used - max, Progression.OverburdenStrain(m, Config))
                : GameText.Format("upgrades.grasp", "Grasp {0} / {1}   <color=#BBBBBB>({2} free)</color>", used, max, max - used);
            GUILayout.Label(text, graspStyle, GUILayout.Height(30f));
        }

        // Form: the stat whose rituals are open (null: none; click a stat to open them).
        private StatId? formStat;

        /// <summary>
        /// Form: each stat as a row (its value and what the rituals made it); clicking one opens its rituals beneath it, each
        /// with what it does and its Grasp, red if doing it would overburden the member. Click the stat again to close them.
        /// </summary>
        private void DrawForm(RosterMember m, UnitDefinition def, ProgressionConfig config)
        {
            GUILayout.Label(GameText.Get("upgrades.form", "Form"), head);
            GUILayout.Label(GameText.Get("upgrades.form_text", "Pick a stat to perform a ritual on it. Rituals change a stat by a share of its base value and stack; Perpetuate resets a stat and locks it for good."), small);
            GUILayout.Space(4f);
            foreach (var f in config.FormStats)
            {
                if (f == null) continue;
                bool locked = Progression.IsLocked(m, f.Stat);
                float share = Progression.RitualShare(m, f.Stat);
                float baseValue = StatValue(def, f.Stat);
                float now = baseValue + share * Progression.StatBase(def, f.Stat, config);
                string state = locked ? GameText.Get("upgrades.form_locked", "<color=#E6C27F>PERPETUATED</color>")
                             : Mathf.Abs(share) < 1e-4f ? GameText.Get("upgrades.form_base", "base")
                             : share > 0f ? GameText.Format("upgrades.form_raised", "<color=#9BE69B>+{0:0}%</color>", share * 100f)
                             : GameText.Format("upgrades.form_lowered", "<color=#FF7A6A>-{0:0}%</color>", -share * 100f);
                bool open = formStat.HasValue && formStat.Value == f.Stat;
                var old = GUI.backgroundColor;
                if (open) GUI.backgroundColor = new Color(1f, 0.85f, 0.4f);
                bool clicked = GUILayout.Button(GameText.Format("upgrades.form_stat", "<b>{0}</b>   {1}   {2}", f.Name, FormValue(f.Stat, locked ? baseValue : now), state),
                                                formRowStyle, GUILayout.Height(30f));
                GUI.backgroundColor = old;
                if (clicked) { formStat = open ? (StatId?)null : f.Stat; GUIUtility.ExitGUI(); }
                if (open) DrawRituals(m, def, f, config);
            }
        }

        /// <summary>The open stat's rituals: a button each (red when it would overburden), what it does beside it.</summary>
        private void DrawRituals(RosterMember m, UnitDefinition def, FormStat f, ProgressionConfig config)
        {
            if (Progression.IsLocked(m, f.Stat))
            {
                GUILayout.Label(GameText.Format("upgrades.ritual_locked_text", "    {0} is Perpetuated: no ritual can change it.", f.Name), small);
                GUILayout.Space(4f);
                return;
            }
            foreach (var r in config.Rituals)
            {
                if (r == null) continue;
                string why;
                bool can = Progression.CanDoRitual(m, r, f.Stat, config, out why);
                bool over = can && Progression.RitualOverburdens(m, r, f.Stat, config);
                GUILayout.BeginHorizontal();
                GUILayout.Space(18f);
                string label = r.GraspBonus > 0 ? GameText.Format("upgrades.ritual_bonus", "{0}\n<size=10>+{1} max</size>", r.Name, r.GraspBonus)
                                                : GameText.Format("upgrades.ritual_cost", "{0}\n<size=10>{1}</size>", r.Name, r.GraspCost);
                var old = GUI.backgroundColor;
                if (over) { GUI.backgroundColor = new Color(1f, 0.25f, 0.2f); label = "<color=#FF8A7A>" + label + "</color>"; }   // red: it would overburden
                GUI.enabled = can;
                bool clicked = TipButton(new GUIContent(label, RitualTip(r, def, f, config)), ritualStyle, GUILayout.Width(96f), GUILayout.Height(36f));
                GUI.enabled = true;
                GUI.backgroundColor = old;
                string effect = RitualEffect(r, def, f, config) + (over ? GameText.Get("upgrades.ritual_overburdens", "   <color=#FF7A6A>(overburdens)</color>") : "");
                GUILayout.Label(effect, wrapped, GUILayout.Height(36f));
                GUILayout.EndHorizontal();
                if (clicked)
                {
                    var done = Progression.DoRitual(m, r.Id, f.Stat, config, new System.Random(System.Environment.TickCount));
                    if (done != null)
                    {
                        Save();
                        message = done.Locks ? GameText.Format("upgrades.msg_perpetuated", "{0} Perpetuated.", f.Name)
                                : GameText.Format("upgrades.msg_ritual", "{0}: {1} {2:+0;-0}% of base.", r.Name, f.Name, done.Share * 100f);
                    }
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.Space(6f);
        }

        /// <summary>A ritual's effect on a stat in a line: "+1.5 to +4.5 m/s", "-30%, +20 max Grasp", "back to base, locked".</summary>
        private static string RitualEffect(RitualDefinition r, UnitDefinition def, FormStat f, ProgressionConfig config)
        {
            if (r.Kind == RitualKind.Perpetuate) return GameText.Get("upgrades.ritual_effect_lock", "back to its base value, locked for good");
            float b = Progression.StatBase(def, f.Stat, config);
            float lo = r.MinShare * b, hi = r.MaxShare * b;
            string sign = r.Kind == RitualKind.Lower ? "-" : "+";
            string range = Mathf.Abs(hi - lo) > 1e-4f ? sign + FormValue(f.Stat, lo, true) + GameText.Get("upgrades.ritual_tip_to", " to ") + sign + FormValue(f.Stat, hi, true)
                                                      : sign + FormValue(f.Stat, lo, true);
            return r.GraspBonus > 0 ? GameText.Format("upgrades.ritual_effect_bonus", "{0}, +{1} max Grasp", range, r.GraspBonus) : range;
        }

        private string RitualTip(RitualDefinition r, UnitDefinition def, FormStat f, ProgressionConfig config)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(GameText.Format("upgrades.ritual_tip_title", "<b>{0}</b> on {1}", r.Name, f.Name));
            if (!string.IsNullOrEmpty(r.Description)) sb.Append("\n").Append(r.Description);
            float b = Progression.StatBase(def, f.Stat, config);
            if (r.Kind != RitualKind.Perpetuate)
            {
                float lo = r.MinShare * b, hi = r.MaxShare * b;
                string sign = r.Kind == RitualKind.Lower ? "-" : "+";
                sb.Append("\n").Append(sign).Append(FormValue(f.Stat, lo, true));
                if (Mathf.Abs(hi - lo) > 1e-4f) sb.Append(GameText.Get("upgrades.ritual_tip_to", " to ")).Append(sign).Append(FormValue(f.Stat, hi, true));
            }
            sb.Append("\n").Append(r.GraspBonus > 0 ? GameText.Format("upgrades.ritual_tip_bonus", "<color=#BBBBBB>{0} Grasp, +{1} max Grasp</color>", r.GraspCost, r.GraspBonus)
                                                   : GameText.Format("upgrades.ritual_tip_cost", "<color=#BBBBBB>{0} Grasp</color>", r.GraspCost));
            return sb.ToString();
        }

        /// <summary>A stat's base value on the class (before any floor: what the unit really has).</summary>
        private static float StatValue(UnitDefinition def, StatId stat)
        {
            return Progression.StatBase(def, stat, null);
        }

        private static string FormValue(StatId stat, float v, bool change = false)
        {
            switch (stat)
            {
                case StatId.Dodge: return (v * 100f).ToString("0.#") + "%";
                case StatId.ActionSpeed: return change ? (v * 100f).ToString("0.#") + "%" : "x" + v.ToString("0.00");
                case StatId.MoveSpeed: return v.ToString("0.##") + " m/s";
                case StatId.StrainRecovery: return v.ToString("0.##") + "/s";
                case StatId.DashDistance: return v.ToString("0.#") + "m";
            }
            return v.ToString("0.#");
        }

        /// <summary>Cognition: the offered Etchings as cards; taking one uses its Grasp and draws a fresh set.</summary>
        private void DrawCognition(RosterMember m, UnitDefinition def, ProgressionConfig config)
        {
            GUILayout.Label(GameText.Get("upgrades.cognition", "Cognition"), head);
            GUILayout.Label(GameText.Format("upgrades.cognition_text", "Taking an Etching uses {0} Grasp now; each one held adds {1} to the price of the next. Taking one draws a fresh set.",
                Progression.EtchingPrice(m, config), config.EtchingGraspStep), small);
            GUILayout.Space(4f);
            if (m.Offer.Count == 0) GUILayout.Label(GameText.Get("upgrades.cognition_none", "No Etchings to offer."), small);
            for (int i = 0; i < m.Offer.Count; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 3, m.Offer.Count); j++) EtchingCard(m, def, config.Etching(m.Offer[j]));
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>Knowledge: the abilities offered this phase, each at the price learning one costs now.</summary>
        private void DrawKnowledge(RosterMember m, UnitDefinition def, ProgressionConfig config)
        {
            int price = Progression.AbilityPrice(m, config);
            int free = Progression.MaxGrasp(m, config) - Progression.UsedGrasp(m);
            GUILayout.Label(GameText.Get("upgrades.knowledge", "Knowledge"), head);
            GUILayout.Label(GameText.Format("upgrades.knowledge_text", "Learning an ability uses {0} Grasp now; each one learned adds {1} to the price of the rest.", price, config.AbilityGraspStep), small);
            GUILayout.Space(4f);
            var offered = m.AbilityOffer.ConvertAll(id => def.Actions.Find(a => a != null && a.Id == id)).FindAll(a => a != null && !m.Unlocked.Contains(a.Id));
            if (offered.Count == 0) GUILayout.Label(Progression.Locked(m, def).Count == 0 ? GameText.Get("upgrades.knowledge_all", "Every ability learned.") : GameText.Get("upgrades.knowledge_none", "Nothing left to learn this time."), small);
            foreach (var a in offered)
            {
                GUILayout.BeginHorizontal();
                TipLabel(new GUIContent(PairName(a), AbilityTip(a, m)), wrapped, GUILayout.Width(420f));
                string label = price > free ? GameText.Format("upgrades.learn_over", "Learn: {0} Grasp  (over)", price) : GameText.Format("upgrades.learn", "Learn: {0} Grasp", price);
                var old = GUI.backgroundColor;
                if (price > free) GUI.backgroundColor = OverColor;
                bool learn = GUILayout.Button(label, GUILayout.Width(160f), GUILayout.Height(26f));
                GUI.backgroundColor = old;
                GUILayout.EndHorizontal();
                if (learn) { Progression.Learn(m, a, config); Save(); message = GameText.Format("upgrades.msg_learned", "{0} learned.", PairName(a)); GUIUtility.ExitGUI(); }
            }
        }

        /// <summary>What the member has: its abilities and Etchings (hold the mouse on one to destroy it) and its rituals.</summary>
        private void DrawHeld(RosterMember m, UnitDefinition def, ProgressionConfig config)
        {
            GUILayout.Label(GameText.Get("upgrades.held_abilities", "Abilities"), head);
            GUILayout.Label(GameText.Format("upgrades.destroy_hint", "Hold the mouse on an ability or Etching for {0:0.#}s to destroy it: its Grasp is freed, max Grasp -{1}.", config.DestroyHoldSeconds, config.DestroyGraspLoss), small);
            if (def.BasicAttack != null) TipLabel(new GUIContent(GameText.Format("upgrades.basic_attack", "{0}  (basic attack)", PairName(def.BasicAttack)), AbilityTip(def.BasicAttack, m)), wrapped);
            foreach (var a in def.Actions)
            {
                if (a == null || a == def.BasicAttack || !m.Unlocked.Contains(a.Id)) continue;
                var use = m.AbilityGrasp.Find(g => g.Id == a.Id);
                string text = use != null ? GameText.Format("upgrades.held_ability", "{0}  <color=#BBBBBB>{1}</color>", PairName(a), use.Grasp) : PairName(a);
                if (HoldToDestroy("a:" + a.Id, text, AbilityTip(a, m)))
                {
                    Progression.DestroyAbility(m, a.Id, config); Save(); message = GameText.Format("upgrades.msg_destroyed", "{0} destroyed.", PairName(a));
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.Space(8f);
            GUILayout.Label(GameText.Get("upgrades.held_etchings", "Etchings"), head);
            if (m.Etchings.Count == 0) GUILayout.Label(GameText.Get("upgrades.held_none", "None yet."), small);
            var abilities = Progression.Abilities(m, def);
            for (int i = 0; i < m.Etchings.Count; i++)
            {
                var p = m.Etchings[i];
                var e = config.Etching(p.Id);
                var on = string.IsNullOrEmpty(p.ActionId) ? null : abilities.Find(a => a.Id == p.ActionId);
                var also = string.IsNullOrEmpty(p.SecondActionId) ? null : abilities.Find(a => a.Id == p.SecondActionId);
                string where = on == null ? "" : also != null ? GameText.Format("upgrades.etched_on_two", "  on {0} and {1}", PairName(on), PairName(also)) : GameText.Format("upgrades.etched_on", "  on {0}", PairName(on));
                string text = GameText.Format("upgrades.held_etching", "{0}{1}  <color=#BBBBBB>{2}</color>", e != null ? e.Name : p.Id, where, p.Grasp);
                if (HoldToDestroy("e:" + i + ":" + p.Id, text, EtchingTip(e, on)))
                {
                    Progression.DestroyEtching(m, i, config); Save(); message = GameText.Format("upgrades.msg_destroyed", "{0} destroyed.", e != null ? e.Name : p.Id);
                    GUIUtility.ExitGUI();
                }
            }
            if (m.Rituals.Count > 0)
            {
                GUILayout.Space(8f);
                GUILayout.Label(GameText.Get("upgrades.held_rituals", "Rituals"), head);
                foreach (var r in m.Rituals)
                {
                    var rd = config.Ritual(r.Ritual);
                    var f = config.Form(r.Stat);
                    string what = r.Locks ? GameText.Get("upgrades.ritual_locked", "locked") : string.Format("{0:+0;-0}%", r.Share * 100f);
                    string name = rd != null ? rd.Name : r.Ritual, stat = f != null ? f.Name : r.Stat.ToString();
                    GUILayout.Label(r.GraspBonus > 0 ? GameText.Format("upgrades.held_ritual_bonus", "{0} {1}  {2}  <color=#BBBBBB>{3}, +{4} max</color>", name, stat, what, r.Grasp, r.GraspBonus)
                                                     : GameText.Format("upgrades.held_ritual", "{0} {1}  {2}  <color=#BBBBBB>{3}</color>", name, stat, what, r.Grasp), wrapped);
                }
            }
        }

        // Hold to destroy: the row the mouse went down on, and when.
        private string holdKey;
        private float holdStart;

        /// <summary>
        /// A row that is destroyed by holding the left mouse button on it for DestroyHoldSeconds: it fills red as the hold goes
        /// on, and letting go (or leaving it) stops. True the moment the hold completes.
        /// </summary>
        private bool HoldToDestroy(string key, string text, string tip)
        {
            GUILayout.Label(text, holdStyle);
            Rect r = GUILayoutUtility.GetLastRect();
            TrackTip(tip);
            var e = Event.current;
            if (e.type == UnityEngine.EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { holdKey = key; holdStart = Time.realtimeSinceStartup; e.Use(); }
            if (holdKey != key) return false;
            if (e.rawType == UnityEngine.EventType.MouseUp || (e.isMouse && !r.Contains(e.mousePosition))) { holdKey = null; return false; }
            float t = (Time.realtimeSinceStartup - holdStart) / Mathf.Max(0.1f, Config.DestroyHoldSeconds);
            if (e.type == UnityEngine.EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 0.25f, 0.2f, 0.55f);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), Texture2D.whiteTexture);
                GUI.color = old;
            }
            if (t < 1f) return false;
            holdKey = null;
            return true;
        }

        /// <summary>
        /// One tab per member, in any order: its name and its Grasp (red when overburdened). Clicking one shows that member;
        /// nothing is lost by switching.
        /// </summary>
        private void DrawMemberTabs()
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < roster.Members.Count; i++)
            {
                var mm = roster.Members[i];
                int used = Progression.UsedGrasp(mm), max = Progression.MaxGrasp(mm, Config);
                string state = used > max ? GameText.Format("upgrades.member_over", "<color=#FF7A6A>{0} / {1} over</color>", used, max) : GameText.Format("upgrades.member_grasp", "{0} / {1} Grasp", used, max);
                var old = GUI.backgroundColor;
                if (i == memberIndex) GUI.backgroundColor = new Color(1f, 0.85f, 0.4f);
                bool clicked = GUILayout.Button(string.Format("{0}\n<size=11>{1}</size>", UnitName(mm), state), tabStyle, GUILayout.Height(40f), GUILayout.MinWidth(110f));
                GUI.backgroundColor = old;
                if (clicked && i != memberIndex)
                {
                    memberIndex = i; message = null; etchingId = null; scroll = Vector2.zero; holdKey = null; formStat = null;
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        /// <summary>On to the next mission (any time: nothing has to be taken).</summary>
        private void DrawFinishButton()
        {
            GUILayout.Space(6f);
            string next = Campaign.MissionTitle(roster.MissionIndex);
            int over = roster.Members.FindAll(mm => Progression.OverGrasp(mm, Config) > 0).Count;
            string label = (string.IsNullOrEmpty(next) ? GameText.Get("upgrades.done", "Done") : GameText.Format("upgrades.next_mission", "Next mission: {0}", next))
                         + (over > 0 ? GameText.Format("upgrades.overburdened_count", "   ({0} overburdened)", over) : "");
            if (GUILayout.Button(label, GUILayout.Height(38f))) { FinishUpgrades(); GUIUtility.ExitGUI(); }
        }

        /// <summary>One offered Etching: its name, what it does, its Grasp, and (for a One Ability one) that it asks for an ability. Click to take it.</summary>
        private void EtchingCard(RosterMember m, UnitDefinition def, EtchingDefinition e)
        {
            if (e == null) return;
            // One Ability Etchings read apart from unit-wide ones by their teal card.
            bool one = e.Scope != EtchingScope.Unit;
            int free = Progression.MaxGrasp(m, Config) - Progression.UsedGrasp(m);
            int price = Progression.EtchingPrice(m, Config);
            string grasp = price > free ? GameText.Format("upgrades.card_grasp_over", "<color=#FF7A6A>{0} Grasp (overburdens)</color>", price)
                                        : GameText.Format("upgrades.card_grasp", "<color=#BBBBBB>{0} Grasp</color>", price);
            string pick = e.Scope == EtchingScope.TwoAbilities ? GameText.Get("upgrades.card_pick_two", "<i><color=#7FE6D9>Click, then pick the two abilities</color></i>\n")
                        : one ? GameText.Get("upgrades.card_pick_one", "<i><color=#7FE6D9>Click, then pick the ability</color></i>\n") : "";
            string text = GameText.Format("upgrades.card", "<b><size=15>{0}</size></b>\n{1}\n{2}{3}", e.Name, e.Description, pick, grasp);
            var oldBackground = GUI.backgroundColor;
            GUI.backgroundColor = one ? OneAbilityCard : UnitCard;
            bool clicked = GUILayout.Button(text, card, GUILayout.Width(192f), GUILayout.Height(160f));
            GUI.backgroundColor = oldBackground;
            if (clicked)
            {
                if (one) { etchingId = e.Id; firstAbilityId = null; message = null; scroll = Vector2.zero; }
                else if (Progression.TakeEtching(roster, m, def, e, Config)) { Save(); message = GameText.Format("upgrades.msg_etched", "{0} etched.", e.Name); }
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>A One Ability Etching was clicked: which ability does it go on? Each one it can go on, with what would change.</summary>
        private void DrawAbilityPicker(RosterMember m, UnitDefinition def, EtchingDefinition e)
        {
            if (e == null) { etchingId = null; return; }
            var preview = PreviewUnit(m, def);
            GUILayout.Label(e.Name, head);
            GUILayout.Label(e.Description, small);
            GUILayout.Space(6f);
            // A pair (Related Concepts): the first ability, then the one it links to.
            bool pair = e.Scope == EtchingScope.TwoAbilities;
            var first = pair && firstAbilityId != null ? Progression.Etchable(m, def, e).Find(x => x.Id == firstAbilityId) : null;
            GUILayout.Label(!pair ? GameText.Get("etch.pick", "Etch it onto which ability?")
                          : first == null ? GameText.Get("etch.pick_first", "Etch it onto which ability? (the first of two)")
                          : GameText.Format("etch.pick_second", "Link {0} to which other ability?", PairName(first)), head);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var a in Progression.Etchable(m, def, e))
            {
                if (first != null && a.Id == first.Id) continue;
                GUILayout.BeginHorizontal();
                if (TipButton(new GUIContent(PairName(a), AbilityTip(a, m)), GUILayout.Width(200f), GUILayout.Height(30f)))
                {
                    if (pair && first == null) { firstAbilityId = a.Id; scroll = Vector2.zero; GUIUtility.ExitGUI(); }
                    if (pair) { if (Progression.TakeEtching(roster, m, def, e, Config, first.Id, a.Id)) { Save(); message = GameText.Format("upgrades.msg_linked", "{0} links {1} and {2}.", e.Name, PairName(first), PairName(a)); } }
                    else if (Progression.TakeEtching(roster, m, def, e, Config, a.Id)) { Save(); message = GameText.Format("upgrades.msg_etched_onto", "{0} etched onto {1}.", e.Name, PairName(a)); }
                    etchingId = null; firstAbilityId = null; scroll = Vector2.zero;
                    GUIUtility.ExitGUI();
                }
                GUILayout.Label(Changes(e, a, preview), small);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            TakeTooltip();
            if (GUILayout.Button(GameText.Get("etch.back", "Back"), GUILayout.Height(30f))) { if (firstAbilityId != null) firstAbilityId = null; else etchingId = null; GUIUtility.ExitGUI(); }
        }

        /// <summary>What a One Ability Etching would change on 'a', number by number ("Cooldown 8s -> 5.6s").</summary>
        private static string Changes(EtchingDefinition e, ActionDefinition a, Unit u)
        {
            var parts = new List<string>();
            if (e.Grant != TranscendGrant.None) parts.Add(GrantText(e.GrantFor(), a, u));
            foreach (var mod in e.ModifiersFor(a))
            {
                if (e.ModifiersFor(a) == e.ActionModifiers && !Stats.Has(a, mod.Stat)) continue;   // a fallback changes what the ability lacks
                if (mod.Stat == ActionStat.Damage && a.Damage <= 0f && a.AuraDamagePerSecond > 0f)   // a dome that hurts (Baneful Domain): its damage per second
                {
                    float dps = u.ActionStat(a, ActionStat.Damage, a.AuraDamagePerSecond);
                    parts.Add(GameText.Format("etch.compare_per_second", "{0} {1:0.#}/s -> {2:0.#}/s", StatText.Name(ActionStat.Damage), dps, mod.Op == ModOp.Multiply ? dps * mod.Value : dps + mod.Value));
                    continue;
                }
                if (Stats.Base(a, mod.Stat) <= 0f && a.SummonUnit != null)   // only what it summons has it: passed on to that unit
                {
                    parts.Add(GameText.Format("etch.compare_summon", "{0} {1} for {2}", StatText.Name(mod.Stat), mod.Op == ModOp.Multiply ? "x" + mod.Value.ToString("0.##") : (mod.Value >= 0f ? "+" : "") + mod.Value.ToString("0.##"), a.SummonUnit.DisplayName));
                    continue;
                }
                float now =mod.Stat == ActionStat.Duration ? Stats.Base(a, mod.Stat) * u.DurationScale(a) : mod.Stat == ActionStat.Range ? u.RangeOf(a) : u.ActionStat(a, mod.Stat);
                float after = mod.Op == ModOp.Multiply ? now * mod.Value : now + mod.Value;
                parts.Add(GameText.Format("etch.compare", "{0} {1} -> {2}", StatText.Name(mod.Stat), Number(mod.Stat, now), Number(mod.Stat, after)));
            }
            return string.Join(GameText.Get("etch.compare_separator", "    "), parts.ToArray());
        }

        /// <summary>
        /// A granted Transcend in words, with the number it does away with: "Transcend (+15 strain): Recovery 1.2s -> 0s"
        /// (Uncloud the Mind), "Cooldown 8s -> 0s" (Echo), "Unstoppable windup (1.5s)" (Body On Strings), "+75% dodge for 2s"
        /// (Flow State), "Windup 1s -> 0.25s, Recovery 0.8s -> 1s" (Quickness And Lethargy).
        /// </summary>
        private static string GrantText(GrantedTranscend g, ActionDefinition a, Unit u)
        {
            string what;
            float strain = u != null && u.GrantOf(a) == g.Kind ? u.TranscendStrainOf(a) : g.Strain;
            switch (g.Kind)
            {
                case TranscendGrant.DodgeOnCast:
                    what = GameText.Format("etch.grant.dodge_on_cast", "+{0:0}% dodge for {1} from the cast", g.Param("dodge", 0.75f) * 100f, Number(ActionStat.Duration, g.Param("seconds", 2f)));
                    break;
                case TranscendGrant.QuickWindup:
                {
                    float windup = u != null ? u.ActionStat(a, ActionStat.Windup) : Stats.Base(a, ActionStat.Windup);
                    float recovery = u != null ? u.ActionStat(a, ActionStat.Recovery) : Stats.Base(a, ActionStat.Recovery);
                    what = GameText.Format("etch.grant.quick_windup", "Windup {0} -> {1}, Recovery {2} -> {3}", Number(ActionStat.Windup, windup), Number(ActionStat.Windup, windup * g.Param("windup_mult", 0.25f)),
                        Number(ActionStat.Recovery, recovery), Number(ActionStat.Recovery, recovery * g.Param("recovery_mult", 1.25f)));
                    break;
                }
                case TranscendGrant.NoRecovery:
                    what = GameText.Format("etch.grant.no_recovery", "Recovery {0} -> 0s", Number(ActionStat.Recovery, u != null ? u.ActionStat(a, ActionStat.Recovery) : Stats.Base(a, ActionStat.Recovery)));
                    break;
                case TranscendGrant.CritChance:
                    what = GameText.Format("etch.grant.crit_chance", "{0:0}% critical strike chance", g.Param("crit_chance", 0.5f) * 100f);
                    break;
                case TranscendGrant.BreakWeakpoints:
                    what = GameText.Format("etch.grant.break_weakpoints", "breaks up to {0:0} weakpoints on each enemy hit, from any side", g.Param("breaks", 2f));
                    break;
                case TranscendGrant.ExposeWeakpoints:
                    what = GameText.Format("etch.grant.expose_weakpoints", "exposes {0:0} weakpoints on enemies hit for {1}", g.Param("count", 4f), Number(ActionStat.Duration, g.Param("seconds", 3f)));
                    break;
                case TranscendGrant.ShieldOnCast:
                    what = GameText.Format("etch.grant.shield_on_cast", "a {0:0} health shield for {1}", g.Param("shield", 40f), Number(ActionStat.Duration, g.Param("seconds", 1.5f)));
                    break;
                case TranscendGrant.NoCooldown:
                    what = GameText.Format("etch.grant.no_cooldown", "Cooldown {0} -> 0s", Number(ActionStat.Cooldown, u != null ? u.ActionStat(a, ActionStat.Cooldown) : Stats.Base(a, ActionStat.Cooldown)));
                    break;
                default:
                    what = GameText.Format("etch.grant.unstoppable_windup", "Unstoppable windup ({0})", Number(ActionStat.Windup, u != null ? u.ActionStat(a, ActionStat.Windup) : Stats.Base(a, ActionStat.Windup)));
                    break;
            }
            return GameText.Format("etch.grant", "Transcend (+{0:0} strain): {1}", strain, what);
        }

        /// <summary>An Etching's hover text: its name, what it does, and the ability it is on (with a granted Transcend's numbers).</summary>
        private string EtchingTip(EtchingDefinition e, ActionDefinition on)
        {
            if (e == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(e.Name).Append("</b>");
            if (on != null) sb.Append("  <color=#7FE6D9>on ").Append(PairName(on)).Append("</color>");
            if (!string.IsNullOrEmpty(e.Description)) sb.Append("\n").Append(e.Description);
            if (on != null && e.Grant != TranscendGrant.None)
            {
                var m = roster != null && memberIndex >= 0 && memberIndex < roster.Members.Count ? roster.Members[memberIndex] : null;
                sb.Append("\n").Append(GrantText(e.GrantFor(), on, m != null ? PreviewFor(m) : null));
            }
            return sb.ToString();
        }

        private static string Number(ActionStat stat, float v)
        {
            switch (stat)
            {
                case ActionStat.Windup: case ActionStat.Recovery: case ActionStat.Cooldown: case ActionStat.StunDuration: case ActionStat.Duration:
                    return v.ToString("0.###") + "s";
                case ActionStat.Range: case ActionStat.AreaRadius: case ActionStat.DisplaceDistance:
                    return v.ToString("0.#") + "m";
            }
            return v.ToString("0.#");
        }

        /// <summary>An ability's description, and the Etchings on it.</summary>
        /// <summary>
        /// An ability's details for the hover text: its description; its timing, range and costs; what it does (its behavior's
        /// effect lines, as the battle's ability popup words them); its Transcend; the Etchings on it. The numbers are the
        /// member's own, its Etchings in.
        /// </summary>
        private string AbilityTip(ActionDefinition a, RosterMember m)
        {
            // The Forsaken's abilities have a Diverged form: both, one after the other.
            if (a.DivergedVersion == null) return AbilityTipOne(a, m, null);
            return AbilityTipOne(a, m, GameText.Get("campaign.form.composed", "Composed")) + "\n\n" + AbilityTipOne(a.DivergedVersion, m, GameText.Get("campaign.form.diverged", "Diverged"));
        }

        /// <summary>An ability's name in the campaign's lists: with a Diverged form, both ("Divulge / Devour").</summary>
        public static string PairName(ActionDefinition a)
        {
            if (a == null) return "";
            return a.DivergedVersion != null ? a.DisplayName + " / " + a.DivergedVersion.DisplayName : a.DisplayName;
        }

        /// <param name="form">The form it is for, after its name (the Forsaken's pairs); null: none.</param>
        private string AbilityTipOne(ActionDefinition a, RosterMember m, string form)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(a.DisplayName).Append("</b>");
            if (!string.IsNullOrEmpty(form)) sb.Append("  <color=#B9A6E6>(").Append(form).Append(")</color>");
            if (!string.IsNullOrEmpty(a.Description)) sb.Append("\n").Append(a.Description);
            var u = m != null ? PreviewFor(m) : null;   // no member (inspecting a class): the ability's own numbers
            var tip = new TacticsGame.Core.AbilityTip(a, u);
            var stats = new List<string>();
            stats.Add("Windup " + TacticsGame.Core.AbilityTip.Secs(tip.V(ActionStat.Windup)));
            stats.Add("Recovery " + TacticsGame.Core.AbilityTip.Secs(tip.V(ActionStat.Recovery)));
            if (tip.V(ActionStat.Cooldown) > 0f) stats.Add("Cooldown " + TacticsGame.Core.AbilityTip.Secs(tip.V(ActionStat.Cooldown)));
            if (a.ChannelSeconds > 0f) stats.Add("Channel " + TacticsGame.Core.AbilityTip.Secs(a.ChannelSeconds));
            if (a.Targeting != TargetType.Self && a.Targeting != TargetType.None && a.BehaviorId != "dash")
                stats.Add("Range " + TacticsGame.Core.AbilityTip.Metres(u != null ? u.RangeOf(a) : a.Range));
            if (Stats.Has(a, ActionStat.AreaRadius)) stats.Add("Radius " + TacticsGame.Core.AbilityTip.Metres(tip.V(ActionStat.AreaRadius)));
            float strain = tip.V(ActionStat.StrainOnSelf);
            if (Mathf.Abs(strain) > 0.01f) stats.Add(strain > 0f ? "Strain +" + strain.ToString("0") : "Sheds " + (-strain).ToString("0") + " strain");
            if (a.HealthCost > 0f) stats.Add("Health -" + tip.V(ActionStat.HealthCost).ToString("0"));
            sb.Append("\n<color=#BBBBBB>").Append(string.Join("   ", stats.ToArray())).Append("</color>");
            ActionBehavior.DescribeNumbers(tip, ActionBehaviors.Get(a.BehaviorId));   // numbers only; the rest is Details
            if (tip.Effect.Count > 0)
                sb.Append("\n").Append(string.Join("   ", tip.Effect.ConvertAll(c => c.Key + " " + c.Value).ToArray()));
            if (!string.IsNullOrEmpty(a.Details)) sb.Append("\n").Append(a.Details.Trim());
            foreach (var g in StatusGlossary.For(a))   // what its statuses do (Tuning & Text > Statuses)
                if (!StatusDisplay.IsHidden(g.Key)) sb.Append("\n<color=#E6C27F>").Append(StatusDisplay.NameOf(g.Key).ToUpperInvariant()).Append("</color> - ").Append(g.Value);
            if (a.CanTranscend) sb.Append(string.Format("\nTranscend: +{0:0} strain", a.TranscendStrain));
            else if (u != null && u.GrantOf(a) != TranscendGrant.None) sb.Append("\n").Append(GrantText(u.GrantedTranscends[a.Id], a, u));
            if (m != null) foreach (var s in EtchingStacks.Of(m.Etchings))   // no member when a class is being inspected
            {
                var p = s.Key;
                if (p.ActionId != a.Id && p.SecondActionId != a.Id) continue;
                var e = Config != null ? Config.Etching(p.Id) : null;
                sb.Append("\n<color=#7FE6D9>Etched: ").Append(e != null ? e.Name + EtchingStacks.Times(s.Value) + " (" + e.Description + ")" : p.Id).Append("</color>");
            }
            return sb.ToString();
        }

        // The member's unit, built once per member and Etching / unlock count, for the hover text's numbers.
        private string previewKey;
        private Unit previewUnit;

        private Unit PreviewFor(RosterMember m)
        {
            var def = Catalog != null ? Catalog.Unit(m.Class) : null;
            if (def == null) return null;
            string key = m.Id + "|" + m.Etchings.Count + "|" + m.Unlocked.Count + "|" + m.Rituals.Count + "|" + Progression.OverGrasp(m, Config);
            if (key != previewKey || previewUnit == null) { previewKey = key; previewUnit = PreviewUnit(m, def); }
            return previewUnit;
        }

        /// <summary>The member's unit as it would fight now (its Etchings in), for the numbers the picker shows.</summary>
        private Unit PreviewUnit(RosterMember m, UnitDefinition def)
        {
            var slot = new SquadSlot { Member = m, Definition = def, Loadout = Progression.Abilities(m, def), Growth = Progression.Growth(m, Config, def) };
            return TacticsGame.Core.Campaign.MakeUnit(slot, "preview", Vector3.zero);
        }

        private string UnitName(RosterMember m) { var d = Catalog.Unit(m.Class); return d != null ? d.DisplayName : m.Class; }

        /// <summary>The upgrade phase is over: on to the next mission's preparation (or the end).</summary>
        private void FinishUpgrades()
        {
            message = null; etchingId = null; scroll = Vector2.zero; holdKey = null;
            Progression.EndUpgrades(roster);
            roster.Stage = Mission != null ? CampaignStage.Preparation : CampaignStage.Finished;
            Save();
            Show(Mission != null ? Screen.Preparation : Screen.Finished);
        }
    }
}
