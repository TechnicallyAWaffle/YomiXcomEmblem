using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>BattleSimulation: Charge abilities, movement helpers for behaviors, and the Mesmerize counter.</summary>
    public partial class BattleSimulation
    {
        // ------------------------------------------------------------------ charge abilities

        /// <summary>
        /// A charge ability (ActionDefinition.IsCharge): pay its costs and start charging. From here the unit can only keep
        /// charging (ContinueCharge) or release it (ReleaseCharge, which picks the target); it is asked at once and then every
        /// time its ContinueCharge runs out. Charging drains the definition's per-second resources and breaks on any damage.
        /// </summary>
        private void StartCharge(Unit unit, UnitCommand cmd, ActionBehavior behavior)
        {
            var def = cmd.Action;
            unit.Waiting = false;
            ClearQueued(unit, null);
            PayCosts(unit, def, behavior, null);
            unit.ActiveAction = new ActionInstance { Actor = unit, Definition = def, Behavior = behavior, StartedAt = Now, ResolveAt = float.MaxValue };
            unit.Phase = UnitPhase.Charging;
            unit.PhaseEndsAt = float.MaxValue;
            unit.ChargeDecisionAt = Now;
            unit.LastActionId = def.Id;
            unit.LastActionTargetId = null;
            Emit(EventType.ChargeStarted, unit.Id, null, new ChargeInfo { ActionId = def.Id, MaxSeconds = def.MaxChargeSeconds });
        }

        /// <summary>0..1: how far a charging unit is toward full power.</summary>
        public float ChargeLevel(Unit unit)
        {
            if (unit.Phase != UnitPhase.Charging || unit.ActiveAction == null) return 0f;
            var def = unit.ActiveAction.Definition;
            return def.MaxChargeSeconds > 0f ? Mathf.Clamp01((Now - unit.ActiveAction.StartedAt) / def.MaxChargeSeconds) : 1f;
        }

        private bool ReleaseCharge(Unit unit, UnitCommand cmd, out string reason)
        {
            reason = null;
            var charged = unit.ActiveAction;
            if (charged == null) { reason = "not charging"; return false; }
            var def = charged.Definition;
            var release = def.Targeting == TargetType.Position ? UnitCommand.UseActionAt(unit.Id, def, cmd.TargetPos)
                                                               : UnitCommand.UseAction(unit.Id, def, cmd.TargetUnitId ?? unit.Id);
            if (def.Targeting == TargetType.Enemy || def.Targeting == TargetType.Ally)
            {
                if (!CanUse(unit, def, GetUnit(release.TargetUnitId), out reason, false)) return false;   // already paid for
            }
            else if (def.Targeting == TargetType.Position && def.Shape != AreaShape.Line && Vector3.Distance(unit.Position, cmd.TargetPos) > unit.ActionStat(def, ActionStat.Range))
            {
                reason = "out of range"; return false;
            }
            float level = ChargeLevel(unit);
            unit.ActiveAction = null;
            unit.Phase = UnitPhase.Ready;
            StartAction(unit, release, charged.Behavior, WindupOf(unit, def), level);
            return true;
        }

        /// <summary>Charging units drain their charge resources (in StrainRecoveryTicks steps, like strain recovery).</summary>
        private void TickCharges()
        {
            if (tick % StrainRecoveryTicks != 0) return;
            float step = StrainRecoveryTicks * TickLength;
            foreach (var u in State.Units)
            {
                if (!u.IsAlive || u.Phase != UnitPhase.Charging || u.ActiveAction == null) continue;
                var def = u.ActiveAction.Definition;
                if (def.ChargeStrainPerSecond > 0f) AddStrain(u, def.ChargeStrainPerSecond * step);
                if (def.ChargeHealthPerSecond > 0f)
                {
                    float drain = def.ChargeHealthPerSecond * step;
                    u.Health -= drain;
                    Emit(EventType.Damage, u.Id, u.Id, new DamageInfo { ActionId = def.Id, Amount = drain, Impact = ImpactType.Light, RemainingHealth = u.Health });
                }
            }
        }

        // ------------------------------------------------------------------ helpers for movement abilities

        /// <summary>Slide a unit to 'to' over 'duration' (Phantom Strike, Mesmerize's blink). Can't start anything until it lands.
        /// phaseFrom: for a move that passes through walls (Phantom Strike), the point a crowded landing spot is searched
        /// from instead of the unit, so the search stays on the far side.</summary>
        public void SlideUnitTo(Unit unit, Vector3 to, float duration, Vector3? phaseFrom = null)
        {
            if (!unit.IsAlive || (to - unit.Position).sqrMagnitude <= 1e-4f) return;
            Relocate(unit, to, duration, false, null, phaseFrom);
        }

        /// <summary>Shove a unit to 'to' over 'duration', as a push does (forced: off a ledge it falls; a jump is broken). Charge's drag.</summary>
        public void PushUnit(Unit unit, Vector3 to, float duration, Unit source)
        {
            if (unit == null || !unit.IsAlive || (to - unit.Position).sqrMagnitude <= 1e-4f) return;
            Relocate(unit, to, duration, true, source);
        }

        /// <summary>Break a unit's windup or charge, if it has one (Stutter). True if something was interrupted.</summary>
        public bool InterruptUnit(Unit unit, string reason)
        {
            if (unit == null || !unit.IsAlive || unit.ActiveAction == null || unit.IsUnstoppable) return false;
            if (unit.Phase != UnitPhase.Windup && unit.Phase != UnitPhase.Charging) return false;
            Interrupt(unit, reason);
            return true;
        }

        public const float MesmerBlinkSeconds = 0.06f;

        /// <summary>
        /// Mesmerize's counter: drop whatever the unit was doing, blink next to the attacker and line up 'strikes' rapid
        /// strikes at it. Each strike is its own order (the AI always continues; a player can stop after any), and any
        /// other order ends the counter.
        /// </summary>
        public void StartMesmerCounter(Unit unit, Unit attacker, ActionDefinition strike, int strikes)
        {
            if (!unit.IsAlive || attacker == null || !attacker.IsAlive || strike == null || unit.AnyState(s => s.BlocksMesmerCounter)) return;
            if (unit.Phase == UnitPhase.Windup || unit.Phase == UnitPhase.Charging) Interrupt(unit, "mesmerize", false);
            HaltWalk(unit);
            ClearChase(unit);
            unit.Waiting = false;
            unit.ResumeAction = null;
            unit.Phase = UnitPhase.Ready;
            unit.PhaseEndsAt = Now;
            EndMesmer(unit);
            unit.AddState(new MesmerState { TargetId = attacker.Id, Strike = strike, StrikesLeft = strikes });
            Vector3 away = unit.Position - attacker.Position; away.y = 0f;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward;
            SlideUnitTo(unit, attacker.Position + away * (unit.BodyRadius + attacker.BodyRadius + 0.3f), MesmerBlinkSeconds);
            Emit(EventType.UnitReady, unit.Id, null);
        }

        /// <summary>The player chose to stop a Mesmerize counter early.</summary>
        public void CancelMesmer(string unitId)
        {
            var u = GetUnit(unitId);
            if (u != null) EndMesmer(u);
        }

        /// <summary>A Mesmerize counter is over (stopped, another order given, or its target gone).</summary>
        private static void EndMesmer(Unit u) { u.RemoveState<MesmerState>(); }
    }
}
