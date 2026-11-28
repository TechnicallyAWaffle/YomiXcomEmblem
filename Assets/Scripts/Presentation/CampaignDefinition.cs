using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// A campaign: its missions in order, the squad's classes, and the progression it is played with (ProgressionConfig).
    /// With a Builder it is a roguelike run: Generated Missions built from points (MissionBuilderDefinition), then the
    /// Finale; without one, the hand-made Missions list. CampaignScreens runs it: preparation, the battle, rewards,
    /// upgrades, the next mission.
    /// </summary>
    [CreateAssetMenu(menuName = "TacticsGame/Campaign")]
    public class CampaignDefinition : ScriptableObject
    {
        public string DisplayName = "Campaign";
        [Tooltip("Builds the run's missions. Empty: the hand-made Missions below, in order.")]
        public MissionBuilderDefinition Builder;
        [Tooltip("With a Builder: how many missions it builds before the Finale.")]
        [Min(0)] public int GeneratedMissions = 10;
        [Tooltip("With a Builder: the last mission, hand-made (empty: the run ends after the built ones).")]
        public EncounterDefinition Finale;
        [Tooltip("Without a Builder: the missions, in order.")]
        public List<EncounterDefinition> Missions = new List<EncounterDefinition>();
        [Tooltip("A lost battle ends the run (a new campaign starts over); off: the mission is tried again.")]
        public bool LossEndsRun = true;
        [Tooltip("The squad a new campaign starts with, in order.")]
        public List<UnitDefinition> Squad = new List<UnitDefinition>();
        [Tooltip("Every class a new campaign can be started with. With more of them than Squad Size, the player picks the squad on a class selection screen; otherwise Squad is used as it is.")]
        public List<UnitDefinition> Classes = new List<UnitDefinition>();
        [Tooltip("How many classes the player picks for a squad.")]
        [Min(1)] public int SquadSize = 4;
        public ProgressionConfig Progression;

        /// <summary>How many missions the campaign has.</summary>
        public int MissionCount { get { return Builder != null ? GeneratedMissions + (Finale != null ? 1 : 0) : Missions.Count; } }

        /// <summary>Is mission 'index' built by the Builder (not hand-made)?</summary>
        public bool IsBuilt(int index) { return Builder != null && index >= 0 && index < GeneratedMissions; }

        /// <summary>
        /// Mission 'index': a hand-made one, or (with a Builder) the one built for it, made now and kept on the roster's run
        /// if it hasn't been yet. Null past the last.
        /// </summary>
        public EncounterDefinition Mission(int index, SquadRoster roster, ContentCatalog catalog)
        {
            if (Builder == null) return index >= 0 && index < Missions.Count ? Missions[index] : null;
            if (index == GeneratedMissions) return Finale;
            if (!IsBuilt(index) || roster == null) return null;
            if (roster.Run == null) roster.Run = new MissionRunState();
            return Builder.Build(Builder.PlanFor(roster.Run, index, roster.Seed), catalog);
        }

        /// <summary>The max Grasp winning every mission before 'index' gives a member.</summary>
        public int GraspBefore(int index)
        {
            int grasp = 0;
            for (int i = 0; i < index; i++)
            {
                if (IsBuilt(i)) { grasp += Builder.GraspFor(i); continue; }
                var m = Builder != null ? (i == GeneratedMissions ? Finale : null) : (i < Missions.Count ? Missions[i] : null);
                if (m != null && m.Spec != null && m.Spec.Rewards != null) grasp += m.Spec.Rewards.Grasp;
            }
            return grasp;
        }

        /// <summary>A mission's name in lists without building it ("Mission 3", or a hand-made one's title).</summary>
        public string MissionTitle(int index)
        {
            if (Builder == null) return index >= 0 && index < Missions.Count && Missions[index] != null ? Missions[index].Title : "";
            if (index == GeneratedMissions && Finale != null) return Finale.Title;
            return string.Format(Builder.MissionName, index + 1);
        }
    }
}
