using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: The run loop, decisions, and starting orders (waits, idles, focus).</summary>
    public partial class BattleSimulation
    {
        /// <summary>
        /// Advance until a player unit needs a command, a dodge-dash needs a direction,
        /// the battle ends, or maxSimSeconds elapse.
        /// </summary>
        public SimSegmentResult RunUntilDecision(float maxSimSeconds = 300f)
        {
            long deadline = tick + (long)(maxSimSeconds / TickLength);
            while (true)
            {
                if (Outcome != BattleOutcome.InProgress) return Segment(SegmentEnd.BattleOver, null);
                playerTurnOpen = false;
                var decisions = ProcessReadyUnits();
                if (decisions.Count > 0)
                {
                    playerTurnOpen = decisions.Any(d => d.Kind == DecisionKind.Action);
                    return Segment(SegmentEnd.DecisionNeeded, decisions);
                }
                if (tick >= deadline) return Segment(SegmentEnd.TimedOut, null);
                Step();
            }
        }

        // ------------------------------------------------------------------ decisions

        private SimSegmentResult Segment(SegmentEnd end, List<DecisionRequest> decisions)
        {
            var r = new SimSegmentResult { End = end, Time = Now, Outcome = Outcome };
            if (decisions != null) r.Decisions = decisions;
            return r;
        }

        private List<DecisionRequest> ProcessReadyUnits()
        {
            var decisions = new List<DecisionRequest>(pendingDashes);
            foreach (var unit in State.Units)
            {
                if (!unit.IsAlive || IsGliding(unit)) continue;   // nothing new mid-dash
                // A charging unit comes up for a decision every ChargeDecisionAt: keep charging or release.
                bool charging = unit.Phase == UnitPhase.Charging;
                if (charging ? Now + 1e-4f < unit.ChargeDecisionAt : unit.Phase != UnitPhase.Ready) continue;
                var kind = charging ? DecisionKind.Charge : DecisionKind.Action;

                // A Mesmerize counter whose target is gone just ends.
                if (unit.MesmerStrikesLeft > 0)
                {
                    var mt = GetUnit(unit.MesmerTargetId);
                    if (mt == null || !mt.IsAlive) EndMesmer(unit);
                }

                UnitCommand cmd = null;
                bool isPlayer = unit.Team == Team.Player;
                if (isPlayer)
                {
                    Queue<UnitCommand> q;
                    if (queues.TryGetValue(unit.Id, out q) && q.Count > 0) cmd = q.Dequeue();
                }
                else if (unit.MesmerStrikesLeft > 0 && !charging)
                {
                    cmd = UnitCommand.UseAction(unit.Id, unit.MesmerStrike, unit.MesmerTargetId);   // the AI always keeps striking
                }
                else
                {
                    cmd = EnemyAI.Decide(this, unit) ?? (charging ? UnitCommand.ContinueCharge(unit.Id, 0.25f) : UnitCommand.Idle(unit.Id, 0.5f));
                }

                if (cmd == null)
                {
                    decisions.Add(new DecisionRequest { Kind = kind, UnitId = unit.Id });
                    continue;
                }

                string reason;
                if (!TryStartCommand(unit, cmd, out reason))
                {
                    if (isPlayer)
                    {
                        queues.Remove(unit.Id);   // a stale plan: drop the rest and ask again
                        decisions.Add(new DecisionRequest { Kind = kind, UnitId = unit.Id, Reason = reason });
                    }
                    else if (charging) TryStartCommand(unit, UnitCommand.ContinueCharge(unit.Id, 0.25f), out reason);
                    else StartIdle(unit, 0.5f);   // never let an AI stall the sim on a bad command
                }
            }
            return decisions;
        }

        private bool TryStartCommand(Unit unit, UnitCommand cmd, out string reason)
        {
            reason = null;
            // Charging can't be cancelled: the only orders are keep charging and release.
            bool chargeOrder = cmd.Type == CommandType.ContinueCharge || cmd.Type == CommandType.ReleaseCharge;
            if (unit.Phase == UnitPhase.Charging && !chargeOrder) { reason = "charging: keep charging or release it"; return false; }
            if (unit.Phase != UnitPhase.Charging && chargeOrder) { reason = "not charging"; return false; }
            // Any order other than the next rapid strike ends a Mesmerize counter.
            if (unit.MesmerStrikesLeft > 0 && (cmd.Action == null || cmd.Action != unit.MesmerStrike)) EndMesmer(unit);
            // Holding someone (Subjugate): only moving, headbutting on (a Wait counts as that) or the throw.
            if (unit.Grapple != null)
            {
                Vector3? aim = cmd.Type == CommandType.ReleaseGrapple && cmd.Aimed ? cmd.TargetPos : (Vector3?)null;
                // The grab ran out on a player's unit and it was asked which way to throw: whatever the order, it throws now.
                if (unit.Grapple.AwaitingThrow) { StartThrow(unit, aim); return true; }
                if (cmd.Type == CommandType.HoldGrapple || cmd.Type == CommandType.Wait) { HoldGrapple(unit); return true; }
                if (cmd.Type == CommandType.ReleaseGrapple) { StartThrow(unit, aim); return true; }
                if (cmd.Type != CommandType.Move) { reason = "grappling: move, keep headbutting or throw"; return false; }
            }
            else if (cmd.Type == CommandType.HoldGrapple || cmd.Type == CommandType.ReleaseGrapple) { reason = "not grappling"; return false; }
            // Up in the air (Skyward Demise's hover, or lifted): no walking, dashing or other moves.
            if (unit.IsLifted && (cmd.Type == CommandType.Move || (cmd.Action != null && ActionBehaviors.Get(cmd.Action.BehaviorId).MovesUser))) { reason = "in the air"; return false; }

            switch (cmd.Type)
            {
                case CommandType.ContinueCharge:
                    unit.ChargeDecisionAt = Now + Mathf.Max(TickLength, cmd.WaitSeconds);
                    return true;

                case CommandType.ReleaseCharge:
                    return ReleaseCharge(unit, cmd, out reason);

                case CommandType.Wait:
                    unit.LastActionId = null;   // a wait or a move breaks a follow-up chain
                    if (cmd.NoFocus) StartIdle(unit, cmd.WaitSeconds);
                    else StartWait(unit, cmd.WaitSeconds, cmd.Transcend);
                    return true;

                case CommandType.Move:
                {
                    if (unit.IsRooted) { reason = "immobilized"; return false; }
                    var path = new List<Vector3>();
                    if (!PlanPath(unit, unit.Position, FreeSpotNear(unit, cmd.TargetPos, unit.Position, true), path)) { reason = "no path"; return false; }
                    unit.LastActionId = null;
                    StartMove(unit, path);
                    return true;
                }

                case CommandType.Action:
                {
                    if (cmd.Action == null) { reason = "no action"; return false; }
                    if (cmd.Action.IsCharge)
                    {
                        // Targeting happens at release, so only resources and the behavior's own rules are checked now.
                        if (!CanAfford(unit, cmd.Action, out reason, null, cmd.Transcend)) return false;
                        var chargeBehavior = ActionBehaviors.Get(cmd.Action.BehaviorId);
                        if (!chargeBehavior.CanStart(this, unit, cmd, out reason)) return false;
                        StartCharge(unit, cmd, chargeBehavior);
                        return true;
                    }
                    var target = GetUnit(cmd.TargetUnitId);
                    if (!CanUse(unit, cmd.Action, target, out reason, true, cmd.Transcend))
                    {
                        if (!cmd.WaitForRange || reason != "out of range") return false;
                        if (!cmd.Action.IsRanged) return StartChase(unit, cmd, target, out reason);
                        QueueRanged(unit, cmd, target);   // the unit stays ready: it is asked for its next order straight away
                        return true;
                    }
                    // Point-targeted actions must land in range; a line only takes its direction from the point.
                    if (cmd.Action.Targeting == TargetType.Position && cmd.Action.Shape != AreaShape.Line &&
                        Vector3.Distance(unit.Position, cmd.TargetPos) > unit.ActionStat(cmd.Action, ActionStat.Range)) { reason = "out of range"; return false; }
                    // A unit and a point: the point must be within DestinationRange of the unit.
                    if (cmd.Action.DestinationRange > 0f && FlatDistance(target.Position, cmd.TargetPos) > cmd.Action.DestinationRange + 1e-3f) { reason = "destination out of range"; return false; }
                    var behavior = ActionBehaviors.Get(cmd.Action.BehaviorId);
                    if (!behavior.CanStart(this, unit, cmd, out reason)) return false;
                    StartAction(unit, cmd, behavior);
                    return true;
                }
            }
            reason = "unknown command";
            return false;
        }

        // ------------------------------------------------------------------ starting things

        /// <summary>A Wait order, shown to the player as Focus: back-to-back ones add up toward FocusDelay (see TickFocus).</summary>
        /// Transcended, it lasts exactly until the unit is Focused (and stretches if a hit restarts the count), so the unit is
        /// asked for orders the moment strain starts draining; already Focused, it is a plain wait.
        private void StartWait(Unit unit, float seconds, bool transcend = false)
        {
            if (unit.Grapple != null) { HoldGrapple(unit); return; }   // no focusing with someone in your grip
            if (unit.FocusSince < 0f)
            {
                unit.FocusSince = Now;
                ApplyStatus(unit, new StatusEffect { Id = StatusIds.Focusing, SourceId = unit.Id, ExpiresAt = Now + FocusDelay  }, unit);
            }
            unit.FocusTranscended = transcend && !unit.HasStatus(StatusIds.Focused);
            if (unit.FocusTranscended)
            {
                seconds = unit.FocusSince + FocusDelay - Now + TickLength;   // a tick past, so Focused is on before it ends
                if (TranscendFocusStrain > 0f) AddStrain(unit, TranscendFocusStrain);
            }
            unit.Waiting = true;
            unit.Idling = false;
            unit.Phase = UnitPhase.Recovery;
            unit.PhaseEndsAt = Now + seconds;
            Emit(EventType.ActionStarted, unit.Id, null, new ActionStartedInfo { ActionId = "wait", Windup = 0f, Recovery = seconds, Transcended = unit.FocusTranscended });
        }

        /// <summary>
        /// An Idle order: stand still for 'seconds' without Focusing (it ends a run of Focus). Otherwise like a Wait: a hit
        /// hands the unit back at once, and a queued ranged order can fire through it.
        /// </summary>
        private void StartIdle(Unit unit, float seconds)
        {
            if (unit.Grapple != null) { HoldGrapple(unit); return; }
            if (unit.FocusSince >= 0f) BreakFocus(unit, false);
            unit.Waiting = true;
            unit.Idling = true;
            unit.Phase = UnitPhase.Recovery;
            unit.PhaseEndsAt = Now + seconds;
            Emit(EventType.ActionStarted, unit.Id, null, new ActionStartedInfo { ActionId = "idle", Windup = 0f, Recovery = seconds });
        }

        /// <summary>Cut a recovery short: the unit is ready (and asked for orders) right now. Block uses it when the guard is hit.</summary>
        public void EndRecoveryNow(Unit u)
        {
            if (u.Phase != UnitPhase.Recovery) return;
            u.Waiting = false;
            u.Phase = UnitPhase.Ready;
            u.PhaseEndsAt = Now;
            Emit(EventType.UnitReady, u.Id, null);
            Triggers.Fire(new TriggerContext { Type = TriggerType.UnitReady, Sim = this, Subject = u });
        }
    }
}
