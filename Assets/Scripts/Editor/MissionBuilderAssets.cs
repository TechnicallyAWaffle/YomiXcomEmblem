using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TacticsGame.Core;
using TacticsGame.Presentation;

/// <summary>
/// The roguelike campaign's mission builder asset (Assets/Campaign/MissionBuilder): every map in the project and a
/// ballpark pool of enemies and objectives (costs 1-10, counts, cooldowns, minimum mission levels) to tune on the asset.
/// Ensure makes it if it is missing and hands it to a campaign that has none; entries already on it keep their tuning.
/// </summary>
public static class MissionBuilderAssets
{
    public const string AssetPath = "Assets/Campaign/MissionBuilder.asset";

    /// <summary>Ballpark spawn numbers by unit asset name: cost, min count, max count, mission cooldown, minimum mission level.</summary>
    private static readonly Dictionary<string, int[]> EnemyBallpark = new Dictionary<string, int[]>
    {
        { "Caster",      new[] { 3, 1, 3, 0, 0 } },
        { "Brutebeast",  new[] { 4, 1, 2, 0, 0 } },
        { "Gunner",      new[] { 3, 1, 3, 0, 0 } },
        { "Brawler",     new[] { 4, 1, 2, 0, 0 } },
        { "Assassin",    new[] { 4, 1, 2, 0, 1 } },
        { "Skirmisher",  new[] { 3, 1, 3, 0, 1 } },
        { "Sniper",      new[] { 4, 1, 2, 0, 1 } },
        { "Howler",      new[] { 2, 1, 3, 0, 2 } },
        { "Healer",      new[] { 4, 1, 1, 0, 2 } },
        { "Matriarch",   new[] { 6, 1, 1, 1, 3 } },
        { "Zola",        new[] { 7, 1, 1, 1, 3 } },
        { "Wraith",      new[] { 1, 2, 4, 0, 4 } },
        { "Primavaeran", new[] { 6, 1, 2, 0, 4 } },
        { "Monarch",     new[] { 9, 1, 1, 2, 5 } },
        { "Exemplar",    new[] { 8, 1, 1, 2, 7 } },
    };

    private static readonly Dictionary<string, int[]> ObjectiveBallpark = new Dictionary<string, int[]>
    {
        { "Omen", new[] { 3, 1, 2, 1, 1 } },
    };

    [MenuItem("Tools/Combat Test/Mission Builder Asset")]
    public static void EnsureMenu()
    {
        var campaign = AssetDatabase.LoadAssetAtPath<CampaignDefinition>("Assets/Campaign/Campaign.asset");
        Selection.activeObject = Ensure(campaign);
    }

    /// <summary>The mission builder asset (made if missing, new maps and pool entries added); 'campaign' gets it if it has none.</summary>
    public static MissionBuilderDefinition Ensure(CampaignDefinition campaign)
    {
        var builder = AssetDatabase.LoadAssetAtPath<MissionBuilderDefinition>(AssetPath);
        if (builder == null)
        {
            builder = ScriptableObject.CreateInstance<MissionBuilderDefinition>();
            AssetDatabase.CreateAsset(builder, AssetPath);
        }
        var units = AssetDatabase.FindAssets("t:UnitDefinition").Select(g => AssetDatabase.LoadAssetAtPath<UnitDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(u => u != null).ToList();
        AddOptions(builder.Settings.Enemies, EnemyBallpark, units);
        AddOptions(builder.Settings.Objectives, ObjectiveBallpark, units);
        foreach (var o in builder.Settings.Objectives) if (o.Rules == null) o.Rules = new List<MissionRule>();
        foreach (var map in AssetDatabase.FindAssets("t:MapTemplate").Select(g => AssetDatabase.LoadAssetAtPath<MapTemplate>(AssetDatabase.GUIDToAssetPath(g))))
            if (map != null && !builder.Maps.Contains(map)) builder.Maps.Add(map);
        EditorUtility.SetDirty(builder);
        AssetDatabase.SaveAssetIfDirty(builder);
        if (campaign != null && campaign.Builder == null)
        {
            campaign.Builder = builder;
            if (campaign.Finale == null) campaign.Finale = campaign.Missions.LastOrDefault(m => m != null && m.Spec != null && m.Spec.Enemies.Exists(e => e != null && e.Unit != null && e.Unit.Boss));
            EditorUtility.SetDirty(campaign);
            AssetDatabase.SaveAssetIfDirty(campaign);
        }
        return builder;
    }

    private static void AddOptions(List<SpawnOption> pool, Dictionary<string, int[]> ballpark, List<UnitDefinition> units)
    {
        foreach (var kv in ballpark)
        {
            var unit = units.Find(u => u.name == kv.Key);
            if (unit == null || pool.Exists(o => o != null && o.Unit == unit)) continue;
            var n = kv.Value;
            pool.Add(new SpawnOption { Unit = unit, Cost = n[0], MinCount = n[1], MaxCount = n[2], MissionCooldown = n[3], MinimumMissionLevel = n[4] });
        }
    }
}
