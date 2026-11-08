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
        madeThisBuild.Clear();
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
            a.Impact = ImpactType.Light; a.Damage = 14f; a.BaseAccuracy = 1f; a.StunDuration = 0.15f; a.StrainOnTarget = 5f;
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
        var pass = GetOrCreate<MapTemplate>(PassMapPath, FillPass);
        var ruins = GetOrCreate<MapTemplate>(RuinsMapPath, FillRuins);
        var hillside = GetOrCreate<MapTemplate>(HillsideMapPath, FillHillside);
        var crossroads = GetOrCreate<MapTemplate>(CrossroadsMapPath, FillCrossroads);
        var threshold = GetOrCreate<MapTemplate>(ThresholdMapPath, FillThreshold);
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

        // The launcher spawns the squad and the enemies and starts the battle; the pre-battle screen is a UI over it and the roster.
        var launcher = controller.GetComponent<BattleLauncher>();
        if (launcher == null) launcher = controller.gameObject.AddComponent<BattleLauncher>();
        launcher.Controller = controller;
        launcher.Context = context;
        launcher.Maps = loader;
        launcher.FallbackUnitPrefab = unitPrefab;
        launcher.PlayerMaterial = playerMat;
        launcher.EnemyMaterial = enemyMat;
        EditorUtility.SetDirty(launcher);

        var catalog = WriteCatalog(new[] { squad.Sharpshooter, squad.Melee, squad.Tank, squad.Paradigm,
                                           enemies.Sniper, enemies.Assassin, enemies.Skirmisher, enemies.Healer, enemies.Caster, enemies.Brutebeast,
                                           enemies.Wraith, enemies.Monarch, enemies.Zola, enemies.Primavaeran, enemies.Exemplar,
                                           enemies.Deliverance, enemies.Syne, enemies.Alio, enemies.Revl, enemies.Wex });
        var firstContact = GetOrCreate<EncounterDefinition>(EncountersFolder + "/FirstContact.asset", e =>
        {
            e.DisplayName = "First Contact";
            e.Description = "Two Casters and two Brutebeasts hold the arena.";
            e.Map = arena;
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Caster, Count = 2 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Brutebeast, Count = 2 });
            e.Spec.Rewards.Currency = 100;
        });
        var openField = AssetDatabase.LoadAssetAtPath<MapTemplate>(OpenFieldMapPath) ?? arena;
        var ambush = GetOrCreate<EncounterDefinition>(EncountersFolder + "/Ambush.asset", e =>
        {
            e.DisplayName = "Ambush";
            e.Description = "A Sniper covers two Assassins and a Skirmisher in the open. Close the distance, or hide from the scope.";
            e.Map = openField;
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Sniper, Count = 1 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Assassin, Count = 2 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Skirmisher, Count = 1 });
            e.Spec.Rewards.Experience = 35; e.Spec.Rewards.AbilityPoints = 1;
        });
        var lastStand = GetOrCreate<EncounterDefinition>(EncountersFolder + "/LastStand.asset", e =>
        {
            e.DisplayName = "Last Stand";
            e.Description = "Their Healer keeps two Brutebeasts on their feet while a Caster and a Sniper hang back. Bring the Healer down first.";
            e.Map = arena;
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Healer, Count = 1 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Brutebeast, Count = 2 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Caster, Count = 1 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Sniper, Count = 1 });
            e.Spec.Rewards.Experience = 40; e.Spec.Rewards.AbilityPoints = 2;
        });
        var thePass = GetOrCreate<EncounterDefinition>(EncountersFolder + "/ThePass.asset", e =>
        {
            e.DisplayName = "The Pass";
            e.Description = "Skirmishers and Assassins come down a narrow pass behind a Caster. Hold the lane, or take the ledges to flank.";
            e.Map = pass;
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Skirmisher, Count = 2 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Assassin, Count = 2 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Caster, Count = 1 });
            e.Spec.Rewards.Experience = 40; e.Spec.Rewards.AbilityPoints = 1;
        });
        var ruinedCourt = GetOrCreate<EncounterDefinition>(EncountersFolder + "/RuinedCourt.asset", e =>
        {
            e.DisplayName = "Ruined Court";
            e.Description = "Two Brutebeasts, a Healer and two Casters fight over the dais. Whoever holds the middle sees the whole court.";
            e.Map = ruins;
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Brutebeast, Count = 2 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Healer, Count = 1 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Caster, Count = 2 });
            e.Spec.Rewards.Experience = 45; e.Spec.Rewards.AbilityPoints = 2;
        });
        var siege = GetOrCreate<EncounterDefinition>(EncountersFolder + "/Siege.asset", e =>
        {
            e.DisplayName = "The Siege";
            e.Description = "They hold the terraces: two Snipers up top, Brutebeasts on the steps, a Healer and a Caster between. Climb under fire.";
            e.Map = hillside;
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Sniper, Count = 2 });   // first in the list: the top-tier spawns
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Brutebeast, Count = 2 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Healer, Count = 1 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Caster, Count = 1 });
            e.Spec.Rewards.Experience = 50; e.Spec.Rewards.AbilityPoints = 2;
        });
        var monarchsCourt = GetOrCreate<EncounterDefinition>(EncountersFolder + "/MonarchsCourt.asset", e =>
        {
            e.DisplayName = "The Monarch's Court";
            e.Description = "A Monarch holds the ruins with two Wraiths and a Healer, and calls more Wraiths as the fight goes on. Wounded badly, it takes to the air for good.";
            e.Map = ruins;
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Monarch, Count = 1 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Wraith, Count = 2 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Healer, Count = 1 });
            e.Spec.Rewards.Experience = 55; e.Spec.Rewards.AbilityPoints = 2;
        });
        var stormOfThought = GetOrCreate<EncounterDefinition>(EncountersFolder + "/StormOfThought.asset", e =>
        {
            e.DisplayName = "Storm of Thought";
            e.Description = "A Zola walks at you, straining everyone near it, while a Primavaeran tears the field apart from afar. Get to the Primavaeran before its reality tears open.";
            e.Map = hillside;
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Primavaeran, Count = 1 });   // first: the top-terrace spawn
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Zola, Count = 1 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Wraith, Count = 3 });
            e.Spec.Rewards.Experience = 60; e.Spec.Rewards.AbilityPoints = 3;
        });
        var finalTrial = GetOrCreate<EncounterDefinition>(EncountersFolder + "/Crossroads.asset", e =>
        {
            e.DisplayName = "Crossroads";
            e.Description = "The last trial: seven of them across four lanes, a Sniper on a rooftop. Pick a lane and break through.";
            e.Map = crossroads;
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Brutebeast, Count = 2 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Assassin, Count = 2 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Healer, Count = 1 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Caster, Count = 1 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Sniper, Count = 1 });   // last: the seventh spawn, on a rooftop
            e.Spec.Rewards.Experience = 60; e.Spec.Rewards.AbilityPoints = 3;
        });
        var gildedVanguard = GetOrCreate<EncounterDefinition>(EncountersFolder + "/GildedVanguard.asset", e =>
        {
            e.DisplayName = "The Gilded Vanguard";
            e.Description = "Two Exemplars hold the field, and more of their order drop in from the sky. Knock them out of the air on the way down.";
            e.Map = openField;
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Exemplar, Count = 2 });
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Exemplar, Count = 1, ArriveAfter = 15f });   // reinforcements
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Sniper, Count = 1, ArriveAfter = 15f });
            e.Spec.Rewards.Experience = 55; e.Spec.Rewards.AbilityPoints = 2;
        });
        var deliverance = GetOrCreate<EncounterDefinition>(EncountersFolder + "/Deliverance.asset", e =>
        {
            e.DisplayName = "Deliverance";
            e.Description = "The end of the road. Deliverance waits at the Threshold, and the enemy keeps pouring in from everywhere, at war with it as much as with you. Bring it down, and then bring down what it becomes.";
            e.Map = threshold;
            e.Spec.Enemies.Add(new EnemyEntry { Unit = enemies.Deliverance, Count = 1, Side = Team.Rogue });   // its own side: against everyone
            e.Spec.Waves.Pool = new List<UnitDefinition> { enemies.Brutebeast, enemies.Caster, enemies.Assassin, enemies.Skirmisher, enemies.Sniper, enemies.Wraith };
            e.Spec.Waves.FirstAfter = 6f; e.Spec.Waves.Every = 12f; e.Spec.Waves.PerWave = 2; e.Spec.Waves.MaxUp = 6;
            e.Spec.Rewards.Experience = 100; e.Spec.Rewards.AbilityPoints = 3;
        });
        var squadClasses = new List<UnitDefinition> { squad.Sharpshooter, squad.Melee, squad.Tank, squad.Paradigm };
        var progression = GetOrCreate<ProgressionConfig>(CampaignFolder + "/Progression.asset", p => FillProgression(p, squadClasses));
        if (progression.Etchings == null || progression.Etchings.Count == 0)   // made before Etchings existed
        {
            progression.FillDefaultEtchings();
            EditorUtility.SetDirty(progression);
        }
        AddNewEtchings(progression);
        AddAbilityCosts(progression, squadClasses);
        var campaign = GetOrCreate<CampaignDefinition>(CampaignFolder + "/Campaign.asset", c =>
        {
            c.DisplayName = "The Proving";
            c.Missions = new List<EncounterDefinition> { firstContact, ambush, lastStand };
            c.Squad = new List<UnitDefinition>(squadClasses);
            c.Progression = progression;
        });
        // Later missions go on the end, of a new campaign asset or one made before them (its own edits stay). Only missions
        // made by this build are added: one already in the project that isn't in the list was taken out of it, and stays out.
        AppendMissions(campaign, thePass, ruinedCourt, siege, finalTrial);
        InsertMissionsBefore(campaign, finalTrial, monarchsCourt, stormOfThought);   // the new enemies come before the finale
        InsertMissionsBefore(campaign, finalTrial, gildedVanguard);   // the Exemplars and the first reinforcements
        AppendMissions(campaign, deliverance);   // the final encounter, last of all

        // Pre-battle screen: it starts the battle once the player has picked an encounter (or enemies) and loadouts.
        var setup = controller.GetComponent<PreBattleSetup>();
        if (setup == null) setup = controller.gameObject.AddComponent<PreBattleSetup>();
        setup.Controller = controller;
        setup.Context = context;
        setup.Launcher = launcher;
        setup.Catalog = catalog;
        setup.EnemyClasses = new List<UnitDefinition> { enemies.Caster, enemies.Brutebeast, enemies.Sniper, enemies.Assassin, enemies.Skirmisher, enemies.Healer,
                                                        enemies.Wraith, enemies.Monarch, enemies.Zola, enemies.Primavaeran, enemies.Exemplar,
                                                        enemies.Deliverance, enemies.Syne };
        setup.Maps = loader;
        // Every encounter in the project is on offer (ones already listed keep their order).
        setup.Encounters.RemoveAll(e => e == null);
        if (!setup.Encounters.Contains(firstContact)) setup.Encounters.Add(firstContact);
        foreach (string guid in AssetDatabase.FindAssets("t:EncounterDefinition"))
        {
            var e = AssetDatabase.LoadAssetAtPath<EncounterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (e != null && !setup.Encounters.Contains(e)) setup.Encounters.Add(e);
        }
        // Every map template in the project is on offer (ones already listed keep their order).
        setup.MapChoices.RemoveAll(m => m == null);
        foreach (string guid in AssetDatabase.FindAssets("t:MapTemplate"))
        {
            var m = AssetDatabase.LoadAssetAtPath<MapTemplate>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null && !setup.MapChoices.Contains(m)) setup.MapChoices.Add(m);
        }
        EditorUtility.SetDirty(setup);

        // The campaign loop (preparation, battle, upgrades): while it has a campaign, it runs the battles instead of the setup screen.
        var screens = controller.GetComponent<CampaignScreens>();
        if (screens == null) screens = controller.gameObject.AddComponent<CampaignScreens>();
        screens.Controller = controller;
        screens.Context = context;
        screens.Launcher = launcher;
        screens.Catalog = catalog;
        if (screens.Campaign == null) screens.Campaign = campaign;
        // The tutorial (Assets/Tutorial/Tutorial.asset: its steps and text are edited there), offered from the campaign's menu.
        var tutorial = TutorialAssets.Create(unitPrefab, squad.Melee, squad.Sharpshooter, arena);
        if (screens.Tutorial == null) screens.Tutorial = tutorial;
        var tutorialRunner = controller.GetComponent<TutorialRunner>();
        if (tutorialRunner == null) tutorialRunner = controller.gameObject.AddComponent<TutorialRunner>();
        tutorialRunner.Controller = controller;
        tutorialRunner.Director = director;
        tutorialRunner.Orders = orders;
        tutorialRunner.CameraRig = rig;
        screens.TutorialRunner = tutorialRunner;
        EditorUtility.SetDirty(tutorialRunner);
        EditorUtility.SetDirty(screens);
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
    internal const string EncountersFolder = "Assets/Encounters";
    internal const string CampaignFolder = "Assets/Campaign";

    /// <summary>Default Etchings the config doesn't have yet (by Id) go on the end; ones it has keep their tuning.</summary>
    private static void AddNewEtchings(ProgressionConfig p)
    {
        bool changed = false;
        foreach (var e in ProgressionConfig.DefaultEtchings())
            if (p.Etching(e.Id) == null) { p.Etchings.Add(e); changed = true; }
        if (changed) EditorUtility.SetDirty(p);
    }

    /// <summary>Unlock costs asked for with newer abilities; any other class ability without a cost gets the ballpark one.</summary>
    private static readonly Dictionary<string, int> AbilityCostsById = new Dictionary<string, int>
    {
        { "cognitive_burden", 2 }, { "banishment", 3 }, { "death_sentence", 3 },
        { "a_million_cuts", 3 }, { "fighter_polystep", 1 }, { "the_endless_hunt", 2 },
        { "regeneration_seal", 2 }, { "true_inversion", 3 }, { "trammels", 2 }, { "minutes_turn_to_centuries", 2 },
        { "flurry", 2 }, { "pierce_the_wound", 2 },
        { "recollections_of_self", 2 }, { "unmake_the_harm", 1 }, { "soulseeker", 2 }, { "refraction_cloak", 2 }, { "destiny_closes_in", 3 },
        { "you", 2 }, { "savior", 2 },
        { "withering_end", 1 }, { "madness", 3 }, { "splinters", 1 }, { "baneful_domain", 2 }, { "mindshatter", 3 },
        { "dreadful_fixation", 1 }, { "crippling_pursuit", 2 }, { "mental_weapon", 2 },
    };

    /// <summary>Every class ability gets an unlock cost on the config (abilities added since it was made had none).</summary>
    private static void AddAbilityCosts(ProgressionConfig p, List<UnitDefinition> classes)
    {
        bool changed = false;
        foreach (var c in classes)
        {
            if (c == null) continue;
            foreach (var a in c.Actions)
            {
                if (a == null || a == c.BasicAttack || p.AbilityCosts.Exists(x => x != null && x.Ability == a)) continue;
                int cost;
                p.AbilityCosts.Add(new AbilityCost { Ability = a, Cost = AbilityCostsById.TryGetValue(a.Id, out cost) ? cost : ProgressionConfig.BallparkCost(a) });
                changed = true;
            }
        }
        if (changed) EditorUtility.SetDirty(p);
    }

    /// <summary>
    /// A new progression config's ballpark numbers: three starting abilities per class (its Default loadout), unlock costs
    /// from ProgressionConfig.BallparkCost, and the starting Etchings. Tune them on the asset.
    /// </summary>
    private static void FillProgression(ProgressionConfig p, List<UnitDefinition> classes)
    {
        var starts = new Dictionary<string, string[]>
        {
            { "Sharpshooter", new[] { "snipe", "snapshot", "take_aim" } },
            { "Fighter", new[] { "rapid_slices", "evasive_strike", "battle_trance" } },
            { "Tank", new[] { "shield_slam", "chain_hook", "brace" } },
            { "Paradigm User", new[] { "rift_line", "reconstruct", "stutter" } },
        };
        p.DefaultStart = LoadoutStart.Default;
        foreach (var c in classes)
        {
            if (c == null) continue;
            string[] ids;
            var cs = new ClassStart { Class = c, RandomCount = 3 };
            if (starts.TryGetValue(c.DisplayName, out ids)) foreach (var id in ids) { var a = c.Actions.Find(x => x != null && x.Id == id); if (a != null) cs.DefaultAbilities.Add(a); }
            p.Classes.Add(cs);
            foreach (var a in c.Actions)
                if (a != null && a != c.BasicAttack) p.AbilityCosts.Add(new AbilityCost { Ability = a, Cost = AbilityCostsById.TryGetValue(a.Id, out var asked) ? asked : ProgressionConfig.BallparkCost(a) });
        }
        p.FillDefaultEtchings();
    }
    internal const string CatalogPath = "Assets/Resources/" + ContentCatalog.ResourceName + ".asset";

    /// <summary>
    /// The content catalog saves resolve ids with (Resources/ContentCatalog): these classes, plus every other unit and every
    /// upgrade in the project. Ones already listed stay (and keep their order).
    /// </summary>
    private static ContentCatalog WriteCatalog(UnitDefinition[] classes)
    {
        var catalog = GetOrCreate<ContentCatalog>(CatalogPath, c => { });
        catalog.Units.RemoveAll(u => u == null);
        catalog.Upgrades.RemoveAll(u => u == null);
        foreach (var u in classes) if (u != null && !catalog.Units.Contains(u)) catalog.Units.Add(u);
        foreach (string guid in AssetDatabase.FindAssets("t:UnitDefinition"))
        {
            var u = AssetDatabase.LoadAssetAtPath<UnitDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (u != null && !catalog.Units.Contains(u)) catalog.Units.Add(u);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:UpgradeDefinition"))
        {
            var u = AssetDatabase.LoadAssetAtPath<UpgradeDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (u != null && !catalog.Upgrades.Contains(u)) catalog.Upgrades.Add(u);
        }
        EditorUtility.SetDirty(catalog);
        return catalog;
    }
    internal const string OpenFieldMapPath = "Assets/Maps/OpenField.asset";
    internal const string PassMapPath = "Assets/Maps/NarrowPass.asset";
    internal const string RuinsMapPath = "Assets/Maps/Ruins.asset";
    internal const string HillsideMapPath = "Assets/Maps/Hillside.asset";
    internal const string CrossroadsMapPath = "Assets/Maps/Crossroads.asset";
    internal const string ThresholdMapPath = "Assets/Maps/Threshold.asset";

    /// <summary>Adds these missions just before 'before' (or at the end without it): only ones this build made (madeThisBuild),
    /// skipping any the campaign already has.</summary>
    private static void InsertMissionsBefore(CampaignDefinition campaign, EncounterDefinition before, params EncounterDefinition[] missions)
    {
        bool changed = false;
        foreach (var e in missions)
        {
            if (e == null || campaign.Missions.Contains(e) || !madeThisBuild.Contains(e)) continue;
            int at = before != null ? campaign.Missions.IndexOf(before) : -1;
            if (at >= 0) campaign.Missions.Insert(at, e); else campaign.Missions.Add(e);
            changed = true;
        }
        if (changed) EditorUtility.SetDirty(campaign);
    }

    /// <summary>Adds these missions to the end of the campaign, in order: only ones this build made, skipping any it has.</summary>
    private static void AppendMissions(CampaignDefinition campaign, params EncounterDefinition[] missions)
    {
        bool changed = false;
        foreach (var e in missions)
            if (e != null && !campaign.Missions.Contains(e) && madeThisBuild.Contains(e)) { campaign.Missions.Add(e); changed = true; }
        if (changed) EditorUtility.SetDirty(campaign);
    }

    /// <summary>A long, narrow pass. Ledges run down both sides (a second tier in the middle of each) for flanking above the
    /// lane; the lane has staggered low walls and pillars. Point-symmetric.</summary>
    private static void FillPass(MapTemplate m)
    {
        m.DisplayName = "Narrow Pass";
        m.Description = "A long lane between two ledges: staggered walls below, high ground on the flanks.";
        m.GroundSize = new Vector2(32f, 56f);
        var lowWall = new Vector3(4f, 1f, 0.5f);
        var pillar = new Vector3(1.2f, 3f, 1.2f);
        const float tier = 1.2f, crates = 0.9f;
        m.Pieces = new List<MapPiece>
        {
            Raised("Ledge_W", -16f, -20f, -10f, 20f, 0f, tier), Raised("Ledge_W_Top", -16f, -8f, -13f, 8f, tier, tier),
            Raised("Ledge_E", 10f, -20f, 16f, 20f, 0f, tier), Raised("Ledge_E_Top", 13f, -8f, 16f, 8f, tier, tier),
            Raised("Steps_SW", -9.5f, -17f, -7.5f, -15f, 0f, crates), Raised("Steps_NE", 7.5f, 15f, 9.5f, 17f, 0f, crates),
            Cover("LowWall_S", -4f, -12f, lowWall), Cover("LowWall_N", 4f, 12f, lowWall),
            Cover("LowWall_SE", 5f, -5f, lowWall), Cover("LowWall_NW", -5f, 5f, lowWall),
            Cover("Pillar_S", 0f, -8f, pillar), Cover("Pillar_N", 0f, 8f, pillar),
            Cover("Pillar_W", -2.5f, 0f, pillar), Cover("Pillar_E", 2.5f, 0f, pillar),
        };
        m.PlayerSpawns = new List<Vector3>();
        m.EnemySpawns = new List<Vector3>();
        for (int i = 0; i < 4; i++)
        {
            m.PlayerSpawns.Add(new Vector3(-4.5f + 3f * i, 0f, -22f));
            m.EnemySpawns.Add(new Vector3(-4.5f + 3f * i, 0f, 22f));
        }
        for (int i = 0; i < 4; i++) m.EnemySpawns.Add(new Vector3(-4.5f + 3f * i, 0f, 25f));
    }

    /// <summary>A ruined courtyard around a two-tier dais. Broken walls at angles, four pillars ringing the dais, a tall ruined
    /// arch in front of each side's start (safe to form up behind) and rubble mounds in two corners. Point-symmetric.</summary>
    private static void FillRuins(MapTemplate m)
    {
        m.DisplayName = "Ruins";
        m.Description = "A courtyard with a raised dais in the middle: whoever holds it sees everything. Broken walls and pillars around it.";
        m.GroundSize = new Vector2(44f, 44f);
        var pillar = new Vector3(1.2f, 3f, 1.2f);
        const float tier = 1.2f, rubble = 0.9f;
        m.Pieces = new List<MapPiece>
        {
            Raised("Dais", -4f, -4f, 4f, 4f, 0f, tier), Raised("Dais_Top", -1.5f, -1.5f, 1.5f, 1.5f, tier, tier),
            Cover("Pillar_W", -6.5f, 0f, pillar), Cover("Pillar_E", 6.5f, 0f, pillar),
            Cover("Pillar_S", 0f, -7f, pillar), Cover("Pillar_N", 0f, 7f, pillar),
            Cover("Wall_SW", -9f, -7f, new Vector3(5f, 1f, 0.5f), 35f), Cover("Wall_NE", 9f, 7f, new Vector3(5f, 1f, 0.5f), 35f),
            Cover("Wall_SE", 10f, -8f, new Vector3(3f, 1f, 0.5f), -20f), Cover("Wall_NW", -10f, 8f, new Vector3(3f, 1f, 0.5f), -20f),
            Cover("Arch_S", 0f, -14f, new Vector3(6f, 3f, 0.6f)), Cover("Arch_N", 0f, 14f, new Vector3(6f, 3f, 0.6f)),
            Raised("Rubble_SE", 13f, -15f, 17f, -11f, 0f, rubble), Raised("Rubble_NW", -17f, 11f, -13f, 15f, 0f, rubble),
        };
        m.PlayerSpawns = new List<Vector3> { new Vector3(-6f, 0f, -17f), new Vector3(-2f, 0f, -18f), new Vector3(2f, 0f, -18f), new Vector3(6f, 0f, -17f) };
        m.EnemySpawns = new List<Vector3>
        {
            new Vector3(6f, 0f, 17f), new Vector3(2f, 0f, 18f), new Vector3(-2f, 0f, 18f), new Vector3(-6f, 0f, 17f),
            new Vector3(-10f, 0f, 16f), new Vector3(10f, 0f, 15f), new Vector3(-4f, 0f, 20f), new Vector3(4f, 0f, 20f),
        };
    }

    /// <summary>A hillside the enemy holds: two terraces rise toward their side (parapets along the lower one's edge), the
    /// player starts low with walls, pillars and crates to work up behind. Not symmetric: an attack uphill. The first two
    /// enemy spawns are on the top terrace.</summary>
    private static void FillHillside(MapTemplate m)
    {
        m.DisplayName = "Hillside";
        m.Description = "The enemy holds two terraces above you. Work up the slope from cover to cover.";
        m.GroundSize = new Vector2(44f, 48f);
        var lowWall = new Vector3(4f, 1f, 0.5f);
        var pillar = new Vector3(1.2f, 3f, 1.2f);
        const float tier = 1.2f, crates = 0.9f;
        m.Pieces = new List<MapPiece>
        {
            Raised("Terrace", -14f, 8f, 14f, 20f, 0f, tier), Raised("Terrace_Top", -9f, 13f, 9f, 20f, tier, tier),
            new MapPiece { Name = "Parapet_W", Kind = MapPieceKind.Cover, Position = new Vector3(-7f, tier, 8.6f), Size = lowWall },
            new MapPiece { Name = "Parapet_E", Kind = MapPieceKind.Cover, Position = new Vector3(7f, tier, 8.6f), Size = lowWall },
            Cover("LowWall_W", -8f, -4f, lowWall), Cover("LowWall_C", 0f, -6f, lowWall), Cover("LowWall_E", 8f, -4f, lowWall),
            Cover("LowWall_FarW", -11f, 4f, lowWall, 20f), Cover("LowWall_FarE", 11f, 4f, lowWall, -20f),
            Cover("Pillar_W", -4f, 2f, pillar), Cover("Pillar_E", 4f, 2f, pillar),
            Raised("Crates_W", -14f, -2f, -12f, 0f, 0f, crates), Raised("Crates_E", 12f, -12f, 14f, -10f, 0f, crates),
        };
        m.PlayerSpawns = new List<Vector3>();
        for (int i = 0; i < 4; i++) m.PlayerSpawns.Add(new Vector3(-4.5f + 3f * i, 0f, -18f));
        m.EnemySpawns = new List<Vector3>
        {
            new Vector3(-4f, 2f * tier, 17f), new Vector3(4f, 2f * tier, 17f),
            new Vector3(-3f, tier, 10.5f), new Vector3(3f, tier, 10.5f),
            new Vector3(-11f, tier, 11f), new Vector3(11f, tier, 11f),
            new Vector3(-12f, tier, 16f), new Vector3(12f, tier, 16f),
        };
    }

    /// <summary>Four city blocks with an open crossroads between them: tall L-shaped walls split the map into lanes that
    /// block sight, a low plaza in the middle and a rooftop on every corner. Point-symmetric.</summary>
    private static void FillCrossroads(MapTemplate m)
    {
        m.DisplayName = "Crossroads";
        m.Description = "Four walled blocks and the lanes between them. Rooftops on the corners, a raised plaza in the middle.";
        m.GroundSize = new Vector2(48f, 48f);
        const float tier = 1.2f, plaza = 0.9f;
        var along = new Vector3(8f, 3f, 0.6f);
        var across = new Vector3(0.6f, 3f, 8f);
        m.Pieces = new List<MapPiece>
        {
            Raised("Plaza", -2.5f, -2.5f, 2.5f, 2.5f, 0f, plaza),
            Cover("LowWall_S", 0f, -12f, new Vector3(4f, 1f, 0.5f)), Cover("LowWall_N", 0f, 12f, new Vector3(4f, 1f, 0.5f)),
            Cover("LowWall_W", -12f, 0f, new Vector3(0.5f, 1f, 4f)), Cover("LowWall_E", 12f, 0f, new Vector3(0.5f, 1f, 4f)),
            Raised("Roof_SW", -22f, -22f, -16f, -16f, 0f, tier), Raised("Roof_SE", 16f, -22f, 22f, -16f, 0f, tier),
            Raised("Roof_NW", -22f, 16f, -16f, 22f, 0f, tier), Raised("Roof_NE", 16f, 16f, 22f, 22f, 0f, tier),
        };
        foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
            {
                string corner = (sz < 0f ? "S" : "N") + (sx < 0f ? "W" : "E");
                m.Pieces.Add(Cover("Block_" + corner + "_A", sx * 10f, sz * 6f, along));
                m.Pieces.Add(Cover("Block_" + corner + "_B", sx * 6f, sz * 10f, across));
            }
        m.PlayerSpawns = new List<Vector3>();
        for (int i = 0; i < 4; i++) m.PlayerSpawns.Add(new Vector3(-4.5f + 3f * i, 0f, -20f));
        m.EnemySpawns = new List<Vector3>();
        for (int i = 0; i < 4; i++) m.EnemySpawns.Add(new Vector3(-4.5f + 3f * i, 0f, 20f));
        m.EnemySpawns.Add(new Vector3(-6f, 0f, 22f)); m.EnemySpawns.Add(new Vector3(6f, 0f, 22f));
        m.EnemySpawns.Add(new Vector3(-19f, tier, 19f)); m.EnemySpawns.Add(new Vector3(19f, tier, 19f));
    }

    private static Vector3 Spawn(MapTemplate map, Team team, int index, Vector3 fallback)
    {
        var spawns = map != null ? map.Spawns(team) : null;
        return spawns != null && index < spawns.Count ? spawns[index] : fallback;
    }

    /// <summary>Cover standing on the ground at (x, z).</summary>
    /// <summary>
    /// The Threshold, the final map: broken ground at every height. A two-tier dais in the middle (where Deliverance waits),
    /// a ring of broken pillars around it, raised terraces on both flanks with a second tier and their own walls, two
    /// towers at the far corners and two sunken courts of rubble near the squad's end, with walls set at angles across every
    /// lane so sight is never long. Reinforcements drop in anywhere.
    /// </summary>
    private static void FillThreshold(MapTemplate m)
    {
        m.DisplayName = "The Threshold";
        m.Description = "Broken ground at every height: a dais, terraces, towers and rubble, with walls across every lane.";
        m.GroundSize = new Vector2(56f, 56f);
        const float tier = 1.2f, low = 0.9f;
        var lowWall = new Vector3(4f, 1f, 0.5f);
        var tall = new Vector3(6f, 3f, 0.6f);
        var pillar = new Vector3(1.2f, 3f, 1.2f);
        m.Pieces = new List<MapPiece>
        {
            // The dais: two tiers in the middle, low walls on its rim.
            Raised("Dais", -6f, 4f, 6f, 14f, 0f, tier), Raised("Dais_Top", -3f, 7f, 3f, 12f, tier, tier),
            new MapPiece { Name = "Dais_Rim_W", Kind = MapPieceKind.Cover, Position = new Vector3(-4.5f, tier, 5f), Size = new Vector3(2.5f, 1f, 0.5f) },
            new MapPiece { Name = "Dais_Rim_E", Kind = MapPieceKind.Cover, Position = new Vector3(4.5f, tier, 5f), Size = new Vector3(2.5f, 1f, 0.5f) },
            // The ring of broken pillars.
            Cover("Pillar_SW", -9f, 1f, pillar), Cover("Pillar_SE", 9f, 1f, pillar), Cover("Pillar_NW", -9f, 17f, pillar), Cover("Pillar_NE", 9f, 17f, pillar),
            Cover("Pillar_W", -11f, 9f, pillar), Cover("Pillar_E", 11f, 9f, pillar),
            // Flank terraces, each with a second tier and a parapet.
            Raised("Terrace_W", -26f, -6f, -16f, 12f, 0f, tier), Raised("Terrace_W_Top", -26f, 2f, -20f, 10f, tier, tier),
            Raised("Terrace_E", 16f, -6f, 26f, 12f, 0f, tier), Raised("Terrace_E_Top", 20f, 2f, 26f, 10f, tier, tier),
            new MapPiece { Name = "Parapet_W", Kind = MapPieceKind.Cover, Position = new Vector3(-17f, tier, 3f), Size = new Vector3(0.5f, 1f, 4f) },
            new MapPiece { Name = "Parapet_E", Kind = MapPieceKind.Cover, Position = new Vector3(17f, tier, 3f), Size = new Vector3(0.5f, 1f, 4f) },
            // Towers at the far corners.
            Raised("Tower_NW", -26f, 19f, -19f, 26f, 0f, tier), Raised("Tower_NW_Top", -24f, 21f, -21f, 24f, tier, tier),
            Raised("Tower_NE", 19f, 19f, 26f, 26f, 0f, tier), Raised("Tower_NE_Top", 21f, 21f, 24f, 24f, tier, tier),
            // Sunken courts of rubble near the squad's end.
            Raised("Rubble_SW", -14f, -20f, -8f, -14f, 0f, low), Raised("Rubble_SE", 8f, -20f, 14f, -14f, 0f, low),
            Raised("Rubble_Heap", -2f, -12f, 2f, -9f, 0f, low),
            // Walls across the lanes, at angles: sight is never long.
            Cover("Wall_S", 0f, -4f, tall, 15f), Cover("Wall_SW", -12f, -8f, tall, -35f), Cover("Wall_SE", 12f, -8f, tall, 35f),
            Cover("Wall_N", 0f, 20f, tall, -10f), Cover("Wall_NW", -14f, 15f, tall, 40f), Cover("Wall_NE", 14f, 15f, tall, -40f),
            Cover("LowWall_W", -6f, -14f, lowWall, 10f), Cover("LowWall_E", 6f, -14f, lowWall, -10f),
            Cover("LowWall_FarW", -20f, -14f, lowWall, 60f), Cover("LowWall_FarE", 20f, -14f, lowWall, -60f),
            Cover("LowWall_NW", -6f, 22f, lowWall), Cover("LowWall_NE", 6f, 22f, lowWall),
        };
        m.PlayerSpawns = new List<Vector3>();
        for (int i = 0; i < 4; i++) m.PlayerSpawns.Add(new Vector3(-4.5f + 3f * i, 0f, -24f));
        m.EnemySpawns = new List<Vector3>
        {
            new Vector3(0f, 2f * tier, 9.5f),   // Deliverance, atop the dais
            new Vector3(-22f, 2f * tier, 22f), new Vector3(22f, 2f * tier, 22f),
            new Vector3(-22f, 2f * tier, 6f), new Vector3(22f, 2f * tier, 6f),
        };
    }

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
    /// The asset made for 'path', created with 'init' if there is none. Found first by the GUID the builder recorded when it
    /// made (or first found) it (BuiltAssetsPath), so an asset renamed or moved anywhere since is still the one used; then at
    /// 'path', or moved to a folder of the same name (FindMoved). A new one goes into a moved folder of the same name if
    /// 'path''s own folder is gone. Renaming or reorganising assets never makes the builder write a second copy.
    /// </summary>
    internal static T GetOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
    {
        string guid;
        if (Built().TryGetValue(path, out guid))
        {
            var known = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (known != null) return known;   // else it was deleted: find or make it again
        }
        var existing = AssetDatabase.LoadAssetAtPath<T>(path) ?? FindMoved<T>(path);
        if (existing != null) { Remember(path, existing); return existing; }
        string at = WhereToCreate(path);
        EnsureFolder(System.IO.Path.GetDirectoryName(at).Replace('\\', '/'));
        var asset = ScriptableObject.CreateInstance<T>();
        init(asset);
        AssetDatabase.CreateAsset(asset, at);
        Remember(path, asset);
        madeThisBuild.Add(asset);
        return asset;
    }

    /// <summary>
    /// The builder's record of what it made: for each path GetOrCreate was asked for, the GUID of the asset it made or first
    /// found there. One "path, tab, guid" line each, kept with the project so every machine finds the same assets. A renamed
    /// asset keeps its GUID, so it stays found.
    /// </summary>
    internal const string BuiltAssetsPath = "Assets/Scripts/Editor/BuiltAssets.txt";
    private static Dictionary<string, string> built;
    /// <summary>Assets GetOrCreate made during this Build (not found): the campaign only takes on new missions.</summary>
    private static readonly HashSet<Object> madeThisBuild = new HashSet<Object>();

    private static Dictionary<string, string> Built()
    {
        if (built != null) return built;
        built = new Dictionary<string, string>();
        if (!System.IO.File.Exists(BuiltAssetsPath)) return built;
        foreach (var line in System.IO.File.ReadAllLines(BuiltAssetsPath))
        {
            int tab = line.LastIndexOf('\t');
            if (tab > 0) built[line.Substring(0, tab)] = line.Substring(tab + 1).Trim();
        }
        return built;
    }

    private static void Remember(string path, Object asset)
    {
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
        string was;
        if (string.IsNullOrEmpty(guid) || (Built().TryGetValue(path, out was) && was == guid)) return;
        built[path] = guid;
        var lines = new List<string>();
        foreach (var pair in built) lines.Add(pair.Key + "\t" + pair.Value);
        lines.Sort(System.StringComparer.Ordinal);
        System.IO.File.WriteAllLines(BuiltAssetsPath, lines.ToArray());
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
