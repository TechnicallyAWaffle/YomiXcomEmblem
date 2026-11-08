using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;
using TacticsGame.Presentation;

/// <summary>
/// The tutorial's assets (Build Scene makes any that are missing): its enemies (a Training Dummy that never acts, a melee
/// Raider and a ranged Marksman, each with a slow, readable attack) and the Tutorial asset itself, with the squad (a
/// Fighter and a Sharpshooter), the Arena map and the step-by-step script. Like the other content, the assets are the
/// source of truth once made: edit the steps' text, goals and spawns on Assets/Tutorial/Tutorial.asset in the Inspector.
/// Delete it to get this default script back.
/// </summary>
public static class TutorialAssets
{
    public const string TutorialPath = "Assets/Tutorial/Tutorial.asset";
    private const string UnitsFolder = "Assets/Units/Tutorial";
    private const string ActionsFolder = "Assets/Actions/Enemies/Tutorial";

    // Where things stand on the Arena: the squad starts at the south end (the Sharpshooter on its perch), the dummy waits
    // in the open gap between the two southern low walls, and the enemies come in from the north (the Marksman on the north perch).
    private static readonly Vector3 DummyAt = new Vector3(0f, 0f, -6f);
    private static readonly Vector3 RaiderAt = new Vector3(0f, 0f, 9f);
    private static readonly Vector3 PairAt = new Vector3(-1f, 0f, 10f);
    private static readonly Vector3 MarksmanAt = new Vector3(6f, 1.2f, 12f);

    public static TutorialDefinition Create(GameObject viewPrefab, UnitDefinition fighter, UnitDefinition sharpshooter, MapTemplate arena)
    {
        var strike = CombatTestSceneBuilder.GetOrCreate<ActionDefinition>(ActionsFolder + "/RaiderStrike.asset", a =>
        {
            a.Id = "raider_strike"; a.DisplayName = "Raider Strike"; a.BehaviorId = "basic_attack";
            a.Description = "A heavy-handed swing with a long windup: easy to see coming.";
            a.Targeting = TargetType.Enemy; a.Range = 1.8f; a.IsRanged = false;
            a.Windup = 0.8f; a.Recovery = 0.7f;
            a.Impact = ImpactType.Light; a.Damage = 9f; a.BaseAccuracy = 1f; a.StunDuration = 0.2f; a.StrainOnTarget = 4f;
            a.Visuals = new ActionVisuals(VfxKind.Slash, new Color(1f, 0.55f, 0.3f), 0.1f);
            a.DataVersion = SquadAssets.DataVersion;
        });
        var shot = CombatTestSceneBuilder.GetOrCreate<ActionDefinition>(ActionsFolder + "/MarksmanShot.asset", a =>
        {
            a.Id = "marksman_shot"; a.DisplayName = "Marksman Shot"; a.BehaviorId = "basic_attack";
            a.Description = "A carefully aimed rifle shot.";
            a.Targeting = TargetType.Enemy; a.Range = 14f; a.IsRanged = true;
            a.Windup = 1f; a.Recovery = 0.8f;
            a.Impact = ImpactType.Light; a.Damage = 7f; a.BaseAccuracy = 1f; a.StunDuration = 0.15f; a.StrainOnTarget = 3f;
            a.Visuals = new ActionVisuals(VfxKind.Tracer, new Color(1f, 0.6f, 0.35f), 0.08f);
            a.DataVersion = SquadAssets.DataVersion;
        });

        var dummy = Unit("TrainingDummy", u =>
        {
            u.Id = "training_dummy"; u.DisplayName = "Training Dummy"; u.MaxHealth = 400f; u.BaseDodge = 0f; u.MoveSpeed = 3f;
            u.AIProfile = IdleAI.Style;   // stands still, even when the step lets the enemies fight
            u.Actions = new List<ActionDefinition>();
            u.ViewPrefab = viewPrefab;
        });
        var raider = Unit("Raider", u =>
        {
            u.Id = "tutorial_raider"; u.DisplayName = "Raider"; u.MaxHealth = 70f; u.BaseDodge = 0f; u.MoveSpeed = 3.5f;
            u.AIProfile = "";   // the basic AI: walk up and hit the nearest
            u.BasicAttack = strike;
            u.Actions = new List<ActionDefinition>();
            u.ViewPrefab = viewPrefab;
        });
        var marksman = Unit("Marksman", u =>
        {
            u.Id = "tutorial_marksman"; u.DisplayName = "Marksman"; u.MaxHealth = 55f; u.BaseDodge = 0f; u.MoveSpeed = 3.5f;
            u.AIProfile = "";
            u.BasicAttack = shot;
            u.Actions = new List<ActionDefinition>();
            u.ViewPrefab = viewPrefab;
        });

        return CombatTestSceneBuilder.GetOrCreate<TutorialDefinition>(TutorialPath, t => Fill(t, fighter, sharpshooter, arena, dummy, raider, marksman));
    }

