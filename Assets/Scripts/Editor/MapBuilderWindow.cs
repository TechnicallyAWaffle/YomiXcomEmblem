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
/// Blockout: for freeform shapes, block the level out with ProBuilder (or any meshes) and Convert Blockout turns it into the
/// template's blockout prefab and the ground heights the simulation walks on (see DrawBlockout).
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
        DrawBlockout(loader);
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
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(template, "Edit map template");
                template.DisplayName = title;
                template.Description = description;
                EditorUtility.SetDirty(template);
            }
            DrawSize(loader);
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

    // ------------------------------------------------------------------ map size

    private MapTemplate sizeFor;     // the template the size field below was filled from
    private Vector2 newSize;
    private bool scaleLayout;

    private static readonly KeyValuePair<string, Vector2>[] SizePresets =
    {
        new KeyValuePair<string, Vector2>("Small 32x32", new Vector2(32f, 32f)),
        new KeyValuePair<string, Vector2>("Medium 40x50", new Vector2(40f, 50f)),
        new KeyValuePair<string, Vector2>("Large 56x56", new Vector2(56f, 56f)),
        new KeyValuePair<string, Vector2>("Huge 80x80", new Vector2(80f, 80f)),
    };

    /// <summary>
    /// The map's overall size (its ground, X by Z, centred on the origin): pick a size, then Resize Map applies it, to the
    /// template and, when it is the one in the scene, to the scene's ground, camera bounds and navmesh. Optionally the
    /// layout (boxes and spawns) is spread or drawn in to match. A blockout's heights are sampled again over the new ground
    /// (its shapes stay where they are).
    /// </summary>
    private void DrawSize(MapLoader loader)
    {
        if (sizeFor != template) { sizeFor = template; newSize = template.GroundSize; }
        newSize = EditorGUILayout.Vector2Field(new GUIContent("Map size (m)", "Width (X) by depth (Z) of the ground, centred on the origin."), newSize);
        newSize = new Vector2(Mathf.Clamp(newSize.x, 10f, 400f), Mathf.Clamp(newSize.y, 10f, 400f));
        EditorGUILayout.BeginHorizontal();
        foreach (var p in SizePresets) if (GUILayout.Button(p.Key, EditorStyles.miniButton)) newSize = p.Value;
        EditorGUILayout.EndHorizontal();
        scaleLayout = EditorGUILayout.Toggle(new GUIContent("Scale layout with it", "Move the boxes and spawns in proportion (their sizes stay). Off: they stay put."), scaleLayout);
        bool changed = (newSize - template.GroundSize).sqrMagnitude > 1e-4f;
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = changed;
        if (GUILayout.Button(string.Format("Resize Map ({0:0} x {1:0} -> {2:0} x {3:0})", template.GroundSize.x, template.GroundSize.y, newSize.x, newSize.y))) Resize(loader, template, newSize, scaleLayout);
        if (GUILayout.Button("Revert", GUILayout.Width(60f))) newSize = template.GroundSize;
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
        if (changed && template != loader.Current) EditorGUILayout.HelpBox("This template isn't the one in the scene: Resize changes the template only.", MessageType.Info);
    }

    private void Resize(MapLoader loader, MapTemplate map, Vector2 size, bool scale)
    {
        Vector2 old = map.GroundSize;
        Vector2 k = new Vector2(size.x / Mathf.Max(1f, old.x), size.y / Mathf.Max(1f, old.y));
        System.Func<Vector3, Vector3> spread = p => new Vector3(p.x * k.x, p.y, p.z * k.y);
        Undo.RecordObject(map, "Resize map");
        map.GroundSize = size;
        bool inScene = map == loader.Current;
        if (scale && inScene)
        {
            // The scene's layout is the one being edited: move it there (Save writes it into the template).
            foreach (var t in loader.Root.GetComponentsInChildren<MapPieceTag>().Select(x => x.transform).Concat(loader.Root.GetComponentsInChildren<MapSpawnPoint>().Select(x => x.transform)).Concat(loader.Root.GetComponentsInChildren<MapObjectiveMarker>().Select(x => x.transform)))
            {
                Undo.RecordObject(t, "Resize map");
                t.position = spread(t.position);
            }
        }
        else if (scale)
        {
            foreach (var p in map.Pieces) p.Position = spread(p.Position);
            for (int i = 0; i < map.PlayerSpawns.Count; i++) map.PlayerSpawns[i] = spread(map.PlayerSpawns[i]);
            for (int i = 0; i < map.EnemySpawns.Count; i++) map.EnemySpawns[i] = spread(map.EnemySpawns[i]);
            for (int i = 0; i < map.ObjectiveSpawns.Count; i++) map.ObjectiveSpawns[i] = spread(map.ObjectiveSpawns[i]);
        }
        if (map.Blockout != null)
            map.Heights = SampleHeights(ClimbableMesh(map.Blockout), size, map.Heights != null && map.Heights.Cell > 0f ? map.Heights.Cell : heightCell);
        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();
        if (inScene)
        {
            if (loader.Ground != null) Undo.RecordObject(loader.Ground, "Resize map");
            var rig = Object.FindFirstObjectByType<TacticsCamera>();
            if (rig != null) Undo.RecordObject(rig, "Resize map");
            loader.ApplyGround(map);
            CombatTestSceneBuilder.BakeNavMesh();
            EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);
        }
        Debug.Log(string.Format("Map Builder: {0} resized from {1:0} x {2:0} to {3:0} x {4:0} m{5}.", map.name, old.x, old.y, size.x, size.y, scale ? ", layout scaled with it" : ""));
    }

    /// <summary>A blockout prefab's climbable mesh (its part with no BlockoutCover), or null.</summary>
    private static Mesh ClimbableMesh(GameObject blockout)
    {
        foreach (var mf in blockout.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null && mf.GetComponentInParent<BlockoutCover>(true) == null) return mf.sharedMesh;
        return null;
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
        EditorGUILayout.LabelField("Objective spawns: where the mission builder can stand the objectives it picks (the Omen)", EditorStyles.miniLabel);
        if (GUILayout.Button("Objective Spawn")) AddObjectiveSpawn(loader);
    }

    private void AddObjectiveSpawn(MapLoader loader)
    {
        int index = loader.Root.GetComponentsInChildren<MapObjectiveMarker>().Length;
        var go = loader.CreateObjectiveSpawn(loader.Root, index, DropPoint());
        Undo.RegisterCreatedObjectUndo(go, "Add objective spawn");
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);
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
        var selected = Selection.gameObjects.Where(g => g.GetComponent<MapPieceTag>() != null || g.GetComponent<MapSpawnPoint>() != null || g.GetComponent<MapObjectiveMarker>() != null).ToList();
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

    // ------------------------------------------------------------------ blockout: freeform shapes (ProBuilder) into a map

    private GameObject blockoutSource;
    private float heightCell = 0.25f;

    /// <summary>
    /// Convert Blockout: block a level out with ProBuilder shapes (or any meshes) under one object in the scene, outside
    /// the "Map" root, then convert it into the template. Every surface is climbable ground unless tagged as cover: an
    /// object with BlockoutCover (it and everything under it), or a face whose material's name starts with "Cover".
    /// </summary>
    private void DrawBlockout(MapLoader loader)
    {
        EditorGUILayout.LabelField("Blockout (ProBuilder shapes, or any meshes)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Build shapes under one object outside \"Map\". Every surface can be stood and climbed on, except cover: tag whole objects below, or give faces a material named \"Cover...\".", EditorStyles.wordWrappedMiniLabel);
        if (blockoutSource == null && Selection.activeGameObject != null && Selection.activeGameObject.scene.IsValid() && !Selection.activeGameObject.transform.IsChildOf(loader.Root))
            blockoutSource = Selection.activeGameObject.transform.root.gameObject;
        blockoutSource = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Source", "The blockout's root object in the scene."), blockoutSource, typeof(GameObject), true);
        heightCell = Mathf.Clamp(EditorGUILayout.FloatField(new GUIContent("Height grid cell (m)", "How finely the climbable surfaces are sampled for the simulation's ground. Smaller: truer slopes and edges, a bigger asset."), heightCell), 0.1f, 1f);

        var picked = Selection.gameObjects.Where(g => g.scene.IsValid()).ToList();
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = picked.Count > 0;
        if (GUILayout.Button(new GUIContent("Tag Selection As Cover", "Blocks sight and walking; nobody can stand on it.")))
            foreach (var g in picked) if (g.GetComponent<BlockoutCover>() == null) Undo.AddComponent<BlockoutCover>(g);
        if (GUILayout.Button(new GUIContent("Make Selection Climbable", "Remove the cover tag.")))
            foreach (var g in picked) { var c = g.GetComponent<BlockoutCover>(); if (c != null) Undo.DestroyObjectImmediate(c); }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        bool inside = blockoutSource != null && blockoutSource.transform.IsChildOf(loader.Root);
        if (inside) EditorGUILayout.HelpBox("The source is under \"Map\", which a rebuild clears: keep it outside.", MessageType.Warning);
        if (template == null) EditorGUILayout.HelpBox("Pick or make a template to convert into.", MessageType.Info);
        GUI.enabled = template != null && blockoutSource != null && !inside && blockoutSource.scene.IsValid();
        if (GUILayout.Button(new GUIContent("Convert Blockout Into Template", "Save the shapes as the template's blockout (a prefab and its meshes in Assets/Maps), sample their heights, and rebuild the map. The source is hidden afterwards; edit it and convert again any time.")))
            ConvertBlockout(loader, template, blockoutSource);
        GUI.enabled = template != null && template.Blockout != null;
        if (GUILayout.Button("Remove Blockout From Template") && EditorUtility.DisplayDialog("Remove blockout", "Take the blockout (and its heights) out of " + template.name + "? Its saved meshes stay in Assets/Maps.", "Remove", "Cancel"))
        {
            Undo.RecordObject(template, "Remove blockout");
            template.Blockout = null;
            template.Heights = new HeightGrid();
            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssets();
            LoadIntoScene(loader, template, false);
        }
        GUI.enabled = true;
        if (template != null && template.Blockout != null)
        {
            int floating = template.Heights.HasUnder ? template.Heights.Under.Count(u => u >= 0f) : 0;
            EditorGUILayout.LabelField("In the template", string.Format("{0}, heights {1} x {2} cells of {3:0.##}m{4}", template.Blockout.name, template.Heights.Width, template.Heights.Depth, template.Heights.Cell,
                floating > 0 ? string.Format(", {0} under floating platforms", floating) : ""));
            if (GUILayout.Button(new GUIContent("Resample Heights", "Sample the blockout's heights again (and the floors under floating platforms), without converting it again.")))
            {
                Undo.RecordObject(template, "Resample heights");
                template.Heights = SampleHeights(ClimbableMesh(template.Blockout), template.GroundSize, template.Heights.Cell > 0f ? template.Heights.Cell : heightCell);
                EditorUtility.SetDirty(template);
                AssetDatabase.SaveAssets();
                Debug.Log(string.Format("Map Builder: {0} heights resampled ({1} cells under floating platforms).", template.name, template.Heights.HasUnder ? template.Heights.Under.Count(u => u >= 0f) : 0));
            }
        }
    }

    /// <summary>Is 'm' a cover material (faces with it are cover): the loader's Cover Material, or one named "Cover..."?</summary>
    private static bool IsCoverMaterial(Material m, MapLoader loader)
    {
        return m != null && (m == loader.CoverMaterial || m.name.StartsWith("Cover", System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The blockout under 'source' into 'map': its faces split into climbable and cover (BlockoutCover on the object or a
    /// parent, or a cover material), each part merged into one world-space mesh (materials kept) saved as an asset, both in
    /// a prefab with mesh colliders (Assets/Maps/&lt;map&gt;_Blockout); the climbable part's top surfaces sampled into the
    /// map's Heights over its ground; the scene's boxes and spawns saved first; then the map rebuilt and the source hidden.
    /// </summary>
    private void ConvertBlockout(MapLoader loader, MapTemplate map, GameObject source)
    {
        var climb = new List<CombineInstance>(); var climbMats = new List<Material>();
        var cover = new List<CombineInstance>(); var coverMats = new List<Material>();
        foreach (var mf in source.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = mf.sharedMesh;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mesh == null || mr == null || !mr.enabled || !mf.gameObject.activeInHierarchy) continue;
            bool coverObject = mf.GetComponentInParent<BlockoutCover>(true) != null;
            var mats = mr.sharedMaterials;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var m = s < mats.Length ? mats[s] : null;
                bool isCover = coverObject || IsCoverMaterial(m, loader);
                (isCover ? cover : climb).Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = mf.transform.localToWorldMatrix });
                (isCover ? coverMats : climbMats).Add(m != null ? m : isCover ? loader.CoverMaterial : loader.ElevationMaterial);
            }
        }
        if (climb.Count == 0 && cover.Count == 0) { EditorUtility.DisplayDialog("Convert blockout", "Nothing to convert: " + source.name + " has no visible meshes under it.", "OK"); return; }

        Save(loader, map);   // the scene's boxes and spawns into the template first: the rebuild below replaces the scene's map
        string folder = EnsureMapsFolder(), stem = folder + "/" + map.name + "_Blockout";
        var climbMesh = SaveMesh(climb, stem + "_Climbable.asset");
        var coverMesh = SaveMesh(cover, stem + "_Cover.asset");

        var root = new GameObject(map.name + "_Blockout");
        if (climbMesh != null) Part(root, "Climbable", climbMesh, climbMats.ToArray(), false);
        if (coverMesh != null) Part(root, "Cover", coverMesh, coverMats.ToArray(), true);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, stem + ".prefab");
        Object.DestroyImmediate(root);

        Undo.RecordObject(map, "Convert blockout");
        map.Blockout = prefab;
        map.Heights = SampleHeights(climbMesh, map.GroundSize, heightCell);
        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(source, "Convert blockout");
        source.SetActive(false);   // the map has it now; turn it back on to edit and convert again
        LoadIntoScene(loader, map, false);
        Debug.Log(string.Format("Map Builder: blockout {0} converted into {1}: {2} climbable and {3} cover parts, heights {4} x {5}.", source.name, map.name, climb.Count, cover.Count, map.Heights.Width, map.Heights.Depth));
    }

    /// <summary>One merged mesh (one submesh per part, in order) saved at 'path' (replacing what was there); null for no parts.</summary>
    private static Mesh SaveMesh(List<CombineInstance> parts, string path)
    {
        if (parts.Count == 0) { AssetDatabase.DeleteAsset(path); return null; }
        var mesh = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(path), indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.CombineMeshes(parts.ToArray(), false, true);
        mesh.RecalculateBounds();
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old != null) { EditorUtility.CopySerialized(mesh, old); EditorUtility.SetDirty(old); Object.DestroyImmediate(mesh); return old; }   // keeps references to it
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static void Part(GameObject root, string name, Mesh mesh, Material[] materials, bool isCover)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        if (isCover) go.AddComponent<BlockoutCover>();
    }

    /// <summary>The room a floating platform needs under it for its cells to keep the floor below (a unit can stand there).</summary>
    public const float FloatingClearance = 2.0f;

    /// <summary>
    /// The top of 'mesh' (world space) over every 'cell' square of the ground, straight down from above it. Where that
    /// top is a floating platform with at least FloatingClearance of open space under it, the floor under it is kept too
    /// (HeightGrid.Under), so units can walk beneath it; a lower gap is solid.
    /// </summary>
    private static HeightGrid SampleHeights(Mesh mesh, Vector2 ground, float cell)
    {
        var grid = new HeightGrid { MinX = -ground.x / 2f, MinZ = -ground.y / 2f, Cell = cell, Width = Mathf.CeilToInt(ground.x / cell), Depth = Mathf.CeilToInt(ground.y / cell) };
        grid.Heights = new float[grid.Width * grid.Depth];
        if (mesh == null) return grid;
        var under = new float[grid.Heights.Length];
        bool floating = false;
        var probe = new GameObject("BlockoutHeightProbe") { hideFlags = HideFlags.HideAndDontSave };
        var collider = probe.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        Physics.SyncTransforms();   // the probe into the physics scene before it is queried
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = false;   // a ray from inside a platform passes out through its underside
        float top = mesh.bounds.max.y + 1f;
        for (int j = 0; j < grid.Depth; j++)
            for (int i = 0; i < grid.Width; i++)
            {
                int k = j * grid.Width + i;
                Vector3 c = grid.Center(i, j);
                RaycastHit hit;
                under[k] = -1f;
                if (!collider.Raycast(new Ray(new Vector3(c.x, top, c.z), Vector3.down), out hit, top + 50f)) { grid.Heights[k] = 0f; continue; }
                float h = Mathf.Max(0f, hit.point.y);
                grid.Heights[k] = h;
                if (h < FloatingClearance) continue;
                // What is below the top: the next floor down (or the ground plane), and the underside of what the top is on.
                float floor = collider.Raycast(new Ray(new Vector3(c.x, h - 0.01f, c.z), Vector3.down), out hit, h + 50f) ? Mathf.Max(0f, hit.point.y) : 0f;
                // From just under the floor: a block standing on it has its underside right there (no gap), a platform higher up.
                float ceiling = collider.Raycast(new Ray(new Vector3(c.x, floor - 0.05f, c.z), Vector3.up), out hit, h + 0.05f) ? hit.point.y : h;
                if (ceiling - floor >= FloatingClearance && floor < h - FloatingClearance) { under[k] = floor; floating = true; }
            }
        Physics.queriesHitBackfaces = backfaces;
        Object.DestroyImmediate(probe);
        grid.Under = floating ? under : new float[0];
        return grid;
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
        // Anything past the map's edge (after a resize, or dragged out): no ground to stand on there.
        if (loader.Current != null)
        {
            Vector2 half = loader.Current.GroundSize / 2f;
            var outside = pieces.Select(p => p.transform).Concat(spawns.Select(s => s.transform))
                .Where(t => Mathf.Abs(t.position.x) > half.x || Mathf.Abs(t.position.z) > half.y).Select(t => t.name).ToList();
            if (outside.Count > 0) EditorGUILayout.HelpBox("Off the map's ground (" + loader.Current.GroundSize.x.ToString("0") + " x " + loader.Current.GroundSize.y.ToString("0") + " m): " + string.Join(", ", outside), MessageType.Warning);
        }
        var tilted = pieces.Where(p => p.Kind == MapPieceKind.Elevation && Quaternion.Angle(p.transform.rotation, Quaternion.identity) > 0.1f).ToList();
        if (tilted.Count > 0) EditorGUILayout.HelpBox("Raised ground is always axis-aligned; these are saved unrotated: " + string.Join(", ", tilted.Select(p => p.name)), MessageType.Warning);
        var tiltedCover = pieces.Where(p => p.Kind == MapPieceKind.Cover && (Mathf.Abs(Mathf.DeltaAngle(p.transform.eulerAngles.x, 0f)) > 0.1f || Mathf.Abs(Mathf.DeltaAngle(p.transform.eulerAngles.z, 0f)) > 0.1f)).ToList();
        if (tiltedCover.Count > 0) EditorGUILayout.HelpBox("Only Y rotation is saved; tilt is dropped for: " + string.Join(", ", tiltedCover.Select(p => p.name)), MessageType.Warning);
    }
}
