using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: Interrupts, stuns, strain, focus and speed changes, and emitting events.</summary>
    public partial class BattleSimulation
    {
        private void Interrupt(Unit u, string reason, bool emitReady = true)
        {
            var a = u.ActiveAction;
            if (a == null) return;
            a.Behavior.OnInterrupted(this, a);
            ClearWindupStatuses(a);
            if (u.HoverEndsAfterAction && a.Actor == u) EndHover(u, false, null);
            u.ActiveAction = null;
            if (u.Phase == UnitPhase.Windup || u.Phase == UnitPhase.Charging) u.Phase = UnitPhase.Ready;
            Emit(EventType.ActionInterrupted, u.Id, null, new InterruptInfo { Reason = reason });
            if (emitReady && u.Phase == UnitPhase.Ready)
            {
                Emit(EventType.UnitReady, u.Id, null);
                Triggers.Fire(new TriggerContext { Type = TriggerType.UnitReady, Sim = this, Subject = u });
            }
        }

        /// <summary>
        /// Stun on a winding-up unit does nothing unless that action is InterruptedByStun (Heavy impacts already
        /// cancelled the windup before we get here). Recovery is replaced by the stun, never shortened.
        /// Stun drops Dodge to 0 through UnitPhase.Stunned.
        /// </summary>
        /// <param name="piercesImmunity">Unravel and falls: lands even on a stun-immune (blocking) unit, and always breaks a windup.</param>
        /// <param name="unravel">It is an Unravel: it lands whatever the unit is immune to, Unstoppable included.</param>
        private void ApplyStun(Unit target, float duration, Unit source, bool piercesImmunity = false, bool unravel = false)
        {
            if (target.Phase == UnitPhase.Dead) return;
            BreakJump(target, source);    // stunned mid-jump: it falls (and that fall's own stun lands first)
            if (unravel) piercesImmunity = true;
            else if (target.IsUnstoppable) return;   // nothing but Unravel stops an Unstoppable unit, not even a fall
            if (target.IsStunImmune && !piercesImmunity) return;   // Block
            target.ResumeAction = null;   // a stun mid-dodge drops the remembered action
            ClearChase(target);           // and a stun ends a chase (a queued ranged order survives it)
            if (target.Phase == UnitPhase.Windup)
            {
                if (target.ActiveAction == null || (!target.ActiveAction.Definition.InterruptedByStun && !piercesImmunity)) return;
                Interrupt(target, "stun", false);
            }
            if (target.Phase == UnitPhase.Charging) Interrupt(target, "stun", false);   // a stun always breaks a charge
            if (target.Phase == UnitPhase.Channeling) EndChannel(target, "stun", false);   // and a channel
            if (target.Phase == UnitPhase.Moving)
            {
                target.Move = null;
                Emit(EventType.MoveEnded, target.Id, null, new MoveEndedInfo { Position = target.Position });
                SettleOutOfBodies(target);
            }

            float end = Now + duration;
            if (target.Phase == UnitPhase.Stunned || target.Phase == UnitPhase.Recovery) end = Mathf.Max(end, target.PhaseEndsAt);
            target.Phase = UnitPhase.Stunned;
            target.PhaseEndsAt = end;
            Emit(EventType.Stun, source != null ? source.Id : null, target.Id, new StunInfo { Duration = end - Now });
            Triggers.Fire(new TriggerContext { Type = TriggerType.Stunned, Sim = this, Subject = target, Other = source, Amount = duration });
        }

        /// <summary>Strain drains on its own once a unit has gone StrainRecoveryDelay without gaining any. Applied in
        /// StrainRecoveryTicks steps so the timeline gets a few events per second rather than one per tick.</summary>
        private void RecoverStrain()
        {
            if (tick % StrainRecoveryTicks != 0) return;
            float step = StrainRecoveryTicks * TickLength;
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u.Strain <= 0f) continue;
                float rate = u.HasStatus(StatusIds.Focused) ? FocusStrainPerSecond : 0f;   // focus drains even right after a strain gain
                if (u.StrainRecoveryPerSecond > 0f && Now - u.LastStrainGainAt >= u.StrainRecoveryDelay) rate += u.StrainRecoveryPerSecond;
                if (rate > 0f) AddStrain(u, -rate * step);
            }
        }

        /// <summary>
        /// Focus bookkeeping, first thing every tick. A unit that isn't in a Wait any more has stopped focusing (back-to-back
        /// Waits never show a tick in between: the next one starts before the sim steps again). Once a unit has focused for
        /// FocusDelay without a break it gets the Focused status, which RecoverStrain drains strain for.
        /// </summary>
        private void TickFocus()
        {
            foreach (var u in State.Units)
            {
                bool waiting = u.IsAlive && u.Phase == UnitPhase.Recovery && u.Waiting && !u.Idling;
                if (!waiting) { if (u.FocusSince >= 0f) BreakFocus(u, false); continue; }
                if (u.FocusSince >= 0f && Now - u.FocusSince >= FocusDelay - 1e-4f && !u.HasStatus(StatusIds.Focused))
                {
                    RemoveStatus(u, StatusIds.Focusing);
                    ApplyStatus(u, new StatusEffect { Id = StatusIds.Focused, SourceId = u.Id, ExpiresAt = float.MaxValue  }, u);
                }
            }
        }

        /// <summary>Stop (or, restart: hit while focusing) a unit's run of focus. A Transcended Focus keeps going until Focused.</summary>
        private void BreakFocus(Unit u, bool restart)
        {
            u.FocusSince = restart ? Now : -1f;
            if (u.HasStatus(StatusIds.Focused)) RemoveStatus(u, StatusIds.Focused);
            if (!restart) { RemoveStatus(u, StatusIds.Focusing); u.FocusTranscended = false; return; }
            ApplyStatus(u, new StatusEffect { Id = StatusIds.Focusing, SourceId = u.Id, ExpiresAt = Now + FocusDelay  }, u);
            if (u.FocusTranscended && u.Waiting && u.Phase == UnitPhase.Recovery)
                u.PhaseEndsAt = Mathf.Max(u.PhaseEndsAt, Now + FocusDelay + TickLength);
        }

        /// <summary>Add (or, negative, relieve) strain, clamped to 0..MaxStrain.</summary>
        public void AddStrain(Unit u, float delta)
        {
            if (delta > 0f) u.LastStrainGainAt = Now;
            float before = u.Strain;
            u.Strain = Mathf.Clamp(u.Strain + delta, 0f, u.MaxStrain);
            if (Mathf.Abs(u.Strain - before) < 1e-4f) return;
            Emit(EventType.StrainChanged, u.Id, null, new StrainInfo { Amount = u.Strain - before, Total = u.Strain });
        }

        /// <summary>
        /// Haste/slow changed a unit's speeds mid-action: rescale the time that is LEFT in its windup/recovery,
        /// and restart a walk in progress at the new speed. Emits events so the view can follow.
        /// </summary>
        private void HandleSpeedChange(Unit u, float oldAction, float oldMove)
        {
            float newAction = u.EffectiveActionSpeed(), newMove = u.EffectiveMoveSpeed();
            bool actionChanged = Mathf.Abs(newAction - oldAction) > 1e-4f;
            bool moveChanged = Mathf.Abs(newMove - oldMove) > 1e-4f;
            if (!actionChanged && !moveChanged) return;

            if (actionChanged && (u.Phase == UnitPhase.Windup || u.Phase == UnitPhase.Recovery) && u.PhaseEndsAt > Now)
            {
                float newEnd = Now + (u.PhaseEndsAt - Now) * oldAction / newAction;
                u.PhaseEndsAt = newEnd;
                if (u.Phase == UnitPhase.Windup && u.ActiveAction != null) u.ActiveAction.ResolveAt = newEnd;
            }

            if (moveChanged && u.Phase == UnitPhase.Moving && u.Move != null)
            {
                u.Move.Speed = newMove;
                if (!u.IsJumping)   // mid-jump, the landing re-sends the rest of the walk at the new speed
                {
                    var rest = new List<Vector3> { u.Position };
                    for (int i = u.Move.NextIndex; i < u.Move.Path.Count; i++) rest.Add(u.Move.Path[i]);
                    Emit(EventType.MoveStarted, u.Id, null, new MoveInfo { Path = rest, Speed = newMove });
                }
            }

            Emit(EventType.SpeedChanged, u.Id, null, new SpeedInfo { ActionSpeed = newAction, MoveSpeed = newMove });
        }

        private void Emit(EventType type, string actorId, string targetId, object payload = null)
        {
            Timeline.AddEvent(new TimelineEvent(Now, actorId, targetId, type, payload));
        }
    }
}
