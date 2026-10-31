using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TacticsGame.Core;
using TacticsGame.Presentation;

/// <summary>
/// Tools > Combat Test > Map Builder: lay out battle maps in the scene and save them as MapTemplate assets the pre-battle
/// screen can pick from. Works on the open scene's MapLoader (Build Scene adds one): its "Map" child holds the pieces
/// being edited. Add pieces and spawns from the palette (they drop at the Scene view's centre), then move, scale and
/// rotate them with the usual tools; Duplicate and Delete work as normal. Save writes the pieces into the template,
/// bakes the navmesh and adds the map to the pre-battle screen's list.
/// </summary>
public class MapBuilderWindow : EditorWindow
{
    private const string MapsFolder = "Assets/Maps";

    private MapTemplate template;
    private float snap = 0.5f;
    private Vector2 scroll;

    private struct Preset
    {
        public string Label; public MapPieceKind Kind; public Vector3 Size;
        public Preset(string label, MapPieceKind kind, Vector3 size) { Label = label; Kind = kind; Size = size; }
    }

    private static readonly Preset[] CoverPresets =
    {
        new Preset("Low Wall", MapPieceKind.Cover, new Vector3(4f, 1f, 0.5f)),
        new Preset("Long Low Wall", MapPieceKind.Cover, new Vector3(8f, 1f, 0.5f)),
        new Preset("Tall Wall", MapPieceKind.Cover, new Vector3(6f, 3f, 0.5f)),
        new Preset("Pillar", MapPieceKind.Cover, new Vector3(1.2f, 3f, 1.2f)),
    };

    private static readonly Preset[] ElevationPresets =
    {
        new Preset("Crates (0.9m)", MapPieceKind.Elevation, new Vector3(2f, 0.9f, 2f)),
        new Preset("Ledge (1.2m)", MapPieceKind.Elevation, new Vector3(5f, 1.2f, 4f)),
        new Preset("Ridge (1.2m)", MapPieceKind.Elevation, new Vector3(6f, 1.2f, 10f)),
        new Preset("Tower (2.4m)", MapPieceKind.Elevation, new Vector3(3f, 2.4f, 3f)),
    };

    [MenuItem("Tools/Combat Test/Map Builder")]
    public static void Open() { GetWindow<MapBuilderWindow>("Map Builder").Show(); }

    private void OnSelectionChange() { Repaint(); }
    private void OnHierarchyChange() { Repaint(); }

    private void OnGUI()
    {
        var loader = Object.FindFirstObjectByType<MapLoader>();
        if (loader == null)
        {
            EditorGUILayout.HelpBox("The open scene has no MapLoader. Open CombatTest and run Tools > Combat Test > Build Scene first.", MessageType.Info);
            return;
        }
        if (template == null && loader.Current != null) template = loader.Current;

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawTemplate(loader);
        EditorGUILayout.Space(8f);
        DrawPalette(loader);
        EditorGUILayout.Space(8f);
        DrawEditing(loader);
        EditorGUILayout.Space(8f);
        DrawSummary(loader);
        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------ template: new / load / save

    private void DrawTemplate(MapLoader loader)
    {
        EditorGUILayout.LabelField("Map template", EditorStyles.boldLabel);
        template = (MapTemplate)EditorGUILayout.ObjectField("Template", template, typeof(MapTemplate), false);
        EditorGUILayout.LabelField("In the scene now", loader.Current != null ? loader.Current.name : "(unsaved / none)");

        if (template != null)
        {
            EditorGUI.BeginChangeCheck();
            string title = EditorGUILayout.TextField("Display name", template.DisplayName);
            EditorGUILayout.LabelField("Description");
            string description = EditorGUILayout.TextArea(template.Description ?? "", GUILayout.MinHeight(36f));
            Vector2 ground = EditorGUILayout.Vector2Field("Ground size (m)", template.GroundSize);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(template, "Edit map template");
                template.DisplayName = title;
                template.Description = description;
                template.GroundSize = new Vector2(Mathf.Max(10f, ground.x), Mathf.Max(10f, ground.y));
                EditorUtility.SetDirty(template);
            }
            if (template != loader.Current)
                EditorGUILayout.HelpBox("Save writes what is in the scene now into " + template.name + ". Load it first to edit its own layout.", MessageType.Warning);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("New")) NewMap(loader);
        GUI.enabled = template != null;
        if (GUILayout.Button("Load into scene")) LoadIntoScene(loader, template);
        if (GUILayout.Button("Save")) Save(loader, template);
        GUI.enabled = true;
        if (GUILayout.Button("Save As...")) SaveAs(loader);
        EditorGUILayout.EndHorizontal();
    }

