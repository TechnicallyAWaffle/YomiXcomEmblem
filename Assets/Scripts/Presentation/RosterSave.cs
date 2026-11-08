using System.IO;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>The squad roster on disk: JSON in the player's data folder (Application.persistentDataPath/roster.json).</summary>
    public static class RosterSave
    {
        public const string FileName = "roster.json";

        public static string FilePath { get { return PathOf(FileName); } }
        public static string PathOf(string file) { return Path.Combine(Application.persistentDataPath, file); }

        /// <summary>The saved roster, or null if there is none (or it can't be read).</summary>
        public static SquadRoster Load(string file = FileName)
        {
            string path = PathOf(file);
            try
            {
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<SquadRoster>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("RosterSave: couldn't read " + path + ": " + e.Message);
                return null;
            }
        }

        public static void Save(SquadRoster roster, string file = FileName)
        {
            if (roster == null) return;
            string path = PathOf(file);
            try { File.WriteAllText(path, JsonUtility.ToJson(roster, true)); }
            catch (System.Exception e) { Debug.LogWarning("RosterSave: couldn't write " + path + ": " + e.Message); }
        }

        public static void Delete(string file = FileName)
        {
            string path = PathOf(file);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (System.Exception e) { Debug.LogWarning("RosterSave: couldn't delete " + path + ": " + e.Message); }
        }
    }
}
