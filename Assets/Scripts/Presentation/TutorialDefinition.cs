using System.Collections.Generic;
using UnityEngine;
using TacticsGame.Core;

namespace TacticsGame.Presentation
{
    /// <summary>
    /// The tutorial as data: a squad with fixed abilities, a map, and the steps it runs through in order (TutorialRunner).
    /// Each step shows a popup (its title and text, edited here in the Inspector) and waits for its goal: a click on
    /// Continue, a key, a camera move, an order of some kind, something happening in the battle. A step can also bring
    /// enemies onto the field, let the enemies fight or hold them still, heal the squad, or make the squad's units dodge
    /// every attack (to show the dodge dash). The battle never ends on its own when the enemies are down
    /// (BattleSimulation.HoldVictory): the last step finishing ends it.
    /// </summary>
    [CreateAssetMenu(menuName = "TacticsGame/Tutorial")]
    public class TutorialDefinition : ScriptableObject
    {
        public string DisplayName = "Tutorial";
        [TextArea(2, 4)] public string Description = "Learn the controls with a Fighter and a Sharpshooter.";
        [Tooltip("The map it is played on (empty: whatever map is loaded).")]
        public MapTemplate Map;
        [Tooltip("The squad, in spawn order (the map's player spawns), each with the abilities it brings. Its basic attack, Block and Dash always come too.")]
        public List<TutorialUnit> Squad = new List<TutorialUnit>();
        [Tooltip("Enemies on the field from the start (more can arrive with a step's Spawns).")]
        public List<TutorialSpawn> StartingEnemies = new List<TutorialSpawn>();
        [Tooltip("The squad's units can't drop below this share of their health, so nobody falls mid-lesson (0: they can).")]
        [Range(0f, 1f)] public float SquadHealthFloor = 0.2f;
        public List<TutorialStep> Steps = new List<TutorialStep>();
        [Tooltip("The last popup, after every step: the button on it ends the tutorial.")]
        public string FinishedTitle = "Tutorial complete";
        [TextArea(2, 6)] public string FinishedText = "You know everything you need. Start a campaign from the main menu.";
        public string FinishedButton = "Back to the menu";
    }

    /// <summary>A squad member in the tutorial and the class abilities it brings (its basic attack, Block and Dash come anyway).</summary>
    [System.Serializable]
    public class TutorialUnit
    {
        public UnitDefinition Unit;
        public List<ActionDefinition> Abilities = new List<ActionDefinition>();
    }

    /// <summary>Enemies placed on the field: Count of Unit around Position (spread out so they don't overlap).</summary>
    [System.Serializable]
    public class TutorialSpawn
    {
        public UnitDefinition Unit;
        public Vector3 Position;
        [Min(1)] public int Count = 1;
    }

    /// <summary>What a step waits for before the next one starts.</summary>
    public enum TutorialGoal
    {
        Continue,          // the Continue button (or Enter): orders and playback wait while it is up
        PressKey,          // Key pressed
        PanCamera,         // the camera panned by hand (Amount metres; default 4)
        RotateCamera,      // rotated by hand (Amount degrees; default 45)
        ZoomCamera,        // zoomed by hand (Amount scroll notches; default 2)
        Move,              // a move ordered
        MoveWithWaypoints, // a move through Shift-clicked waypoints
        UseAbility,        // an ability starts (AbilityId; empty = any)
        HitEnemy,          // an enemy takes damage (AbilityId; empty = any attack)
        Focus,             // Space held to Focus
        QueueOrders,       // a plan of two or more orders confirmed in one go (Shift, then Enter)
        Transcend,         // a Transcended ability starts (AbilityId; empty = any)
        DodgeDash,         // a dodge-dash after dodging an attack
        StopMove,          // a walk stopped from the order panel
        ChaseOrQueue,      // an attack ordered out of range: melee walks in, ranged waits until in range
        DefeatEnemies,     // Count enemies defeated (from when the step starts)
        DefeatAllEnemies,  // no enemy left standing
        Wait,              // Amount seconds of battle time pass (shown as a hint, no button)
    }

    /// <summary>Whether the enemies act while a step is up.</summary>
    public enum TutorialEnemies { Hold, Fight }

    /// <summary>Where a step's popup sits on screen.</summary>
    /// Side (the default): the left edge, under the battle log, out of the way of the field. New values go at the end:
    /// assets store the number.
    public enum TutorialPlace { Side, Center, Left, Right, Top }

    /// <summary>One step: a popup and what moves the tutorial on. Its Title names it in the Inspector's list.</summary>
    [System.Serializable]
    public class TutorialStep
    {
        public string Title = "Step";
        [TextArea(3, 12)] public string Text = "";
        public TutorialGoal Goal = TutorialGoal.Continue;
        [Tooltip("UseAbility, HitEnemy, Transcend: only this ability counts (its id, e.g. steady_strikes). Empty: any.")]
        public string AbilityId = "";
        [Tooltip("Move, UseAbility, HitEnemy, ...: only this squad class's orders count (its UnitDefinition id or asset name). Empty: any member.")]
        public string UnitClass = "";
        [Tooltip("PressKey: the key.")]
        public KeyCode Key = KeyCode.None;
        [Tooltip("How many times the goal must happen (orders, hits, kills).")]
        [Min(1)] public int Count = 1;
        [Tooltip("PanCamera (metres), RotateCamera (degrees), ZoomCamera (notches), Wait (seconds). 0: the goal's default.")]
        [Min(0f)] public float Amount = 0f;
        [Tooltip("Hold: enemies stand still while this step is up. Fight: they attack.")]
        public TutorialEnemies Enemies = TutorialEnemies.Hold;
        [Tooltip("As the step starts, every enemy still standing is removed (a training dummy whose lessons are done), before Spawns arrive.")]
        public bool RemoveEnemies;
        [Tooltip("Enemies that arrive as the step starts.")]
        public List<TutorialSpawn> Spawns = new List<TutorialSpawn>();
        [Tooltip("As the step starts: the squad back to full health, no strain, every cooldown ready.")]
        public bool RestoreSquad;
        [Tooltip("While the step is up, the squad's units dodge every attack (to show the dodge dash).")]
        public bool SquadAlwaysDodges;
        [Tooltip("A goal step shows a Skip button, for when the goal won't come up.")]
        public bool Skippable = true;
        [Tooltip("Where its popup sits. Side: the left edge, under the battle log. A goal step's popup shrinks to one line there while the player does what it asks.")]
        public TutorialPlace Place = TutorialPlace.Side;
    }
}
