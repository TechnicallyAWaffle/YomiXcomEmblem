using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TacticsGame.Core;

/// <summary>
/// Tools > Combat Test > Tuning &amp; Text: every player-facing string and tuning number in one place.
///   - Abilities: every ActionDefinition asset, grouped by class folder. Table shows the key numbers of all of them side by
///     side (name, windup, recovery, cooldown, range, damage, accuracy, stun, strain); Details opens one at a time with
///     every field, its statuses included.
///   - Units: every UnitDefinition asset's names and stats.
///   - Upgrades: every UpgradeDefinition asset (its unit and ability modifiers, granted abilities).
///   - AI Profiles: every AIProfileDefinition asset: its style and the numbers it overrides.
///   - Battle Rules: battle-wide numbers (fall damage, focus, grapple timings...): overrides of BattleSimulation's tunable
///     fields kept in Assets/Resources/BattleTuning.asset, which every battle applies (see BattleTuning).
/// Search narrows any tab by name; the Text / Numbers filter narrows Details. Edits go through Undo and mark the assets
/// dirty: Save writes them to disk. SquadAssets never overwrites a field edited here (its patches only touch what a new
/// DataVersion changes).
/// </summary>
public class TuningWindow : EditorWindow
{
    private enum Tab { Abilities, Units, Upgrades, AIProfiles, BattleRules }
    private enum Show { All, Text, Numbers }
    private static readonly string[] TabNames = { "Abilities", "Units", "Upgrades", "AI Profiles", "Battle Rules" };

    private const string TuningPath = "Assets/Resources/" + BattleTuning.ResourceName + ".asset";

    // Table columns: the ActionDefinition fields and their headers.
    private static readonly string[] TableFields = { "Windup", "Recovery", "Cooldown", "Range", "Damage", "BaseAccuracy", "StunDuration", "StrainOnSelf", "StrainOnTarget" };
    private static readonly string[] TableHeads = { "Windup", "Recovery", "Cooldown", "Range", "Damage", "Accuracy", "Stun", "Strain", "Tgt strain" };
    private const float NameWidth = 170f, CellWidth = 64f;

    // Fields code looks up by value (renaming them breaks the ability): shown, not editable.
    private static readonly HashSet<string> Locked = new HashSet<string> { "Id", "BehaviorId", "AIProfile", "m_Script" };
    // Fields drawn their own way below the rest (an ability's Params: as its behavior's numbers).
    private static readonly HashSet<string> DrawnSeparately = new HashSet<string> { "Params" };

    private Tab tab;
    private Show show;
    private bool table = true;
    private string search = "";
    private Vector2 scroll;
    private readonly HashSet<string> open = new HashSet<string>();
    private List<ActionDefinition> actions = new List<ActionDefinition>();
    private List<UnitDefinition> units = new List<UnitDefinition>();
    private List<UpgradeDefinition> upgrades = new List<UpgradeDefinition>();
    private List<AIProfileDefinition> aiProfiles = new List<AIProfileDefinition>();
    private BattleTuning tuning;
    private BattleSimulation defaults;   // a blank simulation: BattleSimulation's own values, for the Battle Rules defaults

    [MenuItem("Tools/Combat Test/Tuning & Text")]
    public static void Open() { GetWindow<TuningWindow>("Tuning & Text").Show(); }

    private void OnEnable() { Reload(); }
    private void OnProjectChange() { Reload(); Repaint(); }
    private void OnFocus() { Reload(); }

    private void Reload()
    {
        actions = Find<ActionDefinition>().OrderBy(a => Folder(a)).ThenBy(a => a.DisplayName).ToList();
        units = Find<UnitDefinition>().OrderBy(u => Folder(u)).ThenBy(u => u.DisplayName).ToList();
        upgrades = Find<UpgradeDefinition>().OrderBy(u => Folder(u)).ThenBy(u => u.DisplayName).ToList();
        aiProfiles = Find<AIProfileDefinition>().OrderBy(p => p.name).ToList();
        tuning = AssetDatabase.LoadAssetAtPath<BattleTuning>(TuningPath);
    }

    private static List<T> Find<T>() where T : Object
    {
        return AssetDatabase.FindAssets("t:" + typeof(T).Name)
            .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(x => x != null).ToList();
    }

