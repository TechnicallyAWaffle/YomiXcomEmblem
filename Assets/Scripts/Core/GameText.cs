using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// The player-facing wording the code shows (menus, notices, popups, tooltip headings, Etching comparisons), editable
    /// in Tools > Combat Test > Tuning &amp; Text > Text without touching code.
    ///
    /// Code asks for each line by key, with its default wording right there:
    ///     GameText.Get("orders.move", "Move")
    ///     GameText.Format("orders.dodged", "{0} dodged {1}! Pick where to dash", unit, attacker)
    /// Assets/Resources/GameText.asset keeps only the lines that were edited (key -> text); every other line shows its
    /// default, so a new line needs nothing but the call. An edited line left empty is hidden wherever the code allows it
    /// (Shown: a controls row, a notice). Format's {0}, {1}... are the values the code fills in; an edited line that breaks
    /// them falls back to the default.
    /// Keys and defaults must be plain string literals in the call: that is how the window finds every line in the scripts
    /// (GameTextIndex). Keys are grouped by what comes before the first dot ("controls", "orders", "prebattle"...).
    ///
    /// Wording that belongs to one ability (its tooltip lines) is on the ability instead: ActionDefinition.TipOverrides.
    /// </summary>
    public class GameText : ScriptableObject
    {
        public const string ResourceName = "GameText";

        [System.Serializable]
        public class Line
        {
            public string Key;
            [TextArea(1, 4)] public string Text = "";
        }

        [Tooltip("The edited lines, by key. Edit them in Tools > Combat Test > Tuning & Text > Text, which lists every line with its default.")]
        public List<Line> Lines = new List<Line>();

        private static GameText loaded;
        private static bool tried;
        private static Dictionary<string, string> map;

        /// <summary>The edited text for 'key', or 'fallback' (the default wording) when it wasn't edited.</summary>
        public static string Get(string key, string fallback)
        {
            var m = Map();
            string text;
            return m != null && key != null && m.TryGetValue(key, out text) && text != null ? text : fallback;
        }

        /// <summary>Get, filled in with string.Format: an edited line that breaks the placeholders shows the default instead.</summary>
        public static string Format(string key, string fallback, params object[] args)
        {
            string text = Get(key, fallback);
            try { return string.Format(text, args); }
            catch (System.FormatException) { return string.Format(fallback, args); }
        }

        /// <summary>Is a line to be shown? (An edited line left empty is hidden.)</summary>
        public static bool Shown(string text) { return !string.IsNullOrEmpty(text); }

        /// <summary>Read the asset again (after an edit: the window calls it, and OnValidate does in Play mode).</summary>
        public static void Reload() { map = null; tried = false; loaded = null; }

        private void OnValidate() { map = null; }

        private static Dictionary<string, string> Map()
        {
            if (map != null) return map;
            if (loaded == null && !tried)
            {
                tried = true;
                try { loaded = Resources.Load<GameText>(ResourceName); }
                catch (System.Exception) { loaded = null; }   // outside Unity (the sim tests): the defaults
            }
            map = new Dictionary<string, string>();
            if (loaded != null && loaded.Lines != null)
                foreach (var l in loaded.Lines) if (l != null && !string.IsNullOrEmpty(l.Key)) map[l.Key] = l.Text ?? "";
            return map;
        }
    }
}
