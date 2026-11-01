using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Pre-battle screen: a UI over the squad roster (SquadRoster, saved between sessions by RosterSave) and the
    /// BattleLauncher. Before the battle it lets the player pick:
    ///   - an encounter (any of Encounters: its map, enemies and rewards), or Custom: the map (any of MapChoices) and how
    ///     many enemies of which class (any of EnemyClasses);
    ///   - for each squad member, which of its class abilities it brings (kept on the roster).
    /// It shows each member's wounds (carried over from earlier battles; down members sit out until the squad rests) and the
    /// squad's currency and upgrades in stock. Start Battle hands the roster and the encounter to the launcher; afterwards
    /// the result is shown and New Battle reloads the scene back here (the roster, saved, carries over).
    /// The squad of a fresh roster is the squad placed in the scene. Uses GUILayout, so anything that changes the set of
    /// controls ends the GUI pass right away with GUIUtility.ExitGUI; the next frame lays out the new set.
    /// </summary>
    public class PreBattleSetup : MonoBehaviour
    {
        public BattleController Controller;
        public BattleContext Context;
        public BattleLauncher Launcher;
        [Tooltip("Resolves the roster's classes and upgrades. Empty: Resources/ContentCatalog, else the scene's squad and EnemyClasses.")]
        public ContentCatalog Catalog;
        [Tooltip("Encounters to pick from (missions). Custom battles use the map and enemy pickers instead.")]
        public List<EncounterDefinition> Encounters = new List<EncounterDefinition>();
        [Tooltip("Classes a custom battle's enemies can be. The first entries are the defaults for the starting lineup.")]
        public List<UnitDefinition> EnemyClasses = new List<UnitDefinition>();
        public int MaxEnemies = 8;
        [Tooltip("Builds the chosen map. Leave empty to fight on whatever is in the scene.")]
        public MapLoader Maps;
        [Tooltip("Maps a custom battle can be on (Map Builder adds saved maps here). The scene's current map is selected first.")]
        public List<MapTemplate> MapChoices = new List<MapTemplate>();
        [Tooltip("Keep the roster between sessions (RosterSave). Off: every play starts with a fresh squad.")]
        public bool SaveRoster = true;

        private int mapIndex, encounterIndex = -1;   // -1: a custom battle
        private bool mapDirty;   // build the chosen map next Update (not mid-GUI)
        private static MapTemplate lastMap;
        private static int lastEncounter = -1;

        private class MemberLoadout
        {
            public RosterMember Member;
            public UnitDefinition Definition;
            public List<ActionDefinition> Options = new List<ActionDefinition>();
            public List<bool> Enabled = new List<bool>();
        }

        private SquadRoster roster;
        private readonly List<MemberLoadout> squad = new List<MemberLoadout>();
        private readonly List<int> enemyClass = new List<int>();
        private bool open;
        private Vector2 scroll;
        private GUIStyle head, small;
        private string shownTooltip = "", resultText;

        private void Start()
        {
            if (Controller == null || Context == null || Launcher == null) return;
            open = true;
            if (Catalog == null) Catalog = Resources.Load<ContentCatalog>(ContentCatalog.ResourceName);
            if (Catalog == null) Catalog = SceneCatalog();
            // The squad's own classes are never enemies (older scenes still list them).
            EnemyClasses.RemoveAll(c => c == null || Context.Placements.Exists(p => p.Team == Team.Player && p.Definition == c));
            MapChoices.RemoveAll(m => m == null);
            Encounters.RemoveAll(e => e == null);
            // Start on what was picked last time (New Battle reloads the scene), else the map built in the scene.
            mapIndex = Mathf.Max(0, MapChoices.IndexOf(lastMap != null && MapChoices.Contains(lastMap) ? lastMap : Maps != null ? Maps.Current : null));
            encounterIndex = lastEncounter < Encounters.Count ? lastEncounter : -1;
            mapDirty = true;

            roster = SaveRoster ? RosterSave.Load() : null;
            if (roster == null || roster.Members.Count == 0) roster = NewRoster();
            BuildLoadouts();

            // A custom battle starts from the enemies already placed in the scene.
            foreach (var p in Context.Placements)
            {
                if (p.Team != Team.Enemy || p.Definition == null) continue;
                int index = EnemyClasses.IndexOf(p.Definition);
                AddEnemy(index >= 0 ? index : 0);
            }
            if (enemyClass.Count == 0) AddEnemy(0);
            Launcher.OnEncounterFinished += Finished;
        }

        private void OnDestroy() { if (Launcher != null) Launcher.OnEncounterFinished -= Finished; }

        /// <summary>A fresh squad: the classes of the squad placed in the scene, in order.</summary>
        private SquadRoster NewRoster()
        {
            var classes = new List<UnitDefinition>();
            foreach (var p in Context.Placements) if (p.Team == Team.Player && p.Definition != null) classes.Add(p.Definition);
            return SquadRoster.New(classes);
        }

        /// <summary>No catalog asset: one made of the scene's squad and the enemy classes (no upgrades).</summary>
        private ContentCatalog SceneCatalog()
        {
            var c = ScriptableObject.CreateInstance<ContentCatalog>();
            foreach (var p in Context.Placements) if (p.Definition != null && !c.Units.Contains(p.Definition)) c.Units.Add(p.Definition);
            foreach (var e in EnemyClasses) if (e != null && !c.Units.Contains(e)) c.Units.Add(e);
            return c;
        }

        private void BuildLoadouts()
        {
            squad.Clear();
            foreach (var m in roster.Members)
            {
                var def = Catalog.Unit(m.Class);
                if (def == null) continue;
                var s = new MemberLoadout { Member = m, Definition = def };
                foreach (var a in def.Actions)
                {
                    if (a == null || a == def.BasicAttack) continue;
                    s.Options.Add(a);
                    s.Enabled.Add(!m.HasLoadout || m.Loadout.Contains(a.Id));
                }
                squad.Add(s);
            }
        }

        /// <summary>The ability toggles into the roster (a member bringing everything keeps no list).</summary>
        private void StoreLoadouts()
        {
            foreach (var s in squad)
            {
                bool all = !s.Enabled.Contains(false);
                s.Member.HasLoadout = !all;
                s.Member.Loadout.Clear();
                if (!all) for (int i = 0; i < s.Options.Count; i++) if (s.Enabled[i]) s.Member.Loadout.Add(s.Options[i].Id);
            }
        }

        private void AddEnemy(int classIndex)
        {
            if (EnemyClasses.Count == 0 || enemyClass.Count >= MaxEnemies) return;
            classIndex = Mathf.Clamp(classIndex, 0, EnemyClasses.Count - 1);
            enemyClass.Add(classIndex);
        }

        private EncounterDefinition ChosenEncounter { get { return encounterIndex >= 0 && encounterIndex < Encounters.Count ? Encounters[encounterIndex] : null; } }

        private void OnGUI()
        {
            UiScale.Begin();
            if (head == null)
            {
                head = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                small.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            }
            if (!open)
            {
                bool over = Controller != null && Controller.Sim != null && Controller.Sim.Outcome != BattleOutcome.InProgress;
                if (!over) return;
                if (resultText != null)
                {
                    var style = new GUIStyle(head) { alignment = TextAnchor.UpperCenter, wordWrap = true };
                    GUI.Label(new Rect(UiScale.Width / 2f - 300f, 186f, 600f, 60f), resultText, style);
                }
                if (GUI.Button(new Rect(UiScale.Width / 2f - 90f, 140f, 180f, 36f), "New Battle"))
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }

            float w = Mathf.Min(980f, UiScale.Width - 40f), h = Mathf.Min(660f, UiScale.Height - 40f);
            var area = new Rect((UiScale.Width - w) / 2f, (UiScale.Height - h) / 2f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);   // doubled for a more opaque background with the default skin
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 10f, area.width - 28f, area.height - 20f));
            GUILayout.Label("Battle Setup", head);
            DrawEncounterPicker();
            if (ChosenEncounter == null) DrawMapPicker();
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width((w - 50f) * 0.42f));
            if (ChosenEncounter == null) DrawEnemies(); else DrawEncounter(ChosenEncounter);
            GUILayout.EndVertical();
            GUILayout.Space(16f);
            GUILayout.BeginVertical();
            DrawSquad();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.Space(6f);
            GUI.enabled = squad.Exists(s => !s.Member.Down);
            if (GUILayout.Button("Start Battle", GUILayout.Height(40f))) { Begin(); GUIUtility.ExitGUI(); }
            GUI.enabled = true;
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ encounter and map

        /// <summary>Encounter: &lt; Custom / name &gt; and its description.</summary>
        private void DrawEncounterPicker()
        {
            var e = ChosenEncounter;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Battle", head, GUILayout.Width(60f));
            GUI.enabled = Encounters.Count > 0;
            if (GUILayout.Button("<", GUILayout.Width(28f))) { SetEncounter(encounterIndex - 1); GUIUtility.ExitGUI(); }
            GUILayout.Label(e != null ? e.Title : "Custom", head, GUILayout.Width(220f));
            if (GUILayout.Button(">", GUILayout.Width(28f))) { SetEncounter(encounterIndex + 1); GUIUtility.ExitGUI(); }
            GUI.enabled = true;
            string done = e != null && roster.CompletedEncounters.Contains(e.EncounterId) ? "  (won before)" : "";
            GUILayout.Label(e != null ? e.Description + done : "Pick the map and the enemies yourself.", small);
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }

        /// <summary>Cycle through Custom (-1) and the encounters.</summary>
        private void SetEncounter(int index)
        {
            int n = Encounters.Count + 1;
            encounterIndex = ((index + 1) % n + n) % n - 1;
            lastEncounter = encounterIndex;
            mapDirty = true;
        }

        private void DrawEncounter(EncounterDefinition e)
        {
            GUILayout.Label("Enemies", head);
            foreach (var entry in e.Spec.Enemies)
                if (entry != null && entry.Unit != null)
                    GUILayout.Label(string.Format("{0} x{1}{2}", entry.Unit.DisplayName, Mathf.Max(1, entry.Count), entry.Upgrades != null && entry.Upgrades.Count > 0 ? "  (upgraded)" : ""));
            GUILayout.Space(6f);
            GUILayout.Label("Map: " + (e.Map != null ? e.Map.Title : "(the scene as it is)"), small);
            var r = e.Spec.Rewards;
            int ups = r != null && r.Upgrades != null ? r.Upgrades.Count : 0;
            GUILayout.Label(string.Format("Rewards: {0} currency{1}", r != null ? r.Currency : 0, ups > 0 ? ", " + ups + " upgrade" + (ups == 1 ? "" : "s") : ""), small);
            GUILayout.Label(e.Spec.CarryOverWounds ? "Wounds carry over afterwards." : "Wounds don't carry over.", small);
        }

        /// <summary>Map: &lt; name &gt; and its description. Always the same controls, so switching never upsets GUILayout.</summary>
        private void DrawMapPicker()
        {
            var map = ChosenMap;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Map", head, GUILayout.Width(60f));
            GUI.enabled = MapChoices.Count > 1;
            if (GUILayout.Button("<", GUILayout.Width(28f))) SetMap(mapIndex - 1);
            GUILayout.Label(map != null ? map.Title : "(the scene as it is)", head, GUILayout.Width(220f));
            if (GUILayout.Button(">", GUILayout.Width(28f))) SetMap(mapIndex + 1);
            GUI.enabled = true;
            GUILayout.Label(map != null ? map.Description : "", small);
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        private void SetMap(int index)
        {
            int n = MapChoices.Count;
            if (n == 0) return;
            mapIndex = ((index % n) + n) % n;
            lastMap = MapChoices[mapIndex];
            mapDirty = true;
        }

        private void DrawEnemies()
        {
            GUILayout.Label("Enemies", head);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Count: " + enemyClass.Count, GUILayout.Width(90f));
            GUI.enabled = enemyClass.Count > 1;
            if (GUILayout.Button("-", GUILayout.Width(34f))) { enemyClass.RemoveAt(enemyClass.Count - 1); GUIUtility.ExitGUI(); }
            GUI.enabled = enemyClass.Count < MaxEnemies;
            if (GUILayout.Button("+", GUILayout.Width(34f))) { AddEnemy(enemyClass.Count > 0 ? enemyClass[enemyClass.Count - 1] : 0); GUIUtility.ExitGUI(); }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            for (int i = 0; i < enemyClass.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label((i + 1) + ".", GUILayout.Width(22f));
                if (GUILayout.Button("<", GUILayout.Width(28f))) SetEnemyClass(i, enemyClass[i] - 1);
                GUILayout.Label(EnemyClasses[enemyClass[i]].DisplayName, GUILayout.Width(120f));
                if (GUILayout.Button(">", GUILayout.Width(28f))) SetEnemyClass(i, enemyClass[i] + 1);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Enemy classes cycle through the enemy types. Everyone has Block and Dash (G).", small);
        }

        private void SetEnemyClass(int i, int classIndex)
        {
            int n = EnemyClasses.Count;
            enemyClass[i] = ((classIndex % n) + n) % n;
        }

        // ------------------------------------------------------------------ squad

        private void DrawSquad()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Your squad", head, GUILayout.Width(150f));
            GUILayout.Label(string.Format("Currency {0}   Upgrades in stock {1}", roster.Currency, roster.OwnedUpgrades.Count), small, GUILayout.Width(260f));
            if (GUILayout.Button("Rest", GUILayout.Width(60f))) { roster.Rest(); Save(); }
            if (GUILayout.Button("New squad", GUILayout.Width(90f)))
            {
                roster = NewRoster();
                BuildLoadouts();
                Save();
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            foreach (var s in squad)
            {
                var def = s.Definition;
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.IsNullOrEmpty(s.Member.Name) ? def.DisplayName : s.Member.Name, head, GUILayout.Width(170f));
                GUILayout.Label(s.Member.Down ? "DOWN: sits out until the squad rests" : s.Member.Wounds > 0.005f ? string.Format("wounded: {0:0}% health", (1f - s.Member.Wounds) * 100f) : "fit", small);
                GUILayout.EndHorizontal();
                if (def.BasicAttack != null) GUILayout.Label("Basic attack: " + def.BasicAttack.DisplayName + " (always)" + (s.Member.Upgrades.Count > 0 ? "   Upgrades: " + s.Member.Upgrades.Count : ""), small);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < s.Options.Count; i++)
                {
                    if (i > 0 && i % 4 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                    s.Enabled[i] = GUILayout.Toggle(s.Enabled[i], new GUIContent(s.Options[i].DisplayName, s.Options[i].Description), GUILayout.Width(130f));
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(8f);
            }
            // Always one label here, showing the tooltip from the last repaint: GUI.tooltip is only known while repainting,
            // so drawing the label only when it is set would change the control count between Layout and Repaint.
            GUILayout.Label(shownTooltip, small, GUILayout.MinHeight(36f));
            if (Event.current.type == UnityEngine.EventType.Repaint) shownTooltip = GUI.tooltip ?? "";
        }

        private void Save() { if (SaveRoster) RosterSave.Save(roster); }

        // ------------------------------------------------------------------ the battle

        /// <summary>Stores the loadouts and sends the squad in: the chosen encounter, or a custom one from the pickers.</summary>
        private void Begin()
        {
            open = false;
            StoreLoadouts();
            Save();
            var encounter = ChosenEncounter;
            if (encounter != null) { Launcher.Launch(roster, Catalog, encounter); return; }
            var spec = new EncounterSpec { Id = "", CarryOverWounds = false };   // a custom battle is practice: no wounds, no rewards
            foreach (int i in enemyClass) spec.Enemies.Add(new EnemyEntry { Unit = EnemyClasses[i], Count = 1 });
            Launcher.Launch(roster, Catalog, ChosenMap, spec, null);
        }

        private void Finished(EncounterResult result)
        {
            Save();
            var text = result.Won ? "Victory" : result.Outcome == BattleOutcome.Draw ? "Draw" : "Defeat";
            if (result.CurrencyEarned > 0) text += "   +" + result.CurrencyEarned + " currency";
            if (result.UpgradesEarned.Count > 0) text += "   +" + result.UpgradesEarned.Count + " upgrade" + (result.UpgradesEarned.Count == 1 ? "" : "s");
            if (result.MembersDown.Count > 0) text += "   " + result.MembersDown.Count + " down";
            resultText = text;
        }

        // ------------------------------------------------------------------ the map preview behind the panel

        private MapTemplate ChosenMap
        {
            get
            {
                if (ChosenEncounter != null) return ChosenEncounter.Map;
                return MapChoices.Count > 0 ? MapChoices[Mathf.Clamp(mapIndex, 0, MapChoices.Count - 1)] : null;
            }
        }

        private void Update()
        {
            if (!mapDirty || !open) return;
            mapDirty = false;
            ApplyMap();
        }

        /// <summary>Build the chosen map if it isn't the one in the scene, and stand the units placed in the scene on its
        /// spawns (a preview behind the panel; the launcher spawns the real units).</summary>
        private void ApplyMap()
        {
            var map = ChosenMap;
            if (Maps == null || map == null || Maps.Current == map) return;
            Maps.Load(map);
            int player = 0, enemy = 0;
            int players = Context.Placements.FindAll(p => p.Team == Team.Player).Count, enemies = Context.Placements.Count - players;
            foreach (var p in Context.Placements)
            {
                if (p.View == null) continue;
                Vector3 pos = p.Team == Team.Player ? Launcher.StartBattle(Team.Player, player++, players) : Launcher.StartBattle(Team.Enemy, enemy++, enemies);
                p.View.transform.SetPositionAndRotation(pos, Launcher.Facing(p.Team, pos));
            }
        }
    }
}
