using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Pre-battle screen. Before the battle starts it lets the player set:
    ///   - the map (any of MapChoices; MapLoader builds it right away, with the units stood on its spawns),
    ///   - how many enemies (1-MaxEnemies) and each one's class (any of EnemyClasses),
    ///   - for each squad member, which of its class abilities it brings.
    /// The basic attack, Block and Dash always come along. Start Battle swaps the enemy units placed in the scene for freshly spawned
    /// ones, builds everyone from their definitions with the chosen loadout (Placement.Loadout; the assets are untouched), and
    /// starts the BattleController. After the battle, New Battle reloads the scene back to this screen.
    /// Uses GUILayout, so anything that changes the set of controls (adding an enemy row, closing the panel) ends the
    /// GUI pass right away with GUIUtility.ExitGUI; the next frame lays out the new set.
    /// </summary>
    public class PreBattleSetup : MonoBehaviour
    {
        public BattleController Controller;
        public BattleContext Context;
        [Tooltip("Classes enemies can be. The first entries are the defaults for the starting lineup.")]
        public List<UnitDefinition> EnemyClasses = new List<UnitDefinition>();

        [Tooltip("Used for enemies whose definition has no ViewPrefab.")]
        public GameObject FallbackUnitPrefab;
        public Material EnemyMaterial;
        public int MaxEnemies = 8;
        [Tooltip("Builds the chosen map. Leave empty to fight on whatever is in the scene.")]
        public MapLoader Maps;
        [Tooltip("Maps the player can pick from (Map Builder adds saved maps here). The scene's current map is selected first.")]
        public List<MapTemplate> MapChoices = new List<MapTemplate>();
        [Tooltip("Enemies past the map's enemy spawns line up in rows behind them: spacing between units and between rows. " +
                 "EnemyRowCenter is the first row's centre when there is no map.")]
        public Vector3 EnemyRowCenter = new Vector3(0f, 0f, 9f);
        public float EnemySpacing = 4f, EnemyRowSpacing = 3f;
        public int EnemiesPerRow = 4;

        private int mapIndex;
        private bool mapDirty;   // build the chosen map next Update (not mid-GUI)
        private static MapTemplate lastMap;

        private class SquadLoadout
        {
            public BattleContext.Placement Placement;
            public List<ActionDefinition> Options = new List<ActionDefinition>();
            public List<bool> Enabled = new List<bool>();
        }

        private readonly List<SquadLoadout> squad = new List<SquadLoadout>();
        private readonly List<int> enemyClass = new List<int>();
        private bool open;
        private Vector2 scroll;
        private GUIStyle head, small;
        private string shownTooltip = "";

        private void Start()
        {
            if (Controller == null || Context == null) return;
            open = true;
            // Start on the map picked last time (New Battle reloads the scene), else the one built in the scene.
            MapChoices.RemoveAll(m => m == null);
            mapIndex = Mathf.Max(0, MapChoices.IndexOf(lastMap != null && MapChoices.Contains(lastMap) ? lastMap : Maps != null ? Maps.Current : null));
            mapDirty = true;
            // The squad's own classes are never enemies (older scenes still list them).
            EnemyClasses.RemoveAll(c => c == null || Context.Placements.Exists(p => p.Team == Team.Player && p.Definition == c));
            foreach (var p in Context.Placements)
            {
                if (p.Team != Team.Player || p.Definition == null) continue;
                var s = new SquadLoadout { Placement = p };
                foreach (var a in p.Definition.Actions)
                {
                    if (a == null || a == p.Definition.BasicAttack) continue;
                    s.Options.Add(a);
                    s.Enabled.Add(true);
                }
                squad.Add(s);
            }
            // Start from the enemies already placed in the scene.
            foreach (var p in Context.Placements)
            {
                if (p.Team != Team.Enemy || p.Definition == null) continue;
                int index = EnemyClasses.IndexOf(p.Definition);
                AddEnemy(index >= 0 ? index : 0);
            }
            if (enemyClass.Count == 0) AddEnemy(0);
        }

        private void AddEnemy(int classIndex)
        {
            if (EnemyClasses.Count == 0 || enemyClass.Count >= MaxEnemies) return;
            classIndex = Mathf.Clamp(classIndex, 0, EnemyClasses.Count - 1);
            enemyClass.Add(classIndex);
        }

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
                if (over && GUI.Button(new Rect(UiScale.Width / 2f - 90f, 140f, 180f, 36f), "New Battle"))
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }

            float w = Mathf.Min(980f, UiScale.Width - 40f), h = Mathf.Min(640f, UiScale.Height - 40f);
            var area = new Rect((UiScale.Width - w) / 2f, (UiScale.Height - h) / 2f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);   // doubled for a more opaque background with the default skin
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 10f, area.width - 28f, area.height - 20f));
            GUILayout.Label("Battle Setup", head);
            DrawMapPicker();
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width((w - 50f) * 0.42f));
            DrawEnemies();
            GUILayout.EndVertical();
            GUILayout.Space(16f);
            GUILayout.BeginVertical();
            DrawSquad();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.Space(6f);
            if (GUILayout.Button("Start Battle", GUILayout.Height(40f))) { Begin(); GUIUtility.ExitGUI(); }
            GUILayout.EndArea();
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

        private void DrawSquad()
        {
            GUILayout.Label("Your squad", head);
            foreach (var s in squad)
            {
                var def = s.Placement.Definition;
                GUILayout.BeginHorizontal();
                GUILayout.Label(def.DisplayName, head, GUILayout.Width(170f));
                GUILayout.EndHorizontal();
                if (def.BasicAttack != null) GUILayout.Label("Basic attack: " + def.BasicAttack.DisplayName + " (always)", small);
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

        /// <summary>Applies the loadout, swaps in the chosen enemies and starts the battle.</summary>
        private void Begin()
        {
            open = false;
            ApplyMap(true);
            var placements = new List<BattleContext.Placement>();
            foreach (var s in squad)
            {
                var p = s.Placement;
                var chosen = new List<ActionDefinition>();
                for (int i = 0; i < s.Options.Count; i++) if (s.Enabled[i]) chosen.Add(s.Options[i]);
                p.Loadout = chosen;   // the unit is built from the definition with these (Unit.FromDefinition): no copy of the asset
                placements.Add(p);
            }

            foreach (var p in Context.Placements)
                if (p.Team == Team.Enemy && p.View != null) Destroy(p.View.gameObject);

            for (int i = 0; i < enemyClass.Count; i++)
            {
                var def = EnemyClasses[enemyClass[i]];
                var prefab = def.ViewPrefab != null ? def.ViewPrefab : FallbackUnitPrefab;
                if (prefab == null) continue;
                var pos = EnemyStart(i, enemyClass.Count);
                var go = Instantiate(prefab, pos, Facing(Team.Enemy, pos), Context.transform);
                go.name = "Enemy_" + def.DisplayName + "_" + (i + 1);
                if (EnemyMaterial != null) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = EnemyMaterial;
                var view = go.GetComponent<UnitView>();
                if (view == null) { Destroy(go); continue; }
                placements.Add(new BattleContext.Placement
                {
                    View = view, Team = Team.Enemy, Definition = def,
                });
            }

            Context.Placements = placements;
            Controller.StartBattle();
        }

        // ------------------------------------------------------------------ maps

        private MapTemplate ChosenMap { get { return MapChoices.Count > 0 ? MapChoices[Mathf.Clamp(mapIndex, 0, MapChoices.Count - 1)] : null; } }

        private void Update()
        {
            if (!mapDirty) return;
            mapDirty = false;
            ApplyMap(false);
        }

        /// <summary>Build the chosen map if it isn't the one in the scene, and stand the units placed in the scene on its
        /// spawns (a preview behind the panel). 'force' places them even when the map didn't change.</summary>
        private void ApplyMap(bool force)
        {
            var map = ChosenMap;
            if (Maps == null || map == null) return;
            bool changed = Maps.Current != map;
            if (changed) Maps.Load(map);
            if (!changed && !force) return;
            int player = 0, enemy = 0, enemies = Context.Placements.FindAll(p => p.Team == Team.Enemy).Count;
            foreach (var p in Context.Placements)
            {
                if (p.View == null) continue;
                Vector3 pos;
                if (p.Team == Team.Player)
                {
                    if (player >= map.PlayerSpawns.Count) { player++; continue; }
                    pos = map.PlayerSpawns[player++];
                }
                else pos = EnemyStart(enemy++, enemies);
                p.View.transform.SetPositionAndRotation(pos, Facing(p.Team, pos));
            }
        }

        /// <summary>Where the i-th of 'count' enemies starts: the map's enemy spawns in order, then rows behind them
        /// (away from the squad), or EnemyRowCenter's rows when there is no map.</summary>
        private Vector3 EnemyStart(int i, int count)
        {
            var map = Maps != null ? Maps.Current : null;
            var spawns = map != null ? map.EnemySpawns : null;
            if (spawns != null && i < spawns.Count) return spawns[i];
            int k = spawns != null ? i - spawns.Count : i, overflow = spawns != null ? count - spawns.Count : count;
            Vector3 center = EnemyRowCenter, away = Vector3.forward;
            if (spawns != null && spawns.Count > 0)
            {
                Vector3 enemies = Centroid(spawns);
                away = map.PlayerSpawns.Count > 0 ? enemies - Centroid(map.PlayerSpawns) : Vector3.forward;
                away.y = 0f;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
                center = enemies + away * EnemyRowSpacing;
            }
            Vector3 side = Vector3.Cross(Vector3.up, away);
            int perRow = Mathf.Max(1, EnemiesPerRow);
            int row = k / perRow, col = k % perRow;
            int inRow = Mathf.Min(perRow, overflow - row * perRow);
            return center + side * ((col - (inRow - 1) / 2f) * EnemySpacing) + away * (row * EnemyRowSpacing);
        }

        /// <summary>Face the other team's start (or straight across the field without a map).</summary>
        private Quaternion Facing(Team team, Vector3 from)
        {
            var map = Maps != null ? Maps.Current : null;
            var other = map != null ? map.Spawns(team == Team.Player ? Team.Enemy : Team.Player) : null;
            Vector3 look = other != null && other.Count > 0 ? Centroid(other) - from : (team == Team.Player ? Vector3.forward : Vector3.back);
            look.y = 0f;
            return Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look : Vector3.forward);
        }

        private static Vector3 Centroid(List<Vector3> points)
        {
            Vector3 sum = Vector3.zero;
            foreach (var p in points) sum += p;
            return sum / Mathf.Max(1, points.Count);
        }

    }
}
