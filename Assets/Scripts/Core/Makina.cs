using System.Linq;
using UnityEngine;

namespace TacticsGame.Core
{
    /// <summary>
    /// The Strategist's always-on rule, as a passive any unit can carry:
    ///   conscience_flows_into_a_shell - a targeted ability used on a Makina goes on half its cooldown (MakinaCooldownScale).
    /// </summary>
    public static class StrategistPassives
    {
        public const string ConscienceFlows = "conscience_flows_into_a_shell";
        /// <summary>What a targeted ability's cooldown is scaled by when its target is a Makina.</summary>
        public const float MakinaCooldownRefund = 0.5f;
    }

    /// <summary>
    /// BattleSimulation: Makina (UnitDefinition.Makina). Constructs the player orders like any squad unit, but they can't
    /// move (TryStartCommand, StartChase), have no Block or Dash (Unit.FromDefinition), only Wait (StartWait: no Clear Mind),
    /// gain StrainPerSecond always (TickHealOverTime) and break the moment they reach max strain (TickMakina). They are
    /// brought in by an ability that has them act at once (summon_makina: StartEntrance).
    /// </summary>
    public partial class BattleSimulation
    {
        /// <summary>Every tick: a Makina at max strain breaks.</summary>
        private void TickMakina()
        {
            foreach (var u in State.Units.ToArray())
            {
                if (!u.IsMakina || !u.IsAlive || u.Strain < u.MaxStrain - 1e-3f) continue;
                LoseHealth(u, u.Health, "makina_strain");
            }
        }

        /// <summary>Conscience Flows Into A Shell: a targeted ability used on a Makina by a unit with the passive goes on half its
        /// cooldown. 1 otherwise.</summary>
        private static float MakinaCooldownScale(Unit unit, Unit target)
        {
            return target != null && target.IsMakina && target != unit && unit.Passives.Contains(StrategistPassives.ConscienceFlows) ? StrategistPassives.MakinaCooldownRefund : 1f;
        }

        /// <summary>
        /// A unit that has just arrived acts at once: 'def' (one of its own abilities) aimed at 'at', with no windup and nothing
        /// paid (the entrance of a Makina: The Monolith's drop, The Hedron's first Massacre). Its recovery follows as usual.
        /// </summary>
        public void StartEntrance(Unit u, ActionDefinition def, Vector3 at)
        {
            if (u == null || !u.IsAlive || def == null) return;
            var own = u.Actions.FirstOrDefault(a => a != null && a.Id == def.Id) ?? def;
            var behavior = ActionBehaviors.Get(own.BehaviorId);
            if (behavior == null) return;
            u.Phase = UnitPhase.Ready;
            StartAction(u, UnitCommand.UseActionAt(u.Id, own, at), behavior, 0f);
        }

        /// <summary>
        /// Bring 'u' down hard at 'landing' over 'seconds' (Crash Down): from where it hangs in the air, or, if it is on the
        /// ground (an entrance), from 'fromHeight' above the landing, as if dropped from the sky. Any hover or lift ends.
        /// </summary>
        public Vector3 CrashTo(Unit u, Vector3 landing, float seconds, float fromHeight)
        {
            bool airborne = u.IsLifted || u.Hovering;
            ClearHover(u);
            u.RemoveState<LiftState>();
            if (u.Phase == UnitPhase.Moving) EndMove(u, false);
            Vector3 ground = Terrain.OnGround(WalkablePointNear(landing, u.Position));
            ground = Terrain.OnGround(FreeSpotNear(u, ground, ground, false));
            Vector3 from = airborne ? u.Position : ground + Vector3.up * fromHeight;
            u.Position = ground;
            u.GlideFrom = from; u.GlideTo = ground; u.GlideStart = Now; u.GlideEnd = Now + Mathf.Max(TickLength, seconds);
            Emit(EventType.Dash, u.Id, null, new DashInfo { From = from, To = ground, Duration = seconds, Fall = true });
            return ground;
        }
    }
}
