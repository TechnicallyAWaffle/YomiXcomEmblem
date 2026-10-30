using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    public enum CommandType { Action, Move, Wait }

    /// <summary>One order for one unit. Build with the static helpers.</summary>
    public class UnitCommand
    {
        public string UnitId;
        public CommandType Type;
        public ActionDefinition Action;
        public string TargetUnitId;
        public Vector3 TargetPos;
        public float WaitSeconds = 0.5f;
        public bool WaitForRange;   // out of range when ordered: melee chases the target, ranged queues until it is in range

        public static UnitCommand UseAction(string unitId, ActionDefinition action, string targetUnitId = null)
        {
            return new UnitCommand { UnitId = unitId, Type = CommandType.Action, Action = action, TargetUnitId = targetUnitId };
        }
        /// <summary>Like UseAction, but if the target is out of range a melee ability chases it and a ranged one is queued until
        /// the target is in range (see BattleSimulation.StartChase / QueueRanged).</summary>
        public static UnitCommand UseActionWhenInRange(string unitId, ActionDefinition action, string targetUnitId)
        {
            return new UnitCommand { UnitId = unitId, Type = CommandType.Action, Action = action, TargetUnitId = targetUnitId, WaitForRange = true };
        }
        public static UnitCommand UseActionAt(string unitId, ActionDefinition action, Vector3 pos)
        {
            return new UnitCommand { UnitId = unitId, Type = CommandType.Action, Action = action, TargetPos = pos };
        }
        public static UnitCommand MoveTo(string unitId, Vector3 pos)
        {
            return new UnitCommand { UnitId = unitId, Type = CommandType.Move, TargetPos = pos };
        }
        public static UnitCommand Wait(string unitId, float seconds)
        {
            return new UnitCommand { UnitId = unitId, Type = CommandType.Wait, WaitSeconds = seconds };
        }
    }

    public enum DecisionKind { Action, DodgeDash }

    /// <summary>The sim is paused because a player unit needs input.</summary>
    public class DecisionRequest
    {
        public DecisionKind Kind;
        public string UnitId;
        public string AttackerId;   // DodgeDash only
        public string Reason;       // set when a previously submitted command was rejected
    }

    public enum SegmentEnd { DecisionNeeded, BattleOver, TimedOut }

    public class SimSegmentResult
    {
        public SegmentEnd End;
        public float Time;                 // sim time the segment stopped at
        public BattleOutcome Outcome;
        public List<DecisionRequest> Decisions = new List<DecisionRequest>();
    }
}
