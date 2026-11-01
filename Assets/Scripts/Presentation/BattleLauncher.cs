using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// Starts a battle from a squad and an encounter, the one entry point for any screen that sends the squad in (the
    /// pre-battle screen now, a mission map later): it builds the map, replaces whatever units stand in the scene with
    /// freshly spawned ones (the squad on the map's player spawns, the enemies on its enemy spawns and in rows behind
    /// them), hands them to the BattleController, and when the battle ends writes the result back into the roster
    /// (Campaign.Finish) and reports it (OnEncounterFinished).
    /// </summary>
    public class BattleLauncher : MonoBehaviour
    {
        public BattleController Controller;
        public BattleContext Context;
        [Tooltip("Builds the encounter's map. Leave empty to fight on whatever is in the scene.")]
        public MapLoader Maps;
        [Tooltip("Used for units whose definition has no ViewPrefab.")]
        public GameObject FallbackUnitPrefab;
        [Tooltip("Team colours for spawned units (none: the prefab's own; the squad's is taken from a unit placed in the scene if empty).")]
        public Material PlayerMaterial, EnemyMaterial;
        [Tooltip("Units past the map's spawns line up in rows behind them: spacing between units and between rows. RowCenter is the first enemy row's centre when there is no map.")]
        public Vector3 EnemyRowCenter = new Vector3(0f, 0f, 9f);
        public float Spacing = 4f, RowSpacing = 3f;
        public int PerRow = 4;

        /// <summary>The battle just finished: how it went (already written into the roster).</summary>
        public event System.Action<EncounterResult> OnEncounterFinished;

        public EncounterResult LastResult { get; private set; }

        private SquadRoster roster;
        private EncounterSpec spec;
        private string encounterId;
        private readonly Dictionary<string, string> unitOfMember = new Dictionary<string, string>();

        /// <summary>Fight 'encounter' (its map and spec) with the deployable members of 'roster'.</summary>
        public void Launch(SquadRoster squad, ContentCatalog catalog, EncounterDefinition encounter)
        {
            Launch(squad, catalog, encounter.Map, encounter.Spec, encounter.EncounterId);
        }

        /// <summary>Fight 'encounterSpec' on 'map' (null: the loaded one) with the deployable members of 'squad'.</summary>
        public void Launch(SquadRoster squad, ContentCatalog catalog, MapTemplate map, EncounterSpec encounterSpec, string id)
        {
            roster = squad;
            spec = encounterSpec;
            encounterId = id;
            if (Maps != null && map != null && Maps.Current != map) Maps.Load(map);
            if (PlayerMaterial == null) PlayerMaterial = SceneMaterial(Team.Player);

            foreach (var p in Context.Placements) if (p.View != null) Destroy(p.View.gameObject);
            var placements = new List<BattleContext.Placement>();
            var memberAt = new List<string>();
            var squadSlots = Campaign.Squad(squad, catalog);
            for (int i = 0; i < squadSlots.Count; i++)
            {
                var s = squadSlots[i];
                var view = Spawn(s.Definition, Team.Player, StartBattle(Team.Player, i, squadSlots.Count), PlayerMaterial, "Player_" + s.Definition.DisplayName + "_" + (i + 1));
                if (view == null) continue;
                placements.Add(new BattleContext.Placement { View = view, Team = Team.Player, Definition = s.Definition, Upgrades = s.Upgrades, Loadout = s.Loadout, Wounds = s.Member.Wounds });
                memberAt.Add(s.Member.Id);
            }
            var enemySlots = Campaign.Enemies(encounterSpec);
            for (int i = 0; i < enemySlots.Count; i++)
            {
                var e = enemySlots[i];
                var view = Spawn(e.Definition, Team.Enemy, StartBattle(Team.Enemy, i, enemySlots.Count), EnemyMaterial, "Enemy_" + e.Definition.DisplayName + "_" + (i + 1));
                if (view == null) continue;
                placements.Add(new BattleContext.Placement { View = view, Team = Team.Enemy, Definition = e.Definition, Upgrades = e.Upgrades });
                memberAt.Add(null);
            }

            Context.Placements = placements;
            Controller.OnBattleFinished -= Finished;
            Controller.OnBattleFinished += Finished;
            Controller.StartBattle();
            // The context gave each placed view its unit id while building the battle: remember whose unit is whose.
            unitOfMember.Clear();
            for (int i = 0; i < placements.Count; i++) if (memberAt[i] != null) unitOfMember[memberAt[i]] = placements[i].View.UnitId;
        }

        private void Finished(BattleOutcome outcome)
        {
            Controller.OnBattleFinished -= Finished;
            LastResult = Campaign.Finish(roster, spec, encounterId, Controller.Sim, unitOfMember);
            if (OnEncounterFinished != null) OnEncounterFinished(LastResult);
        }

        private UnitView Spawn(UnitDefinition def, Team team, Vector3 pos, Material material, string name)
        {
            var prefab = def.ViewPrefab != null ? def.ViewPrefab : FallbackUnitPrefab;
            if (prefab == null) return null;
            var go = Instantiate(prefab, pos, Facing(team, pos), Context.transform);
            go.name = name;
            if (material != null) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
            var view = go.GetComponent<UnitView>();
            if (view == null) Destroy(go);
            return view;
        }

        /// <summary>A team's material from a unit of that team already standing in the scene, if any.</summary>
        private Material SceneMaterial(Team team)
        {
            foreach (var p in Context.Placements)
            {
                if (p.Team != team || p.View == null) continue;
                var r = p.View.GetComponentInChildren<Renderer>();
                if (r != null) return r.sharedMaterial;
            }
            return null;
        }

        // ------------------------------------------------------------------ where units start

        /// <summary>
        /// Where the i-th of 'count' units of 'team' starts: the map's spawns for that team in order, then rows behind them
        /// (away from the other side), or rows around EnemyRowCenter (mirrored for the squad) with no map.
        /// </summary>
        public Vector3 StartBattle(Team team, int i, int count)
        {
            var map = Maps != null ? Maps.Current : null;
            var spawns = map != null ? map.Spawns(team) : null;
            if (spawns != null && i < spawns.Count) return spawns[i];
            int k = spawns != null ? i - spawns.Count : i, overflow = spawns != null ? count - spawns.Count : count;
            Vector3 center = team == Team.Enemy ? EnemyRowCenter : new Vector3(EnemyRowCenter.x, EnemyRowCenter.y, -EnemyRowCenter.z);
            Vector3 away = team == Team.Enemy ? Vector3.forward : Vector3.back;
            if (spawns != null && spawns.Count > 0)
            {
                Vector3 own = Centroid(spawns);
                var other = map.Spawns(team == Team.Player ? Team.Enemy : Team.Player);
                away = other.Count > 0 ? own - Centroid(other) : away;
                away.y = 0f;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
                center = own + away * RowSpacing;
            }
            Vector3 side = Vector3.Cross(Vector3.up, away);
            int perRow = Mathf.Max(1, PerRow);
            int row = k / perRow, col = k % perRow;
            int inRow = Mathf.Min(perRow, overflow - row * perRow);
            return center + side * ((col - (inRow - 1) / 2f) * Spacing) + away * (row * RowSpacing);
        }

        /// <summary>Face the other team's start (or straight across the field without a map).</summary>
        public Quaternion Facing(Team team, Vector3 from)
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