    private static UnitDefinition Unit(string file, System.Action<UnitDefinition> init)
    {
        return CombatTestSceneBuilder.GetOrCreate<UnitDefinition>(UnitsFolder + "/" + file + ".asset", u => { init(u); u.DataVersion = SquadAssets.DataVersion; });
    }

    private static List<ActionDefinition> Abilities(UnitDefinition unit, params string[] ids)
    {
        var list = new List<ActionDefinition>();
        foreach (var id in ids)
        {
            var a = unit != null ? unit.Actions.Find(x => x != null && x.Id == id) : null;
            if (a != null) list.Add(a);
            else Debug.LogWarning("TutorialAssets: " + (unit != null ? unit.DisplayName : "?") + " has no ability '" + id + "' for the tutorial.");
        }
        return list;
    }

    private static TutorialSpawn At(UnitDefinition unit, Vector3 at, int count = 1) { return new TutorialSpawn { Unit = unit, Position = at, Count = count }; }

    /// <summary>The default script: one step per thing the player can do, in the order a battle needs them.</summary>
    private static void Fill(TutorialDefinition t, UnitDefinition fighter, UnitDefinition sharpshooter, MapTemplate arena,
                             UnitDefinition dummy, UnitDefinition raider, UnitDefinition marksman)
    {
        t.DisplayName = "Tutorial";
        t.Description = "A guided battle with a Fighter and a Sharpshooter that shows every control, step by step.";
        t.Map = arena;
        t.Squad = new List<TutorialUnit>
        {
            new TutorialUnit { Unit = sharpshooter, Abilities = Abilities(sharpshooter, "snapshot", "trap") },   // first: the perch spawn
            new TutorialUnit { Unit = fighter, Abilities = Abilities(fighter, "steady_strikes", "rapid_slices") },
        };
        t.SquadHealthFloor = 0.2f;
        t.FinishedTitle = "Tutorial complete";
        t.FinishedText = "That's everything a battle asks of you. Start a campaign from the menu: between missions your units earn ability points to unlock new abilities, and Etchings, permanent upgrades you choose from.";
        t.FinishedButton = "Back to the menu";

        t.Steps = new List<TutorialStep>
        {
            new TutorialStep
            {
                Title = "Welcome", Place = TutorialPlace.Center,
                Text = "This battle teaches you the controls with two units: a <b>Fighter</b> (melee) and a <b>Sharpshooter</b> (ranged).\n\n" +
                       "Battles play out in real time, but the world holds still whenever one of your units is ready for new orders, so take as long as you like to decide.\n\n" +
                       "Read each note, then press Continue (or Enter). Notes that ask you to do something move on by themselves once you've done it.",
            },
            new TutorialStep
            {
                Title = "Panning the camera", Goal = TutorialGoal.PanCamera,
                Text = "Pan the camera with <b>WASD</b> or the <b>arrow keys</b>, or drag with the <b>middle mouse button</b>.",
            },
            new TutorialStep
            {
                Title = "Rotating the camera", Goal = TutorialGoal.RotateCamera,
                Text = "Rotate with <b>Q</b> and <b>E</b>, or hold the <b>right mouse button</b> and drag (dragging up and down tilts the view).",
            },
            new TutorialStep
            {
                Title = "Zooming", Goal = TutorialGoal.ZoomCamera,
                Text = "Zoom in and out with the <b>mouse wheel</b>.\n\nPress <b>Z</b> at any time to centre the camera on the unit you're giving orders to.",
            },
            new TutorialStep
            {
                Title = "Who's giving orders",
                Text = "The panel at the bottom gives orders to one unit at a time: the one pulsing on the field. When you've given it its orders, the next unit that's ready comes up.\n\n" +
                       "Bottom left: that unit's <b>health</b> (green) and <b>strain</b> (orange). Along the top: the <b>timeline</b>, showing when each unit will be free to act next. Hover over a chip on it to centre the camera on that unit.",
            },
            new TutorialStep
            {
                Title = "Moving", Goal = TutorialGoal.Move,
                Text = "Press <b>F</b> (or click Move), then click the ground to walk there.\n\nThe line shows the route and how long the walk takes, and the enemies the unit would see from there light up.",
            },
            new TutorialStep
            {
                Title = "Waypoints", Goal = TutorialGoal.MoveWithWaypoints,
                Text = "Press <b>F</b>, then hold <b>Shift</b> and click to drop waypoints. Click without Shift for the last point. The unit walks through each one in turn without stopping.",
            },
            new TutorialStep
            {
                Title = "Something to hit",
                Spawns = new List<TutorialSpawn> { At(dummy, DummyAt) },
                Text = "A <b>Training Dummy</b> has been set up in front of you. It won't fight back.\n\nEvery unit's basic attack is always on key <b>1</b>; its other abilities are on the next keys, as their buttons show.",
            },
            new TutorialStep
            {
                Title = "Attacking", Goal = TutorialGoal.HitEnemy, Count = 2,
                Text = "Press <b>1</b>, then click the dummy. Hit it with both units.\n\n" +
                       "Ordered out of reach, a melee attack walks the unit in and strikes when it gets there. A ranged attack ordered out of range waits until the target is in range.",
            },
            new TutorialStep
            {
                Title = "Line of sight and cover",
                Text = "Ranged attacks need <b>line of sight</b> from where the unit stands. Pillars block it completely.\n\n" +
                       "Low walls are <b>cover</b>: a shot from the far side at a unit behind one can strike the wall instead. Raised ground lets you see over cover. While aiming, targets you can't hit are marked in red.",
            },
            new TutorialStep
            {
                Title = "Abilities", Goal = TutorialGoal.UseAbility, AbilityId = "steady_strikes",
                Text = "Hover over a button in the panel to see what an ability does, its <b>windup</b> (before it lands), <b>recovery</b> (after) and <b>cooldown</b>.\n\n" +
                       "Use the Fighter's <b>Steady Strikes</b> on the dummy.",
            },
            new TutorialStep
            {
                Title = "Strain",
                Text = "Some abilities cost <b>strain</b> (Rapid Slices, Block, Dash), and every attack that lands adds strain to its target. A unit can't use an ability that would push it past its maximum strain, so keep an eye on the orange bar.",
            },
            new TutorialStep
            {
                Title = "Focus", Goal = TutorialGoal.Focus,
                Text = "Hold <b>Space</b> while a unit is up for orders to <b>Focus</b>: it stands still and sheds strain, and time runs slowly for as long as you hold.",
            },
            new TutorialStep
            {
                Title = "Plans", Goal = TutorialGoal.QueueOrders,
                Text = "Hold <b>Shift</b> while giving orders to queue several in a row, such as a move and then an attack. <b>Enter</b> confirms the plan, <b>Backspace</b> undoes the last order, and <b>right-click</b> or <b>Esc</b> goes back a step.\n\n" +
                       "Queue two orders and confirm them.",
            },
            new TutorialStep
            {
                Title = "Follow-ups", Goal = TutorialGoal.UseAbility, AbilityId = "rapid_slices", Count = 2, RestoreSquad = true,
                Text = "<b>Rapid Slices</b> is a follow-up: right after it, press its key again to repeat it on the same target for half the strain, up to four more times.\n\n" +
                       "Use it twice in a row on the dummy.",
            },
            new TutorialStep
            {
                Title = "Transcend", Goal = TutorialGoal.Transcend, RestoreSquad = true,
                Text = "<b>Hold</b> an ability's key (or its button) instead of tapping it to <b>Transcend</b> it: a stronger version for extra strain. The screen darkens as the hold builds.\n\n" +
                       "Transcend the Fighter's Steady Strikes: it becomes a hit that can't miss and pierces block. Block and Dash can be Transcended too.",
            },
            new TutorialStep
            {
                Title = "Targeting the ground", Goal = TutorialGoal.UseAbility, AbilityId = "trap",
                Text = "Some abilities are aimed at a point on the ground. Have the Sharpshooter throw a <b>Trap</b>: it arms after a moment, and the first enemy to step in is immobilised and marked.",
            },
            new TutorialStep
            {
                Title = "A real opponent", RemoveEnemies = true,
                Spawns = new List<TutorialSpawn> { At(raider, RaiderAt) },
                Text = "The dummy's done its job. A <b>Raider</b> has arrived to the north.\n\n" +
                       "Enemies show what they're about to do: the label over a unit's head and its chip on the timeline show its ability winding up and when it will land.",
            },
            new TutorialStep
            {
                Title = "Blocking", Goal = TutorialGoal.UseAbility, AbilityId = "block", Enemies = TutorialEnemies.Fight, RestoreSquad = true,
                Text = "The Raider is coming. <b>Block</b> raises a unit's guard for a second, halving the damage of whatever lands in that time. Use it on the unit the Raider is about to hit.",
            },
            new TutorialStep
            {
                Title = "Interrupting", Goal = TutorialGoal.HitEnemy, AbilityId = "snapshot", Enemies = TutorialEnemies.Fight,
                Text = "Heavy hits interrupt a windup. Hit the Raider with the Sharpshooter's <b>Snapshot</b>, ideally while it's winding up an attack, to cancel it.",
            },
            new TutorialStep
            {
                Title = "Dodging", Goal = TutorialGoal.DodgeDash, Enemies = TutorialEnemies.Fight, SquadAlwaysDodges = true,
                Text = "Units sometimes <b>dodge</b> an attack. When one of yours does, time stops and you pick where it dashes: click a point inside the ring around it.\n\n" +
                       "For this step, your units dodge every attack. Let the Raider swing at one.",
            },
            new TutorialStep
            {
                Title = "Stopping a walk", Goal = TutorialGoal.StopMove, Enemies = TutorialEnemies.Fight,
                Text = "Send a unit on a long walk. While it's walking, the next time another of your units is up for orders, the panel offers to <b>stop</b> the walker where it stands, for some strain.",
            },
            new TutorialStep
            {
                Title = "Finish the Raider", Goal = TutorialGoal.DefeatAllEnemies, Enemies = TutorialEnemies.Fight,
                Text = "Take the Raider down.",
            },
            new TutorialStep
            {
                Title = "Final exercise", Goal = TutorialGoal.DefeatAllEnemies, Enemies = TutorialEnemies.Fight, RestoreSquad = true, Skippable = false,
                Spawns = new List<TutorialSpawn> { At(raider, PairAt, 2), At(marksman, MarksmanAt) },
                Text = "Two Raiders and a <b>Marksman</b>. The Marksman shoots from the north perch: keep the pillars between it and your units, or close in on it. Use everything you've learned.",
            },
            new TutorialStep
            {
                Title = "A few more keys", Place = TutorialPlace.Center,
                Text = "<b>H</b>  shows or hides the controls list (top right)\n<b>Alt</b>  hides the interface so you can watch the battle (Alt again brings it back)\n<b>C</b>  switches the cinematic camera between key moments, everything and off\n<b>R</b>  replays a battle once it's over",
            },
        };
    }
}
