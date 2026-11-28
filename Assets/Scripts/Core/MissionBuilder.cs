using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // ------------------------------------------------------------------ what the builder can spawn

    /// <summary>
    /// One enemy or map objective the mission builder may spawn (MissionBuilderSettings.Enemies / Objectives): what it costs
    /// out of the mission's points, how many come when it is picked, how long it sits out after a mission it was in, and
    /// from which mission on it can show up. An objective's Rules are conditions it puts on the mission (MissionRules).
    /// </summary>
    [System.Serializable]
    public class SpawnOption
    {
        public UnitDefinition Unit;
        [Tooltip("Points each one costs out of the mission's budget.")]
        [Range(1, 10)] public int Cost = 3;
        [Tooltip("When picked, at least this many come (skipped if that many don't fit the points left).")]
        [Min(1)] public int MinCount = 1;
        [Tooltip("At most this many in one mission.")]
        [Min(1)] public int MaxCount = 3;
        [Tooltip("Missions it sits out after one it was in (0: it can come every mission).")]
        [Min(0)] public int MissionCooldown = 0;
        [Tooltip("Missions the squad must have got through before it can come (0: from the first).")]
        [Min(0)] public int MinimumMissionLevel = 0;
        [Tooltip("Upgrades it fights with.")]
        public List<UpgradeDefinition> Upgrades = new List<UpgradeDefinition>();
        [Tooltip("Objectives: conditions it puts on the mission it is in (MissionRules: reinforcements, ...).")]
        public List<MissionRule> Rules = new List<MissionRule>();

        public string UnitId { get { return ContentCatalog.IdOf(Unit); } }
    }

    /// <summary>A condition put on a mission (by an objective in it): a MissionRules id and its numbers.</summary>
    [System.Serializable]
    public class MissionRule
    {
        [Tooltip("A MissionRules id: reinforcements.")]
        public string Id = "";
        public List<BehaviorParam> Params = new List<BehaviorParam>();

        public float Param(string key, float fallback)
        {
            if (Params != null) foreach (var p in Params) if (p.Key == key) return p.Value;
            return fallback;
        }
    }

    /// <summary>The mission builder's numbers (MissionBuilderDefinition holds them with the maps).</summary>
    [System.Serializable]
    public class MissionBuilderSettings
    {
        [Tooltip("Points the first mission is built with.")]
        [Min(0)] public int StartingPoints = 5;
        [Tooltip("Points added for every mission after the first.")]
        [Min(0)] public int PointsPerMission = 5;
        [Tooltip("Objectives a mission gets, at random in this range (also capped by Max Objectives, the map's objective spawn points and the points).")]
        [Min(0)] public int ObjectivesMin = 0;
        [Min(0)] public int ObjectivesMax = 1;
        [Tooltip("The most objectives a mission can ever have, whatever the range above says.")]
        [Min(0)] public int MaxObjectives = 2;
        public List<SpawnOption> Objectives = new List<SpawnOption>();
        public List<SpawnOption> Enemies = new List<SpawnOption>();

        [Header("Waves")]
        [Tooltip("Enemy points on the field at the start of the first mission; past them, enemies come as reinforcements.")]
        [Min(0)] public int FirstWavePoints = 10;
        [Tooltip("First-wave points added for every mission after the first.")]
        [Min(0)] public int FirstWavePointsPerMission = 5;
        [Tooltip("Each reinforcement wave takes the next this-many points of enemies (rolled per wave in this range).")]
        [Min(1)] public int WavePointsMin = 10;
        [Min(1)] public int WavePointsMax = 15;
        [Tooltip("Seconds after the wave before it that a reinforcement wave drops in (rolled per wave in this range; the first: after the battle starts).")]
        [Min(0f)] public float WaveDelayMin = 7f;
        [Min(0f)] public float WaveDelayMax = 10f;

        /// <summary>The points mission 'index' (0: the first) is built with.</summary>
        public int PointsFor(int index) { return StartingPoints + PointsPerMission * Mathf.Max(0, index); }

        /// <summary>The enemy points on the field when mission 'index' starts.</summary>
        public int FirstWaveFor(int index) { return FirstWavePoints + FirstWavePointsPerMission * Mathf.Max(0, index); }
    }

    // ------------------------------------------------------------------ what it built (saved on the roster)

    /// <summary>Some of one unit in a built mission, in one wave: on the field at the start (ArriveAfter 0) or dropped in then.</summary>
    [System.Serializable]
    public class PlannedUnit
    {
        public string UnitId;
        public int Count;
        public float ArriveAfter;
    }

    /// <summary>A map objective in a built mission, at one of its map's objective spawn points.</summary>
    [System.Serializable]
    public class PlannedObjective
    {
        public string UnitId;
        public int SpawnPoint;
    }

    /// <summary>A mission the builder made: its map, its points, who is in it. Saved, so a reload fights the same one.</summary>
    [System.Serializable]
    public class MissionPlan
    {
        public int MissionIndex = -1;
        public string MapId;
        public int Points, PointsSpent;
        public List<PlannedUnit> Enemies = new List<PlannedUnit>();
        public List<PlannedObjective> Objectives = new List<PlannedObjective>();
    }

    /// <summary>A unit sitting out missions (SpawnOption.MissionCooldown): it can come again from mission index 'From'.</summary>
    [System.Serializable]
    public class SpawnCooldown
    {
        public string UnitId;
        public int From;
    }

    /// <summary>What the builder remembers across a run: the maps not used yet this round, who is sitting out, the mission made.</summary>
    [System.Serializable]
    public class MissionRunState
    {
        public List<string> MapBag = new List<string>();
        public List<SpawnCooldown> Cooldowns = new List<SpawnCooldown>();
        public MissionPlan Plan;

        public bool OnCooldown(string unitId, int missionIndex)
        {
            var c = Cooldowns.Find(x => x.UnitId == unitId);
            return c != null && missionIndex < c.From;
        }

        public void SitOut(string unitId, int missionIndex, int missions)
        {
            if (missions <= 0) return;
            Cooldowns.RemoveAll(x => x.UnitId == unitId);
            Cooldowns.Add(new SpawnCooldown { UnitId = unitId, From = missionIndex + 1 + missions });
        }
    }

    // ------------------------------------------------------------------ the builder

    /// <summary>
    /// Builds a mission out of points: a map drawn from a bag (each map once a round, then the bag refills), then a random
    /// number of objectives (ObjectivesMin..Max, at most MaxObjectives, as the map's objective spawn points and the points
    /// allow, always leaving enough for the cheapest enemy), then the rest of the points spent on a random spread of
    /// enemies, split into a first wave and reinforcements (Waves). A unit is only picked if it is past its minimum mission level and off
    /// cooldown; picked, it comes at least MinCount strong and at most MaxCount. Everything used then sits out its cooldown.
    /// </summary>
    public static class MissionBuilder
    {
        /// <param name="missionIndex">0 for the first mission (also how many the squad has got through).</param>
        /// <param name="mapIds">Every map it may use.</param>
        /// <param name="objectiveSpawns">How many objective spawn points a map (by id) has.</param>
        public static MissionPlan Build(MissionBuilderSettings settings, int missionIndex, IList<string> mapIds, System.Func<string, int> objectiveSpawns,
                                        MissionRunState run, System.Random rng)
        {
            var plan = new MissionPlan { MissionIndex = missionIndex, Points = settings.PointsFor(missionIndex) };
            plan.MapId = DrawMap(mapIds, run, rng);
            int left = plan.Points;

            // ---- objectives first, leaving enough for the cheapest enemy that may come
            int reserve = int.MaxValue;
            foreach (var o in settings.Enemies)
                if (Eligible(o, missionIndex, run)) reserve = Mathf.Min(reserve, o.Cost * Mathf.Min(Mathf.Max(1, o.MinCount), Mathf.Max(1, o.MaxCount)));
            if (reserve == int.MaxValue) reserve = 0;
            int slots = plan.MapId != null && objectiveSpawns != null ? Mathf.Max(0, objectiveSpawns(plan.MapId)) : 0;
            int lo = Mathf.Min(settings.ObjectivesMin, settings.ObjectivesMax), hi = Mathf.Max(settings.ObjectivesMin, settings.ObjectivesMax);
            int wanted = Mathf.Min(rng.Next(lo, hi + 1), slots, settings.MaxObjectives);
            var taken = new Dictionary<string, int>();
            var spawnPoints = new List<int>();
            for (int i = 0; i < slots; i++) spawnPoints.Add(i);
            while (plan.Objectives.Count < wanted)
            {
                var option = Pick(settings.Objectives, missionIndex, run, taken, left - reserve, wanted - plan.Objectives.Count, rng);
                if (option == null) break;
                int n = HowMany(option, taken, left - reserve, wanted - plan.Objectives.Count, rng);
                for (int i = 0; i < n; i++)
                {
                    int k = rng.Next(spawnPoints.Count);
                    plan.Objectives.Add(new PlannedObjective { UnitId = option.UnitId, SpawnPoint = spawnPoints[k] });
                    spawnPoints.RemoveAt(k);
                }
                Count(taken, option.UnitId, n);
                left -= option.Cost * n;
            }

            // ---- the rest on enemies, one pick at a time
            var enemyCounts = new Dictionary<string, int>();
            while (true)
            {
                var option = Pick(settings.Enemies, missionIndex, run, enemyCounts, left, int.MaxValue, rng);
                if (option == null) break;
                int n = enemyCounts.ContainsKey(option.UnitId) ? 1 : option.MinCount;
                Count(enemyCounts, option.UnitId, n);
                left -= option.Cost * n;
            }
            // A mission always has someone to fight: too few points for anyone, the cheapest who may come.
            if (enemyCounts.Count == 0)
            {
                SpawnOption cheapest = null;
                foreach (var o in settings.Enemies)
                    if (Eligible(o, missionIndex, run) && (cheapest == null || o.Cost < cheapest.Cost)) cheapest = o;
                if (cheapest != null) { enemyCounts[cheapest.UnitId] = 1; left -= cheapest.Cost; }
            }
            plan.Enemies = Waves(settings, missionIndex, enemyCounts, rng);
            plan.PointsSpent = plan.Points - left;

            // ---- whoever came sits out its cooldown
            foreach (var o in settings.Objectives) if (o != null && o.Unit != null && taken.ContainsKey(o.UnitId)) run.SitOut(o.UnitId, missionIndex, o.MissionCooldown);
            foreach (var o in settings.Enemies) if (o != null && o.Unit != null && enemyCounts.ContainsKey(o.UnitId)) run.SitOut(o.UnitId, missionIndex, o.MissionCooldown);
            run.Plan = plan;
            return plan;
        }

        /// <summary>
        /// Split the enemies into waves: in a random order, they fill the first wave (on the field at the start) up to
        /// FirstWaveFor(missionIndex) points; the rest come as reinforcements, each wave the next WavePointsMin..Max points of
        /// them (at least one), dropping in WaveDelayMin..Max seconds after the wave before.
        /// </summary>
        public static List<PlannedUnit> Waves(MissionBuilderSettings settings, int missionIndex, Dictionary<string, int> counts, System.Random rng)
        {
            var order = new List<SpawnOption>();
            foreach (var o in settings.Enemies)   // the pool's order, then shuffled with the seeded rng: the same split every time
            {
                int n;
                if (o == null || o.Unit == null || !counts.TryGetValue(o.UnitId, out n) || order.Exists(x => x.UnitId == o.UnitId)) continue;
                for (int i = 0; i < n; i++) order.Add(o);
            }
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = order[i]; order[i] = order[j]; order[j] = t; }

            var waves = new List<PlannedUnit>();
            int k = 0, budget = settings.FirstWaveFor(missionIndex);
            float at = 0f;
            while (k < order.Count)
            {
                int spent = 0;
                // Whoever fits the wave's points (a reinforcement wave always takes at least one).
                while (k < order.Count && (spent + order[k].Cost <= budget || (spent == 0 && at > 0f)))
                {
                    spent += order[k].Cost;
                    var into = waves.Find(w => w.UnitId == order[k].UnitId && Mathf.Abs(w.ArriveAfter - at) < 1e-3f);
                    if (into != null) into.Count++; else waves.Add(new PlannedUnit { UnitId = order[k].UnitId, Count = 1, ArriveAfter = at });
                    k++;
                }
                if (k >= order.Count) break;
                if (spent == 0 && at <= 0f && waves.Count == 0) { budget = int.MaxValue; continue; }   // a first wave too small for anyone: the first one comes anyway
                float lo = Mathf.Min(settings.WaveDelayMin, settings.WaveDelayMax), hi = Mathf.Max(settings.WaveDelayMin, settings.WaveDelayMax);
                at += lo + (float)rng.NextDouble() * (hi - lo);
                at = Mathf.Round(at * 10f) / 10f;
                int plo = Mathf.Min(settings.WavePointsMin, settings.WavePointsMax), phi = Mathf.Max(settings.WavePointsMin, settings.WavePointsMax);
                budget = rng.Next(plo, phi + 1);
            }
            return waves;
        }

        /// <summary>A map out of the bag (refilled with every map once it runs out), taken out of it.</summary>
        public static string DrawMap(IList<string> mapIds, MissionRunState run, System.Random rng)
        {
            if (mapIds == null || mapIds.Count == 0) return null;
            run.MapBag.RemoveAll(id => !mapIds.Contains(id));   // maps no longer in the pool
            if (run.MapBag.Count == 0) foreach (var id in mapIds) if (!string.IsNullOrEmpty(id) && !run.MapBag.Contains(id)) run.MapBag.Add(id);
            if (run.MapBag.Count == 0) return null;
            int k = rng.Next(run.MapBag.Count);
            string map = run.MapBag[k];
            run.MapBag.RemoveAt(k);
            return map;
        }

        /// <summary>Can 'o' come in mission 'missionIndex' at all: past its minimum level, off cooldown?</summary>
        public static bool Eligible(SpawnOption o, int missionIndex, MissionRunState run)
        {
            return o != null && o.Unit != null && missionIndex >= o.MinimumMissionLevel && !run.OnCooldown(o.UnitId, missionIndex);
        }

        /// <summary>A random option that can still come and fits: a new one at its MinCount (within 'room'), one already in at one more.</summary>
        private static SpawnOption Pick(List<SpawnOption> pool, int missionIndex, MissionRunState run, Dictionary<string, int> counts, int points, int room, System.Random rng)
        {
            var fits = new List<SpawnOption>();
            foreach (var o in pool)
            {
                if (!Eligible(o, missionIndex, run) || fits.Exists(f => f.UnitId == o.UnitId)) continue;
                int have;
                counts.TryGetValue(o.UnitId, out have);
                int max = Mathf.Max(1, o.MaxCount);
                if (have >= max) continue;
                int need = have > 0 ? 1 : Mathf.Min(Mathf.Max(1, o.MinCount), max);
                if (need > room || o.Cost * need > points) continue;
                fits.Add(o);
            }
            return fits.Count > 0 ? fits[rng.Next(fits.Count)] : null;
        }

        /// <summary>How many of objective 'o' come: MinCount to MaxCount, as the points and spawn points left allow.</summary>
        private static int HowMany(SpawnOption o, Dictionary<string, int> counts, int points, int room, System.Random rng)
        {
            int have;
            counts.TryGetValue(o.UnitId, out have);
            int max = Mathf.Min(Mathf.Max(1, o.MaxCount) - have, room, o.Cost > 0 ? points / o.Cost : room);
            int min = have > 0 ? 1 : Mathf.Max(1, o.MinCount);
            return max <= min ? Mathf.Max(1, max) : rng.Next(min, max + 1);
        }

        private static void Count(Dictionary<string, int> counts, string id, int n)
        {
            int have;
            counts.TryGetValue(id, out have);
            counts[id] = have + n;
        }
    }

    // ------------------------------------------------------------------ conditions objectives put on a mission

    /// <summary>A condition on a mission, started with the battle (MissionRules).</summary>
    public interface IMissionRule
    {
        void Begin(BattleSimulation sim, EncounterSpec spec, MissionRule rule);
    }

    /// <summary>
    /// The conditions objectives can put on the missions they are in, by id (MissionRule.Id). New ones register here:
    ///   reinforcements: endless waves of the mission's own enemies, starting 'after' seconds in (20), one wave 'every'
    ///                   (12) of 'per_wave' (2), none while 'max_up' (6) of them are up.
    /// </summary>
    public static class MissionRules
    {
        public const string Reinforcements = "reinforcements";

        private static readonly Dictionary<string, IMissionRule> rules = new Dictionary<string, IMissionRule>
        {
            { Reinforcements, new ReinforcementsRule() },
        };

        public static void Register(string id, IMissionRule rule) { rules[id] = rule; }

        public static IMissionRule Get(string id) { IMissionRule r; return id != null && rules.TryGetValue(id, out r) ? r : null; }

        /// <summary>Start every condition on 'spec' (EncounterSpec.Rules) in 'sim'.</summary>
        public static void BeginAll(BattleSimulation sim, EncounterSpec spec)
        {
            if (spec == null || spec.Rules == null) return;
            foreach (var r in spec.Rules)
            {
                var rule = r != null ? Get(r.Id) : null;
                if (rule != null) rule.Begin(sim, spec, r);
            }
        }

        private class ReinforcementsRule : IMissionRule
        {
            public void Begin(BattleSimulation sim, EncounterSpec spec, MissionRule rule)
            {
                var pool = new List<UnitDefinition>();
                foreach (var e in spec.Enemies) if (e != null && e.Unit != null && !e.Unit.Boss && !pool.Contains(e.Unit)) pool.Add(e.Unit);
                if (pool.Count == 0) return;
                sim.StartReinforcementWaves(pool, Team.Enemy, rule.Param("after", 20f), rule.Param("every", 12f), Mathf.RoundToInt(rule.Param("per_wave", 2f)), Mathf.RoundToInt(rule.Param("max_up", 6f)));
            }
        }
    }
}
