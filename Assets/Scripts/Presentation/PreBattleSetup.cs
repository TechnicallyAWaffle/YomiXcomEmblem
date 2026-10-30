using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Pre-battle screen. Before the battle starts it lets the player set:
    ///   - how many enemies (1-MaxEnemies) and each one's class (any of EnemyClasses) and Block-or-Dash option,
    ///   - for each squad member, which of its class abilities it brings and whether it gets Block or Dash.
    /// The basic attack always comes along. Start Battle swaps the enemy units placed in the scene for freshly spawned
    /// ones, gives everyone runtime copies of their definitions with the chosen loadout (the assets are untouched), and
    /// starts the BattleController. After the battle, New Battle reloads the scene back to this screen.
    /// </summary>
    public class PreBattleSetup : MonoBehaviour
    {
        public BattleController Controller;
        public BattleContext Context;
        [Tooltip("Classes enemies can be. The first entries are the defaults for the starting lineup.")]
        public List<UnitDefinition> EnemyClasses = new List<UnitDefinition>();
        public ActionDefinition BlockAction, DashAction;
        [Tooltip("Used for enemies whose definition has no ViewPrefab.")]
        public GameObject FallbackUnitPrefab;
        public Material EnemyMaterial;
        public int MaxEnemies = 8;
        [Tooltip("Enemy spawn rows: centre of the first row, spacing between units and between rows (rows go away from the squad).")]
        public Vector3 EnemyRowCenter = new Vector3(0f, 0f, 9f);
        public float EnemySpacing = 4f, EnemyRowSpacing = 3f;
        public int EnemiesPerRow = 4;

        private class SquadLoadout
        {
            public BattleContext.Placement Placement;
            public List<ActionDefinition> Options = new List<ActionDefinition>();
            public List<bool> Enabled = new List<bool>();
            public bool Dash;
        }

        private readonly List<SquadLoadout> squad = new List<SquadLoadout>();
        private readonly List<int> enemyClass = new List<int>();
        private readonly List<bool> enemyDash = new List<bool>();
        private bool open;
        private Vector2 scroll;
        private GUIStyle head, small;

        private void Start()
        {
            if (Controller == null || Context == null) return;
            open = true;
            foreach (var p in Context.Placements)
            {
                if (p.Team != Team.Player || p.Definition == null) continue;
                var s = new SquadLoadout { Placement = p, Dash = p.Definition.Universal != null ? p.Definition.Universal == DashAction : p.Definition.PrefersDash };
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
            enemyDash.Add(EnemyClasses[classIndex].PrefersDash);
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
            if (GUILayout.Button("Start Battle", GUILayout.Height(40f))) Begin();
            GUILayout.EndArea();
        }

        private void DrawEnemies()
        {
            GUILayout.Label("Enemies", head);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Count: " + enemyClass.Count, GUILayout.Width(90f));
            GUI.enabled = enemyClass.Count > 1;
            if (GUILayout.Button("-", GUILayout.Width(34f))) { enemyClass.RemoveAt(enemyClass.Count - 1); enemyDash.RemoveAt(enemyDash.Count - 1); }
            GUI.enabled = enemyClass.Count < MaxEnemies;
            if (GUILayout.Button("+", GUILayout.Width(34f))) AddEnemy(enemyClass.Count > 0 ? enemyClass[enemyClass.Count - 1] : 0);
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
                enemyDash[i] = GUILayout.Toolbar(enemyDash[i] ? 1 : 0, new[] { "Block", "Dash" }, GUILayout.Width(120f)) == 1;
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Enemy classes cycle through every class, the squad's included. Each unit gets Block or Dash, never both.", small);
        }

        private void SetEnemyClass(int i, int classIndex)
        {
            int n = EnemyClasses.Count;
            enemyClass[i] = ((classIndex % n) + n) % n;
            enemyDash[i] = EnemyClasses[enemyClass[i]].PrefersDash;   // a new class takes its default option
        }

        private void DrawSquad()
        {
            GUILayout.Label("Your squad", head);
            foreach (var s in squad)
            {
                var def = s.Placement.Definition;
                GUILayout.BeginHorizontal();
                GUILayout.Label(def.DisplayName, head, GUILayout.Width(170f));
                s.Dash = GUILayout.Toolbar(s.Dash ? 1 : 0, new[] { "Block", "Dash" }, GUILayout.Width(130f)) == 1;
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
            if (!string.IsNullOrEmpty(GUI.tooltip)) GUILayout.Label(GUI.tooltip, small);
        }

        /// <summary>Applies the loadout, swaps in the chosen enemies and starts the battle.</summary>
        private void Begin()
        {
            open = false;
            var placements = new List<BattleContext.Placement>();
            foreach (var s in squad)
            {
                var p = s.Placement;
                var chosen = new List<ActionDefinition>();
                for (int i = 0; i < s.Options.Count; i++) if (s.Enabled[i]) chosen.Add(s.Options[i]);
                p.Definition = Loadout(p.Definition, chosen, s.Dash);
                placements.Add(p);
            }

            foreach (var p in Context.Placements)
                if (p.Team == Team.Enemy && p.View != null) Destroy(p.View.gameObject);

            for (int i = 0; i < enemyClass.Count; i++)
            {
                var def = EnemyClasses[enemyClass[i]];
                var prefab = def.ViewPrefab != null ? def.ViewPrefab : FallbackUnitPrefab;
                if (prefab == null) continue;
                int row = i / Mathf.Max(1, EnemiesPerRow), col = i % Mathf.Max(1, EnemiesPerRow);
                int inRow = Mathf.Min(EnemiesPerRow, enemyClass.Count - row * EnemiesPerRow);
                var pos = EnemyRowCenter + new Vector3((col - (inRow - 1) / 2f) * EnemySpacing, 0f, row * EnemyRowSpacing);
                var go = Instantiate(prefab, pos, Quaternion.LookRotation(Vector3.back), Context.transform);
                go.name = "Enemy_" + def.DisplayName + "_" + (i + 1);
                if (EnemyMaterial != null) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = EnemyMaterial;
                var view = go.GetComponent<UnitView>();
                if (view == null) { Destroy(go); continue; }
                placements.Add(new BattleContext.Placement
                {
                    View = view, Team = Team.Enemy,
                    Definition = Loadout(def, new List<ActionDefinition>(def.Actions), enemyDash[i]),
                });
            }

            Context.Placements = placements;
            Controller.StartBattle();
        }

        /// <summary>A runtime copy of 'def' with these abilities and Block or Dash; the asset itself is left untouched.</summary>
        private UnitDefinition Loadout(UnitDefinition def, List<ActionDefinition> actions, bool dash)
        {
            var copy = Instantiate(def);
            copy.name = def.name;   // unit ids are built from it
            copy.Actions = actions;
            copy.Universal = dash ? DashAction : BlockAction;
            return copy;
        }
    }
}
