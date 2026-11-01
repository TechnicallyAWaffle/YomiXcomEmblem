using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: Unravel, and out-of-range orders: melee chases and queued ranged shots.</summary>
    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ out-of-range orders

        /// <summary>Melee, target out of reach: walk the shortest path at it (re-pathed as it moves) and strike once in range.</summary>
        /// <summary>Unravel: strain reset and a long stun that pierces stun immunity (a Heavy hit at max strain, or an over-max Praa's Truth).</summary>
        public void UnravelUnit(Unit target, Unit source)
        {
            if (target == null || !target.IsAlive) return;
            AddStrain(target, -target.Strain);   // assumption: Unravel resets strain
            Emit(EventType.Unravel, source != null ? source.Id : null, target.Id);
            if (target.Slam != null) BreakAerialSlam(target, source);   // unravelled in mid-air: it drops what it carries, and falls
            ApplyStun(target, UnravelStunSeconds, source, true, true);
        }

        private bool StartChase(Unit unit, UnitCommand cmd, Unit target, out string reason)
        {
            reason = null;
            if (unit.IsRooted) { reason = "immobilized"; return false; }
            if (unit.IsLifted) { reason = "in the air"; return false; }
            ClearQueued(unit, null);   // committing to an ability overrides a queued ranged one
            unit.LastActionId = null;
            if (!RepathChase(unit, target)) { reason = "no path"; return false; }
            unit.Chase = cmd;
            unit.ChaseUntil = Now + MaxRangeWait;
            Emit(EventType.QueuedActionChanged, unit.Id, target.Id, new QueuedActionInfo { ActionId = cmd.Action.Id, Chase = true });
            return true;
        }

        /// <summary>A chase only strikes once inside this fraction of the ability's range, so a target stepping back doesn't
        /// leave it swinging at the edge (never closer than the two bodies allow). Abilities that close the distance themselves
        /// (ActionBehavior.ClosesDistance: Phantom Strike) go as soon as the target is in range.</summary>
        public float ChaseStrikeFraction = 0.5f;

        private bool ChaseCloseEnough(Unit u, ActionDefinition def, Unit target)
        {
            if (ActionBehaviors.Get(def.BehaviorId).ClosesDistance) return true;   // CanUse already found it in range
            float range = u.ActionStat(def, ActionStat.Range);
            float close = Mathf.Min(range, Mathf.Max(range * ChaseStrikeFraction, u.BodyRadius + target.BodyRadius + 0.15f));
            if (Vector3.Distance(u.Position, target.Position) <= close) return true;
            return u.Move == null || u.Move.NextIndex >= u.Move.Path.Count;   // can't get any closer on this path: strike from here
        }

        private bool RepathChase(Unit unit, Unit target)
        {
            var path = new List<Vector3>();
            if (!PlanPath(unit, unit.Position, target.Position, path)) return false;
            StartMove(unit, path);
            unit.ChaseRepathAt = Now + ChaseRepathSeconds;
            return true;
        }

        /// <summary>Ranged, target out of range: a standing order. The unit stays free to move or wait (moving doesn't drop it,
        /// using another ability replaces it) and fires the moment the target is in range.</summary>
        private void QueueRanged(Unit unit, UnitCommand cmd, Unit target)
        {
            unit.Queued = cmd;
            unit.QueuedUntil = Now + MaxRangeWait;
            Emit(EventType.QueuedActionChanged, unit.Id, target.Id, new QueuedActionInfo { ActionId = cmd.Action.Id });
        }

        private void ClearQueued(Unit u, string reason)
        {
            if (u.Queued == null) return;
            u.Queued = null;
            Emit(EventType.QueuedActionChanged, u.Id, null, new QueuedActionInfo());
            if (reason != null) Emit(EventType.ActionInterrupted, u.Id, null, new InterruptInfo { Reason = reason });
        }

        private void ClearChase(Unit u)
        {
            if (u.Chase == null) return;
            u.Chase = null;
            Emit(EventType.QueuedActionChanged, u.Id, null, new QueuedActionInfo { Chase = true });
        }

        /// <summary>Give up a chase: stop where the unit is and hand it back for orders.</summary>
        private void StopChase(Unit u, string reason)
        {
            ClearChase(u);
            Emit(EventType.ActionInterrupted, u.Id, null, new InterruptInfo { Reason = reason });
            if (u.Phase == UnitPhase.Moving) EndMove(u);
        }

        /// <summary>Stop a walk on the spot without handing the unit back (it is about to start an action).</summary>
        private void HaltWalk(Unit u)
        {
            if (u.Phase != UnitPhase.Moving) return;
            if (u.IsJumping) { u.Position = u.JumpTo; u.JumpEnd = -1f; }   // finish the jump rather than stop in mid-air
            u.Move = null;
            u.Phase = UnitPhase.Ready;
            Emit(EventType.MoveEnded, u.Id, null, new MoveEndedInfo { Position = u.Position });
            SettleOutOfBodies(u);
        }

        private void TickOutOfRangeOrders()
        {
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || IsGliding(u) || u.IsJumping) continue;   // mid-air: no stopping to strike or re-planning
                string reason;
                if (u.Chase != null)
                {
                    var cmd = u.Chase;
                    var target = GetUnit(cmd.TargetUnitId);
                    if (u.Phase != UnitPhase.Moving) { ClearChase(u); continue; }   // something else stopped the walk
                    if (target == null || !target.IsAlive || !IsTargetableBy(target, u)) { StopChase(u, "target lost"); continue; }
                    if (Now >= u.ChaseUntil) { StopChase(u, "never came in range"); continue; }
                    if (CanUse(u, cmd.Action, target, out reason))
                    {
                        var behavior = ActionBehaviors.Get(cmd.Action.BehaviorId);
                        if (!ChaseCloseEnough(u, cmd.Action, target)) reason = "out of range";   // in reach, but keep closing in
                        else if (behavior.CanStart(this, u, cmd, out reason))
                        {
                            ClearChase(u);
                            HaltWalk(u);
                            StartAction(u, cmd, behavior);
                            continue;
                        }
                    }
                    if (reason != "out of range") { StopChase(u, reason); continue; }
                    if (Now >= u.ChaseRepathAt && !RepathChase(u, target)) StopChase(u, "no path");
                    continue;
                }
                if (u.Queued != null)
                {
                    var cmd = u.Queued;
                    var target = GetUnit(cmd.TargetUnitId);
                    if (target == null || !target.IsAlive || !IsTargetableBy(target, u)) { ClearQueued(u, "target lost"); continue; }
                    if (Now >= u.QueuedUntil) { ClearQueued(u, "never came in range"); continue; }
                    bool free = u.Phase == UnitPhase.Ready || u.Phase == UnitPhase.Moving || (u.Phase == UnitPhase.Recovery && u.Waiting);
                    if (!free || !CanUse(u, cmd.Action, target, out reason)) continue;   // busy, or still out of reach: keep it
                    var behavior = ActionBehaviors.Get(cmd.Action.BehaviorId);
                    if (!behavior.CanStart(this, u, cmd, out reason)) continue;
                    u.Queued = null;
                    Emit(EventType.QueuedActionChanged, u.Id, null, new QueuedActionInfo());
                    HaltWalk(u);
                    u.Waiting = false;
                    StartAction(u, cmd, behavior);
                }
            }
        }
    }
}
