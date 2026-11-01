using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: Subjugate: holding, headbutting, carrying and throwing.</summary>
    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ grapples (Subjugate)

        // Holding someone: a headbutt (the Subjugate's LinkedAction) every GrappleHeadbuttSeconds while the grab's stun lasts,
        // then (or when the holder lets go early) a GrappleThrowWindup windup and a throw GrappleThrowDistance onward that
        // deals the Subjugate's own damage and stuns for GrappleThrowStun.
        public float GrappleHeadbuttSeconds = 0.5f;
        public float GrappleThrowWindup = 0.35f;
        public float GrappleThrowDistance = 4f;
        public float GrappleThrowStun = 0.5f;
        /// <summary>ActionInstance.Tag of Subjugate's throw (the same action, second stage).</summary>
        public const string ThrowTag = "subjugate_throw";

        /// <summary>How far in front of the grappler the held unit is carried.</summary>
        public static float CarryOffset(Unit holder, Unit held) { return holder.BodyRadius + held.BodyRadius + 0.1f; }

        /// <summary>
        /// Subjugate's grab landed and stunned 'target': 'unit' holds it (stun immune, "grappling") and carries it in front
        /// of itself until the stun runs out or it lets go (ReleaseGrapple), then throws it. Meanwhile the unit can only move
        /// or keep headbutting: it is asked for orders after every headbutt (see TickGrapples, HoldGrapple).
        /// </summary>
        public void StartGrapple(Unit unit, Unit target, ActionDefinition def)
        {
            if (!unit.IsAlive || !target.IsAlive || unit.Grapple != null || target.Phase != UnitPhase.Stunned || target.IsLifted || target.FallLandAt >= 0f) return;
            if (target.HeldById != null) ReleaseHeld(target);   // taken out of someone else's grip
            if (target.Grapple != null) EndGrapple(target);     // and it lets go of whoever it was holding
            target.GlideEnd = -1f;                              // a shove in progress stops here
            Vector3 facing = target.Position - unit.Position; facing.y = 0f;
            unit.AddState(new GrappleState
            {
                TargetId = target.Id, Definition = def, NextHeadbuttAt = Now + GrappleHeadbuttSeconds, EndsAt = target.PhaseEndsAt,
                Facing = facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.forward,
            });
            target.AddState(new HeldState { HolderId = unit.Id });
            ApplyStatus(unit, new StatusEffect { Id = StatusIds.Grappling, SourceId = unit.Id, ExpiresAt = float.MaxValue, Flags = StatusFlags.StunImmune }, unit);
            ApplyStatus(target, new StatusEffect { Id = StatusIds.Grappled, SourceId = unit.Id, ExpiresAt = float.MaxValue  }, unit);
            PlaceHeld(unit, target);
            Emit(EventType.Carry, unit.Id, target.Id, new CarryInfo { Carrying = true, Offset = CarryOffset(unit, target), Position = target.Position });
            HoldGrapple(unit);
        }

        /// <summary>Keep holding: stand and headbutt, asked for orders again right after the next headbutt.</summary>
        private void HoldGrapple(Unit unit)
        {
            var g = unit.Grapple;
            if (g == null || g.Throwing) return;
            unit.Waiting = false;
            unit.Phase = UnitPhase.Recovery;
            unit.PhaseEndsAt = Mathf.Max(Now + TickLength, Mathf.Min(g.NextHeadbuttAt, g.EndsAt));
        }

        /// <summary>Let go into the throw: its windup starts now (the held unit stays stunned through it). 'aim': throw toward
        /// that point (the held unit swings round to that side), else the way the holder faces.</summary>
        private void StartThrow(Unit unit, Vector3? aim = null)
        {
            var g = unit.Grapple;
            var held = g != null ? GetUnit(g.TargetId) : null;
            if (g == null || g.Throwing || held == null) return;
            HaltWalk(unit);
            if (aim.HasValue)
            {
                Vector3 d = aim.Value - unit.Position; d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) { g.Facing = d.normalized; PlaceHeld(unit, held); }
            }
            g.Throwing = true;
            g.AwaitingThrow = false;
            unit.Waiting = false;
            var def = g.Definition;
            float speed = unit.EffectiveActionSpeed(), windup = GrappleThrowWindup / speed;
            var inst = new ActionInstance
            {
                Actor = unit, Definition = def, Behavior = ActionBehaviors.Get(def.BehaviorId), TargetUnitId = held.Id, TargetPos = held.Position,
                StartedAt = Now, ResolveAt = Now + windup, Tag = ThrowTag,
            };
            unit.ActiveAction = inst;
            unit.Phase = UnitPhase.Windup;
            unit.PhaseEndsAt = inst.ResolveAt;
            ApplyStun(held, windup + DisplaceDuration, unit, true);
            Emit(EventType.ActionStarted, unit.Id, held.Id, new ActionStartedInfo { ActionId = def.Id, Windup = windup, Recovery = unit.ActionStat(def, ActionStat.Recovery) / speed, TargetPos = held.Position });
            Triggers.Fire(new TriggerContext { Type = TriggerType.ActionStarted, Sim = this, Subject = unit, Action = def });
        }

        /// <summary>Stop holding (the throw went off, or the hold broke): both statuses come off and the held unit is put down.
        /// A unit that was only holding (not throwing) is handed back for orders.</summary>
        public void EndGrapple(Unit unit)
        {
            var g = unit.Grapple;
            if (g == null) return;
            unit.RemoveState(g);
            RemoveStatus(unit, StatusIds.Grappling);
            var held = GetUnit(g.TargetId);
            if (held != null)
            {
                held.RemoveState<HeldState>();
                RemoveStatus(held, StatusIds.Grappled);
                Emit(EventType.Carry, unit.Id, held.Id, new CarryInfo { Carrying = false, Position = held.Position });
            }
            if (!g.Throwing && unit.IsAlive && unit.Phase == UnitPhase.Recovery) EndRecoveryNow(unit);
        }

        /// <summary>Free 'held' from whoever is holding it.</summary>
        internal void ReleaseHeld(Unit held)
        {
            var holder = GetUnit(held.HeldById);
            if (holder != null && holder.Grapple != null && holder.Grapple.TargetId == held.Id) EndGrapple(holder);
            held.RemoveState<HeldState>();
        }

        /// <summary>A player's grab ran out: its holder is being asked which way to throw.</summary>
        public bool AwaitingThrow(Unit unit) { return unit.Grapple != null && unit.Grapple.AwaitingThrow; }

        /// <summary>The direction a unit holding someone is facing right now (where the throw goes).</summary>
        public Vector3 GrappleFacing(Unit unit) { return unit.Grapple != null ? unit.Grapple.Facing : Vector3.forward; }

        private void PlaceHeld(Unit unit, Unit held)
        {
            held.Position = Paths.ClampDash(unit.Position, unit.Position + unit.Grapple.Facing * CarryOffset(unit, held));
        }

        private void TickGrapples()
        {
            foreach (var u in State.Units)
            {
                if (u.Grapple == null)
                {
                    // Stun immunity from a grab that never took hold (missed, dodged, blocked) goes once the windup is over.
                    if (u.HasStatus(StatusIds.Grappling) && !(u.Phase == UnitPhase.Windup && u.ActiveAction != null && u.ActiveAction.Behavior is SubjugateBehavior))
                        RemoveStatus(u, StatusIds.Grappling);
                    continue;
                }
                var g = u.Grapple;
                var held = GetUnit(g.TargetId);
                // The hold breaks if either goes down, the holder is knocked out of it (Unravel, a fall), or the held unit is lifted away.
                if (!u.IsAlive || held == null || !held.IsAlive || u.Phase == UnitPhase.Stunned || held.IsLifted || held.FallLandAt >= 0f) { EndGrapple(u); continue; }
                if (!g.Throwing && u.Phase == UnitPhase.Moving && u.Move != null && u.Move.NextIndex < u.Move.Path.Count)
                {
                    Vector3 d = u.Move.Path[u.Move.NextIndex] - u.Position; d.y = 0f;
                    if (d.sqrMagnitude > 1e-4f) g.Facing = d.normalized;   // carried in front, the way it walks
                }
                PlaceHeld(u, held);
                if (g.Throwing) continue;
                if (g.AwaitingThrow)
                {
                    if (held.Phase == UnitPhase.Stunned) held.PhaseEndsAt = Mathf.Max(held.PhaseEndsAt, Now + 0.25f);   // held on until the throw goes
                    continue;
                }
                if (Now + 1e-4f >= g.EndsAt)
                {
                    // A player picks the throw's direction: the unit is handed back for that order (HoldGrapple/Wait throw ahead).
                    // The AI throws the way it faces.
                    if (u.Team == Team.Player)
                    {
                        g.AwaitingThrow = true;
                        if (held.Phase == UnitPhase.Stunned) held.PhaseEndsAt = Mathf.Max(held.PhaseEndsAt, Now + 0.25f);
                        if (u.Phase == UnitPhase.Recovery) EndRecoveryNow(u);
                        else if (u.Phase == UnitPhase.Moving) { HaltWalk(u); Emit(EventType.UnitReady, u.Id, null); }   // stop to throw
                        continue;
                    }
                    StartThrow(u);
                    continue;
                }
                if (Now + 1e-4f >= g.NextHeadbuttAt)
                {
                    g.NextHeadbuttAt += GrappleHeadbuttSeconds;
                    if (g.Definition.LinkedAction != null) QueueFreeAction(u, g.Definition.LinkedAction, held.Id, 0f);
                }
            }
        }
    }
}
