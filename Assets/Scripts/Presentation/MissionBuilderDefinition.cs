using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// The mission builder a roguelike campaign uses (CampaignDefinition.Builder): the maps it draws from and its points,
    /// enemies, objectives and waves (MissionBuilderSettings), and what every mission it builds shares (Grasp, Temperance).
    /// Build turns a MissionBuilder plan into an EncounterDefinition the launcher fights like any other.
    /// </summary>
    [CreateAssetMenu(menuName = "TacticsGame/Mission Builder")]
    public class MissionBuilderDefinition : ScriptableObject
    {
        [Tooltip("Maps a mission can be on. Each is used once before any repeats (then the pool refills).")]
        public List<MapTemplate> Maps = new List<MapTemplate>();
        public MissionBuilderSettings Settings = new MissionBuilderSettings();
        [Tooltip("Max Grasp every squad member gains for winning the first built mission.")]
        [Min(0)] public int GraspPerMission = 100;
        [Tooltip("Grasp per mission growth: added to that reward for every mission after the first.")]
        public int GraspPerMissionGrowth = 0;

        /// <summary>The max Grasp winning built mission 'index' (0: the first) gives every member.</summary>
        public int GraspFor(int index) { return Mathf.Max(0, GraspPerMission + GraspPerMissionGrowth * Mathf.Max(0, index)); }
        [Tooltip("A built mission is won only once its reinforcements have all arrived and fallen (off: beating the first wave early skips the rest).")]
        public bool AwaitReinforcements = true;
        [Tooltip("Temperance in every built mission.")]
        public TemperanceSettings Temperance = new TemperanceSettings();
        [Tooltip("A built mission's name: {0} is its number.")]
        public string MissionName = "Mission {0}";

        public MapTemplate Map(string id) { return Maps.Find(m => m != null && m.name == id); }

        /// <summary>Make (or keep: the saved one, if it is for this mission) the mission at 'index' and remember it on the run.</summary>
        public MissionPlan PlanFor(MissionRunState run, int index, int seed)
        {
            if (run.Plan != null && run.Plan.MissionIndex == index && Map(run.Plan.MapId) != null) return run.Plan;
            var ids = Maps.FindAll(m => m != null).ConvertAll(m => m.name);
            var rng = new System.Random(seed * 486187739 + index * 7919 + 101);
            return MissionBuilder.Build(Settings, index, ids, id => { var map = Map(id); return map != null ? map.ObjectiveSpawns.Count : 0; }, run, rng);
        }

        /// <summary>The encounter a plan stands for: its map, enemies and objectives (with their conditions), this builder's rewards.</summary>
        public EncounterDefinition Build(MissionPlan plan, ContentCatalog catalog)
        {
            var e = CreateInstance<EncounterDefinition>();
            e.name = "Built_" + (plan.MissionIndex + 1);
            e.hideFlags = HideFlags.DontSave;
            e.DisplayName = string.Format(MissionName, plan.MissionIndex + 1);
            e.Map = Map(plan.MapId);
            var spec = new EncounterSpec
            {
                Id = "built_" + (plan.MissionIndex + 1),
                Rewards = new EncounterRewards { Grasp = GraspFor(plan.MissionIndex), Experience = 0 },
                AwaitReinforcements = AwaitReinforcements,
                Temperance = new TemperanceSettings { Enabled = Temperance.Enabled, ArriveAfter = Temperance.ArriveAfter },
            };
            foreach (var p in plan.Enemies)
            {
                var option = Settings.Enemies.Find(o => o != null && o.UnitId == p.UnitId);
                var unit = option != null ? option.Unit : catalog != null ? catalog.Unit(p.UnitId) : null;
                if (unit == null) continue;
                spec.Enemies.Add(new EnemyEntry { Unit = unit, Count = Mathf.Max(1, p.Count), ArriveAfter = p.ArriveAfter, Upgrades = option != null ? new List<UpgradeDefinition>(option.Upgrades) : new List<UpgradeDefinition>() });
            }
            foreach (var p in plan.Objectives)
            {
                var option = Settings.Objectives.Find(o => o != null && o.UnitId == p.UnitId);
                var unit = option != null ? option.Unit : catalog != null ? catalog.Unit(p.UnitId) : null;
                if (unit == null) continue;
                spec.Objectives.Add(new ObjectiveEntry { Unit = unit, SpawnPoint = p.SpawnPoint, Upgrades = option != null ? new List<UpgradeDefinition>(option.Upgrades) : new List<UpgradeDefinition>() });
                if (option != null) foreach (var r in option.Rules) if (r != null && !spec.Rules.Exists(x => x.Id == r.Id)) spec.Rules.Add(r);   // one of each condition
            }
            e.Spec = spec;
            return e;
        }
    }
}
