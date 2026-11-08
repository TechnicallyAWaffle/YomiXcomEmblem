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
    ///   Upgrades:     before the first mission (EtchingsBeforeFirstMission) and after each win: every member, in any order
    ///                 (a tab each), takes its Etchings from the ones drawn for it (Progression.BeginUpgrades) and spends
    ///                 ability points on unlocking abilities. Once all have chosen, the next mission.
    /// A win moves on to the next mission (wounds and strain never carry over); a loss retries it. After the last mission
    /// the campaign is complete. The scene reloads between battles, so each one starts clean; the roster's Stage says
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

        private enum Screen { NewCampaign, Preparation, Battle, Results, Upgrades, Finished, Tutorial }

        private SquadRoster roster;
        private Screen screen;
        private EncounterResult result;
        private int memberIndex;
        private string etchingId;   // a One Ability Etching clicked: its ability is being picked
        private Vector2 scroll;
        private GUIStyle head, title, small, card, wrapped, tipStyle, tabStyle;
        private static readonly Color OneAbilityCard = new Color(0.45f, 1f, 0.95f), UnitCard = new Color(1f, 0.9f, 0.7f);   // card tints
        private const float AbilityColumn = 330f;   // the upgrade panel's right column (names wrap rather than cut off)
        private string message;

        private ProgressionConfig Config { get { return Campaign != null ? Campaign.Progression : null; } }
        private EncounterDefinition Mission { get { return Campaign != null && roster != null ? Campaign.Mission(roster.MissionIndex) : null; } }

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
            Show(roster.Stage == CampaignStage.Upgrading ? Screen.Upgrades : roster.Stage == CampaignStage.Finished ? Screen.Finished : Screen.Preparation);
        }

        private void OnDestroy() { if (Launcher != null) Launcher.OnEncounterFinished -= Finished; }

        private ContentCatalog CampaignCatalog()
        {
            var c = ScriptableObject.CreateInstance<ContentCatalog>();
            foreach (var u in Campaign.Squad) if (u != null && !c.Units.Contains(u)) c.Units.Add(u);
            return c;
        }

        private void Save() { RosterSave.Save(roster, SaveFile); }

        /// <summary>Etchings each member takes in this upgrade phase: the campaign's number for the mission just won.</summary>
        private int Picks
        {
            get
            {
                if (Campaign == null || roster == null) return -1;
                return roster.MissionIndex <= 0 ? Campaign.EtchingsBeforeFirstMission : Campaign.EtchingsFor(Campaign.Mission(roster.MissionIndex - 1));
            }
        }

        // ------------------------------------------------------------------ dev menu (DevMenu)

        public SquadRoster DevRoster { get { return enabled ? roster : null; } }
        public bool DevInBattle { get { return screen == Screen.Battle; } }
        public bool DevUpgrading { get { return screen == Screen.Upgrades; } }
        public bool DevPreparing { get { return screen == Screen.Preparation; } }
        public string DevScreenName { get { return screen.ToString(); } }

        public void DevGrantPoints(int points)
        {
            if (roster == null) return;
            foreach (var m in roster.Members) m.AbilityPoints += points;
            Save();
        }

        /// <summary>A fresh draw of every member's Etching offers (a new seed, so not the same six again).</summary>
        public void DevRerollOffers()
        {
            if (roster == null || screen != Screen.Upgrades) return;
            roster.Seed++;
            foreach (var m in roster.Members) { m.Offer.Clear(); m.PicksLeft = 0; }
            Progression.BeginUpgrades(roster, Config, Catalog, Picks);
            memberIndex = Mathf.Max(0, Progression.NextToChoose(roster));
            etchingId = null;
            Save();
        }

        /// <summary>The member choosing now may take one more Etching.</summary>
        public void DevExtraPick()
        {
            if (roster == null || screen != Screen.Upgrades || memberIndex < 0 || memberIndex >= roster.Members.Count) return;
            roster.Members[memberIndex].PicksLeft++;
            Save();
        }

        /// <summary>The member choosing upgrades now, and its class (null off the upgrade screen).</summary>
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

        /// <summary>The campaign's missions, in order, for the dev menu's list.</summary>
        public IList<EncounterDefinition> DevMissions { get { return Campaign != null ? Campaign.Missions : null; } }

        /// <summary>The screens a jump to another mission can be made from.</summary>
        public bool DevCanJump { get { return screen == Screen.Preparation || screen == Screen.Upgrades; } }

        /// <summary>Straight to the preparation of mission 'index'. Any upgrade picks still open are dropped; nothing is earned.</summary>
        public void DevJumpToMission(int index)
        {
            if (roster == null || !DevCanJump || Campaign.Mission(index) == null) return;
            roster.MissionIndex = index;
            roster.Stage = CampaignStage.Preparation;
            foreach (var m in roster.Members) { m.Offer.Clear(); m.PicksLeft = 0; m.AbilityOffer.Clear(); }
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
        /// Bring the squad up to the mission it is about to fight (from preparation): the Etchings and ability points a member
        /// would have by now, had it won every mission before this one, gained at random (Progression.CatchUp).
        /// </summary>
        public void DevScaleSquadToMission()
        {
            if (roster == null || screen != Screen.Preparation || Config == null) return;
            int etchings = Campaign.EtchingsBeforeFirstMission, points = 0;
            for (int i = 0; i < roster.MissionIndex; i++)
            {
                var mission = Campaign.Mission(i);
                if (mission == null) continue;
                etchings += Campaign.EtchingsFor(mission);
                if (mission.Spec != null && mission.Spec.Rewards != null) points += mission.Spec.Rewards.AbilityPoints;
            }
            var rng = new System.Random(System.Environment.TickCount);
            int learned = 0, etched = 0;
            foreach (var m in roster.Members)
            {
                var def = Catalog != null ? Catalog.Unit(m.Class) : null;
                if (def == null) continue;
                var got = Progression.CatchUp(m, def, Config, etchings, Campaign.StartingPointsFor(def) + points, rng);
                learned += got.Key; etched += got.Value;
            }
            Save();
            Show(Screen.Preparation);
            message = string.Format("Scaled to mission {0}: +{1} Etchings and +{2} abilities across the squad (each member now has up to {3} Etchings).", roster.MissionIndex + 1, etched, learned, etchings);
        }

        /// <summary>Counts the coming mission as won without fighting it (from preparation): on to its upgrades.</summary>
        public void DevSkipMission()
        {
            if (roster == null || screen != Screen.Preparation || Mission == null) return;
            roster.MissionIndex++;
            roster.Stage = roster.MissionIndex >= Campaign.Missions.Count ? CampaignStage.Finished : CampaignStage.Upgrading;
            foreach (var m in roster.Members) { m.Offer.Clear(); m.PicksLeft = 0; }
            Save();
            Show(roster.Stage == CampaignStage.Upgrading ? Screen.Upgrades : Screen.Finished);   // Show draws the offers
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
            if (s == Screen.Preparation && Mission != null) Launcher.Preview(roster, Catalog, Mission, Config);   // the map and who is on it
            if (s == Screen.Upgrades)
            {
                // A save from before Etchings (or one cut off before the draw) has nothing offered yet: draw now.
                if (roster.Members.TrueForAll(m => m.Offer.Count == 0)) { Progression.BeginUpgrades(roster, Config, Catalog, Picks); Save(); }
                memberIndex = Mathf.Max(0, Progression.NextToChoose(roster));
                etchingId = null;
            }
        }

        // ------------------------------------------------------------------ the loop

        private void NewCampaign(LoadoutStart start)
        {
            roster = Progression.NewCampaign(Campaign.Squad, Config, start, System.Environment.TickCount);
            foreach (var m in roster.Members) m.AbilityPoints += Campaign.StartingPointsFor(Catalog != null ? Catalog.Unit(m.Class) : null);
            // An upgrade phase before the first mission, unless the campaign gives no Etchings for it.
            bool upgradeFirst = Config != null && Campaign.EtchingsBeforeFirstMission > 0;
            roster.Stage = upgradeFirst ? CampaignStage.Upgrading : CampaignStage.Preparation;
            if (upgradeFirst) Progression.BeginUpgrades(roster, Config, Catalog, Picks);
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
            return GUILayout.Button(new GUIContent(label, Tutorial.Description), GUILayout.Height(big ? 36f : 28f));
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
            Launcher.Launch(squad, catalog, Tutorial.Map, new EncounterSpec { Id = "tutorial" }, "tutorial");
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
                roster.MissionIndex++;
                roster.Stage = roster.MissionIndex >= Campaign.Missions.Count ? CampaignStage.Finished : CampaignStage.Upgrading;
                if (roster.Stage == CampaignStage.Upgrading) Progression.BeginUpgrades(roster, Config, Catalog, Picks);
            }
            else roster.Stage = CampaignStage.Preparation;   // try again
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
                wrapped = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
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
        private void TakeTooltip()
        {
            if (Event.current.type == UnityEngine.EventType.Repaint) hoverTip = GUI.tooltip;
        }

        /// <summary>The hover text in a popup just above the cursor (below it when there is no room above), kept on screen.</summary>
        private void DrawHoverPopup()
        {
            if (string.IsNullOrEmpty(hoverTip) || screen != Screen.Upgrades || !hoverReal) return;
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
            GUILayout.Label(string.Format("{0} missions. Before the first and after each win, every member takes Etchings (permanent passives) and can spend ability points unlocking abilities.", Campaign.Missions.Count), small);
            GUILayout.Space(10f);
            GUILayout.Label("How does the squad start?", head);
            bool def = GUILayout.Button("Default loadouts" + (Config != null && Config.DefaultStart == LoadoutStart.Default ? "  (recommended)" : ""), GUILayout.Height(36f));
            bool rnd = GUILayout.Button("Random loadouts" + (Config != null && Config.DefaultStart == LoadoutStart.Random ? "  (recommended)" : ""), GUILayout.Height(36f));
            bool tutorial = DrawTutorialButton(true);
            if (def) { NewCampaign(LoadoutStart.Default); GUIUtility.ExitGUI(); }
            if (rnd) { NewCampaign(LoadoutStart.Random); GUIUtility.ExitGUI(); }
            if (tutorial) { StartTutorial(); GUIUtility.ExitGUI(); }
        }

        private void DrawPreparation()
        {
            var mission = Mission;
            if (mission == null) { GUILayout.Label("No mission set up.", head); return; }
            GUILayout.Label(string.Format("Mission {0} of {1}", roster.MissionIndex + 1, Campaign.Missions.Count), small);
            GUILayout.Label(mission.Title, title);
            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message, small);   // what a dev tool just did
            if (!string.IsNullOrEmpty(mission.Description)) GUILayout.Label(mission.Description, small);
            GUILayout.Label("Map: " + (mission.Map != null ? mission.Map.Title : "(the scene as it is)"), small);
            GUILayout.Space(6f);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Enemies", head);
            foreach (var e in mission.Spec.Enemies)
            {
                if (e == null || e.Unit == null) continue;
                GUILayout.Label(string.Format("{0} x{1}   {2:0} health{3}", e.Unit.DisplayName, Mathf.Max(1, e.Count), e.Unit.MaxHealth, e.ArriveAfter > 0f ? string.Format("   (drops in at {0:0}s)", e.ArriveAfter) : ""), wrapped);
                var names = new List<string>();
                if (e.Unit.BasicAttack != null) names.Add(e.Unit.BasicAttack.DisplayName);
                foreach (var a in e.Unit.Actions) if (a != null) names.Add(a.DisplayName);
                GUILayout.Label(string.Join(", ", names.ToArray()), small);
            }
            var r = mission.Spec.Rewards;
            GUILayout.Space(6f);
            GUILayout.Label("Rewards", head);
            GUILayout.Label(r != null ? string.Format("{0} ability point{1} for every squad member, and Etchings to choose", r.AbilityPoints, r.AbilityPoints == 1 ? "" : "s") : "none", small);
            GUILayout.Space(6f);
            GUILayout.Label("Your squad", head);
            foreach (var m in roster.Members)
            {
                var def = Catalog.Unit(m.Class);
                if (def == null) continue;
                GUILayout.Label(string.Format("{0}   {1} AP", def.DisplayName, m.AbilityPoints));
                GUILayout.Label(string.Join(", ", AbilityNames(def, m).ToArray()), small);
                if (m.Etchings.Count > 0) GUILayout.Label("Etchings: " + string.Join(", ", EtchingNames(m).ToArray()), small);
            }
            GUILayout.EndScrollView();
            GUILayout.Space(6f);
            bool go = GUILayout.Button("Start mission", GUILayout.Height(40f));
            bool restart = GUILayout.Button("New campaign", GUILayout.Height(28f));
            bool tutorialPrep = DrawTutorialButton(false);
            if (go) { StartMission(); GUIUtility.ExitGUI(); }
            if (restart) { screen = Screen.NewCampaign; GUIUtility.ExitGUI(); }
            if (tutorialPrep) { StartTutorial(); GUIUtility.ExitGUI(); }
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
            foreach (var p in m.Etchings) { var e = Config != null ? Config.Etching(p.Id) : null; names.Add(e != null ? e.Name : p.Id); }
            return names;
        }

        private void DrawResults()
        {
            if (result == null || Controller.Sim == null || Controller.Sim.Outcome == BattleOutcome.InProgress) return;
            // One row at the top, under the battle's own banner (BattleHUD), so the replay plays in the clear.
            float w = 640f, x = UiScale.Width / 2f - w / 2f, y = 78f;
            var box = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
            string text = result.Won
                ? string.Format("Victory   <size=14>every member +{0} ability point{1}</size>", result.AbilityPointsEarned, result.AbilityPointsEarned == 1 ? "" : "s")
                : "Defeat   <size=14>try the mission again</size>";
            GUI.Box(new Rect(x, y, w - 230f, 42f), text, box);
            string next = roster.Stage == CampaignStage.Upgrading ? "Continue to Etchings" : roster.Stage == CampaignStage.Finished ? "Continue" : "Back to preparation";
            if (GUI.Button(new Rect(x + w - 220f, y + 3f, 220f, 36f), next)) SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void DrawFinished()
        {
            GUILayout.Label("Campaign complete", title);
            GUILayout.Label(string.Format("All {0} missions won.", Campaign.Missions.Count), small);
            GUILayout.Space(10f);
            if (GUILayout.Button("New campaign", GUILayout.Height(36f))) { screen = Screen.NewCampaign; GUIUtility.ExitGUI(); }
        }

        // ------------------------------------------------------------------ upgrades: the Etching draft

        /// <summary>
        /// The upgrade phase: a tab per member, picked in any order; the shown member's offered Etchings as cards; clicking one takes it (a One Ability one
        /// first asks which ability, showing what changes). Beside them, the Etchings it has and its abilities (unlock with
        /// ability points). Once every member has taken its picks, on to the next mission.
        /// </summary>
        private void DrawUpgrades()
        {
            var config = Config;
            if (config == null) { GUILayout.Label("This campaign has no progression config.", small); return; }
            if (memberIndex < 0 || memberIndex >= roster.Members.Count) memberIndex = Mathf.Max(0, Progression.NextToChoose(roster));
            var m = roster.Members[memberIndex];
            var def = Catalog.Unit(m.Class);

            DrawMemberTabs();
            if (def == null) { GUILayout.Label("This member's class is missing from the catalog.", small); DrawFinishButton(); return; }
            GUILayout.Label(def.DisplayName, title);
            string points = string.Format("{0} ability point{1}", m.AbilityPoints, m.AbilityPoints == 1 ? "" : "s");
            GUILayout.Label(m.Offer.Count == 0 ? "Nothing to choose from.   " + points
                : m.PicksLeft > 0 ? string.Format("Choose {0} Etching{1}.   {2}", m.PicksLeft, m.PicksLeft == 1 ? "" : "s", points)
                : "Done choosing.   " + points, head);
            GUILayout.Label(message ?? "", small);

            if (etchingId != null) { DrawAbilityPicker(m, def, config.Etching(etchingId)); return; }

            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.BeginHorizontal();
            // Left: the offered Etchings, three to a row, then the ones it has.
            GUILayout.BeginVertical(GUILayout.Width(600f));
            for (int i = 0; i < m.Offer.Count; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 3, m.Offer.Count); j++) EtchingCard(m, def, config.Etching(m.Offer[j]));
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8f);
            GUILayout.Label("Etchings", head);
            if (m.Etchings.Count == 0) GUILayout.Label("None yet.", small);
            foreach (var p in m.Etchings)
            {
                var e = config.Etching(p.Id);
                var on = string.IsNullOrEmpty(p.ActionId) ? null : Progression.Abilities(m, def).Find(a => a.Id == p.ActionId);
                GUILayout.Label(new GUIContent((e != null ? e.Name : p.Id) + (on != null ? "  on " + on.DisplayName : ""), EtchingTip(e, on)), small);
            }
            GUILayout.EndVertical();
            GUILayout.Space(12f);
            // Right: the abilities it has, then the few locked ones it may unlock this phase (Progression.DrawAbilityOffer).
            GUILayout.BeginVertical(GUILayout.Width(AbilityColumn));
            GUILayout.Label("Abilities", head);
            if (def.BasicAttack != null) GUILayout.Label(new GUIContent(def.BasicAttack.DisplayName + "  (basic attack)", AbilityTip(def.BasicAttack, m)), wrapped);
            foreach (var a in def.Actions)
                if (a != null && a != def.BasicAttack && m.Unlocked.Contains(a.Id)) GUILayout.Label(new GUIContent(a.DisplayName, AbilityTip(a, m)), wrapped);
            GUILayout.Space(8f);
            GUILayout.Label("Learn", head);
            var offered = m.AbilityOffer.ConvertAll(id => def.Actions.Find(a => a != null && a.Id == id)).FindAll(a => a != null && !m.Unlocked.Contains(a.Id));
            if (offered.Count == 0) GUILayout.Label(Progression.Locked(m, def).Count == 0 ? "Every ability learned." : "Nothing to learn this time.", small);
            foreach (var a in offered)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent(a.DisplayName, AbilityTip(a, m)), wrapped, GUILayout.Width(AbilityColumn - 112f));
                string why;
                GUI.enabled = Progression.CanUnlock(m, a, config, out why);
                if (GUILayout.Button(string.Format("Unlock: {0} AP", config.CostOf(a)), GUILayout.Width(104f)))
                {
                    Progression.Unlock(m, a, config); Save(); message = a.DisplayName + " unlocked.";
                    GUI.enabled = true;
                    GUIUtility.ExitGUI();
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            TakeTooltip();   // drawn over the cursor after the panel (DrawHoverPopup)
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            DrawFinishButton();
        }

        /// <summary>A member still has Etchings to take this phase.</summary>
        private static bool Choosing(RosterMember m) { return m.PicksLeft > 0 && m.Offer.Count > 0; }

        /// <summary>
        /// One tab per member, in any order: its name and how many Etchings it has left to take (or that it is done).
        /// Clicking one shows that member; nothing is lost by switching.
        /// </summary>
        private void DrawMemberTabs()
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < roster.Members.Count; i++)
            {
                var mm = roster.Members[i];
                string state = Choosing(mm) ? string.Format("{0} to choose", mm.PicksLeft) : "done";
                var old = GUI.backgroundColor;
                if (i == memberIndex) GUI.backgroundColor = new Color(1f, 0.85f, 0.4f);
                else if (!Choosing(mm)) GUI.backgroundColor = new Color(0.6f, 0.6f, 0.6f);
                bool clicked = GUILayout.Button(string.Format("{0}\n<size=11>{1}</size>", UnitName(mm), state), tabStyle, GUILayout.Height(40f), GUILayout.MinWidth(110f));
                GUI.backgroundColor = old;
                if (clicked && i != memberIndex)
                {
                    memberIndex = i; message = null; etchingId = null; scroll = Vector2.zero;
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        /// <summary>On to the next mission, once every member has taken its Etchings.</summary>
        private void DrawFinishButton()
        {
            GUILayout.Space(6f);
            var mission = Mission;
            int left = roster.Members.FindAll(Choosing).Count;
            string label = left > 0 ? string.Format("{0} member{1} still choosing Etchings", left, left == 1 ? "" : "s")
                : mission != null ? "Next mission: " + mission.Title : "Done";
            GUI.enabled = left == 0;
            if (GUILayout.Button(label, GUILayout.Height(38f))) { FinishUpgrades(); GUIUtility.ExitGUI(); }
            GUI.enabled = true;
        }

        /// <summary>One offered Etching: its name, what it does, and (for a One Ability one) that it asks for an ability. Click to take it.</summary>
        private void EtchingCard(RosterMember m, UnitDefinition def, EtchingDefinition e)
        {
            if (e == null) return;
            // One Ability Etchings read apart from unit-wide ones by their teal card.
            bool one = e.Scope == EtchingScope.OneAbility;
            string text = one
                ? string.Format("<b><size=15>{0}</size></b>\n{1}\n<i><color=#7FE6D9>Click, then pick the ability</color></i>", e.Name, e.Description)
                : string.Format("<b><size=15>{0}</size></b>\n{1}", e.Name, e.Description);
            GUI.enabled = m.PicksLeft > 0;
            var oldBackground = GUI.backgroundColor;
            GUI.backgroundColor = one ? OneAbilityCard : UnitCard;
            bool clicked = GUILayout.Button(text, card, GUILayout.Width(192f), GUILayout.Height(140f));
            GUI.backgroundColor = oldBackground;
            if (clicked)
            {
                if (one) { etchingId = e.Id; message = null; scroll = Vector2.zero; }
                else if (Progression.Take(m, def, e)) { Save(); message = e.Name + " etched."; }
                GUI.enabled = true;
                GUIUtility.ExitGUI();
            }
            GUI.enabled = true;
        }

        /// <summary>A One Ability Etching was clicked: which ability does it go on? Each one it can go on, with what would change.</summary>
        private void DrawAbilityPicker(RosterMember m, UnitDefinition def, EtchingDefinition e)
        {
            if (e == null) { etchingId = null; return; }
            var preview = PreviewUnit(m, def);
            GUILayout.Label(e.Name, head);
            GUILayout.Label(e.Description, small);
            GUILayout.Space(6f);
            GUILayout.Label("Etch it onto which ability?", head);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var a in Progression.Etchable(m, def, e))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent(a.DisplayName, AbilityTip(a, m)), GUILayout.Width(200f), GUILayout.Height(30f)))
                {
                    if (Progression.Take(m, def, e, a.Id)) { Save(); message = e.Name + " etched onto " + a.DisplayName + "."; }
                    etchingId = null; scroll = Vector2.zero;
                    GUIUtility.ExitGUI();
                }
                GUILayout.Label(Changes(e, a, preview), small);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            TakeTooltip();
            if (GUILayout.Button("Back", GUILayout.Height(30f))) { etchingId = null; GUIUtility.ExitGUI(); }
        }

        /// <summary>What a One Ability Etching would change on 'a', number by number ("Cooldown 8s -> 5.6s").</summary>
        private static string Changes(EtchingDefinition e, ActionDefinition a, Unit u)
        {
            var parts = new List<string>();
            if (e.Grant != TranscendGrant.None) parts.Add(GrantText(e.GrantFor(), a, u));
            foreach (var mod in e.ModifiersFor(a))
            {
                if (e.ModifiersFor(a) == e.ActionModifiers && !Stats.Has(a, mod.Stat)) continue;   // a fallback changes what the ability lacks
                float now = mod.Stat == ActionStat.Duration ? Stats.Base(a, mod.Stat) * u.DurationScale(a) : mod.Stat == ActionStat.Range ? u.RangeOf(a) : u.ActionStat(a, mod.Stat);
                float after = mod.Op == ModOp.Multiply ? now * mod.Value : now + mod.Value;
                parts.Add(string.Format("{0} {1} -> {2}", StatText.Name(mod.Stat), Number(mod.Stat, now), Number(mod.Stat, after)));
            }
            return string.Join("    ", parts.ToArray());
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
                    what = string.Format("+{0:0}% dodge for {1} from the cast", g.Param("dodge", 0.75f) * 100f, Number(ActionStat.Duration, g.Param("seconds", 2f)));
                    break;
                case TranscendGrant.QuickWindup:
                {
                    float windup = u != null ? u.ActionStat(a, ActionStat.Windup) : Stats.Base(a, ActionStat.Windup);
                    float recovery = u != null ? u.ActionStat(a, ActionStat.Recovery) : Stats.Base(a, ActionStat.Recovery);
                    what = string.Format("Windup {0} -> {1}, Recovery {2} -> {3}", Number(ActionStat.Windup, windup), Number(ActionStat.Windup, windup * g.Param("windup_mult", 0.25f)),
                        Number(ActionStat.Recovery, recovery), Number(ActionStat.Recovery, recovery * g.Param("recovery_mult", 1.25f)));
                    break;
                }
                case TranscendGrant.NoRecovery:
                    what = "Recovery " + Number(ActionStat.Recovery, u != null ? u.ActionStat(a, ActionStat.Recovery) : Stats.Base(a, ActionStat.Recovery)) + " -> 0s";
                    break;
                case TranscendGrant.NoCooldown:
                    what = "Cooldown " + Number(ActionStat.Cooldown, u != null ? u.ActionStat(a, ActionStat.Cooldown) : Stats.Base(a, ActionStat.Cooldown)) + " -> 0s";
                    break;
                default:
                    what = "Unstoppable windup (" + Number(ActionStat.Windup, u != null ? u.ActionStat(a, ActionStat.Windup) : Stats.Base(a, ActionStat.Windup)) + ")";
                    break;
            }
            return string.Format("Transcend (+{0:0} strain): {1}", strain, what);
        }

        /// <summary>An Etching's hover text: its name, what it does, and the ability it is on (with a granted Transcend's numbers).</summary>
        private string EtchingTip(EtchingDefinition e, ActionDefinition on)
        {
            if (e == null) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(e.Name).Append("</b>");
            if (on != null) sb.Append("  <color=#7FE6D9>on ").Append(on.DisplayName).Append("</color>");
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
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(a.DisplayName).Append("</b>");
            if (!string.IsNullOrEmpty(a.Description)) sb.Append("\n").Append(a.Description);
            var u = PreviewFor(m);
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
            ActionBehaviors.Get(a.BehaviorId).Describe(tip);
            if (tip.Effect.Count > 0)
                sb.Append("\n").Append(string.Join("   ", tip.Effect.ConvertAll(c => c.Key + " " + c.Value).ToArray()));
            if (a.CanTranscend) sb.Append(string.Format("\nTranscend: +{0:0} strain", a.TranscendStrain));
            else if (u != null && u.GrantOf(a) != TranscendGrant.None) sb.Append("\n").Append(GrantText(u.GrantedTranscends[a.Id], a, u));
            foreach (var p in m.Etchings)
            {
                if (p.ActionId != a.Id) continue;
                var e = Config != null ? Config.Etching(p.Id) : null;
                sb.Append("\n<color=#7FE6D9>Etched: ").Append(e != null ? e.Name + " (" + e.Description + ")" : p.Id).Append("</color>");
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
            string key = m.Id + "|" + m.Etchings.Count + "|" + m.Unlocked.Count;
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
            message = null; etchingId = null; scroll = Vector2.zero;
            foreach (var mm in roster.Members) { mm.Offer.Clear(); mm.PicksLeft = 0; mm.AbilityOffer.Clear(); }
            roster.Stage = Mission != null ? CampaignStage.Preparation : CampaignStage.Finished;
            Save();
            Show(Mission != null ? Screen.Preparation : Screen.Finished);
        }
    }
}
