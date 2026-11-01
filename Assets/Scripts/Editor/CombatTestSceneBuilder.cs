using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using TacticsGame.Core;
using TacticsGame.Presentation;

/// <summary>
/// Tools > Combat Test > Build Scene: lays out the four-class squad vs four enemies in CombatTest (units, the map from a MapTemplate, navmesh bake,
/// camera, HUD) and wires it into the existing BattleSetup / BattleController. Safe to re-run: everything it
/// places lives under one generated root that is rebuilt each time. Assets it creates are only written once,
/// so tweaks you make to the action/unit definitions survive a rebuild.
/// </summary>
public static class CombatTestSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/CombatTest.unity";
    private const string UnitPrefabPath = "Assets/Prefabs/UnitPrefab.prefab";
    private const string PlaceholderPrefabPath = "Assets/Units/Test/Test.prefab";
    private const string DefaultNavMeshPath = "Assets/Scenes/CombatTest/NavMesh-Navmesh.asset";
    private const string GeneratedRootName = "CombatTest_Generated";

    [MenuItem("Tools/Combat Test/Build Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath);
        }

        int environmentLayer = LayerMask.NameToLayer("Environment");
        int unitLayer = LayerMask.NameToLayer("Units");
        if (environmentLayer < 0 || unitLayer < 0)
        {
            Debug.LogError("CombatTestSceneBuilder: add 'Environment' and 'Units' layers in Project Settings > Tags and Layers first.");
            return;
        }

        // ---- Assets
        var rifle = GetOrCreate<ActionDefinition>("Assets/Actions/RifleShot.asset", a =>
        {
            a.Id = "rifle_shot"; a.DisplayName = "Rifle Shot"; a.BehaviorId = "basic_attack";
            a.Targeting = TargetType.Enemy; a.Range = 14f; a.IsRanged = true;
            a.Windup = 0.6f; a.Recovery = 0.5f;
            a.Impact = ImpactType.Light; a.Damage = 14f; a.BaseAccuracy = 0.85f; a.StunDuration = 0.15f; a.StrainOnTarget = 5f;
        });
        SquadAssets.ApplyVisuals(rifle);
        // The Gunner and Brawler (Assets/Units/Gunner, Brawler) became the Caster and Brutebeast (SquadAssets.CreateEnemies);
        // their old assets are no longer used.
        var unitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitPrefabPath);

        var playerMat = GetOrCreateMaterial("Assets/Materials/Team_Player.mat", new Color(0.25f, 0.5f, 1f));
        var enemyMat = GetOrCreateMaterial("Assets/Materials/Team_Enemy.mat", new Color(1f, 0.3f, 0.25f));
        var coverMat = GetOrCreateMaterial("Assets/Materials/Cover.mat", new Color(0.55f, 0.55f, 0.5f));
        var elevationMat = GetOrCreateMaterial("Assets/Materials/Elevation.mat", new Color(0.5f, 0.44f, 0.36f));

        FixUnitPrefab(unitLayer);
        unitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitPrefabPath);
        SquadAssets.ApplyDescription(rifle, "RifleShot");
        var squad = SquadAssets.Create(unitPrefab, rifle);
        var enemies = SquadAssets.CreateEnemies(unitPrefab);

        // ---- Scene
        foreach (var go in scene.GetRootGameObjects())
        {
            bool placeholder = PrefabUtility.IsAnyPrefabInstanceRoot(go) &&
                               PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == PlaceholderPrefabPath;
            if (go.name == GeneratedRootName || placeholder) Object.DestroyImmediate(go);
        }

        var ground = GameObject.Find("Plane");
        if (ground != null)
        {
            ground.transform.position = Vector3.zero;   // 40 x 50 m at the existing scale
            ground.layer = environmentLayer;
        }

        var context = Object.FindFirstObjectByType<BattleContext>();
        var controller = Object.FindFirstObjectByType<BattleController>();
        var director = Object.FindFirstObjectByType<PlaybackDirector>();
        if (context == null || controller == null || director == null)
        {
            Debug.LogError("CombatTestSceneBuilder: scene needs a BattleContext, BattleController and PlaybackDirector.");
            return;
        }

        var root = new GameObject(GeneratedRootName);
        var unitsRoot = new GameObject("Units").transform;
        unitsRoot.SetParent(root.transform, false);

        // ---- The map. Cover and raised ground come from MapTemplate assets (made once, so Map Builder edits survive a
        // rebuild). The scene keeps whichever map it had; a fresh scene starts on the Arena.
        var arena = GetOrCreate<MapTemplate>(ArenaMapPath, FillArena);
        GetOrCreate<MapTemplate>(OpenFieldMapPath, FillOpenField);
        var loader = controller.GetComponent<MapLoader>();
        if (loader == null) loader = controller.gameObject.AddComponent<MapLoader>();
        loader.Ground = ground != null ? ground.transform : null;
        loader.Surface = Object.FindFirstObjectByType<NavMeshSurface>();
        loader.CoverMaterial = coverMat;
        loader.ElevationMaterial = elevationMat;
        var map = loader.Current != null ? loader.Current : arena;
        loader.Build(map);
        EditorUtility.SetDirty(loader);

        var placements = new List<BattleContext.Placement>
        {
            // The squad: backliners behind, front line forward (each at the map's spawn for its place in the squad).
            Place(unitsRoot, unitPrefab, "Player_Sharpshooter", squad.Sharpshooter, Team.Player, Spawn(map, Team.Player, 0, new Vector3(-6f, 0f, -11f)), playerMat),
            Place(unitsRoot, unitPrefab, "Player_Paradigm", squad.Paradigm, Team.Player, Spawn(map, Team.Player, 1, new Vector3(-1.5f, 0f, -11.5f)), playerMat),
            Place(unitsRoot, unitPrefab, "Player_Tank", squad.Tank, Team.Player, Spawn(map, Team.Player, 2, new Vector3(2f, 0f, -8f)), playerMat),
            Place(unitsRoot, unitPrefab, "Player_Fighter", squad.Melee, Team.Player, Spawn(map, Team.Player, 3, new Vector3(6f, 0f, -8.5f)), playerMat),
            Place(unitsRoot, unitPrefab, "Enemy_Caster_A", enemies.Caster, Team.Enemy, Spawn(map, Team.Enemy, 0, new Vector3(-5f, 0f, 10f)), enemyMat),
            Place(unitsRoot, unitPrefab, "Enemy_Caster_B", enemies.Caster, Team.Enemy, Spawn(map, Team.Enemy, 1, new Vector3(5f, 0f, 10f)), enemyMat),
            Place(unitsRoot, unitPrefab, "Enemy_Brutebeast_A", enemies.Brutebeast, Team.Enemy, Spawn(map, Team.Enemy, 2, new Vector3(-2f, 0f, 8f)), enemyMat),
            Place(unitsRoot, unitPrefab, "Enemy_Brutebeast_B", enemies.Brutebeast, Team.Enemy, Spawn(map, Team.Enemy, 3, new Vector3(2f, 0f, 8f)), enemyMat),
        };

        context.Placements = placements;
        EditorUtility.SetDirty(context);

        controller.Context = context;
        controller.Director = director;
        controller.EnvironmentMask = (1 << 0) | (1 << environmentLayer);   // static geometry only, never Units
        EditorUtility.SetDirty(controller);

        TacticsCamera rig = null;
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 17f, -19f);
            cam.transform.LookAt(Vector3.zero);
            EditorUtility.SetDirty(cam.transform);
            rig = SetUpCameras(cam, root.transform, director, controller.EnvironmentMask);
            rig.BoundsMax = map.GroundSize / 2f;   // MapLoader.Build ran before the rig existed
            rig.BoundsMin = -rig.BoundsMax;
        }

        // Battle plays at normal speed and only freezes while the player is giving orders.
        director.playbackSpeed = 1f;
        director.holdTimescale = 0f;
        EditorUtility.SetDirty(director);

        // Player orders come from the order panel. AutoPlayerInput stays in the scene as a fallback you can drag back in.
        var orders = Object.FindFirstObjectByType<PlayerOrdersUI>();
        if (orders == null)
        {
            var host = controller.Input != null ? controller.Input.gameObject : controller.gameObject;
            orders = host.AddComponent<PlayerOrdersUI>();
        }
        orders.Context = context;
        orders.Camera = cam;
        orders.CameraRig = rig;
        orders.Director = director;
        EditorUtility.SetDirty(orders);
        controller.Input = orders;
        EditorUtility.SetDirty(controller);

        var fieldMarkers = controller.GetComponent<BattlefieldMarkers>();
        if (fieldMarkers == null) fieldMarkers = controller.gameObject.AddComponent<BattlefieldMarkers>();
        fieldMarkers.Director = director;
        EditorUtility.SetDirty(fieldMarkers);

        var hud = controller.GetComponent<BattleHUD>();
        if (hud == null) hud = controller.gameObject.AddComponent<BattleHUD>();
        hud.Controller = controller;
        hud.Director = director;
        hud.Camera = cam;
        EditorUtility.SetDirty(hud);

        if (controller.GetComponent<ControlsLegend>() == null) controller.gameObject.AddComponent<ControlsLegend>();

        var vfx = controller.GetComponent<CombatVfx>();
        if (vfx == null) vfx = controller.gameObject.AddComponent<CombatVfx>();
        vfx.Director = director;
        vfx.Camera = cam;
        EditorUtility.SetDirty(vfx);

        var timeline = controller.GetComponent<TimelineBar>();
        if (timeline == null) timeline = controller.gameObject.AddComponent<TimelineBar>();
        timeline.Director = director;
        timeline.Vfx = vfx;
        timeline.Orders = orders;
        timeline.CameraRig = rig;
        EditorUtility.SetDirty(timeline);

        // Pre-battle screen: it starts the battle once the player has picked enemies and loadouts.
        var setup = controller.GetComponent<PreBattleSetup>();
        if (setup == null) setup = controller.gameObject.AddComponent<PreBattleSetup>();
        setup.Controller = controller;
        setup.Context = context;
        setup.EnemyClasses = new List<UnitDefinition> { enemies.Caster, enemies.Brutebeast, enemies.Sniper, enemies.Assassin, enemies.Skirmisher, enemies.Healer };
        setup.FallbackUnitPrefab = unitPrefab;
        setup.EnemyMaterial = enemyMat;
        setup.Maps = loader;
        // Every map template in the project is on offer (ones already listed keep their order).
        setup.MapChoices.RemoveAll(m => m == null);
        foreach (string guid in AssetDatabase.FindAssets("t:MapTemplate"))
        {
            var m = AssetDatabase.LoadAssetAtPath<MapTemplate>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null && !setup.MapChoices.Contains(m)) setup.MapChoices.Add(m);
        }
        EditorUtility.SetDirty(setup);
        controller.StartOnPlay = false;
        EditorUtility.SetDirty(controller);

        BakeNavMesh();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuildSettings(ScenePath);   // New Battle reloads the scene by name
        AssetDatabase.SaveAssets();
        Debug.Log("CombatTestSceneBuilder: CombatTest built (squad vs 4 on the scene map, navmesh). Tools > Combat Test > Map Builder edits maps. Press Play; R replays after the battle ends.");
    }

    /// <summary>
    /// Cinemachine rig: a brain on the main camera blending between TacticalCam (player-driven TacticsCamera) and
    /// CinematicCam (posed by InteractionCamera for unit-vs-unit shots), with an impulse source for hit shake.
    /// The virtual cameras live under the generated root, so a rebuild replaces them.
    /// </summary>
    private static TacticsCamera SetUpCameras(Camera cam, Transform root, PlaybackDirector director, LayerMask environmentMask)
    {
        var oldTactics = cam.GetComponent<TacticsCamera>();   // older builds drove the main camera directly
        if (oldTactics != null) Object.DestroyImmediate(oldTactics);

        var brain = cam.GetComponent<CinemachineBrain>();
        if (brain == null) brain = cam.gameObject.AddComponent<CinemachineBrain>();
        brain.IgnoreTimeScale = true;   // blends keep going while the world is frozen for orders
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.45f);
        EditorUtility.SetDirty(brain);

        var rig = new GameObject("Cameras").transform;
        rig.SetParent(root, false);

        var tactical = new GameObject("TacticalCam").AddComponent<CinemachineCamera>();
        tactical.transform.SetParent(rig, false);
        tactical.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
        tactical.Lens.FieldOfView = cam.fieldOfView;
        tactical.Priority = 10;
        tactical.gameObject.AddComponent<CinemachineImpulseListener>();
        var tactics = tactical.gameObject.AddComponent<TacticsCamera>();

        var cinematic = new GameObject("CinematicCam").AddComponent<CinemachineCamera>();
        cinematic.transform.SetParent(rig, false);
        cinematic.Lens.FieldOfView = 42f;
        cinematic.Priority = 0;
        cinematic.gameObject.AddComponent<CinemachineImpulseListener>();

        var shots = new GameObject("InteractionCamera");
        shots.transform.SetParent(rig, false);
        var impulse = shots.AddComponent<CinemachineImpulseSource>();
        impulse.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
        impulse.ImpulseDefinition.ImpulseDuration = 0.2f;
        var interaction = shots.AddComponent<InteractionCamera>();
        interaction.Director = director;
        interaction.TacticalCam = tactical;
        interaction.CinematicCam = cinematic;
        interaction.Tactics = tactics;
        interaction.Impulse = impulse;
        interaction.EnvironmentMask = environmentMask;
        return tactics;
    }

    private static void AddToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static BattleContext.Placement Place(Transform parent, GameObject prefab, string name, UnitDefinition def,
                                                 Team team, Vector3 position, Material mat)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.position = position;
        go.transform.rotation = Quaternion.LookRotation(team == Team.Player ? Vector3.forward : Vector3.back);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
        return new BattleContext.Placement { View = go.GetComponent<UnitView>(), Definition = def, Team = team };
    }

    // ------------------------------------------------------------------ starter maps

    internal const string ArenaMapPath = "Assets/Maps/Arena.asset";
    internal const string OpenFieldMapPath = "Assets/Maps/OpenField.asset";

    private static Vector3 Spawn(MapTemplate map, Team team, int index, Vector3 fallback)
    {
        var spawns = map != null ? map.Spawns(team) : null;
        return spawns != null && index < spawns.Count ? spawns[index] : fallback;
    }

    /// <summary>Cover standing on the ground at (x, z).</summary>
    private static MapPiece Cover(string name, float x, float z, Vector3 size, float yaw = 0f)
    {
        return new MapPiece { Name = name, Kind = MapPieceKind.Cover, Position = new Vector3(x, 0f, z), Size = size, Yaw = yaw };
    }

    /// <summary>Raised ground from (minX, minZ) to (maxX, maxZ), 'height' tall, sitting on 'baseHeight' (stack a second
    /// tier on a first rather than overlapping them, so the side faces don't z-fight).</summary>
    private static MapPiece Raised(string name, float minX, float minZ, float maxX, float maxZ, float baseHeight, float height)
    {
        return new MapPiece
        {
            Name = name, Kind = MapPieceKind.Elevation,
            Position = new Vector3((minX + maxX) / 2f, baseHeight, (minZ + maxZ) / 2f), Size = new Vector3(maxX - minX, height, maxZ - minZ),
        };
    }

    /// <summary>The original CombatTest layout. Low walls give partial cover (eye height clears them), pillars block sight
    /// entirely. Raised ground is point-symmetric so neither side starts ahead: a two-tier ridge down each flank, a sniper
    /// perch behind each team (the Sharpshooter starts on the south one) and crate stacks on the corners.</summary>
    private static void FillArena(MapTemplate m)
    {
        m.DisplayName = "Arena";
        m.Description = "Low walls and pillars in the middle, ridges down both flanks, a sniper perch behind each side.";
        m.GroundSize = new Vector2(40f, 50f);
        var lowWall = new Vector3(4f, 1f, 0.5f);
        var pillar = new Vector3(1.2f, 3f, 1.2f);
        const float tier = 1.2f, crates = 0.9f;
        m.Pieces = new List<MapPiece>
        {
            Cover("LowWall_SW", -4f, -4f, lowWall), Cover("LowWall_SE", 4f, -4f, lowWall),
            Cover("LowWall_NW", -4f, 4f, lowWall), Cover("LowWall_NE", 4f, 4f, lowWall),
            Cover("Pillar_W", -8f, 0f, pillar), Cover("Pillar_C", 0f, 0f, pillar), Cover("Pillar_E", 8f, 0f, pillar),
            Raised("Ridge_W", -16f, -5f, -10f, 5f, 0f, tier), Raised("Ridge_W_Top", -16f, -2f, -13f, 2f, tier, tier),
            Raised("Ridge_E", 10f, -5f, 16f, 5f, 0f, tier), Raised("Ridge_E_Top", 13f, -2f, 16f, 2f, tier, tier),
            Raised("Perch_S", -8.5f, -13.5f, -3.5f, -10f, 0f, tier), Raised("Perch_N", 3.5f, 10f, 8.5f, 13.5f, 0f, tier),
            Raised("Crates_SE", 9f, -12f, 11f, -10f, 0f, crates), Raised("Crates_NW", -11f, 10f, -9f, 12f, 0f, crates),
        };
        m.PlayerSpawns = new List<Vector3> { new Vector3(-6f, tier, -11f), new Vector3(-1.5f, 0f, -11.5f), new Vector3(2f, 0f, -8f), new Vector3(6f, 0f, -8.5f) };
        m.EnemySpawns = new List<Vector3>
        {
            new Vector3(-5f, 0f, 10f), new Vector3(5f, tier, 10.5f), new Vector3(-2f, 0f, 8f), new Vector3(2f, 0f, 8f),
            new Vector3(-8f, 0f, 8.5f), new Vector3(9f, 0f, 7f), new Vector3(-3f, 0f, 12f), new Vector3(1f, 0f, 12f),
        };
    }

    /// <summary>A small, mostly open field: a few pillars, two low walls and two crate stacks.</summary>
    private static void FillOpenField(MapTemplate m)
    {
        m.DisplayName = "Open Field";
        m.Description = "Little cover and almost no high ground: a straight fight.";
        m.GroundSize = new Vector2(40f, 40f);
        var lowWall = new Vector3(4f, 1f, 0.5f);
        var pillar = new Vector3(1.2f, 3f, 1.2f);
        var crates = new Vector3(2f, 0.9f, 2f);
        m.Pieces = new List<MapPiece>
        {
            Cover("Pillar_W", -7f, 0f, pillar), Cover("Pillar_E", 7f, 0f, pillar),
            Cover("LowWall_S", 0f, -6f, lowWall), Cover("LowWall_N", 0f, 6f, lowWall),
            new MapPiece { Name = "Crates_W", Kind = MapPieceKind.Elevation, Position = new Vector3(-3f, 0f, 3f), Size = crates },
            new MapPiece { Name = "Crates_E", Kind = MapPieceKind.Elevation, Position = new Vector3(3f, 0f, -3f), Size = crates },
        };
        m.PlayerSpawns = new List<Vector3>();
        m.EnemySpawns = new List<Vector3>();
        for (int i = 0; i < 4; i++)
        {
            m.PlayerSpawns.Add(new Vector3(-4.5f + 3f * i, 0f, -12f));
            m.EnemySpawns.Add(new Vector3(-4.5f + 3f * i, 0f, 12f));
        }
        for (int i = 0; i < 4; i++) m.EnemySpawns.Add(new Vector3(-4.5f + 3f * i, 0f, 15f));
    }

    /// <summary>Puts the capsule on the unit's feet (the root is the sim position) and onto the Units layer,
    /// which is excluded from sight checks so units never block their own line of sight.</summary>
    private static void FixUnitPrefab(int unitLayer)
    {
        var root = PrefabUtility.LoadPrefabContents(UnitPrefabPath);
        try
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = unitLayer;
            var model = root.transform.Find("Model");
            if (model != null)
            {
                model.localPosition = new Vector3(0f, 1f, 0f);
                model.localRotation = Quaternion.identity;
            }
            var view = root.GetComponent<UnitView>();
            if (view != null && view.VfxAnchor == null)
            {
                var anchor = new GameObject("VfxAnchor");
                anchor.layer = unitLayer;
                anchor.transform.SetParent(root.transform, false);
                anchor.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                view.VfxAnchor = anchor.transform;
            }
            PrefabUtility.SaveAsPrefabAsset(root, UnitPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    internal static void BakeNavMesh()
    {
        var surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface == null) { Debug.LogWarning("CombatTestSceneBuilder: no NavMeshSurface in the scene; skipping bake."); return; }

        string path = surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : null;
        if (string.IsNullOrEmpty(path)) path = DefaultNavMeshPath;

        surface.BuildNavMesh();
        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateAsset(surface.navMeshData, path);
        EditorUtility.SetDirty(surface);
    }

    internal static void MarkDirty(Object asset) { EditorUtility.SetDirty(asset); }

    /// <summary>Puts every serialized field of an asset back to its script default, keeping the asset's name.</summary>
    internal static void ResetToDefaults(ScriptableObject asset)
    {
        var fresh = ScriptableObject.CreateInstance(asset.GetType());
        try
        {
            string name = asset.name;
            EditorUtility.CopySerialized(fresh, asset);
            asset.name = name;
        }
        finally { Object.DestroyImmediate(fresh); }
    }

    /// <summary>
    /// The asset at 'path', created with 'init' if there is none. An asset moved since it was made is still found (see
    /// FindMoved), and a new one goes into a moved folder of the same name if 'path''s own folder is gone, so reorganising
    /// the Assets folders never makes the builder write a second copy.
    /// </summary>
    internal static T GetOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path) ?? FindMoved<T>(path);
        if (existing != null) return existing;
        path = WhereToCreate(path);
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        var asset = ScriptableObject.CreateInstance<T>();
        init(asset);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    /// <summary>A T with the file name of 'path' in a folder named like 'path''s (Assets/Actions/Tank/Bash.asset moved to
    /// Assets/Actions/Player Units/Tank/Bash.asset), if there is exactly one; else null.</summary>
    private static T FindMoved<T>(string path) where T : Object
    {
        string file = System.IO.Path.GetFileNameWithoutExtension(path);
        string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
        var found = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets(file + " t:" + typeof(T).Name))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(p) == file && System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(p)) == folder) found.Add(p);
        }
        return found.Count == 1 ? AssetDatabase.LoadAssetAtPath<T>(found[0]) : null;
    }

    /// <summary>Where to create an asset meant for 'path': there, unless its folder doesn't exist but exactly one folder of that
    /// name does elsewhere under the same top folder (Assets/Actions): the folder was moved, so the asset goes in it.</summary>
    private static string WhereToCreate(string path)
    {
        string dir = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(dir)) return path;
        string[] parts = dir.Split('/');
        string top = parts.Length >= 3 ? parts[0] + "/" + parts[1] : null;
        if (top == null || !AssetDatabase.IsValidFolder(top)) return path;
        string name = System.IO.Path.GetFileName(dir);
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(top);
        while (pending.Count > 0)
            foreach (var sub in AssetDatabase.GetSubFolders(pending.Pop()))
            {
                if (System.IO.Path.GetFileName(sub) == name) found.Add(sub);
                pending.Push(sub);
            }
        return found.Count == 1 ? found[0] + "/" + System.IO.Path.GetFileName(path) : path;
    }

    private static Material GetOrCreateMaterial(string path, Color color)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
