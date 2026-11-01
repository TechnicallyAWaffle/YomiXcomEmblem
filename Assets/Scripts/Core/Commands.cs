using System.Collections.Generic;
using UnityEngine;

namespace TacticsGame.Core
{
    // HoldGrapple / ReleaseGrapple: a unit holding someone with Subjugate keeps headbutting (until just after the next one)
    // or throws now. Moving is the only other order it takes.
    public enum CommandType { Action, Move, Wait, ContinueCharge, ReleaseCharge, HoldGrapple, ReleaseGrapple }

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
        public bool Transcend;      // use the ability's Transcended version (ActionDefinition.CanTranscend), for its extra strain
        public bool Aimed;          // ReleaseGrapple: throw toward TargetPos
        public bool NoFocus;        // Wait: a pause, not a Focus (UnitCommand.Idle)

        /// <summary>This order, Transcended (ignored for actions that can't be).</summary>
        public UnitCommand Transcended(bool transcend = true) { Transcend = transcend; return this; }

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
        /// <summary>A unit and a point (ActionDefinition.DestinationRange: Wrinkles of Reality sends 'targetUnitId' to 'destination').
        /// Out of range, it is queued like any ranged order.</summary>
        public static UnitCommand UseActionOn(string unitId, ActionDefinition action, string targetUnitId, Vector3 destination)
        {
            return new UnitCommand { UnitId = unitId, Type = CommandType.Action, Action = action, TargetUnitId = targetUnitId, TargetPos = destination, WaitForRange = true };
        }
        public static UnitCommand HoldGrapple(string unitId) { return new UnitCommand { UnitId = unitId, Type = CommandType.HoldGrapple }; }
        public static UnitCommand ReleaseGrapple(string unitId) { return new UnitCommand { UnitId = unitId, Type = CommandType.ReleaseGrapple }; }
        /// <summary>Throw now, toward 'toward' (the held unit swings round to that side first) instead of the way the holder faces.</summary>
        public static UnitCommand ReleaseGrapple(string unitId, Vector3 toward) { return new UnitCommand { UnitId = unitId, Type = CommandType.ReleaseGrapple, TargetPos = toward, Aimed = true }; }
        /// <summary>A charging unit keeps charging for 'seconds', then is asked again.</summary>
        public static UnitCommand ContinueCharge(string unitId, float seconds)
        {
            return new UnitCommand { UnitId = unitId, Type = CommandType.ContinueCharge, WaitSeconds = seconds };
        }
        /// <summary>Release a charging ability at a unit or a point (targeting happens now, not when the charge began).</summary>
        public static UnitCommand ReleaseCharge(string unitId, string targetUnitId, Vector3 pos)
        {
            return new UnitCommand { UnitId = unitId, Type = CommandType.ReleaseCharge, TargetUnitId = targetUnitId, TargetPos = pos };
        }
        public static UnitCommand MoveTo(string unitId, Vector3 pos)
        {
            return new UnitCommand { UnitId = unitId, Type = CommandType.Move, TargetPos = pos };
        }
        /// <summary>Focus: stand still and rest off strain (see BattleSimulation.StartWait).</summary>
        public static UnitCommand Wait(string unitId, float seconds)
        {
            return new UnitCommand { UnitId = unitId, Type = CommandType.Wait, WaitSeconds = seconds };
        }
        /// <summary>Hold still for a moment without Focusing (an AI's pause between moves): breaks a run of Focus instead of adding to it.</summary>
        public static UnitCommand Idle(string unitId, float seconds)
        {
            return new UnitCommand { UnitId = unitId, Type = CommandType.Wait, WaitSeconds = seconds, NoFocus = true };
        }
    }

    public enum DecisionKind { Action, DodgeDash, Charge }   // Charge: a charging unit must keep charging or release

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
