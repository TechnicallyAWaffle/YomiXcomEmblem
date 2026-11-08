using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// A campaign: its missions in order (each an EncounterDefinition: map, enemies, rewards), the squad's classes, and the
    /// progression it is played with (ProgressionConfig). CampaignScreens runs it: preparation, the battle, rewards,
    /// upgrades, the next mission.
    /// </summary>
    [CreateAssetMenu(menuName = "TacticsGame/Campaign")]
    public class CampaignDefinition : ScriptableObject
    {
        public string DisplayName = "Campaign";
        public List<EncounterDefinition> Missions = new List<EncounterDefinition>();
        [Tooltip("The squad a new campaign starts with, in order.")]
        public List<UnitDefinition> Squad = new List<UnitDefinition>();
        public ProgressionConfig Progression;
        [Tooltip("How many of their offered Etchings each member takes in the upgrade phase before the first mission. 0 skips that phase.")]
        [Min(0)] public int EtchingsBeforeFirstMission = 1;
        [Tooltip("Ability points each unit starts the campaign with (to spend in the first upgrade phase), unless its class has its own number below.")]
        [Min(0)] public int StartingAbilityPoints = 0;
        [Tooltip("Squad classes that start with a different number of ability points than StartingAbilityPoints.")]
        public List<UnitAbilityPoints> StartingAbilityPointsByUnit = new List<UnitAbilityPoints>();
        [Tooltip("How many of their offered Etchings each member takes after winning a mission, unless the mission has its own number below.")]
        [Min(0)] public int EtchingsPerMission = 1;
        [Tooltip("Missions that reward a different number of Etchings than EtchingsPerMission.")]
        public List<MissionEtchings> EtchingsByMission = new List<MissionEtchings>();

        public EncounterDefinition Mission(int index) { return index >= 0 && index < Missions.Count ? Missions[index] : null; }

        /// <summary>How many ability points a member of class 'unit' starts the campaign with.</summary>
        public int StartingPointsFor(UnitDefinition unit)
        {
            var own = StartingAbilityPointsByUnit.Find(x => x != null && x.Unit == unit && unit != null);
            return own != null ? own.Points : StartingAbilityPoints;
        }

        /// <summary>How many Etchings each member takes after winning 'mission'.</summary>
        public int EtchingsFor(EncounterDefinition mission)
        {
            var own = EtchingsByMission.Find(x => x != null && x.Mission == mission && mission != null);
            return own != null ? own.Etchings : EtchingsPerMission;
        }
    }

    /// <summary>A squad class's own starting ability points (CampaignDefinition.StartingAbilityPointsByUnit).</summary>
    [System.Serializable]
    public class UnitAbilityPoints
    {
        public UnitDefinition Unit;
        [Min(0)] public int Points = 1;
    }

    /// <summary>A mission's own Etching reward (CampaignDefinition.EtchingsByMission).</summary>
    [System.Serializable]
    public class MissionEtchings
    {
        public EncounterDefinition Mission;
        [Min(0)] public int Etchings = 1;
    }
}
