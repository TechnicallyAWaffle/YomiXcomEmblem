using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Every line of GameText in the scripts: each GameText.Get / GameText.Format call's key and default wording, and where it
/// is (file and line), found by reading Assets/Scripts. That is why a call's key and default must be plain string literals.
/// The Tuning &amp; Text window's Text tab lists these, so a line is editable as soon as the code asks for it.
/// </summary>
public static class GameTextIndex
{
    public class Entry
    {
        public string Key, Default, File;
        public int Line;
        public string Group { get { int dot = Key.IndexOf('.'); return dot > 0 ? Key.Substring(0, dot) : Key; } }
        /// <summary>Other defaults the same key has elsewhere (two calls disagreeing: the window warns).</summary>
        public readonly List<string> OtherDefaults = new List<string>();
    }

    // GameText.Get("key", "default") / GameText.Format("key", "default", ...): plain literals, escapes allowed.
    private static readonly Regex Call = new Regex("GameText\\.(?:Get|Format)\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*,\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    public static List<Entry> Scan()
    {
        var byKey = new Dictionary<string, Entry>();
        string root = Path.Combine(Application.dataPath, "Scripts");
        if (!Directory.Exists(root)) return new List<Entry>();
        foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            foreach (Match m in Call.Matches(text))
            {
                if (InComment(text, m.Index)) continue;   // an example in a doc comment, not a line the game shows
                string key = Unescape(m.Groups[1].Value), def = Unescape(m.Groups[2].Value);
                Entry e;
                if (byKey.TryGetValue(key, out e))
                {
                    if (e.Default != def && !e.OtherDefaults.Contains(def)) e.OtherDefaults.Add(def);
                    continue;
                }
                byKey[key] = new Entry
                {
                    Key = key, Default = def, Line = LineOf(text, m.Index),
                    File = "Assets" + path.Substring(Application.dataPath.Length).Replace('\\', '/'),
                };
            }
        }
        var list = new List<Entry>(byKey.Values);
        list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return list;
    }

    /// <summary>Is 'index' after a // on its line (a comment)?</summary>
    private static bool InComment(string text, int index)
    {
        int start = text.LastIndexOf('\n', System.Math.Max(0, index - 1)) + 1;
        return text.IndexOf("//", start, index - start, System.StringComparison.Ordinal) >= 0;
    }

    private static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++) if (text[i] == '\n') line++;
        return line;
    }

    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            char n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '\'': sb.Append('\''); break;
                default: sb.Append('\\').Append(n); break;
            }
        }
        return sb.ToString();
    }
}
