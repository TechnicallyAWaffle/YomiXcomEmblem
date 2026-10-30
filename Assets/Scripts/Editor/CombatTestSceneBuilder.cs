using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using TacticsGame.Core;
using TacticsGame.Presentation;

/// <summary>
/// Tools > Combat Test > Build Scene: lays out the four-class squad vs four enemies in CombatTest (units, cover, navmesh bake,
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
        var strike = GetOrCreate<ActionDefinition>("Assets/Actions/HeavyStrike.asset", a =>
        {
            a.Id = "heavy_strike"; a.DisplayName = "Heavy Strike"; a.BehaviorId = "basic_attack";
            a.Targeting = TargetType.Enemy; a.Range = 1.8f; a.IsRanged = false;
            a.Windup = 0.9f; a.Recovery = 0.7f;
            a.Impact = ImpactType.Heavy; a.Damage = 28f; a.BaseAccuracy = 0.95f; a.StunDuration = 0.8f; a.StrainOnTarget = 25f;
        });

        var unitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitPrefabPath);
        var gunner = GetOrCreate<UnitDefinition>("Assets/Units/Gunner/Gunner.asset", u =>
        {
            u.DisplayName = "Gunner"; u.MaxHealth = 80f; u.BaseDodge = 0.15f; u.BaseBlock = 0f; u.MoveSpeed = 4f;
            u.Actions = new List<ActionDefinition> { rifle }; u.ViewPrefab = unitPrefab;
        });
        var brawler = GetOrCreate<UnitDefinition>("Assets/Units/Brawler/Brawler.asset", u =>
        {
            u.DisplayName = "Brawler"; u.MaxHealth = 130f; u.BaseDodge = 0.05f; u.BaseBlock = 0.2f; u.MoveSpeed = 5f;
            u.Actions = new List<ActionDefinition> { strike }; u.ViewPrefab = unitPrefab;
        });

        // The builder owns these two: always give them the default universal (gunners dash, brawlers block).
        gunner.Universal = SquadAssets.DefaultUniversal(gunner); MarkDirty(gunner);
        brawler.Universal = SquadAssets.DefaultUniversal(brawler); MarkDirty(brawler);

        var playerMat = GetOrCreateMaterial("Assets/Materials/Team_Player.mat", new Color(0.25f, 0.5f, 1f));
        var enemyMat = GetOrCreateMaterial("Assets/Materials/Team_Enemy.mat", new Color(1f, 0.3f, 0.25f));
        var coverMat = GetOrCreateMaterial("Assets/Materials/Cover.mat", new Color(0.55f, 0.55f, 0.5f));

        FixUnitPrefab(unitLayer);
        unitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitPrefabPath);
        SquadAssets.ApplyDescription(rifle, "RifleShot");
        SquadAssets.ApplyDescription(strike, "HeavyStrike");
        var squad = SquadAssets.Create(unitPrefab, rifle);

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

        var root = new GameObject(GeneratedRootName);
        var coverRoot = new GameObject("Cover").transform;
        coverRoot.SetParent(root.transform, false);
        var unitsRoot = new GameObject("Units").transform;
        unitsRoot.SetParent(root.transform, false);

        // Low walls give partial cover (eye height clears them), pillars block sight entirely.
        var lowWall = new Vector3(4f, 1f, 0.5f);
        var pillar = new Vector3(1.2f, 3f, 1.2f);
        AddCover(coverRoot, "LowWall_SW", new Vector3(-4f, 0f, -4f), lowWall, coverMat, environmentLayer);
        AddCover(coverRoot, "LowWall_SE", new Vector3(4f, 0f, -4f), lowWall, coverMat, environmentLayer);
        AddCover(coverRoot, "LowWall_NW", new Vector3(-4f, 0f, 4f), lowWall, coverMat, environmentLayer);
        AddCover(coverRoot, "LowWall_NE", new Vector3(4f, 0f, 4f), lowWall, coverMat, environmentLayer);
        AddCover(coverRoot, "Pillar_W", new Vector3(-8f, 0f, 0f), pillar, coverMat, environmentLayer);
        AddCover(coverRoot, "Pillar_C", new Vector3(0f, 0f, 0f), pillar, coverMat, environmentLayer);
        AddCover(coverRoot, "Pillar_E", new Vector3(8f, 0f, 0f), pillar, coverMat, environmentLayer);

        var placements = new List<BattleContext.Placement>
        {
            // The squad: backliners behind, front line forward.
            Place(unitsRoot, unitPrefab, "Player_Sharpshooter", squad.Sharpshooter, Team.Player, new Vector3(-6f, 0f, -11f), playerMat),
            Place(unitsRoot, unitPrefab, "Player_Paradigm", squad.Paradigm, Team.Player, new Vector3(-1.5f, 0f, -11.5f), playerMat),
            Place(unitsRoot, unitPrefab, "Player_Tank", squad.Tank, Team.Player, new Vector3(2f, 0f, -8f), playerMat),
            Place(unitsRoot, unitPrefab, "Player_Fighter", squad.Melee, Team.Player, new Vector3(6f, 0f, -8.5f), playerMat),
            Place(unitsRoot, unitPrefab, "Enemy_Gunner_A", gunner, Team.Enemy, new Vector3(-5f, 0f, 10f), enemyMat),
            Place(unitsRoot, unitPrefab, "Enemy_Gunner_B", gunner, Team.Enemy, new Vector3(5f, 0f, 10f), enemyMat),
            Place(unitsRoot, unitPrefab, "Enemy_Brawler_A", brawler, Team.Enemy, new Vector3(-2f, 0f, 8f), enemyMat),
            Place(unitsRoot, unitPrefab, "Enemy_Brawler_B", brawler, Team.Enemy, new Vector3(2f, 0f, 8f), enemyMat),
        };

        var context = Object.FindFirstObjectByType<BattleContext>();
        var controller = Object.FindFirstObjectByType<BattleController>();
        var director = Object.FindFirstObjectByType<PlaybackDirector>();
        if (context == null || controller == null || director == null)
        {
            Debug.LogError("CombatTestSceneBuilder: scene needs a BattleContext, BattleController and PlaybackDirector.");
            return;
        }

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
        setup.EnemyClasses = new List<UnitDefinition> { gunner, brawler, squad.Sharpshooter, squad.Melee, squad.Tank, squad.Paradigm };
        setup.BlockAction = SquadAssets.UniversalBlock();
        setup.DashAction = SquadAssets.UniversalDash();
        setup.FallbackUnitPrefab = unitPrefab;
        setup.EnemyMaterial = enemyMat;
        EditorUtility.SetDirty(setup);
        controller.StartOnPlay = false;
        EditorUtility.SetDirty(controller);

        BakeNavMesh();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuildSettings(ScenePath);   // New Battle reloads the scene by name
        AssetDatabase.SaveAssets();
        Debug.Log("CombatTestSceneBuilder: CombatTest built (squad vs 4, cover, navmesh). Press Play; R replays after the battle ends.");
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

    private static void AddCover(Transform parent, string name, Vector3 basePosition, Vector3 size, Material mat, int layer)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.position = basePosition + Vector3.up * (size.y / 2f);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        // The surface only collects objects marked with a modifier: mark cover so it cuts holes in the navmesh.
        var modifier = go.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = NavMesh.GetAreaFromName("Not Walkable");
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

    private static void BakeNavMesh()
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

    internal static T GetOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        var asset = ScriptableObject.CreateInstance<T>();
        init(asset);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
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