    private void NewMap(MapLoader loader)
    {
        string path = EditorUtility.SaveFilePanelInProject("New map", "NewMap", "asset", "Name the new map template.", EnsureMapsFolder());
        if (string.IsNullOrEmpty(path)) return;
        var map = CreateInstance<MapTemplate>();
        map.DisplayName = System.IO.Path.GetFileNameWithoutExtension(path);
        // Start playable: four spawns a side, across the middle of the field.
        for (int i = 0; i < 4; i++)
        {
            map.PlayerSpawns.Add(new Vector3(-4.5f + i * 3f, 0f, -10f));
            map.EnemySpawns.Add(new Vector3(-4.5f + i * 3f, 0f, 10f));
        }
        AssetDatabase.CreateAsset(map, path);
        AssetDatabase.SaveAssets();
        template = map;
        LoadIntoScene(loader, map, false);
    }

    private void LoadIntoScene(MapLoader loader, MapTemplate map, bool confirm = true)
    {
        if (confirm && loader.Root.childCount > 0 &&
            !EditorUtility.DisplayDialog("Load map", "Replace the map in the scene with " + map.name + "? Unsaved changes to the current pieces are lost.", "Load", "Cancel"))
            return;
        Undo.RecordObject(loader, "Load map");
        if (loader.Ground != null) Undo.RecordObject(loader.Ground, "Load map");
        loader.Build(map);
        PlaceUnits(map);
        EditorUtility.SetDirty(loader);
        CombatTestSceneBuilder.BakeNavMesh();
        EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);
    }

    private void Save(MapLoader loader, MapTemplate map)
    {
        Undo.RecordObject(map, "Save map");
        Undo.RecordObject(loader, "Save map");
        loader.Capture(map, snap);
        EditorUtility.SetDirty(map);
        EditorUtility.SetDirty(loader);
        AssetDatabase.SaveAssets();
        AddToBattleChoices(map);
        CombatTestSceneBuilder.BakeNavMesh();
        EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);
        Debug.Log("Map Builder: saved " + map.Pieces.Count + " pieces, " + map.PlayerSpawns.Count + " squad and " + map.EnemySpawns.Count + " enemy spawns to " + AssetDatabase.GetAssetPath(map));
    }

    private void SaveAs(MapLoader loader)
    {
        string path = EditorUtility.SaveFilePanelInProject("Save map as", template != null ? template.name + "_Copy" : "NewMap", "asset", "Save the scene's map as a new template.", EnsureMapsFolder());
        if (string.IsNullOrEmpty(path)) return;
        var map = CreateInstance<MapTemplate>();
        if (template != null)
        {
            map.Description = template.Description;
            map.GroundSize = template.GroundSize;
        }
        map.DisplayName = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(map, path);
        template = map;
        Save(loader, map);
    }

    /// <summary>Stand the scene's units on the map's spawns, so the edited scene matches what a battle will start with.</summary>
    private static void PlaceUnits(MapTemplate map)
    {
        var context = Object.FindFirstObjectByType<BattleContext>();
        if (context == null) return;
        int player = 0, enemy = 0;
        foreach (var p in context.Placements)
        {
            if (p.View == null) continue;
            var spawns = map.Spawns(p.Team);
            int i = p.Team == Team.Player ? player++ : enemy++;
            if (i >= spawns.Count) continue;
            Undo.RecordObject(p.View.transform, "Place units");
            p.View.transform.position = spawns[i];
        }
    }

    private static void AddToBattleChoices(MapTemplate map)
    {
        var setup = Object.FindFirstObjectByType<PreBattleSetup>();
        if (setup == null || setup.MapChoices.Contains(map)) return;
        Undo.RecordObject(setup, "Add map to battle choices");
        setup.MapChoices.Add(map);
        EditorUtility.SetDirty(setup);
    }

    private static string EnsureMapsFolder()
    {
        if (!AssetDatabase.IsValidFolder(MapsFolder)) AssetDatabase.CreateFolder("Assets", "Maps");
        return MapsFolder;
    }

    // ------------------------------------------------------------------ palette

    private void DrawPalette(MapLoader loader)
    {
        EditorGUILayout.LabelField("Add (at the Scene view's centre)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Cover: blocks sight and walking", EditorStyles.miniLabel);
        PresetRow(loader, CoverPresets);
        EditorGUILayout.LabelField("Raised ground: units jump onto it; shoves can knock them off", EditorStyles.miniLabel);
        PresetRow(loader, ElevationPresets);
        EditorGUILayout.LabelField("Spawns: units start at them in order", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Squad Spawn")) AddSpawn(loader, Team.Player);
        if (GUILayout.Button("Enemy Spawn")) AddSpawn(loader, Team.Enemy);
        EditorGUILayout.EndHorizontal();
    }

    private void PresetRow(MapLoader loader, Preset[] presets)
    {
        EditorGUILayout.BeginHorizontal();
        foreach (var p in presets)
            if (GUILayout.Button(p.Label)) AddPiece(loader, new MapPiece { Name = p.Label, Kind = p.Kind, Size = p.Size, Position = DropPoint() });
        EditorGUILayout.EndHorizontal();
    }

    private Vector3 DropPoint()
    {
        var view = SceneView.lastActiveSceneView;
        Vector3 p = view != null ? view.pivot : Vector3.zero;
        return SnapXZ(new Vector3(p.x, 0f, p.z));
    }

    private void AddPiece(MapLoader loader, MapPiece piece)
    {
        var go = loader.CreatePiece(loader.Root, piece);
        Undo.RegisterCreatedObjectUndo(go, "Add " + piece.Name);
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);
    }

    private void AddSpawn(MapLoader loader, Team team)
    {
        int index = loader.Root.GetComponentsInChildren<MapSpawnPoint>().Count(s => s.Team == team);
        var go = loader.CreateSpawn(loader.Root, team, index, DropPoint());
        Undo.RegisterCreatedObjectUndo(go, "Add spawn");
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);
    }

    // ------------------------------------------------------------------ editing helpers

    private void DrawEditing(MapLoader loader)
    {
        EditorGUILayout.LabelField("Edit", EditorStyles.boldLabel);
        snap = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Snap (m)", "Ground grid for new pieces, Snap Selection and Save. 0 = off."), snap));
        var selected = Selection.gameObjects.Where(g => g.GetComponent<MapPieceTag>() != null || g.GetComponent<MapSpawnPoint>() != null).ToList();
        var elevation = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<MapPieceTag>() : null;
        if (elevation != null && elevation.Kind != MapPieceKind.Elevation) elevation = null;

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = selected.Count > 0;
        if (GUILayout.Button("Snap Selection")) SnapSelection(selected);
        GUI.enabled = elevation != null;
        if (GUILayout.Button(new GUIContent("Stack Tier On Selected", "A smaller 1.2 m block on top of the selected raised ground."))) StackTier(loader, elevation);
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Renumber Spawns")) RenumberSpawns(loader);
        if (GUILayout.Button("Bake NavMesh")) CombatTestSceneBuilder.BakeNavMesh();
        if (GUILayout.Button("Clear Map") && EditorUtility.DisplayDialog("Clear map", "Remove every piece and spawn from the scene's map?", "Clear", "Cancel"))
        {
            foreach (Transform child in loader.Root.Cast<Transform>().ToList()) Undo.DestroyObjectImmediate(child.gameObject);
            EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void SnapSelection(List<GameObject> selected)
    {
        foreach (var go in selected)
        {
            Undo.RecordObject(go.transform, "Snap");
            var t = go.transform;
            Vector3 p = SnapXZ(t.position);
            var tag = go.GetComponent<MapPieceTag>();
            if (tag != null && tag.Kind == MapPieceKind.Elevation) t.rotation = Quaternion.identity;
            t.position = p;
        }
    }

    private void StackTier(MapLoader loader, MapPieceTag below)
    {
        var t = below.transform;
        Vector3 size = t.lossyScale;
        float top = t.position.y + size.y / 2f;
        AddPiece(loader, new MapPiece
        {
            Name = below.name + "_Top", Kind = MapPieceKind.Elevation,
            Size = new Vector3(Mathf.Max(1f, size.x * 0.5f), 1.2f, Mathf.Max(1f, size.z * 0.5f)),
            Position = new Vector3(t.position.x, top, t.position.z),
        });
    }

    private static void RenumberSpawns(MapLoader loader)
    {
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            var spawns = loader.Root.GetComponentsInChildren<MapSpawnPoint>().Where(s => s.Team == team).OrderBy(s => s.Index).ThenBy(s => s.transform.GetSiblingIndex()).ToList();
            for (int i = 0; i < spawns.Count; i++)
            {
                Undo.RecordObject(spawns[i], "Renumber spawns");
                Undo.RecordObject(spawns[i].gameObject, "Renumber spawns");
                spawns[i].Index = i;
                spawns[i].name = (team == Team.Player ? "PlayerSpawn_" : "EnemySpawn_") + (i + 1);
            }
        }
    }

    private Vector3 SnapXZ(Vector3 p)
    {
        if (snap <= 0f) return p;
        return new Vector3(Mathf.Round(p.x / snap) * snap, p.y, Mathf.Round(p.z / snap) * snap);
    }

    // ------------------------------------------------------------------ summary and warnings

    private static void DrawSummary(MapLoader loader)
    {
        var root = loader.Root;
        var pieces = root.GetComponentsInChildren<MapPieceTag>();
        var spawns = root.GetComponentsInChildren<MapSpawnPoint>();
        int cover = pieces.Count(p => p.Kind == MapPieceKind.Cover), raised = pieces.Length - cover;
        int squad = spawns.Count(s => s.Team == Team.Player), enemies = spawns.Length - squad;
        EditorGUILayout.LabelField("In the scene", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(string.Format("{0} cover, {1} raised ground, {2} squad spawns, {3} enemy spawns", cover, raised, squad, enemies));

        var context = Object.FindFirstObjectByType<BattleContext>();
        int squadSize = context != null ? context.Placements.Count(p => p.Team == Team.Player) : 4;
        if (squad < squadSize) EditorGUILayout.HelpBox("Fewer squad spawns than squad members (" + squadSize + "): the rest keep their scene positions.", MessageType.Warning);
        if (enemies == 0) EditorGUILayout.HelpBox("No enemy spawns: enemies line up in rows at the pre-battle screen's EnemyRowCenter.", MessageType.Warning);
        var tilted = pieces.Where(p => p.Kind == MapPieceKind.Elevation && Quaternion.Angle(p.transform.rotation, Quaternion.identity) > 0.1f).ToList();
        if (tilted.Count > 0) EditorGUILayout.HelpBox("Raised ground is always axis-aligned; these are saved unrotated: " + string.Join(", ", tilted.Select(p => p.name)), MessageType.Warning);
        var tiltedCover = pieces.Where(p => p.Kind == MapPieceKind.Cover && (Mathf.Abs(Mathf.DeltaAngle(p.transform.eulerAngles.x, 0f)) > 0.1f || Mathf.Abs(Mathf.DeltaAngle(p.transform.eulerAngles.z, 0f)) > 0.1f)).ToList();
        if (tiltedCover.Count > 0) EditorGUILayout.HelpBox("Only Y rotation is saved; tilt is dropped for: " + string.Join(", ", tiltedCover.Select(p => p.name)), MessageType.Warning);
    }
}
