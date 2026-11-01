using System.IO;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>The squad roster on disk: JSON in the player's data folder (Application.persistentDataPath/roster.json).</summary>
    public static class RosterSave
    {
        public const string FileName = "roster.json";

        public static string FilePath { get { return Path.Combine(Application.persistentDataPath, FileName); } }

        /// <summary>The saved roster, or null if there is none (or it can't be read).</summary>
        public static SquadRoster Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                return JsonUtility.FromJson<SquadRoster>(File.ReadAllText(FilePath));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("RosterSave: couldn't read " + FilePath + ": " + e.Message);
                return null;
            }
        }

        public static void Save(SquadRoster roster)
        {
            if (roster == null) return;
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(roster, true)); }
            catch (System.Exception e) { Debug.LogWarning("RosterSave: couldn't write " + FilePath + ": " + e.Message); }
        }

        public static void Delete()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (System.Exception e) { Debug.LogWarning("RosterSave: couldn't delete " + FilePath + ": " + e.Message); }
        }
    }
}