    /// <summary>The folder an asset is in (Actions/Fighter -> "Fighter"): its class.</summary>
    private static string Folder(Object o)
    {
        string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(o)) ?? "";
        dir = dir.Replace('\\', '/');
        return dir.Substring(dir.LastIndexOf('/') + 1);
    }

    private bool Matches(string name, Object o)
    {
        if (string.IsNullOrEmpty(search)) return true;
        string s = search.Trim();
        return (name ?? "").IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               o.name.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               Folder(o).IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        tab = (Tab)GUILayout.Toolbar((int)tab, TabNames, EditorStyles.toolbarButton, GUILayout.Width(470f));
        GUILayout.Space(8f);
        search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120f));
        if (tab == Tab.Abilities) table = GUILayout.Toggle(table, table ? "Table" : "Details", EditorStyles.toolbarButton, GUILayout.Width(60f));
        if (tab != Tab.BattleRules && tab != Tab.AIProfiles && !(tab == Tab.Abilities && table))
            show = (Show)EditorGUILayout.EnumPopup(show, EditorStyles.toolbarDropDown, GUILayout.Width(80f));
        if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(50f))) AssetDatabase.SaveAssets();
        EditorGUILayout.EndHorizontal();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        switch (tab)
        {
            case Tab.Abilities:
                if (table) DrawActionTable();
                else DrawAssets(actions, a => a.DisplayName);
                break;
            case Tab.Units:
                DrawAssets(units, u => u.DisplayName);
                break;
            case Tab.Upgrades:
                if (upgrades.Count == 0) EditorGUILayout.HelpBox("No upgrades yet: create one with Assets > Create > TacticsGame > Upgrade Definition, and give it to a unit in the scene's BattleContext (Placements > Upgrades).", MessageType.Info);
                DrawAssets(upgrades, u => u.DisplayName);
                break;
            case Tab.AIProfiles:
                DrawAIProfiles();
                break;
            case Tab.BattleRules:
                DrawRules();
                break;
        }
        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------ abilities table

    private void DrawActionTable()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Ability", EditorStyles.miniBoldLabel, GUILayout.Width(NameWidth));
        foreach (var h in TableHeads) GUILayout.Label(h, EditorStyles.miniBoldLabel, GUILayout.Width(CellWidth));
        EditorGUILayout.EndHorizontal();

        string group = null;
        foreach (var a in actions)
        {
            if (!Matches(a.DisplayName, a)) continue;
            string folder = Folder(a);
            if (folder != group)
            {
                group = folder;
                GUILayout.Space(4f);
                EditorGUILayout.LabelField(folder, EditorStyles.boldLabel);
            }
            var so = new SerializedObject(a);
            so.Update();
            EditorGUILayout.BeginHorizontal();
            var name = so.FindProperty("DisplayName");
            name.stringValue = EditorGUILayout.TextField(name.stringValue, GUILayout.Width(NameWidth));
            foreach (var field in TableFields)
            {
                var p = so.FindProperty(field);
                if (p != null) p.floatValue = EditorGUILayout.FloatField(p.floatValue, GUILayout.Width(CellWidth));
            }
            if (GUILayout.Button("Details", EditorStyles.miniButton, GUILayout.Width(56f)))
            {
                table = false;
                open.Add(AssetDatabase.GetAssetPath(a));
                search = a.DisplayName;
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
            so.ApplyModifiedProperties();
        }
    }

    // ------------------------------------------------------------------ details (abilities, units)

    private void DrawAssets<T>(List<T> assets, System.Func<T, string> nameOf) where T : Object
    {
        string group = null;
        foreach (var o in assets)
        {
            string name = nameOf(o);
            if (!Matches(name, o)) continue;
            string folder = Folder(o);
            if (folder != group)
            {
                group = folder;
                GUILayout.Space(4f);
                EditorGUILayout.LabelField(folder, EditorStyles.boldLabel);
            }
            DrawDetails(o, string.Format("{0}   ({1})", string.IsNullOrEmpty(name) ? o.name : name, o.name));
        }
    }

    private void DrawDetails(Object o, string title)
    {
        string key = AssetDatabase.GetAssetPath(o);
        bool wasOpen = open.Contains(key);
        bool isOpen = EditorGUILayout.Foldout(wasOpen, title, true);
        if (isOpen != wasOpen) { if (isOpen) open.Add(key); else open.Remove(key); }
        if (!isOpen) return;

        var so = new SerializedObject(o);
        so.Update();
        EditorGUI.indentLevel++;
        var it = so.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            DrawProperty(it.Copy());
        }
        EditorGUI.indentLevel--;
        so.ApplyModifiedProperties();
        var action = o as ActionDefinition;
        if (action != null && show != Show.Text) DrawBehaviorNumbers(action);
        GUILayout.Space(6f);
    }

    /// <summary>An ability's behavior numbers (ActionBehavior.Parameters): ticked ones are set on this ability (its Params).</summary>
    private static void DrawBehaviorNumbers(ActionDefinition def)
    {
        var declared = ActionBehaviors.Get(def.BehaviorId).Parameters.ToList();
        if (declared.Count == 0) return;
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField("Behavior numbers (" + def.BehaviorId + ")", EditorStyles.miniBoldLabel);
        if (def.Params == null) def.Params = new List<BehaviorParam>();
        foreach (var p in declared)
        {
            int at = def.Params.FindIndex(x => x.Key == p.Key);
            bool on = at >= 0, nowOn;
            float now;
            if (!OverrideRow(ObjectNames.NicifyVariableName(p.Key.Replace('_', ' ')), false, false, p.Value, on, on ? def.Params[at].Value : p.Value, out nowOn, out now)) continue;
            Undo.RecordObject(def, "Behavior number");
            if (at >= 0) def.Params.RemoveAt(at);
            if (nowOn) def.Params.Add(new BehaviorParam(p.Key, now));
            EditorUtility.SetDirty(def);
        }
        EditorGUI.indentLevel--;
    }

    /// <summary>One field: strings and numbers (per the Text / Numbers filter); lists of statuses opened up entry by entry;
    /// references to other assets left out.</summary>
    private void DrawProperty(SerializedProperty p)
    {
        if (DrawnSeparately.Contains(p.name)) return;
        if (Locked.Contains(p.name))
        {
            if (show != Show.Numbers && p.propertyType == SerializedPropertyType.String)
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextField(p.displayName, p.stringValue);
            return;
        }
        switch (p.propertyType)
        {
            case SerializedPropertyType.String:
                if (show != Show.Numbers) EditorGUILayout.PropertyField(p);
                return;
            case SerializedPropertyType.Float:
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.Boolean:
            case SerializedPropertyType.Enum:
            case SerializedPropertyType.Vector3:
                if (show != Show.Text) EditorGUILayout.PropertyField(p);
                return;
            case SerializedPropertyType.Generic:
                if (!p.isArray) return;
                if (p.arrayElementType == "string")
                {
                    if (show != Show.Numbers) EditorGUILayout.PropertyField(p, true);
                    return;
                }
                if (p.arrayElementType.StartsWith("PPtr")) return;   // other assets (a unit's abilities): edit those in their own entry
                for (int i = 0; i < p.arraySize; i++)
                {
                    var el = p.GetArrayElementAtIndex(i);
                    EditorGUILayout.LabelField(string.Format("{0}: {1}", p.displayName, ElementName(el, i)), EditorStyles.miniBoldLabel);
                    EditorGUI.indentLevel++;
                    var child = el.Copy();
                    var end = el.GetEndProperty();
                    bool enter = true;
                    while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                    {
                        enter = false;
                        DrawProperty(child.Copy());
                    }
                    EditorGUI.indentLevel--;
                }
                return;
        }
    }

    /// <summary>How a list entry is labelled: a status by its Id, a modifier by the stat it changes (and ability), else its index.</summary>
    private static string ElementName(SerializedProperty el, int index)
    {
        var id = el.FindPropertyRelative("Id");
        if (id != null && id.propertyType == SerializedPropertyType.String) return id.stringValue;
        var stat = el.FindPropertyRelative("Stat");
        if (stat != null && stat.propertyType == SerializedPropertyType.Enum && stat.enumValueIndex >= 0)
        {
            var action = el.FindPropertyRelative("ActionId");
            string name = stat.enumDisplayNames[stat.enumValueIndex];
            return action != null && !string.IsNullOrEmpty(action.stringValue) ? action.stringValue + " " + name : name;
        }
        return index.ToString();
    }

    // ------------------------------------------------------------------ battle rules

    private void DrawRules()
    {
        if (tuning == null)
        {
            EditorGUILayout.HelpBox("No battle tuning asset yet: every battle uses BattleSimulation's own values.", MessageType.Info);
            if (GUILayout.Button("Create " + TuningPath)) CreateTuning();
            return;
        }
        if (defaults == null) defaults = new BattleSimulation(new BattleState(), 0);
        EditorGUILayout.HelpBox("Tick a rule to override it for every battle; untick it to go back to the value in BattleSimulation (shown on the right).", MessageType.None);

        foreach (var f in BattleTuning.TunableFields())
        {
            if (!MatchesField(f)) continue;
            float value;
            bool on = tuning.TryGet(f.Name, out value), nowOn;
            float now;
            if (!OverrideRow(f, BattleTuning.Read(defaults, f), on, value, out nowOn, out now)) continue;
            Undo.RecordObject(tuning, "Battle tuning");
            if (nowOn) tuning.Set(f.Name, now);
            else tuning.Clear(f.Name);
            EditorUtility.SetDirty(tuning);
        }
    }

    private bool MatchesField(System.Reflection.FieldInfo f)
    {
        if (string.IsNullOrEmpty(search)) return true;
        string s = search.Trim();
        return ObjectNames.NicifyVariableName(f.Name).IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0 || f.Name.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>One overridable number: a tick, its value (editable when ticked) and the default. True if anything changed.</summary>
    private static bool OverrideRow(System.Reflection.FieldInfo f, float def, bool on, float value, out bool nowOn, out float now)
    {
        return OverrideRow(ObjectNames.NicifyVariableName(f.Name), f.FieldType == typeof(bool), f.FieldType == typeof(int), def, on, value, out nowOn, out now);
    }

    private static bool OverrideRow(string label, bool isBool, bool isInt, float def, bool on, float value, out bool nowOn, out float now)
    {
        EditorGUILayout.BeginHorizontal();
        nowOn = EditorGUILayout.ToggleLeft(label, on, GUILayout.Width(260f));
        float shown = on ? value : def;
        using (new EditorGUI.DisabledScope(!nowOn))
        {
            if (isBool) now = EditorGUILayout.Toggle(shown >= 0.5f, GUILayout.Width(90f)) ? 1f : 0f;
            else if (isInt) now = EditorGUILayout.IntField(Mathf.RoundToInt(shown), GUILayout.Width(90f));
            else now = EditorGUILayout.FloatField(shown, GUILayout.Width(90f));
        }
        GUILayout.Label("default " + (isBool ? (def >= 0.5f ? "on" : "off") : def.ToString("0.###")), EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
        return nowOn != on || (nowOn && !Mathf.Approximately(now, value));
    }

    // ------------------------------------------------------------------ AI profiles

    private void DrawAIProfiles()
    {
        EditorGUILayout.HelpBox("A profile is a style plus its numbers. Give it to a unit in its definition (AI). Style auto picks the style from the unit's abilities, by their roles. Tick a number to override the style's default.", MessageType.None);
        if (aiProfiles.Count == 0) EditorGUILayout.HelpBox("No AI profiles yet: Assets > Create > TacticsGame > AI Profile.", MessageType.Info);
        var styles = UnitAIs.Styles.Concat(new[] { UnitAIs.Auto }).Distinct().ToArray();
        var labels = styles.Select(s => s == "" ? "basic" : s).ToArray();
        foreach (var p in aiProfiles)
        {
            if (!Matches(p.name, p) && !p.Tuning.Exists(e => e.Field != null && e.Field.IndexOf(search.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            string key = AssetDatabase.GetAssetPath(p);
            bool wasOpen = open.Contains(key);
            bool isOpen = EditorGUILayout.Foldout(wasOpen, string.Format("{0}   ({1})", p.name, p.Style == "" ? "basic" : p.Style), true);
            if (isOpen != wasOpen) { if (isOpen) open.Add(key); else open.Remove(key); }
            if (!isOpen) continue;
            EditorGUI.indentLevel++;
            int at = System.Array.IndexOf(styles, p.Style ?? "");
            int now = EditorGUILayout.Popup("Style", Mathf.Max(0, at), labels);
            if (now != at) { Undo.RecordObject(p, "AI style"); p.Style = styles[now]; EditorUtility.SetDirty(p); }
            // The numbers of that style (auto: the ones every style shares), defaulting to a fresh one's.
            var sample = UnitAIs.Create(p.Style == UnitAIs.Auto ? "" : p.Style) ?? new BasicEnemyAI();
            foreach (var f in BattleTuning.TunableFields(sample.GetType()))
            {
                float value;
                bool on = p.TryGet(f.Name, out value), nowOn;
                float v;
                if (!OverrideRow(f, BattleTuning.Read(sample, f), on, value, out nowOn, out v)) continue;
                Undo.RecordObject(p, "AI tuning");
                if (nowOn) p.Set(f.Name, v); else p.Clear(f.Name);
                EditorUtility.SetDirty(p);
            }
            EditorGUI.indentLevel--;
            GUILayout.Space(6f);
        }
    }

    private void CreateTuning()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        tuning = CreateInstance<BattleTuning>();
        AssetDatabase.CreateAsset(tuning, TuningPath);
        AssetDatabase.SaveAssets();
    }
}
