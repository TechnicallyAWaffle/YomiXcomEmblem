using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TacticsGame.Core;
using TacticsGame.Presentation;

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
///   - Text: every line of interface wording the code shows (GameText: menus, notices, popups, tooltip headings, Etching
///     comparisons), found in the scripts (GameTextIndex), grouped by key; edits are kept in Assets/Resources/GameText.asset
///     and show at once, in Play mode too. An ability's own tooltip lines are changed on the ability (Abilities > Details:
///     Tooltip lines, ActionDefinition.TipOverrides).
/// Search narrows any tab by name; the Text / Numbers filter narrows Details. Edits go through Undo and mark the assets
/// dirty: Save writes them to disk. SquadAssets never overwrites a field edited here (its patches only touch what a new
/// DataVersion changes).
/// </summary>
public class TuningWindow : EditorWindow
{
    private enum Tab { Abilities, Units, Upgrades, AIProfiles, BattleRules, Text, Statuses }
    private enum Show { All, Text, Numbers }
    private static readonly string[] TabNames = { "Abilities", "Units", "Upgrades", "AI Profiles", "Battle Rules", "Text", "Statuses" };

    private const string TuningPath = "Assets/Resources/" + BattleTuning.ResourceName + ".asset";
    private const string GameTextPath = "Assets/Resources/" + GameText.ResourceName + ".asset";

    // Table columns: the ActionDefinition fields and their headers.
    private static readonly string[] TableFields = { "Windup", "Recovery", "Cooldown", "Range", "Damage", "BaseAccuracy", "StunDuration", "StrainOnSelf", "StrainOnTarget" };
    private static readonly string[] TableHeads = { "Windup", "Recovery", "Cooldown", "Range", "Damage", "Accuracy", "Stun", "Strain", "Tgt strain" };
    private const float NameWidth = 170f, CellWidth = 64f;

    // Fields code looks up by value (renaming them breaks the ability): shown, not editable.
    private static readonly HashSet<string> Locked = new HashSet<string> { "Id", "BehaviorId", "AIProfile", "m_Script" };
    // Fields drawn their own way below the rest (an ability's Params: as its behavior's numbers).
    private static readonly HashSet<string> DrawnSeparately = new HashSet<string> { "Params", "TipOverrides" };

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
        gameText = AssetDatabase.LoadAssetAtPath<GameText>(GameTextPath);
        textIndex = null;   // scanned again when the Text tab is next drawn (scripts may have changed)
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
        tab = (Tab)GUILayout.Toolbar((int)tab, TabNames, EditorStyles.toolbarButton, GUILayout.Width(650f));
        GUILayout.Space(8f);
        search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120f));
        if (tab == Tab.Abilities) table = GUILayout.Toggle(table, table ? "Table" : "Details", EditorStyles.toolbarButton, GUILayout.Width(60f));
        if (tab == Tab.Text)
        {
            editedOnly = GUILayout.Toggle(editedOnly, "Edited only", EditorStyles.toolbarButton, GUILayout.Width(80f));
            if (GUILayout.Button("Rescan", EditorStyles.toolbarButton, GUILayout.Width(60f))) textIndex = null;
        }
        if (tab == Tab.Statuses) editedOnly = GUILayout.Toggle(editedOnly, "Edited only", EditorStyles.toolbarButton, GUILayout.Width(80f));
        if (tab != Tab.BattleRules && tab != Tab.AIProfiles && tab != Tab.Text && tab != Tab.Statuses && !(tab == Tab.Abilities && table))
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
            case Tab.Text:
                DrawText();
                break;
            case Tab.Statuses:
                DrawStatuses();
                break;
        }
        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------ text (GameText)

    private GameText gameText;
    private List<GameTextIndex.Entry> textIndex;
    private bool editedOnly;
    private GUIStyle defaultTextStyle;

    /// <summary>
    /// Every GameText line, grouped by its key's first part: the key, its default wording, and its edited text if it has
    /// one. Edit copies the default in to start from; Hide keeps an empty text (hidden where the code allows); Reset drops
    /// the edit. {0}, {1}... are values the code fills in: keep them.
    /// </summary>
    private void DrawText()
    {
        if (textIndex == null) textIndex = GameTextIndex.Scan();
        if (defaultTextStyle == null)
        {
            defaultTextStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            defaultTextStyle.normal.textColor = new Color(0.62f, 0.62f, 0.62f);
        }
        if (gameText == null)
        {
            EditorGUILayout.HelpBox("No GameText asset yet: every line shows its default wording. Create it to start editing.", MessageType.Info);
            if (!GUILayout.Button("Create " + GameTextPath, GUILayout.Width(320f))) return;
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            gameText = CreateInstance<GameText>();
            AssetDatabase.CreateAsset(gameText, GameTextPath);
            AssetDatabase.SaveAssets();
            GameText.Reload();
        }

        var edited = new Dictionary<string, GameText.Line>();
        foreach (var l in gameText.Lines) if (l != null && !string.IsNullOrEmpty(l.Key)) edited[l.Key] = l;
        EditorGUILayout.LabelField(string.Format("{0} lines, {1} edited. {{0}}, {{1}}... are values filled in by the game: keep them. Hide leaves a line empty (hidden where the game allows it).",
            textIndex.Count, edited.Count), EditorStyles.wordWrappedMiniLabel);

        string s = (search ?? "").Trim();
        string group = null;
        bool groupOpen = false;
        foreach (var e in textIndex)
        {
            GameText.Line line;
            bool isEdited = edited.TryGetValue(e.Key, out line);
            if (editedOnly && !isEdited) continue;
            if (s.Length > 0 && e.Key.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) < 0 && e.Default.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                !(isEdited && (line.Text ?? "").IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            if (e.Group != group)
            {
                group = e.Group;
                string k = "text:" + group;
                bool was = open.Contains(k) || s.Length > 0 || editedOnly;
                groupOpen = EditorGUILayout.Foldout(was, ObjectNames.NicifyVariableName(group), true, EditorStyles.foldoutHeader);
                if (groupOpen && !open.Contains(k) && s.Length == 0 && !editedOnly) open.Add(k);
                else if (!groupOpen) open.Remove(k);
            }
            if (!groupOpen) continue;
            DrawTextLine(e, isEdited ? line : null);
        }
        if (textIndex.Count == 0) EditorGUILayout.HelpBox("No GameText.Get / GameText.Format calls found in Assets/Scripts.", MessageType.Info);
    }

    private void DrawTextLine(GameTextIndex.Entry e, GameText.Line line)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.SelectableLabel(e.Key, EditorStyles.miniBoldLabel, GUILayout.Height(16f));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(new GUIContent("Source", e.File + ":" + e.Line), EditorStyles.miniButtonLeft, GUILayout.Width(52f)))
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(e.File), e.Line);
        if (line == null)
        {
            if (GUILayout.Button("Edit", EditorStyles.miniButtonMid, GUILayout.Width(40f))) SetText(e.Key, e.Default);
            if (GUILayout.Button("Hide", EditorStyles.miniButtonRight, GUILayout.Width(40f))) SetText(e.Key, "");
        }
        else if (GUILayout.Button("Reset", EditorStyles.miniButtonRight, GUILayout.Width(50f))) { RemoveText(e.Key); line = null; }
        EditorGUILayout.EndHorizontal();
        GUILayout.Label((line != null ? "Default: " : "") + e.Default, defaultTextStyle);
        if (e.OtherDefaults.Count > 0)
            EditorGUILayout.HelpBox("This key has other defaults elsewhere in the code: " + string.Join(" | ", e.OtherDefaults.ToArray()), MessageType.Warning);
        if (line != null)
        {
            EditorGUI.BeginChangeCheck();
            string now = EditorGUILayout.TextArea(line.Text ?? "", EditorStyles.textArea);
            if (EditorGUI.EndChangeCheck()) SetText(e.Key, now);
            if (string.IsNullOrEmpty(line.Text)) EditorGUILayout.LabelField("(hidden)", EditorStyles.centeredGreyMiniLabel);
            string missing = MissingPlaceholders(e.Default, line.Text ?? "");
            if (missing != null && !string.IsNullOrEmpty(line.Text)) EditorGUILayout.HelpBox("Leaves out " + missing + ": that value won't show.", MessageType.None);
        }
        EditorGUILayout.EndVertical();
    }

    /// <summary>The {0}, {1}... the default has and the text doesn't (null: none missing).</summary>
    private static string MissingPlaceholders(string def, string text)
    {
        var missing = new List<string>();
        for (int i = 0; i < 10; i++) { string p = "{" + i; if (def.Contains(p) && !text.Contains(p)) missing.Add("{" + i + "}"); }
        return missing.Count > 0 ? string.Join(", ", missing.ToArray()) : null;
    }

    private void SetText(string key, string text)
    {
        Undo.RecordObject(gameText, "Edit text");
        var line = gameText.Lines.Find(l => l != null && l.Key == key);
        if (line == null) gameText.Lines.Add(line = new GameText.Line { Key = key });
        line.Text = text;
        EditorUtility.SetDirty(gameText);
        GameText.Reload();
    }

    private void RemoveText(string key)
    {
        Undo.RecordObject(gameText, "Reset text");
        gameText.Lines.RemoveAll(l => l == null || l.Key == key);
        EditorUtility.SetDirty(gameText);
        GameText.Reload();
    }

    // ------------------------------------------------------------------ statuses (StatusGlossary)

    /// <summary>
    /// Every status the game knows a description for or an ability puts on units: its shared description (what every
    /// ability's STATUS EFFECTS section says; edited into the GameText asset under status.desc.id), and, per ability using it,
    /// that ability's own wording (its StatusSpec's Look.Description), which replaces the shared one in that popup only.
    /// </summary>
    private void DrawStatuses()
    {
        if (defaultTextStyle == null)
        {
            defaultTextStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            defaultTextStyle.normal.textColor = new Color(0.62f, 0.62f, 0.62f);
        }
        if (gameText == null)
        {
            EditorGUILayout.HelpBox("No GameText asset yet: create it in the Text tab to edit status descriptions. Each ability's own wording can still be edited below.", MessageType.Info);
        }
        // id -> the abilities that put it on someone, with the property path of each spec.
        var uses = new SortedDictionary<string, List<KeyValuePair<ActionDefinition, string>>>();
        foreach (var id in StatusGlossary.Known) uses[id] = new List<KeyValuePair<ActionDefinition, string>>();
        string[] fields = { "SelfStatuses", "SelfStatusesDuringWindup", "TargetStatusesDuringWindup", "TargetStatusesOnHit", "SelfStatusesOnHit", "AuraStatuses", "RandomStatusesOnHit" };
        foreach (var a in actions)
        {
            int f = 0;
            foreach (var list in StatusGlossary.SpecLists(a))
            {
                string field = fields[f++];
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var spec = list[i];
                    if (spec == null || string.IsNullOrEmpty(spec.Id)) continue;
                    List<KeyValuePair<ActionDefinition, string>> l;
                    if (!uses.TryGetValue(spec.Id, out l)) uses[spec.Id] = l = new List<KeyValuePair<ActionDefinition, string>>();
                    l.Add(new KeyValuePair<ActionDefinition, string>(a, field + ".Array.data[" + i + "]"));
                }
            }
            foreach (var id in ActionBehaviors.Get(a.BehaviorId).StatusesApplied(a))
                if (!uses.ContainsKey(id)) uses[id] = new List<KeyValuePair<ActionDefinition, string>>();
        }
        var edited = new Dictionary<string, GameText.Line>();
        if (gameText != null) foreach (var l in gameText.Lines) if (l != null && !string.IsNullOrEmpty(l.Key)) edited[l.Key] = l;
        EditorGUILayout.LabelField(string.Format("{0} statuses. The shared description is what every ability's STATUS EFFECTS section says about it; an ability's own wording replaces it in that ability's popup only. Hide leaves it out.", uses.Count), EditorStyles.wordWrappedMiniLabel);

        string s = (search ?? "").Trim();
        foreach (var kv in uses)
        {
            string id = kv.Key, key = StatusGlossary.Key(id);
            GameText.Line line;
            bool isEdited = edited.TryGetValue(key, out line);
            bool ownWording = kv.Value.Exists(u => OwnWording(u.Key, u.Value) != null);
            if (editedOnly && !isEdited && !ownWording) continue;
            string name = StatusDisplay.NameOf(id), def = StatusGlossary.Default(id) ?? "";
            if (s.Length > 0 && id.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                def.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) < 0 && !kv.Value.Exists(u => u.Key.DisplayName.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0)) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name.ToUpperInvariant() + "   (" + id + ")", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (gameText != null)
            {
                if (!isEdited)
                {
                    if (GUILayout.Button("Edit", EditorStyles.miniButtonLeft, GUILayout.Width(40f))) SetText(key, def);
                    if (GUILayout.Button("Hide", EditorStyles.miniButtonRight, GUILayout.Width(40f))) SetText(key, "");
                }
                else if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(50f))) { RemoveText(key); isEdited = false; }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Label((isEdited ? "Default: " : "") + (def.Length > 0 ? def : "(no built-in description: nothing is shown until one is written)"), defaultTextStyle);
            if (isEdited)
            {
                EditorGUI.BeginChangeCheck();
                string now = EditorGUILayout.TextArea(line.Text ?? "", EditorStyles.textArea);
                if (EditorGUI.EndChangeCheck()) SetText(key, now);
                if (string.IsNullOrEmpty(line.Text)) EditorGUILayout.LabelField("(hidden)", EditorStyles.centeredGreyMiniLabel);
            }
            if (kv.Value.Count > 0)
            {
                string k = "status:" + id;
                bool was = open.Contains(k) || ownWording;
                bool now = EditorGUILayout.Foldout(was, string.Format("Used by {0} {1}", kv.Value.Count, kv.Value.Count == 1 ? "ability" : "abilities"), true);
                if (now && !was) open.Add(k); else if (!now) open.Remove(k);
                if (now) foreach (var u in kv.Value) DrawOwnWording(u.Key, u.Value);
            }
            EditorGUILayout.EndVertical();
        }
    }

    /// <summary>An ability's own wording for one of its statuses (that spec's Look.Description), or null.</summary>
    private static string OwnWording(ActionDefinition a, string specPath)
    {
        var so = new SerializedObject(a);
        var p = so.FindProperty(specPath + ".Look.Description");
        return p != null && !string.IsNullOrEmpty(p.stringValue) ? p.stringValue : null;
    }

    private void DrawOwnWording(ActionDefinition a, string specPath)
    {
        var so = new SerializedObject(a);
        var p = so.FindProperty(specPath + ".Look.Description");
        if (p == null) return;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("   " + a.DisplayName + "  (" + Folder(a) + ")", GUILayout.Width(240f));
        EditorGUI.BeginChangeCheck();
        string now = EditorGUILayout.TextField(p.stringValue ?? "");
        if (EditorGUI.EndChangeCheck()) { p.stringValue = now; so.ApplyModifiedProperties(); }
        if (string.IsNullOrEmpty(p.stringValue)) GUILayout.Label("shared", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(44f));
        else if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(44f))) { p.stringValue = ""; so.ApplyModifiedProperties(); }
        EditorGUILayout.EndHorizontal();
    }

    // ------------------------------------------------------------------ an ability's tooltip lines

    /// <summary>
    /// An ability's hover popup lines as its behavior words them now (ActionBehavior.Describe and the numbers), each with
    /// Edit / Hide to add an override for it (ActionDefinition.TipOverrides), then the overrides themselves. The orders
    /// panel's own lines (Windup, Recovery, Cooldown, Strain...) can be overridden by their labels the same way.
    /// </summary>
    private void DrawTipLines(ActionDefinition def)
    {
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField("Tooltip lines", EditorStyles.miniBoldLabel);
        var behavior = ActionBehaviors.Get(def.BehaviorId);
        var raw = new List<KeyValuePair<string, string>>();
        var saved = def.TipOverrides;
        def.TipOverrides = null;   // the lines as generated, before this ability's own changes
        try
        {
            var full = new AbilityTip(def, null);
            behavior.Describe(full);
            var numbers = new AbilityTip(def, null);
            ActionBehavior.DescribeNumbers(numbers, behavior);
            foreach (var c in numbers.Effect) raw.Add(c);
            foreach (var c in full.Effect) if (!raw.Exists(r => r.Key == c.Key)) raw.Add(c);
        }
        catch (System.Exception ex) { EditorGUILayout.HelpBox("Couldn't word its lines here: " + ex.Message, MessageType.None); }
        finally { def.TipOverrides = saved; }
        foreach (var c in raw)
        {
            bool has = def.TipOverrides != null && def.TipOverrides.Exists(o => o != null && string.Equals((o.Label ?? "").Trim(), c.Key, System.StringComparison.OrdinalIgnoreCase));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(c.Key, c.Value, EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(has))
            {
                if (GUILayout.Button("Edit", EditorStyles.miniButtonLeft, GUILayout.Width(40f))) AddTipOverride(def, c.Key, false);
                if (GUILayout.Button("Hide", EditorStyles.miniButtonRight, GUILayout.Width(40f))) AddTipOverride(def, c.Key, true);
            }
            EditorGUILayout.EndHorizontal();
        }
        var so = new SerializedObject(def);
        so.Update();
        EditorGUILayout.PropertyField(so.FindProperty("TipOverrides"), new GUIContent("Overrides"), true);
        so.ApplyModifiedProperties();
        EditorGUI.indentLevel--;
    }

    private static void AddTipOverride(ActionDefinition def, string label, bool hide)
    {
        Undo.RecordObject(def, "Tooltip line");
        if (def.TipOverrides == null) def.TipOverrides = new List<ActionDefinition.TipOverride>();
        def.TipOverrides.Add(new ActionDefinition.TipOverride { Label = label, Hide = hide });
        EditorUtility.SetDirty(def);
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
        if (action != null && show != Show.Numbers) DrawTipLines(action);
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
